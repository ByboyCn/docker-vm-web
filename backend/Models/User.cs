using System.ComponentModel.DataAnnotations;

namespace DockerVm.Models;

public class User
{
    [Key]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>登录用户名,唯一。3-32 字符。</summary>
    [MaxLength(32)]
    public string Username { get; set; } = string.Empty;

    /// <summary>PBKDF2-SHA256 输出(Base64)。</summary>
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>密码 salt(Base64)。</summary>
    public string PasswordSalt { get; set; } = string.Empty;

    /// <summary>是否管理员。</summary>
    public bool IsAdmin { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
