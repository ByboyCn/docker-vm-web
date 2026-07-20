using Docker.DotNet;
using Docker.DotNet.Models;
using DockerVm.Data;
using DockerVm.Models;
using DockerVm.Options;
using Microsoft.EntityFrameworkCore;

namespace DockerVm.Services;

public class DockerService : IDockerService
{
    private readonly IDockerClient _docker;
    private readonly AppOptions _opts;
    private readonly ILogger<DockerService> _logger;

    public DockerService(IDockerClient docker, AppOptions opts, ILogger<DockerService> logger)
    {
        _docker = docker;
        _opts = opts;
        _logger = logger;
    }

    public async Task<VmContainer> CreateContainerAsync(
        string key,
        int hostPort,
        string username,
        string password,
        CancellationToken ct = default)
    {
        var name = $"vm-{key[..Math.Min(8, key.Length)]}";

        var createParams = new CreateContainerParameters
        {
            Image = _opts.SshImageName,
            Name = name,
            Hostname = name,
            Env = new List<string>
            {
                $"SSH_USER={username}",
                $"SSH_PASSWORD={password}",
            },
            ExposedPorts = new Dictionary<string, EmptyStruct>
            {
                ["22/tcp"] = new EmptyStruct(),
            },
            HostConfig = new HostConfig
            {
                PortBindings = new Dictionary<string, IList<PortBinding>>
                {
                    ["22/tcp"] = new List<PortBinding>
                    {
                        new() { HostPort = hostPort.ToString() },
                    },
                },
                RestartPolicy = new RestartPolicy { Name = RestartPolicyKind.No },

                // ---------- 资源限制 ----------
                // CPU:NanoCPUs = 核数 * 1e9(1 核 = 10 亿纳秒)
                NanoCPUs = (long)(_opts.VmCpuCores * 1_000_000_000),
                // 内存上限
                Memory = (long)_opts.VmMemoryMB * 1024 * 1024,
                // 进程/线程数上限,防 fork 炸弹
                PidsLimit = _opts.VmPidsLimit,
                // 磁盘配额(依赖宿主 xfs/ext4 quota + overlay2,不生效时不报错)
                StorageOpt = new Dictionary<string, string>
                {
                    ["size"] = _opts.VmDiskSize,
                },
            },
            Labels = new Dictionary<string, string>
            {
                ["vm.managed"] = "true",
                ["vm.key"] = key,
            },
        };

        var created = await _docker.Containers.CreateContainerAsync(createParams, ct);
        await _docker.Containers.StartContainerAsync(created.ID, new ContainerStartParameters(), ct);

        _logger.LogInformation("已创建并启动容器 {Name}(id={Id}, port={Port})", name, created.ID[..12], hostPort);

        return new VmContainer
        {
            Key = key,
            ContainerId = created.ID,
            HostPort = hostPort,
            Username = username,
            Password = password,
            ContainerName = name,
            Status = "running",
            CreatedAt = DateTime.UtcNow,
        };
    }

    public async Task StartContainerAsync(string containerId, CancellationToken ct = default)
    {
        await _docker.Containers.StartContainerAsync(containerId, new ContainerStartParameters(), ct);
    }

    public async Task<string> GetStatusAsync(string containerId, CancellationToken ct = default)
    {
        try
        {
            var info = await _docker.Containers.InspectContainerAsync(containerId, ct);
            return info.State.Status ?? "unknown";
        }
        catch (DockerContainerNotFoundException)
        {
            return "missing";
        }
    }

    public async Task RemoveContainerAsync(string containerId, CancellationToken ct = default)
    {
        try
        {
            await _docker.Containers.RemoveContainerAsync(
                containerId,
                new ContainerRemoveParameters { Force = true, RemoveVolumes = true },
                ct);
        }
        catch (DockerContainerNotFoundException)
        {
            // 容器已不存在,视作成功
        }
    }

    public async Task<IReadOnlyList<VmContainer>> ListManagedAsync(AppDbContext db, CancellationToken ct = default)
    {
        // 数据库为准;同时刷新每条记录的状态
        var list = await db.Containers.ToListAsync(ct);
        foreach (var item in list)
        {
            item.Status = await GetStatusAsync(item.ContainerId, ct);
        }
        return list;
    }
}
