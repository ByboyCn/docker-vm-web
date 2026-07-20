using DockerVm.Models;

namespace DockerVm.Dtos;

/// <summary>返回给前端的连接信息。</summary>
public record VmDto
{
    public string Key { get; init; } = "";
    public string Ip { get; init; } = "";
    public int Port { get; init; }
    public string Username { get; init; } = "";
    public string Password { get; init; } = "";
    public string ContainerName { get; init; } = "";
    public string Status { get; init; } = "";
    public DateTime CreatedAt { get; init; }
    public DateTime? StoppedAt { get; init; }

    public static VmDto From(VmContainer c, string ip) => new()
    {
        Key = c.Key,
        Ip = ip,
        Port = c.HostPort,
        Username = c.Username,
        Password = c.Password,
        ContainerName = c.ContainerName,
        Status = c.Status,
        CreatedAt = c.CreatedAt,
        StoppedAt = c.StoppedAt,
    };
}
