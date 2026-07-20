using DockerVm.Data;
using DockerVm.Dtos;
using DockerVm.Models;
using DockerVm.Options;
using DockerVm.Services;
using Microsoft.EntityFrameworkCore;

namespace DockerVm.Endpoints;

public static class VmEndpoints
{
    public static IEndpointRouteBuilder MapVmEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/api/vm").WithTags("vm");

        // 创建一台新机器
        grp.MapPost("/", async (
            AppDbContext db,
            IDockerService docker,
            PortAllocator ports,
            AppOptions opts,
            CancellationToken ct) =>
        {
            // IP 探测只在每次请求时做一次,实际开销极小
            var ip = HostIpDetector.Detect(opts.HostIp);

            var key = Guid.NewGuid().ToString("N");
            var username = opts.SshUser;
            var password = PasswordGenerator.Generate(16);

            var port = await ports.AllocateAsync(ct);

            VmContainer container;
            try
            {
                container = await docker.CreateContainerAsync(key, port, username, password, ct);
            }
            catch (Exception)
            {
                // 容器创建失败,不写库
                throw;
            }

            db.Containers.Add(container);
            await db.SaveChangesAsync(ct);

            return Results.Ok(VmDto.From(container, ip));
        });

        // 列出"我的容器"——通过 X-VM-Key header 传 key 列表(逗号分隔)
        grp.MapGet("/", async (
            HttpRequest req,
            AppDbContext db,
            IDockerService docker,
            AppOptions opts,
            CancellationToken ct) =>
        {
            var keysRaw = req.Headers["X-VM-Key"].ToString();
            if (string.IsNullOrWhiteSpace(keysRaw))
            {
                return Results.Ok(Array.Empty<VmDto>());
            }

            var keys = keysRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var list = await db.Containers
                .Where(c => keys.Contains(c.Key))
                .ToListAsync(ct);

            var ip = HostIpDetector.Detect(opts.HostIp);
            // 刷新状态
            foreach (var c in list)
            {
                c.Status = await docker.GetStatusAsync(c.ContainerId, ct);
                if (c.Status is "exited" or "dead" or "missing" && c.StoppedAt is null)
                {
                    c.StoppedAt = DateTime.UtcNow;
                }
            }
            await db.SaveChangesAsync(ct);

            return Results.Ok(list.Select(c => VmDto.From(c, ip)).ToList());
        });

        // 查询单台详情
        grp.MapGet("/{key}", async (
            string key,
            AppDbContext db,
            IDockerService docker,
            AppOptions opts,
            CancellationToken ct) =>
        {
            var c = await db.Containers.FindAsync(new object?[] { key }, ct);
            if (c is null)
            {
                return Results.NotFound(new { error = "容器不存在或已被销毁" });
            }

            c.Status = await docker.GetStatusAsync(c.ContainerId, ct);
            await db.SaveChangesAsync(ct);

            return Results.Ok(VmDto.From(c, HostIpDetector.Detect(opts.HostIp)));
        });

        // 自助销毁
        grp.MapDelete("/{key}", async (
            string key,
            AppDbContext db,
            IDockerService docker,
            CancellationToken ct) =>
        {
            var c = await db.Containers.FindAsync(new object?[] { key }, ct);
            if (c is null)
            {
                return Results.NotFound(new { error = "容器不存在或已被销毁" });
            }

            await docker.RemoveContainerAsync(c.ContainerId, ct);
            db.Containers.Remove(c);
            await db.SaveChangesAsync(ct);

            return Results.Ok(new { ok = true });
        });

        return app;
    }
}

/// <summary>生成符合 SSH 安全要求的随机密码。</summary>
file static class PasswordGenerator
{
    private const string Upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Lower = "abcdefghijkmnopqrstuvwxyz";
    private const string Digit = "23456789";
    private const string Symbol = "!@#$%^&*-_=+";
    private static readonly char[] All = (Upper + Lower + Digit + Symbol).ToCharArray();
    private static readonly Random Rng = Random.Shared;

    public static string Generate(int length)
    {
        var chars = new char[length];
        // 保证每类至少 1 个
        chars[0] = Upper[Rng.Next(Upper.Length)];
        chars[1] = Lower[Rng.Next(Lower.Length)];
        chars[2] = Digit[Rng.Next(Digit.Length)];
        chars[3] = Symbol[Rng.Next(Symbol.Length)];
        for (var i = 4; i < length; i++)
        {
            chars[i] = All[Rng.Next(All.Length)];
        }
        // 洗牌
        for (var i = chars.Length - 1; i > 0; i--)
        {
            var j = Rng.Next(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }
        return new string(chars);
    }
}
