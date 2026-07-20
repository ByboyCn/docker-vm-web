namespace DockerVm.Services;

/// <summary>
/// 探测宿主 LXCFS 是否就绪。就绪条件:目录存在且里面有 /proc 相关文件。
/// </summary>
public static class LxcfsProbe
{
    private static readonly string[] MarkerFiles = { "meminfo", "cpuinfo", "loadavg", "stat" };

    public static bool IsAvailable(string lxcfsProcDir)
    {
        if (string.IsNullOrWhiteSpace(lxcfsProcDir))
        {
            return false;
        }
        if (!Directory.Exists(lxcfsProcDir))
        {
            return false;
        }
        // 至少有几个 /proc 关键文件才能确认是 lxcfs 挂载点
        var hits = 0;
        foreach (var name in MarkerFiles)
        {
            if (File.Exists(Path.Combine(lxcfsProcDir, name)))
            {
                hits++;
            }
        }
        return hits >= 2;
    }
}
