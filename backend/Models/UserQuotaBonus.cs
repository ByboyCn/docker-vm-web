using System.ComponentModel.DataAnnotations;

namespace DockerVm.Models;

/// <summary>
/// admin 给指定用户的额外名额加量。
/// </summary>
public class UserQuotaBonus
{
    [Key]
    [MaxLength(64)]
    public string UserId { get; set; } = string.Empty;

    /// <summary>额外名额数。</summary>
    public int Bonus { get; set; }

    [MaxLength(128)]
    public string Note { get; set; } = string.Empty;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
