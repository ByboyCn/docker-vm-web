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

    /// <summary>管理后台访问 token。</summary>
    public string AdminToken { get; set; } = "change-me-to-random-string";

    /// <summary>容器内创建的 SSH 用户名。</summary>
    public string SshUser { get; set; } = "user";

    /// <summary>Alpine SSH 镜像名(后端启动时自动 build)。</summary>
    public string SshImageName { get; set; } = "docker-vm-alpine:latest";

    /// <summary>放置镜像 Dockerfile 的目录(容器内路径)。</summary>
    public string SshImageContextDir { get; set; } = "/app/image";

    /// <summary>允许跨域的来源(逗号分隔),默认 *。</summary>
    public string CorsOrigins { get; set; } = "*";
}
