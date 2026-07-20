using DockerVm.Data;
using DockerVm.Models;
using Microsoft.EntityFrameworkCore;

namespace DockerVm.Services;

/// <summary>
/// 名额池管理。所有扣减/退还/查询都在事务里完成,SQLite 写锁保证并发安全。
/// </summary>
public class QuotaService
{
    private const int SingletonId = 1;

    private readonly AppDbContext _db;
    private readonly ILogger<QuotaService> _logger;

    public QuotaService(AppDbContext db, ILogger<QuotaService> logger)
    {
        _db = db;
        _logger = logger;
    }

    // ---------- 初始化(首启调用) ----------
    public async Task EnsureInitializedAsync(int initialTotal, CancellationToken ct = default)
    {
        var existing = await _db.QuotaStates.FindAsync(new object?[] { SingletonId }, ct);
        if (existing is null)
        {
            _db.QuotaStates.Add(new QuotaState
            {
                Id = SingletonId,
                Total = initialTotal,
                Used = 0,
                UpdatedAt = DateTime.UtcNow,
            });
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("初始化全局名额池:Total={Total}", initialTotal);
        }
    }

    // ---------- 用户视角查询 ----------
    public async Task<(QuotaState state, int userBonus)> GetForUserAsync(string userId, CancellationToken ct = default)
    {
        var state = await GetOrCreateStateAsync(ct);
        var bonus = await GetUserBonusAsync(userId, ct);
        return (state, bonus);
    }

    // ---------- admin 视角查询 ----------
    public async Task<QuotaState> GetStateAsync(CancellationToken ct = default)
        => await GetOrCreateStateAsync(ct);

    public async Task<List<UserQuotaBonus>> GetAllBonusesAsync(CancellationToken ct = default)
        => await _db.UserQuotaBonuses.AsNoTracking().ToListAsync(ct);

    // ---------- 扣减名额(创建容器前调用) ----------
    /// <summary>
    /// 尝试为某用户扣 1 个名额。优先消耗全局池,全局空了才消耗个人 bonus。
    /// 返回消耗来源("global" / "bonus");失败抛 QuotaExceededException。
    /// 全程事务。
    /// </summary>
    public async Task<string> TryConsumeAsync(string userId, CancellationToken ct = default)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);

        var state = await GetOrCreateStateAsync(ct);
        var bonus = await GetUserBonusAsync(userId, ct);

        var globalRemaining = state.Total - state.Used;
        if (globalRemaining <= 0 && bonus <= 0)
        {
            throw new QuotaExceededException();
        }

        string consumedFrom;
        if (globalRemaining > 0)
        {
            state.Used++;
            consumedFrom = "global";
        }
        else
        {
            // 消耗个人 bonus
            var row = await GetOrCreateBonusRowAsync(userId, ct);
            row.Bonus--;
            row.UpdatedAt = DateTime.UtcNow;
            consumedFrom = "bonus";
        }

        state.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        _logger.LogInformation("用户 {UserId} 消耗 1 个名额(来源={From})", userId, consumedFrom);
        return consumedFrom;
    }

    // ---------- 退还名额(docker 创建失败时调用) ----------
    public async Task RefundAsync(string userId, string consumedFrom, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(consumedFrom))
        {
            return;
        }

        await using var tx = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);

        var state = await GetOrCreateStateAsync(ct);
        if (consumedFrom == "global")
        {
            if (state.Used > 0)
            {
                state.Used--;
            }
        }
        else // bonus
        {
            var row = await GetOrCreateBonusRowAsync(userId, ct);
            row.Bonus++;
        }
        state.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        _logger.LogInformation("用户 {UserId} 退还 1 个名额(来源={From})", userId, consumedFrom);
    }

    // ---------- admin:设置全局额度 ----------
    public async Task<QuotaState> SetTotalAsync(int total, int? used, CancellationToken ct = default)
    {
        if (total < 0) throw new ArgumentException("total 不能为负");
        if (used.HasValue && used.Value < 0) throw new ArgumentException("used 不能为负");

        await using var tx = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var state = await GetOrCreateStateAsync(ct);
        state.Total = total;
        if (used.HasValue)
        {
            state.Used = used.Value;
        }
        state.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        _logger.LogInformation("admin 修改全局额度:Total={Total}, Used={Used}", state.Total, state.Used);
        return state;
    }

    // ---------- admin:一键重置 ----------
    public async Task<QuotaState> ResetAsync(int total, CancellationToken ct = default)
    {
        if (total < 0) throw new ArgumentException("total 不能为负");

        await using var tx = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var state = await GetOrCreateStateAsync(ct);
        state.Total = total;
        state.Used = 0;
        state.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        _logger.LogInformation("admin 重置名额池:Total={Total}, Used=0", total);
        return state;
    }

    // ---------- admin:设置用户 bonus ----------
    public async Task<UserQuotaBonus> SetUserBonusAsync(string userId, int bonus, string note, CancellationToken ct = default)
    {
        if (bonus < 0) throw new ArgumentException("bonus 不能为负");

        await using var tx = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var row = await GetOrCreateBonusRowAsync(userId, ct);
        row.Bonus = bonus;
        row.Note = note ?? "";
        row.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        _logger.LogInformation("admin 设置用户 {UserId} 的 bonus={Bonus}", userId, bonus);
        return row;
    }

    // ---------- 内部 ----------
    private async Task<QuotaState> GetOrCreateStateAsync(CancellationToken ct)
    {
        var state = await _db.QuotaStates.FindAsync(new object?[] { SingletonId }, ct);
        if (state is null)
        {
            state = new QuotaState { Id = SingletonId, Total = 5, Used = 0 };
            _db.QuotaStates.Add(state);
            await _db.SaveChangesAsync(ct);
        }
        return state;
    }

    private async Task<int> GetUserBonusAsync(string userId, CancellationToken ct)
    {
        var row = await _db.UserQuotaBonuses.AsNoTracking()
            .FirstOrDefaultAsync(b => b.UserId == userId, ct);
        return row?.Bonus ?? 0;
    }

    private async Task<UserQuotaBonus> GetOrCreateBonusRowAsync(string userId, CancellationToken ct)
    {
        var row = await _db.UserQuotaBonuses.FirstOrDefaultAsync(b => b.UserId == userId, ct);
        if (row is null)
        {
            row = new UserQuotaBonus { UserId = userId, Bonus = 0, Note = "" };
            _db.UserQuotaBonuses.Add(row);
            await _db.SaveChangesAsync(ct);
        }
        return row;
    }
}

public class QuotaExceededException : Exception
{
    public QuotaExceededException() : base("今日名额已用完") { }
}
