using System.Net;
using Docker.DotNet;
using DockerVm.Data;
using DockerVm.Endpoints;
using DockerVm.Options;
using DockerVm.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ---------- 配置 ----------
var opts = new AppOptions();
opts.HostIp = Environment.GetEnvironmentVariable("HOST_IP");
_ = int.TryParse(Environment.GetEnvironmentVariable("PORT_MIN"), out var pmin) ? pmin : opts.PortMin;
_ = int.TryParse(Environment.GetEnvironmentVariable("PORT_MAX"), out var pmax) ? pmax : opts.PortMax;
opts.PortMin = pmin;
opts.PortMax = pmax;
opts.AdminToken = Environment.GetEnvironmentVariable("ADMIN_TOKEN") ?? opts.AdminToken;
opts.SshUser = Environment.GetEnvironmentVariable("SSH_USER") ?? opts.SshUser;
opts.SshImageName = Environment.GetEnvironmentVariable("SSH_IMAGE_NAME") ?? opts.SshImageName;
opts.SshImageContextDir = Environment.GetEnvironmentVariable("SSH_IMAGE_CONTEXT_DIR") ?? opts.SshImageContextDir;
opts.CorsOrigins = Environment.GetEnvironmentVariable("CORS_ORIGINS") ?? opts.CorsOrigins;

builder.Services.AddSingleton(opts);

// ---------- 数据库 ----------
var dbPath = Environment.GetEnvironmentVariable("DB_PATH") ?? "/data/docker-vm.db";
var dbDir = Path.GetDirectoryName(dbPath);
if (!string.IsNullOrEmpty(dbDir) && !Directory.Exists(dbDir))
{
    Directory.CreateDirectory(dbDir);
}

builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseSqlite($"Data Source={dbPath}"));

// ---------- Docker 客户端 ----------
// 默认连 unix socket,挂载 /var/run/docker.sock;也支持 DOCKER_HOST=tcp://...
var dockerHost = Environment.GetEnvironmentVariable("DOCKER_HOST") ?? "unix:///var/run/docker.sock";
builder.Services.AddSingleton<IDockerClient>(_ =>
{
    var config = new DockerClientConfiguration(new Uri(dockerHost));
    return config.CreateClient();
});

builder.Services.AddScoped<PortAllocator>(sp => new PortAllocator(
    sp.GetRequiredService<AppDbContext>(), opts.PortMin, opts.PortMax));
builder.Services.AddScoped<IDockerService, DockerService>();

builder.Services.AddCors(o =>
{
    var origins = opts.CorsOrigins.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    o.AddDefaultPolicy(p =>
    {
        if (origins.Contains("*"))
        {
            p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
        }
        else
        {
            p.WithOrigins(origins).AllowAnyMethod().AllowAnyHeader().AllowCredentials();
        }
    });
});

builder.Services.AddSingleton<SshImageBuilder>(sp => new SshImageBuilder(
    sp.GetRequiredService<IDockerClient>(),
    opts.SshImageName,
    opts.SshImageContextDir,
    sp.GetRequiredService<ILogger<SshImageBuilder>>()));
builder.Services.AddHostedService<StartupInitializer>();

var app = builder.Build();

app.UseCors();
app.UseRouting();

// 自动建库建表(无需 EF migration)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

app.MapGet("/api/health", () => Results.Ok(new { ok = true }));

app.MapVmEndpoints();
app.MapAdminEndpoints(opts);

app.Run();

// ---------- 启动初始化:自动构建 SSH 镜像 ----------
internal sealed class StartupInitializer : IHostedService
{
    private readonly IServiceProvider _sp;
    private readonly ILogger<StartupInitializer> _logger;

    public StartupInitializer(IServiceProvider sp, ILogger<StartupInitializer> logger)
    {
        _sp = sp;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _sp.CreateScope();
        var builder = scope.ServiceProvider.GetRequiredService<SshImageBuilder>();
        try
        {
            await builder.EnsureImageAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "启动时构建 SSH 镜像失败。请检查镜像目录 {Dir} 是否正确挂载。",
                scope.ServiceProvider.GetRequiredService<AppOptions>().SshImageContextDir);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
