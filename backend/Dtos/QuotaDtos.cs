namespace DockerVm.Dtos;

/// <summary>用户视角的配额信息。</summary>
public record UserQuotaDto(
    int Remaining,   // 用户当前可用 = 全局剩余 + 个人 bonus
    int GlobalRemaining,
    int GlobalTotal,
    int GlobalUsed,
    int Bonus
);

/// <summary>admin 视角的全局配额。</summary>
public record AdminQuotaDto(
    int Total,
    int Used,
    int Remaining,
    DateTime UpdatedAt,
    List<UserBonusItem> UserBonuses
);

public record UserBonusItem(
    string UserId,
    string Username,
    int Bonus,
    string Note,
    DateTime UpdatedAt
);

public class SetQuotaRequest
{
    public int Total { get; set; }
    public int? Used { get; set; }
}

public class ResetQuotaRequest
{
    public int Total { get; set; }
}

public class SetUserBonusRequest
{
    public int Bonus { get; set; }
    public string Note { get; set; } = "";
}
