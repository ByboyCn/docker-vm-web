namespace DockerVm.Options;

/// <summary>
/// 应用配置,从环境变量加载。所有字段都有默认值,可在 .env 覆盖。
/// </summary>
public class AppOptions
{
    /// <summary>返回给用户的 IP。留空则自动探测宿主机 IP。</summary>
    public string? HostIp { get; set; }

    /// <summary>SSH 宿主端口范围下限。</summary>
    public int PortMin { get; set; } = 20000;

    /// <summary>SSH 宿主端口范围上限。</summary>
    public int PortMax { get; set; } = 30000;

    /// <summary>管理后台访问 token。(已废弃,改用登录 + IsAdmin。保留字段向后兼容旧 .env。)</summary>
    public string AdminToken { get; set; } = "change-me-to-random-string";

    /// <summary>初始管理员用户名。仅首启 Users 表为空时创建一次。</summary>
    public string InitialAdminUsername { get; set; } = "admin";

    /// <summary>初始管理员密码。⚠️ 上线必改。</summary>
    public string InitialAdminPassword { get; set; } = "change-me-please";

    /// <summary>容器内创建的 SSH 用户名。</summary>
    public string SshUser { get; set; } = "user";

    /// <summary>Alpine SSH 镜像名(后端启动时自动 build)。</summary>
    public string SshImageName { get; set; } = "docker-vm-alpine:latest";

    /// <summary>放置镜像 Dockerfile 的目录(容器内路径)。</summary>
    public string SshImageContextDir { get; set; } = "/app/image";

    /// <summary>允许跨域的来源(逗号分隔),默认 *。</summary>
    public string CorsOrigins { get; set; } = "*";

    // ---------- 容器资源限制 ----------

    /// <summary>每台容器 CPU 核数(可小数)。</summary>
    public double VmCpuCores { get; set; } = 1.0;

    /// <summary>每台容器内存上限(MB)。</summary>
    public int VmMemoryMB { get; set; } = 1024;

    /// <summary>每台容器进程/线程数上限。</summary>
    public long VmPidsLimit { get; set; } = 200;

    /// <summary>每台容器磁盘配额(如 "5g")。依赖宿主 xfs/ext4 quota,不生效时不报错。</summary>
    public string VmDiskSize { get; set; } = "5g";

    // ---------- 名额池 ----------

    /// <summary>首启初始化的全局名额总额度。</summary>
    public int QuotaInitialTotal { get; set; } = 5;
}
