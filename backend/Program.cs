using System.Net;
using Docker.DotNet;
using DockerVm.Data;
using DockerVm.Endpoints;
using DockerVm.Middleware;
using DockerVm.Models;
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
opts.InitialAdminUsername = Environment.GetEnvironmentVariable("INITIAL_ADMIN_USERNAME") ?? opts.InitialAdminUsername;
opts.InitialAdminPassword = Environment.GetEnvironmentVariable("INITIAL_ADMIN_PASSWORD") ?? opts.InitialAdminPassword;
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
var dockerHost = Environment.GetEnvironmentVariable("DOCKER_HOST") ?? "unix:///var/run/docker.sock";
builder.Services.AddSingleton<IDockerClient>(_ =>
{
    var config = new DockerClientConfiguration(new Uri(dockerHost));
    return config.CreateClient();
});

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<PortAllocator>(sp => new PortAllocator(
    sp.GetRequiredService<AppDbContext>(), opts.PortMin, opts.PortMax));
builder.Services.AddScoped<IDockerService, DockerService>();

builder.Services.AddSingleton<SshImageBuilder>(sp => new SshImageBuilder(
    sp.GetRequiredService<IDockerClient>(),
    opts.SshImageName,
    opts.SshImageContextDir,
    sp.GetRequiredService<ILogger<SshImageBuilder>>()));
builder.Services.AddHostedService<StartupInitializer>();

// CORS:允许前端跨域带 cookie
builder.Services.AddCors(o =>
{
    var origins = opts.CorsOrigins.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    o.AddDefaultPolicy(p =>
    {
        if (origins.Contains("*"))
        {
            // 带 cookie 时不能用 *,降级为允许任意来源但不带凭证
            p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
        }
        else
        {
            p.WithOrigins(origins).AllowAnyMethod().AllowAnyHeader().AllowCredentials();
        }
    });
});

var app = builder.Build();

app.UseCors();
app.UseRouting();
app.UseMiddleware<AuthMiddleware>();   // 解析 cookie → 当前用户

// 自动建库建表
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

app.MapGet("/api/health", () => Results.Ok(new { ok = true }));

app.MapAuthEndpoints();
app.MapVmEndpoints();
app.MapAdminEndpoints();

app.Run();

// ---------- 启动初始化:自动构建 SSH 镜像 + 首启建 admin ----------
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
        var sp = scope.ServiceProvider;
        var opts = sp.GetRequiredService<AppOptions>();

        // 1. 构建 SSH 镜像
        try
        {
            var imageBuilder = sp.GetRequiredService<SshImageBuilder>();
            await imageBuilder.EnsureImageAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "启动时构建 SSH 镜像失败。请检查镜像目录 {Dir} 是否正确挂载。",
                opts.SshImageContextDir);
        }

        // 2. 首启建初始管理员
        try
        {
            var db = sp.GetRequiredService<AppDbContext>();
            if (!await db.Users.AnyAsync(cancellationToken))
            {
                var (hash, salt) = PasswordHasher.Hash(opts.InitialAdminPassword);
                db.Users.Add(new User
                {
                    Username = opts.InitialAdminUsername,
                    PasswordHash = hash,
                    PasswordSalt = salt,
                    IsAdmin = true,
                    CreatedAt = DateTime.UtcNow,
                });
                await db.SaveChangesAsync(cancellationToken);
                _logger.LogInformation("已创建初始管理员账号:{User}(请尽快登录修改密码)", opts.InitialAdminUsername);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "创建初始管理员失败");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
