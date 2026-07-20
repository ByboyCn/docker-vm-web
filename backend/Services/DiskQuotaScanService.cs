using DockerVm.Data;
using DockerVm.Options;
using Microsoft.EntityFrameworkCore;

namespace DockerVm.Services;

/// <summary>
/// 后台定时扫描容器磁盘占用,超过阈值的容器打 warning 日志。
/// 阈值由 AppOptions.DiskAlertBytes 控制。
/// </summary>
public class DiskQuotaScanService : IHostedService, IDisposable
{
    private readonly IServiceProvider _sp;
    private readonly AppOptions _opts;
    private readonly ILogger<DiskQuotaScanService> _logger;
    private Timer? _timer;

    public DiskQuotaScanService(IServiceProvider sp, AppOptions opts, ILogger<DiskQuotaScanService> logger)
    {
        _sp = sp;
        _opts = opts;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // 启动后 1 分钟跑第一次,之后每 N 分钟一次
        var interval = TimeSpan.FromMinutes(Math.Max(1, _opts.DiskScanIntervalMinutes));
        _timer = new Timer(ScanCallback, null, TimeSpan.FromMinutes(1), interval);
        _logger.LogInformation("磁盘扫描服务已启动,间隔 {Min} 分钟,告警阈值 {Bytes} 字节",
            _opts.DiskScanIntervalMinutes, _opts.DiskAlertBytes);
        return Task.CompletedTask;
    }

    private async void ScanCallback(object? state)
    {
        try
        {
            using var scope = _sp.CreateScope();
            var diskQuota = scope.ServiceProvider.GetRequiredService<DiskQuotaService>();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var usageByHash = await diskQuota.ScanContainerDiskUsageAsync();
            if (usageByHash.Count == 0)
            {
                return;
            }

            var containers = await db.Containers.AsNoTracking().ToListAsync();
            var userNames = await db.Users.ToDictionaryAsync(u => u.Id, u => u.Username);

            foreach (var c in containers)
            {
                if (!usageByHash.TryGetValue(c.ContainerId, out var bytes))
                {
                    continue;
                }
                if (bytes > _opts.DiskAlertBytes)
                {
                    var username = userNames.GetValueOrDefault(c.UserId, "?");
                    _logger.LogWarning(
                        "容器 {Name}(key={Key}, user={User})磁盘占用 {Bytes} 字节超过阈值 {Threshold} 字节",
                        c.ContainerName, c.Key, username, bytes, _opts.DiskAlertBytes);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "磁盘扫描任务异常");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _timer?.Change(Timeout.Infinite, 0);
        return Task.CompletedTask;
    }

    public void Dispose() => _timer?.Dispose();
}
