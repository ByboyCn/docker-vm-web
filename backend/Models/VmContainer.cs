using System.ComponentModel.DataAnnotations;

namespace DockerVm.Models;

/// <summary>
/// 一台被本系统管理的 Docker 虚拟机记录。
/// </summary>
public class VmContainer
{
    [Key]
    public string Key { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>所属用户 Id。</summary>
    [MaxLength(64)]
    public string UserId { get; set; } = string.Empty;

    /// <summary>本机消耗的名额来源:"global"(全局池) 或 "bonus"(用户加量)。仅记录,销毁不退。</summary>
    [MaxLength(16)]
    public string ConsumedFrom { get; set; } = "global";

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
