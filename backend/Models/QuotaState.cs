using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DockerVm.Models;

/// <summary>
/// 全局名额池状态。表中永远只有一行(Id=1)。
/// </summary>
public class QuotaState
{
    [Key]
    public int Id { get; set; } = 1;

    /// <summary>admin 设置的总额度。</summary>
    public int Total { get; set; } = 5;

    /// <summary>已消耗数量(每开一台 +1,销毁不退)。</summary>
    public int Used { get; set; } = 0;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>剩余 = Total - Used。不入库。</summary>
    [NotMapped]
    public int Remaining => Total - Used;
}
