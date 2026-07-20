using DockerVm.Data;
using DockerVm.Models;

namespace DockerVm.Services;

public interface IDockerService
{
    /// <summary>创建并启动一个 SSH 容器。</summary>
    Task<VmContainer> CreateContainerAsync(
        string key,
        int hostPort,
        string username,
        string password,
        DiskQuotaService diskQuota,
        CancellationToken ct = default);

    /// <summary>启动已停止的容器。</summary>
    Task StartContainerAsync(string containerId, CancellationToken ct = default);

    /// <summary>查询容器状态字符串(running / exited 等)。容器不存在时返回 "missing"。</summary>
    Task<string> GetStatusAsync(string containerId, CancellationToken ct = default);

    /// <summary>强制删除容器。</summary>
    Task RemoveContainerAsync(string containerId, CancellationToken ct = default);

    /// <summary>列出所有带 vm.managed=true 标签的容器。</summary>
    Task<IReadOnlyList<VmContainer>> ListManagedAsync(AppDbContext db, CancellationToken ct = default);
}
