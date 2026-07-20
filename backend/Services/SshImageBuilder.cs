using Docker.DotNet;
using Docker.DotNet.Models;

namespace DockerVm.Services;

/// <summary>
/// 后端启动时检测并构建 Alpine SSH 镜像,确保用户只需一条命令即可部署。
/// </summary>
public class SshImageBuilder
{
    private readonly IDockerClient _docker;
    private readonly string _imageName;
    private readonly string _contextDir;
    private readonly ILogger<SshImageBuilder> _logger;

    public SshImageBuilder(
        IDockerClient docker,
        string imageName,
        string contextDir,
        ILogger<SshImageBuilder> logger)
    {
        _docker = docker;
        _imageName = imageName;
        _contextDir = contextDir;
        _logger = logger;
    }

    public async Task EnsureImageAsync(CancellationToken ct = default)
    {
        // 已存在则跳过
        try
        {
            await _docker.Images.InspectImageAsync(_imageName, ct);
            _logger.LogInformation("SSH 镜像已存在:{Image}", _imageName);
            return;
        }
        catch (DockerImageNotFoundException)
        {
            // 继续构建
        }

        if (!Directory.Exists(_contextDir))
        {
            throw new DirectoryNotFoundException($"SSH 镜像构建目录不存在:{_contextDir}");
        }

        _logger.LogInformation("开始构建 SSH 镜像 {Image}(context={Ctx})", _imageName, _contextDir);

        var progress = new Progress<JSONMessage>(msg =>
        {
            if (!string.IsNullOrWhiteSpace(msg.Stream))
            {
                _logger.LogInformation("[build] {Line}", msg.Stream.TrimEnd());
            }
            if (msg.ErrorMessage != null)
            {
                _logger.LogError("[build error] {Err}", msg.ErrorMessage);
            }
        });

        // Tar 打包 context 目录,Docker.DotNet 需要 tarball
        using var tarStream = TarHelper.CreateFromDirectory(_contextDir);
        await _docker.Images.BuildImageFromDockerfileAsync(
            new ImageBuildParameters
            {
                Tags = new List<string> { _imageName },
                Remove = true,
                ForceRemove = true,
            },
            tarStream,
            null,
            null,
            progress,
            ct);

        _logger.LogInformation("SSH 镜像构建完成:{Image}", _imageName);
    }
}
