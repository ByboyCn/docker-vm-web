using System.ComponentModel.DataAnnotations;

namespace DockerVm.Models;

/// <summary>
/// 一台被本系统管理的 Docker 虚拟机记录。
/// </summary>
public class VmContainer
{
    [Key]
    public string Key { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Docker 容器 ID。</summary>
    public string ContainerId { get; set; } = string.Empty;

    /// <summary>对宿主机暴露的 SSH 端口。</summary>
    public int HostPort { get; set; }

    /// <summary>容器内创建的 SSH 用户名。</summary>
    public string Username { get; set; } = "user";

    /// <summary>SSH 密码(明文存储,用户可凭 key 再次查看)。</summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>Docker 容器名。</summary>
    public string ContainerName { get; set; } = string.Empty;

    /// <summary>最近一次观测到的容器状态(running / exited 等)。</summary>
    public string Status { get; set; } = "running";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? StoppedAt { get; set; }
}
