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

        // 创建一台新机器(需登录)
        grp.MapPost("/", async (
            HttpContext ctx,
            AppDbContext db,
            IDockerService docker,
            PortAllocator ports,
            AppOptions opts,
            CancellationToken ct) =>
        {
            if (!ctx.RequireUser(out var user))
            {
                return Results.Json(new { error = "未登录或会话已过期" }, statusCode: 401);
            }

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
            catch (Exception ex)
            {
                return Results.Json(new { error = "容器创建失败:" + ex.Message }, statusCode: 500);
            }

            container.UserId = user!.Id;
            db.Containers.Add(container);
            await db.SaveChangesAsync(ct);

            return Results.Ok(VmDto.From(container, ip));
        });

        // 列出我的容器(需登录,只返回当前用户的)
        grp.MapGet("/", async (
            HttpContext ctx,
            AppDbContext db,
            IDockerService docker,
            AppOptions opts,
            CancellationToken ct) =>
        {
            if (!ctx.RequireUser(out var user))
            {
                return Results.Json(new { error = "未登录或会话已过期" }, statusCode: 401);
            }

            var list = await db.Containers
                .Where(c => c.UserId == user!.Id)
                .ToListAsync(ct);

            var ip = HostIpDetector.Detect(opts.HostIp);
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

        // 查询单台详情(需登录 + 归属校验)
        grp.MapGet("/{key}", async (
            string key,
            HttpContext ctx,
            AppDbContext db,
            IDockerService docker,
            AppOptions opts,
            CancellationToken ct) =>
        {
            if (!ctx.RequireUser(out var user))
            {
                return Results.Json(new { error = "未登录或会话已过期" }, statusCode: 401);
            }

            var c = await db.Containers.FindAsync(new object?[] { key }, ct);
            if (c is null || c.UserId != user!.Id)
            {
                return Results.NotFound(new { error = "容器不存在或无权访问" });
            }

            c.Status = await docker.GetStatusAsync(c.ContainerId, ct);
            await db.SaveChangesAsync(ct);
            return Results.Ok(VmDto.From(c, HostIpDetector.Detect(opts.HostIp)));
        });

        // 自助销毁(需登录 + 归属校验)
        grp.MapDelete("/{key}", async (
            string key,
            HttpContext ctx,
            AppDbContext db,
            IDockerService docker,
            CancellationToken ct) =>
        {
            if (!ctx.RequireUser(out var user))
            {
                return Results.Json(new { error = "未登录或会话已过期" }, statusCode: 401);
            }

            var c = await db.Containers.FindAsync(new object?[] { key }, ct);
            if (c is null || c.UserId != user!.Id)
            {
                return Results.NotFound(new { error = "容器不存在或无权访问" });
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
        chars[0] = Upper[Rng.Next(Upper.Length)];
        chars[1] = Lower[Rng.Next(Lower.Length)];
        chars[2] = Digit[Rng.Next(Digit.Length)];
        chars[3] = Symbol[Rng.Next(Symbol.Length)];
        for (var i = 4; i < length; i++)
        {
            chars[i] = All[Rng.Next(All.Length)];
        }
        for (var i = chars.Length - 1; i > 0; i--)
        {
            var j = Rng.Next(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }
        return new string(chars);
    }
}
