using DockerVm.Dtos;
using DockerVm.Services;

namespace DockerVm.Endpoints;

public static class QuotaEndpoints
{
    public static IEndpointRouteBuilder MapQuotaEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/api/quota").WithTags("quota");

        // 用户视角:我的剩余名额
        grp.MapGet("/", async (
            HttpContext ctx,
            QuotaService quota,
            CancellationToken ct) =>
        {
            if (!ctx.RequireUser(out var user))
            {
                return Results.Json(new { error = "未登录或会话已过期" }, statusCode: 401);
            }

            var (state, bonus) = await quota.GetForUserAsync(user!.Id, ct);
            var globalRemaining = state.Total - state.Used;
            var myRemaining = Math.Max(0, globalRemaining) + bonus;

            return Results.Ok(new UserQuotaDto(
                Remaining: myRemaining,
                GlobalRemaining: globalRemaining,
                GlobalTotal: state.Total,
                GlobalUsed: state.Used,
                Bonus: bonus
            ));
        });

        return app;
    }
}
