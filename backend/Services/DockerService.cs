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
                // 把资源配额透传给容器,供 entrypoint 的包装脚本使用
                // (cgroup 路径在不同环境下不一致,环境变量最可靠)
                $"VM_CPU_CORES={_opts.VmCpuCores}",
                $"VM_MEMORY_MB={_opts.VmMemoryMB}",
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

                // ---------- LXCFS(可选,让容器内 /proc 反映实际配额)----------
                // 仅在宿主 lxcfs 可用时挂载;否则保持空,容器内 /proc 看到的是宿主真实资源
                Binds = BuildBinds(),

                // ---------- 安全:容器内非 root 拿不到真实宿主信息 ----------
                ReadonlyRootfs = false,   // true 会破坏 sshd 写 host key,保持 false
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
        catch (DockerApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Docker.DotNet 在容器不存在时通常抛 DockerApiException(404),而不是
            // DockerContainerNotFoundException —— 这里统一兜底成 "missing"
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
        catch (DockerApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // 同上,Docker.DotNet 的 404 兜底
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

    /// <summary>
    /// 构造 bind mounts:LXCFS(可选)。
    /// 不再 bind loop 文件到 /home —— docker 不支持文件 → 目录 bind。
    /// 磁盘配额靠 storage-opt(需要宿主 xfs pquota 或 ext4 quota)+ 后台扫描兜底。
    /// </summary>
    private List<string> BuildBinds()
    {
        var binds = new List<string>();

        // LXCFS /proc 文件(可选)
        if (_opts.LxcfsActuallyEnabled)
        {
            binds.AddRange(new[]
            {
                $"{_opts.LxcfsProcDir}/meminfo:/proc/meminfo:ro",
                $"{_opts.LxcfsProcDir}/cpuinfo:/proc/cpuinfo:ro",
                $"{_opts.LxcfsProcDir}/loadavg:/proc/loadavg:ro",
                $"{_opts.LxcfsProcDir}/stat:/proc/stat:ro",
                $"{_opts.LxcfsProcDir}/uptime:/proc/uptime:ro",
                $"{_opts.LxcfsProcDir}/diskstats:/proc/diskstats:ro",
                $"{_opts.LxcfsProcDir}/swaps:/proc/swaps:ro",
            });
        }

        return binds;
    }
}
