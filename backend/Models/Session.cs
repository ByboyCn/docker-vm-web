using System.ComponentModel.DataAnnotations;

namespace DockerVm.Models;

public class Session
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = string.Empty;  // 32 字节随机 → Base64Url

    [Required]
    [MaxLength(64)]
    public string UserId { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime ExpiresAt { get; set; }

    public DateTime? LastUsedAt { get; set; }
}
