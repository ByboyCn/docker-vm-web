using System.Diagnostics;
using DockerVm.Options;

namespace DockerVm.Services;

/// <summary>
/// 容器磁盘配额:用 loop 文件挂到 /home 限制用户主目录写入(主防线),
/// 后台扫描容器可写层总大小作为告警兜底。
/// </summary>
public class DiskQuotaService
{
    private readonly AppOptions _opts;
    private readonly ILogger<DiskQuotaService> _logger;

    public DiskQuotaService(AppOptions opts, ILogger<DiskQuotaService> logger)
    {
        _opts = opts;
        _logger = logger;
    }

    /// <summary>容器 key → loop 文件路径。</summary>
    public string VolumePath(string key) => Path.Combine(_opts.VolumeDir, $"{key}.img");

    // ---------- 主防线:loop 文件 ----------

    /// <summary>
    /// 创建稀疏 loop 文件 + mkfs.ext4。失败抛异常。
    /// </summary>
    public async Task PrepareVolumeAsync(string key, CancellationToken ct = default)
    {
        // 确保宿主目录存在(docker-compose 挂载点要先存在,否则挂载会创建空目录归 root,backend 写不进)
        Directory.CreateDirectory(_opts.VolumeDir);
        var path = VolumePath(key);

        var sizeStr = $"{_opts.DiskQuotaBytes}";

        // 用 sh -c 让 shell 处理引号和参数拆分。Process.Start(UseShellExecute=false)
        // 不会自己拆参数,也不会处理引号,直接传会被当成完整字符串。
        // 1. truncate 创建稀疏文件(秒建,实际占 0 字节)
        await RunShAsync($"truncate -s {sizeStr} {ShellQuote(path)}", ct);

        // 2. mkfs.ext4 格式化
        await RunShAsync($"mkfs.ext4 -F -q -E lazy_itable_init=1,lazy_journal_init=1 {ShellQuote(path)}", ct);

        _logger.LogInformation("已创建 loop 卷 {Path}(大小 {Size} 字节)", path, _opts.DiskQuotaBytes);
    }

    /// <summary>删除 loop 文件(销毁容器时调)。文件不存在不报错。</summary>
    public Task RemoveVolumeAsync(string key, CancellationToken ct = default)
    {
        var path = VolumePath(key);
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
                _logger.LogInformation("已删除 loop 卷 {Path}", path);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "删除 loop 卷 {Path} 失败", path);
        }
        return Task.CompletedTask;
    }

    // ---------- 兜底:扫描容器可写层大小 ----------

    /// <summary>
    /// 扫描所有 docker overlay2 容器可写层大小,返回 [容器ID → 字节数]。
    /// 用 du 比 docker API 更可靠(API 需要开 --size)。
    /// </summary>
    public async Task<Dictionary<string, long>> ScanContainerDiskUsageAsync(CancellationToken ct = default)
    {
        var result = new Dictionary<string, long>();
        if (!Directory.Exists(_opts.DockerOverlayDir))
        {
            _logger.LogWarning("docker overlay 目录不存在:{Dir}", _opts.DockerOverlayDir);
            return result;
        }

        // 跑 du 一次性扫描所有 diff 目录(用 sh -c 让 * glob 展开)
        // 输出格式:字节数 + tab + 路径(如 /var/lib/docker/overlay2/{id}/diff)
        var (exit, stdout, stderr) = await RunShCaptureAsync(
            $"du -sb {ShellQuote(_opts.DockerOverlayDir + "/")}*/diff 2>/dev/null || true",
            ct);

        if (exit != 0)
        {
            _logger.LogWarning("du 扫描失败(exit={Exit}):{Err}", exit, stderr);
            return result;
        }

        foreach (var line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split(new[] { '\t', ' ' }, 2);
            if (parts.Length < 2) continue;
            if (!long.TryParse(parts[0], out var bytes)) continue;

            // 从路径提取容器 ID:/var/lib/docker/overlay2/{id}/diff
            var path = parts[1].TrimEnd('/');
            var dirName = Path.GetFileName(path);           // diff
            var parent = Path.GetDirectoryName(path);       // .../{id}
            if (dirName != "diff" || parent is null) continue;
            var hash = Path.GetFileName(parent);
            result[hash] = bytes;
        }

        return result;
    }

    // ---------- 工具 ----------

    /// <summary>用 sh -c 执行命令(让 shell 处理引号 + glob)。失败抛异常。</summary>
    private async Task RunShAsync(string shellCommand, CancellationToken ct)
    {
        var (exit, _, stderr) = await RunShCaptureAsync(shellCommand, ct);
        if (exit != 0)
        {
            throw new InvalidOperationException($"`{shellCommand}` 失败(exit={exit}):{stderr}");
        }
    }

    /// <summary>用 sh -c 执行命令,捕获输出。不抛异常(由调用方判断 exit code)。</summary>
    private async Task<(int exit, string stdout, string stderr)> RunShCaptureAsync(
        string shellCommand, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "/bin/sh",
            ArgumentList = { "-c", shellCommand },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var p = Process.Start(psi);
        if (p is null) return (-1, "", $"无法启动 /bin/sh");
        var stdoutTask = p.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = p.StandardError.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct);
        return (p.ExitCode, await stdoutTask, await stderrTask);
    }

    /// <summary>POSIX shell 引用:整个串用单引号包,内部单引号转义。</summary>
    private static string ShellQuote(string s) => "'" + s.Replace("'", "'\\''") + "'";
}
