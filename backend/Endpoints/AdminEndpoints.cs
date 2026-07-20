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

            // 顺便取每人 bonus
            var bonuses = await db.UserQuotaBonuses
                .ToDictionaryAsync(b => b.UserId, b => b.Bonus, ct);

            var items = users.Select(u => new
            {
                id = u.Id,
                username = u.Username,
                isAdmin = u.IsAdmin,
                createdAt = u.CreatedAt,
                containerCount = counts.GetValueOrDefault(u.Id, 0),
                bonus = bonuses.GetValueOrDefault(u.Id, 0),
            });

            return Results.Ok(new { items });
        });

        // ---------- 名额管理 ----------

        // 查全局配额 + 所有用户 bonus
        grp.MapGet("/quota", async (
            HttpContext ctx,
            QuotaService quota,
            AppDbContext db,
            CancellationToken ct) =>
        {
            if (!ctx.RequireAdmin(out var _))
            {
                return ctx.Response.StatusCode == 401
                    ? Results.Json(new { error = "未登录或会话已过期" }, statusCode: 401)
                    : Results.Json(new { error = "需要管理员权限" }, statusCode: 403);
            }

            var state = await quota.GetStateAsync(ct);
            var bonusRows = await quota.GetAllBonusesAsync(ct);
            var userNames = await db.Users
                .ToDictionaryAsync(u => u.Id, u => u.Username, ct);

            var userBonuses = bonusRows.Select(b => new UserBonusItem(
                b.UserId,
                userNames.GetValueOrDefault(b.UserId, "(已删除)"),
                b.Bonus,
                b.Note,
                b.UpdatedAt
            )).ToList();

            return Results.Ok(new AdminQuotaDto(
                state.Total,
                state.Used,
                state.Remaining,
                state.UpdatedAt,
                userBonuses
            ));
        });

        // 设置全局额度(可选重置 Used)
        grp.MapPut("/quota", async (
            SetQuotaRequest req,
            HttpContext ctx,
            QuotaService quota,
            CancellationToken ct) =>
        {
            if (!ctx.RequireAdmin(out var _))
            {
                return ctx.Response.StatusCode == 401
                    ? Results.Json(new { error = "未登录或会话已过期" }, statusCode: 401)
                    : Results.Json(new { error = "需要管理员权限" }, statusCode: 403);
            }

            try
            {
                var s = await quota.SetTotalAsync(req.Total, req.Used, ct);
                return Results.Ok(new { s.Total, s.Used, remaining = s.Remaining });
            }
            catch (ArgumentException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: 400);
            }
        });

        // 一键重置(Used=0,Total=给定值)
        grp.MapPost("/quota/reset", async (
            ResetQuotaRequest req,
            HttpContext ctx,
            QuotaService quota,
            CancellationToken ct) =>
        {
            if (!ctx.RequireAdmin(out var _))
            {
                return ctx.Response.StatusCode == 401
                    ? Results.Json(new { error = "未登录或会话已过期" }, statusCode: 401)
                    : Results.Json(new { error = "需要管理员权限" }, statusCode: 403);
            }

            try
            {
                var s = await quota.ResetAsync(req.Total, ct);
                return Results.Ok(new { total = s.Total, used = 0, remaining = s.Remaining });
            }
            catch (ArgumentException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: 400);
            }
        });

        // 设置某用户的 bonus
        grp.MapPost("/quota/users/{userId}/bonus", async (
            string userId,
            SetUserBonusRequest req,
            HttpContext ctx,
            QuotaService quota,
            AppDbContext db,
            CancellationToken ct) =>
        {
            if (!ctx.RequireAdmin(out var _))
            {
                return ctx.Response.StatusCode == 401
                    ? Results.Json(new { error = "未登录或会话已过期" }, statusCode: 401)
                    : Results.Json(new { error = "需要管理员权限" }, statusCode: 403);
            }

            // 用户必须存在
            var exists = await db.Users.AnyAsync(u => u.Id == userId, ct);
            if (!exists)
            {
                return Results.NotFound(new { error = "用户不存在" });
            }

            try
            {
                var row = await quota.SetUserBonusAsync(userId, req.Bonus, req.Note ?? "", ct);
                return Results.Ok(new { row.UserId, row.Bonus, row.Note });
            }
            catch (ArgumentException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: 400);
            }
        });

        return app;
    }
}
