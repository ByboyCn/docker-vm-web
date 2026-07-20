using DockerVm.Data;
using DockerVm.Dtos;
using DockerVm.Options;
using DockerVm.Services;
using Microsoft.EntityFrameworkCore;

namespace DockerVm.Endpoints;

public static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/api/admin").WithTags("admin");

        // 列出全部容器(需管理员)
        grp.MapGet("/containers", async (
            HttpContext ctx,
            AppDbContext db,
            IDockerService docker,
            AppOptions o,
            CancellationToken ct) =>
        {
            if (!ctx.RequireAdmin(out var _))
            {
                return ctx.Response.StatusCode == 401
                    ? Results.Json(new { error = "未登录或会话已过期" }, statusCode: 401)
                    : Results.Json(new { error = "需要管理员权限" }, statusCode: 403);
            }

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

        // 强制销毁单个(需管理员)
        grp.MapDelete("/containers/{key}", async (
            string key,
            HttpContext ctx,
            AppDbContext db,
            IDockerService docker,
            CancellationToken ct) =>
        {
            if (!ctx.RequireAdmin(out var _))
            {
                return ctx.Response.StatusCode == 401
                    ? Results.Json(new { error = "未登录或会话已过期" }, statusCode: 401)
                    : Results.Json(new { error = "需要管理员权限" }, statusCode: 403);
            }

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

        // 清理孤儿:数据库有记录但 docker 已不存在的(需管理员)
        grp.MapPost("/cleanup-orphans", async (
            HttpContext ctx,
            AppDbContext db,
            IDockerService docker,
            CancellationToken ct) =>
        {
            if (!ctx.RequireAdmin(out var _))
            {
                return ctx.Response.StatusCode == 401
                    ? Results.Json(new { error = "未登录或会话已过期" }, statusCode: 401)
                    : Results.Json(new { error = "需要管理员权限" }, statusCode: 403);
            }

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

        // 列出所有用户(需管理员)
        grp.MapGet("/users", async (
            HttpContext ctx,
            AppDbContext db,
            CancellationToken ct) =>
        {
            if (!ctx.RequireAdmin(out var _))
            {
                return ctx.Response.StatusCode == 401
                    ? Results.Json(new { error = "未登录或会话已过期" }, statusCode: 401)
                    : Results.Json(new { error = "需要管理员权限" }, statusCode: 403);
            }

            var users = await db.Users
                .Select(u => new { u.Id, u.Username, u.IsAdmin, u.CreatedAt })
                .ToListAsync(ct);

            // 顺便统计每人容器数
            var counts = await db.Containers
                .GroupBy(c => c.UserId)
                .Select(g => new { UserId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.UserId, x => x.Count, ct);

            var items = users.Select(u => new
            {
                u.Id,
                u.Username,
                u.IsAdmin,
                u.CreatedAt,
                containerCount = counts.GetValueOrDefault(u.Id, 0),
            });

            return Results.Ok(new { items });
        });

        return app;
    }
}
