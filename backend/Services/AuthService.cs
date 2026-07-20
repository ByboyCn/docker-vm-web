using System.Security.Cryptography;
using DockerVm.Data;
using DockerVm.Models;
using Microsoft.EntityFrameworkCore;

namespace DockerVm.Services;

/// <summary>
/// 处理用户注册、登录、登出、当前用户解析。
/// </summary>
public class AuthService
{
    public const string SessionCookieName = "sid";
    private static readonly TimeSpan SessionTtl = TimeSpan.FromDays(7);

    private readonly AppDbContext _db;

    public AuthService(AppDbContext db)
    {
        _db = db;
    }

    // ---------- 注册 ----------
    public async Task<(User user, Session session)> RegisterAsync(string username, string password, CancellationToken ct = default)
    {
        username = ValidateUsername(username);
        ValidatePassword(password);

        var exists = await _db.Users.AnyAsync(u => u.Username == username, ct);
        if (exists)
        {
            throw new AuthException("用户名已存在", 409);
        }

        var (hash, salt) = PasswordHasher.Hash(password);
        var user = new User
        {
            Username = username,
            PasswordHash = hash,
            PasswordSalt = salt,
            IsAdmin = false,   // 管理员只能由首启 INITIAL_ADMIN_* 或后台改库创建
            CreatedAt = DateTime.UtcNow,
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync(ct);

        var session = await CreateSessionAsync(user.Id, ct);
        return (user, session);
    }

    // ---------- 登录 ----------
    public async Task<(User user, Session session)> LoginAsync(string username, string password, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            throw new AuthException("用户名或密码错误", 401);
        }

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == username, ct);
        if (user is null || !PasswordHasher.Verify(password, user.PasswordHash, user.PasswordSalt))
        {
            // 故意模糊错误信息,避免用户名枚举
            throw new AuthException("用户名或密码错误", 401);
        }

        var session = await CreateSessionAsync(user.Id, ct);
        return (user, session);
    }

    // ---------- 登出 ----------
    public async Task LogoutAsync(string? sessionId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(sessionId))
        {
            return;
        }
        var s = await _db.Sessions.FindAsync(new object?[] { sessionId }, ct);
        if (s is not null)
        {
            _db.Sessions.Remove(s);
            await _db.SaveChangesAsync(ct);
        }
    }

    // ---------- 由 session id 解析当前用户 ----------
    public async Task<User?> ResolveBySessionAsync(string? sessionId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(sessionId))
        {
            return null;
        }

        var session = await _db.Sessions.FindAsync(new object?[] { sessionId }, ct);
        if (session is null || session.ExpiresAt <= DateTime.UtcNow)
        {
            return null;
        }

        // 更新最后使用时间
        session.LastUsedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return await _db.Users.FindAsync(new object?[] { session.UserId }, ct);
    }

    // ---------- 工具:创建 session ----------
    private async Task<Session> CreateSessionAsync(string userId, CancellationToken ct)
    {
        var session = new Session
        {
            Id = GenerateSessionId(),
            UserId = userId,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.Add(SessionTtl),
            LastUsedAt = DateTime.UtcNow,
        };
        _db.Sessions.Add(session);
        await _db.SaveChangesAsync(ct);
        return session;
    }

    private static string GenerateSessionId()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Base64UrlEncode(bytes);
    }

    public static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

    // ---------- 输入校验 ----------
    private static string ValidateUsername(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new AuthException("用户名不能为空", 400);
        }
        var u = raw.Trim();
        if (u.Length is < 3 or > 32)
        {
            throw new AuthException("用户名长度需 3-32 字符", 400);
        }
        // 允许字母数字下划线横线点 @
        foreach (var ch in u)
        {
            var ok = char.IsLetterOrDigit(ch) || ch is '_' or '-' or '.' or '@';
            if (!ok)
            {
                throw new AuthException("用户名只能包含字母、数字、_ - . @", 400);
            }
        }
        return u;
    }

    private static void ValidatePassword(string raw)
    {
        if (raw is null || raw.Length < 6)
        {
            throw new AuthException("密码至少 6 位", 400);
        }
        if (raw.Length > 128)
        {
            throw new AuthException("密码最长 128 位", 400);
        }
    }
}

public class AuthException : Exception
{
    public int StatusCode { get; }
    public AuthException(string message, int statusCode) : base(message)
    {
        StatusCode = statusCode;
    }
}
