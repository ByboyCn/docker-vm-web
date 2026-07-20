using DockerVm.Middleware;
using DockerVm.Models;

namespace DockerVm.Services;

/// <summary>
/// 从 HttpContext.Items 取当前登录用户。
/// 提供两种语义:必需登录 / 必需管理员。
/// 返回 false 表示已经写了响应,endpoint 应当立即 return。
/// </summary>
public static class CurrentUserResolver
{
    /// <summary>取当前用户,可能为 null。</summary>
    public static User? Current(this HttpContext ctx) =>
        ctx.Items.TryGetValue(AuthMiddleware.UserItemsKey, out var u) ? u as User : null;

    /// <summary>要求已登录,否则返回 401 并停止管道。</summary>
    public static bool RequireUser(this HttpContext ctx, out User? user)
    {
        user = ctx.Current();
        if (user is null)
        {
            ctx.Response.StatusCode = 401;
            return false;
        }
        return true;
    }

    /// <summary>要求是管理员,否则返回 401/403 并停止管道。</summary>
    public static bool RequireAdmin(this HttpContext ctx, out User? user)
    {
        user = ctx.Current();
        if (user is null)
        {
            ctx.Response.StatusCode = 401;
            return false;
        }
        if (!user.IsAdmin)
        {
            ctx.Response.StatusCode = 403;
            return false;
        }
        return true;
    }
}
