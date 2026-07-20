using System.Net;
using Docker.DotNet;
using DockerVm.Data;
using DockerVm.Endpoints;
using DockerVm.Middleware;
using DockerVm.Models;
using DockerVm.Options;
using DockerVm.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ---------- 配置 ----------
var opts = new AppOptions();
opts.HostIp = Environment.GetEnvironmentVariable("HOST_IP");
_ = int.TryParse(Environment.GetEnvironmentVariable("PORT_MIN"), out var pmin) ? pmin : opts.PortMin;
_ = int.TryParse(Environment.GetEnvironmentVariable("PORT_MAX"), out var pmax) ? pmax : opts.PortMax;
opts.PortMin = pmin;
opts.PortMax = pmax;
opts.AdminToken = Environment.GetEnvironmentVariable("ADMIN_TOKEN") ?? opts.AdminToken;
opts.InitialAdminUsername = Environment.GetEnvironmentVariable("INITIAL_ADMIN_USERNAME") ?? opts.InitialAdminUsername;
opts.InitialAdminPassword = Environment.GetEnvironmentVariable("INITIAL_ADMIN_PASSWORD") ?? opts.InitialAdminPassword;
opts.SshUser = Environment.GetEnvironmentVariable("SSH_USER") ?? opts.SshUser;
opts.SshImageName = Environment.GetEnvironmentVariable("SSH_IMAGE_NAME") ?? opts.SshImageName;
opts.SshImageContextDir = Environment.GetEnvironmentVariable("SSH_IMAGE_CONTEXT_DIR") ?? opts.SshImageContextDir;
opts.CorsOrigins = Environment.GetEnvironmentVariable("CORS_ORIGINS") ?? opts.CorsOrigins;

// ---------- 资源/名额配置 ----------
_ = double.TryParse(Environment.GetEnvironmentVariable("VM_CPU_CORES"), out var cpuCores) ? cpuCores : opts.VmCpuCores;
opts.VmCpuCores = cpuCores;
_ = int.TryParse(Environment.GetEnvironmentVariable("VM_MEMORY_MB"), out var memMb) ? memMb : opts.VmMemoryMB;
opts.VmMemoryMB = memMb;
_ = long.TryParse(Environment.GetEnvironmentVariable("VM_PIDS_LIMIT"), out var pidsLimit) ? pidsLimit : opts.VmPidsLimit;
opts.VmPidsLimit = pidsLimit;
opts.VmDiskSize = Environment.GetEnvironmentVariable("VM_DISK_SIZE") ?? opts.VmDiskSize;
opts.DiskQuotaBytes = StartupInitializer.ParseDiskSize(Environment.GetEnvironmentVariable("VM_DISK_SIZE"), opts.DiskQuotaBytes);
opts.DiskAlertBytes = StartupInitializer.ParseDiskSize(Environment.GetEnvironmentVariable("VM_DISK_ALERT"), opts.DiskAlertBytes);
_ = int.TryParse(Environment.GetEnvironmentVariable("VM_DISK_SCAN_MINUTES"), out var scanMin) ? scanMin : opts.DiskScanIntervalMinutes;
opts.DiskScanIntervalMinutes = scanMin;
opts.VolumeDir = Environment.GetEnvironmentVariable("VM_VOLUME_DIR") ?? opts.VolumeDir;
opts.DockerOverlayDir = Environment.GetEnvironmentVariable("DOCKER_OVERLAY_DIR") ?? opts.DockerOverlayDir;
_ = int.TryParse(Environment.GetEnvironmentVariable("QUOTA_INITIAL_TOTAL"), out var qInit) ? qInit : opts.QuotaInitialTotal;
opts.QuotaInitialTotal = qInit;

// ---------- LXCFS 配置 ----------
// ENABLE_LXCFS 不设置时默认 "true"(向后兼容)。明确传 "false"/"0" 才关闭
var enableLxcfsRaw = Environment.GetEnvironmentVariable("ENABLE_LXCFS");
opts.EnableLxcfs = enableLxcfsRaw is null
    ? true
    : !enableLxcfsRaw.Equals("false", StringComparison.OrdinalIgnoreCase)
        && !enableLxcfsRaw.Equals("0", StringComparison.OrdinalIgnoreCase);
opts.LxcfsProcDir = Environment.GetEnvironmentVariable("LXCFS_PROC_DIR") ?? opts.LxcfsProcDir;

builder.Services.AddSingleton(opts);

// ---------- 数据库 ----------
var dbPath = Environment.GetEnvironmentVariable("DB_PATH") ?? "/data/docker-vm.db";
var dbDir = Path.GetDirectoryName(dbPath);
if (!string.IsNullOrEmpty(dbDir) && !Directory.Exists(dbDir))
{
    Directory.CreateDirectory(dbDir);
}

builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseSqlite($"Data Source={dbPath}"));

// ---------- Docker 客户端 ----------
var dockerHost = Environment.GetEnvironmentVariable("DOCKER_HOST") ?? "unix:///var/run/docker.sock";
builder.Services.AddSingleton<IDockerClient>(_ =>
{
    var config = new DockerClientConfiguration(new Uri(dockerHost));
    return config.CreateClient();
});

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<QuotaService>();
builder.Services.AddScoped<DiskQuotaService>();
builder.Services.AddScoped<PortAllocator>(sp => new PortAllocator(
    sp.GetRequiredService<AppDbContext>(), opts.PortMin, opts.PortMax));
builder.Services.AddScoped<IDockerService, DockerService>();
builder.Services.AddHostedService<DiskQuotaScanService>();

builder.Services.AddSingleton<SshImageBuilder>(sp => new SshImageBuilder(
    sp.GetRequiredService<IDockerClient>(),
    opts.SshImageName,
    opts.SshImageContextDir,
    sp.GetRequiredService<ILogger<SshImageBuilder>>()));
builder.Services.AddHostedService<StartupInitializer>();

// CORS:允许前端跨域带 cookie
builder.Services.AddCors(o =>
{
    var origins = opts.CorsOrigins.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    o.AddDefaultPolicy(p =>
    {
        if (origins.Contains("*"))
        {
            // 带 cookie 时不能用 *,降级为允许任意来源但不带凭证
            p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
        }
        else
        {
            p.WithOrigins(origins).AllowAnyMethod().AllowAnyHeader().AllowCredentials();
        }
    });
});

var app = builder.Build();

app.UseCors();
app.UseRouting();
app.UseMiddleware<AuthMiddleware>();   // 解析 cookie → 当前用户

// 自动建库建表
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

app.MapGet("/api/health", () => Results.Ok(new { ok = true }));

app.MapAuthEndpoints();
app.MapVmEndpoints();
app.MapQuotaEndpoints();
app.MapAdminEndpoints();

app.Run();

// ---------- 启动初始化:自动构建 SSH 镜像 + 首启建 admin ----------
internal sealed class StartupInitializer : IHostedService
{
    private readonly IServiceProvider _sp;
    private readonly ILogger<StartupInitializer> _logger;

    public StartupInitializer(IServiceProvider sp, ILogger<StartupInitializer> logger)
    {
        _sp = sp;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _sp.CreateScope();
        var sp = scope.ServiceProvider;
        var opts = sp.GetRequiredService<AppOptions>();

        // 1. 构建 SSH 镜像
        try
        {
            var imageBuilder = sp.GetRequiredService<SshImageBuilder>();
            await imageBuilder.EnsureImageAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "启动时构建 SSH 镜像失败。请检查镜像目录 {Dir} 是否正确挂载。",
                opts.SshImageContextDir);
        }

        // 2. 首启建初始管理员
        try
        {
            var db = sp.GetRequiredService<AppDbContext>();
            if (!await db.Users.AnyAsync(cancellationToken))
            {
                var (hash, salt) = PasswordHasher.Hash(opts.InitialAdminPassword);
                db.Users.Add(new User
                {
                    Username = opts.InitialAdminUsername,
                    PasswordHash = hash,
                    PasswordSalt = salt,
                    IsAdmin = true,
                    CreatedAt = DateTime.UtcNow,
                });
                await db.SaveChangesAsync(cancellationToken);
                _logger.LogInformation("已创建初始管理员账号:{User}(请尽快登录修改密码)", opts.InitialAdminUsername);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "创建初始管理员失败");
        }

        // 3. 初始化全局名额池
        try
        {
            var quota = sp.GetRequiredService<QuotaService>();
            await quota.EnsureInitializedAsync(opts.QuotaInitialTotal, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "初始化名额池失败");
        }

        // 4. 探测宿主 LXCFS 是否可用
        if (opts.EnableLxcfs)
        {
            opts.LxcfsActuallyEnabled = LxcfsProbe.IsAvailable(opts.LxcfsProcDir);
            if (opts.LxcfsActuallyEnabled)
            {
                _logger.LogInformation("LXCFS 探测成功,新容器将挂载 {Dir} 以隔离 /proc 视图", opts.LxcfsProcDir);
            }
            else
            {
                _logger.LogWarning("LXCFS 未启用或 {Dir} 不可用,容器内 /proc 将看到宿主真实资源(降级运行)", opts.LxcfsProcDir);
            }
        }
        else
        {
            _logger.LogInformation("LXCFS 已被 ENABLE_LXCFS=false 关闭");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// 把 "5g" / "512m" / "1073741824" 之类的磁盘大小字符串解析成字节数。
    /// 失败返回 fallback。
    /// </summary>
    public static long ParseDiskSize(string? raw, long fallback)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return fallback;
        }
        raw = raw.Trim().ToLowerInvariant();
        var match = System.Text.RegularExpressions.Regex.Match(raw, @"^(\d+(?:\.\d+)?)\s*([kmgt]?)b?$");
        if (!match.Success)
        {
            return long.TryParse(raw, out var asBytes) ? asBytes : fallback;
        }
        var num = double.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        var unit = match.Groups[2].Value;
        var mult = unit switch
        {
            "" => 1L,
            "k" => 1024L,
            "m" => 1024L * 1024,
            "g" => 1024L * 1024 * 1024,
            "t" => 1024L * 1024 * 1024 * 1024,
            _ => 1L,
        };
        return (long)(num * mult);
    }
}
