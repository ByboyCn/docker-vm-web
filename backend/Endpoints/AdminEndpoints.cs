using DockerVm.Data;
using DockerVm.Dtos;
using DockerVm.Options;
using DockerVm.Services;
using Microsoft.EntityFrameworkCore;

namespace DockerVm.Endpoints;

public static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app, AppOptions opts)
    {
        var grp = app.MapGroup("/api/admin").WithTags("admin").AddEndpointFilter(async (ctx, next) =>
        {
            // 校验 Bearer token
            var auth = ctx.HttpContext.Request.Headers.Authorization.ToString();
            if (!auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                || !CryptographicEquals(auth["Bearer ".Length..].Trim(), opts.AdminToken))
            {
                return Results.Unauthorized();
            }
            return await next(ctx);
        });

        // 列出全部容器
        grp.MapGet("/containers", async (
            AppDbContext db,
            IDockerService docker,
            AppOptions o,
            CancellationToken ct) =>
        {
            var list = await db.Containers.AsNoTracking().ToListAsync(ct);
            var ip = HostIpDetector.Detect(o.HostIp);
            var result = new List<VmDto>();
            foreach (var c in list)
            {
                c.Status = await docker.GetStatusAsync(c.ContainerId, ct);
                result.Add(VmDto.From(c, ip));
            }
            return Results.Ok(new
            {
                total = result.Count,
                running = result.Count(x => x.Status == "running"),
                items = result,
            });
        });

        // 强制销毁单个
        grp.MapDelete("/containers/{key}", async (
            string key,
            AppDbContext db,
            IDockerService docker,
            CancellationToken ct) =>
        {
            var c = await db.Containers.FindAsync(new object?[] { key }, ct);
            if (c is null)
            {
                return Results.NotFound(new { error = "容器不存在" });
            }
            await docker.RemoveContainerAsync(c.ContainerId, ct);
            db.Containers.Remove(c);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { ok = true });
        });

        // 清理孤儿:数据库有记录但 docker 已不存在的
        grp.MapPost("/cleanup-orphans", async (
            AppDbContext db,
            IDockerService docker,
            CancellationToken ct) =>
        {
            var all = await db.Containers.ToListAsync(ct);
            var removed = new List<string>();
            foreach (var c in all)
            {
                var status = await docker.GetStatusAsync(c.ContainerId, ct);
                if (status == "missing")
                {
                    db.Containers.Remove(c);
                    removed.Add(c.Key);
                }
            }
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { ok = true, removed });
        });

        return app;
    }

    private static bool CryptographicEquals(string a, string b)
    {
        var ab = System.Text.Encoding.UTF8.GetBytes(a ?? "");
        var bb = System.Text.Encoding.UTF8.GetBytes(b ?? "");
        if (ab.Length != bb.Length)
        {
            return false;
        }
        var diff = 0;
        for (var i = 0; i < ab.Length; i++)
        {
            diff |= ab[i] ^ bb[i];
        }
        return diff == 0;
    }
}
