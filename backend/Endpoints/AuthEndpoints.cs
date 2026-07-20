using DockerVm.Dtos;
using DockerVm.Services;

namespace DockerVm.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/api/auth").WithTags("auth");

        // 注册
        grp.MapPost("/register", async (
            LoginRequest req,
            AuthService auth,
            HttpContext ctx,
            CancellationToken ct) =>
        {
            try
            {
                var (user, session) = await auth.RegisterAsync(req.Username, req.Password, ct);
                SetSessionCookie(ctx, session.Id);
                return Results.Ok(UserDto.From(user));
            }
            catch (AuthException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: ex.StatusCode);
            }
        });

        // 登录
        grp.MapPost("/login", async (
            LoginRequest req,
            AuthService auth,
            HttpContext ctx,
            CancellationToken ct) =>
        {
            try
            {
                var (user, session) = await auth.LoginAsync(req.Username, req.Password, ct);
                SetSessionCookie(ctx, session.Id);
                return Results.Ok(UserDto.From(user));
            }
            catch (AuthException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: ex.StatusCode);
            }
        });

        // 登出
        grp.MapPost("/logout", async (
            AuthService auth,
            HttpContext ctx,
            CancellationToken ct) =>
        {
            var sid = ctx.Request.Cookies[AuthService.SessionCookieName];
            await auth.LogoutAsync(sid, ct);
            ClearSessionCookie(ctx);
            return Results.Ok(new { ok = true });
        });

        // 当前用户(前端进入页面时校验登录态)
        grp.MapGet("/me", (HttpContext ctx) =>
        {
            var user = ctx.Current();
            if (user is null)
            {
                return Results.Unauthorized();
            }
            return Results.Ok(UserDto.From(user));
        });

        // 修改密码(需登录,需校验旧密码)
        grp.MapPost("/change-password", async (
            ChangePasswordRequest req,
            HttpContext ctx,
            AuthService auth,
            CancellationToken ct) =>
        {
            if (!ctx.RequireUser(out var user))
            {
                return Results.Json(new { error = "未登录或会话已过期" }, statusCode: 401);
            }

            try
            {
                var ok = await auth.ChangePasswordAsync(user!.Id, req.OldPassword, req.NewPassword, ct);
                if (!ok)
                {
                    return Results.Json(new { error = "旧密码不正确" }, statusCode: 400);
                }
                return Results.Ok(new { ok = true });
            }
            catch (AuthException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: ex.StatusCode);
            }
        });

        return app;
    }

    // ---------- Cookie 工具 ----------
    private static void SetSessionCookie(HttpContext ctx, string sid)
    {
        ctx.Response.Cookies.Append(AuthService.SessionCookieName, sid, BuildCookieOptions(ctx));
    }

    private static void ClearSessionCookie(HttpContext ctx)
    {
        ctx.Response.Cookies.Delete(AuthService.SessionCookieName, BuildCookieOptions(ctx));
    }

    private static CookieOptions BuildCookieOptions(HttpContext ctx)
    {
        var secure = ctx.Request.IsHttps || ctx.Request.Headers["X-Forwarded-Proto"] == "https";
        return new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Strict,
            Secure = secure,
            Path = "/",
            MaxAge = TimeSpan.FromDays(7),
        };
    }
}
