using DockerVm.Services;

namespace DockerVm.Middleware;

/// <summary>
/// 从 cookie 读 sid 并解析当前用户,挂到 HttpContext.Items["User"]。
/// 不强制登录 —— 是否需要登录由各 endpoint 通过 CurrentUserResolver 判断。
/// </summary>
public class AuthMiddleware
{
    public const string UserItemsKey = "CurrentUser";

    private readonly RequestDelegate _next;

    public AuthMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext ctx, AuthService auth)
    {
        if (ctx.Request.Cookies.TryGetValue(AuthService.SessionCookieName, out var sid))
        {
            var user = await auth.ResolveBySessionAsync(sid, ctx.RequestAborted);
            if (user is not null)
            {
                ctx.Items[UserItemsKey] = user;
            }
        }
        await _next(ctx);
    }
}
