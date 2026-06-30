using System.Text.Json;
using AI_Study_Hub_v2.Data;
using AI_Study_Hub_v2.Dtos;
using Microsoft.EntityFrameworkCore;

namespace AI_Study_Hub_v2.Services;

public sealed record AiQuotaReservation(Guid UserId, int ReservedTokens, DateOnly UsageDate);

public interface IAiQuotaService
{
    Task<AiQuotaReservation> ReserveAsync(
        Guid supabaseUserId,
        int estimatedTokens,
        CancellationToken cancellationToken = default);

    Task CompleteAsync(
        AiQuotaReservation reservation,
        int actualTokens,
        CancellationToken cancellationToken = default);

    Task ReleaseAsync(
        AiQuotaReservation reservation,
        CancellationToken cancellationToken = default);

    Task<AiQuotaSnapshotDto> GetSnapshotAsync(
        Guid supabaseUserId,
        CancellationToken cancellationToken = default);
}

public sealed class AiQuotaException : Exception
{
    public AiQuotaException(int statusCode, string code, string message) : base(message)
    {
        StatusCode = statusCode;
        Code = code;
    }

    public int StatusCode { get; }

    public string Code { get; }
}

public sealed class AiQuotaService : IAiQuotaService
{
    private readonly AppDbContext _db;
    private readonly IAuditLogService _audit;

    public AiQuotaService(AppDbContext db, IAuditLogService audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<AiQuotaReservation> ReserveAsync(
        Guid supabaseUserId,
        int estimatedTokens,
        CancellationToken cancellationToken = default)
    {
        if (estimatedTokens <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(estimatedTokens));
        }

        var user = await _db.Users
            .FirstOrDefaultAsync(item => item.SupabaseUserId == supabaseUserId, cancellationToken)
            ?? throw new AiQuotaException(404, "user_not_found", "User profile not found.");

        if (!user.IsActive)
        {
            throw new AiQuotaException(403, "user_inactive", "User account is inactive.");
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        ResetDailyUsageIfNeeded(user, today);
        var remaining = Math.Max(0, user.DailyTokenQuota - user.TokensUsedToday);
        if (estimatedTokens > remaining)
        {
            _audit.Add(
                user.Id,
                "AI_QUOTA_BLOCKED",
                "users",
                user.Id.ToString(),
                "Medium",
                contextJson: JsonSerializer.Serialize(new
                {
                    estimatedTokens,
                    user.DailyTokenQuota,
                    user.TokensUsedToday,
                    remaining,
                }));
            await _db.SaveChangesAsync(cancellationToken);
            throw new AiQuotaException(
                StatusCodes.Status429TooManyRequests,
                "ai_quota_exceeded",
                $"Daily AI token quota exceeded. Remaining allowance: {remaining:N0} tokens.");
        }

        user.TokensUsedToday += estimatedTokens;
        await _db.SaveChangesAsync(cancellationToken);
        return new AiQuotaReservation(user.Id, estimatedTokens, today);
    }

    public async Task CompleteAsync(
        AiQuotaReservation reservation,
        int actualTokens,
        CancellationToken cancellationToken = default)
    {
        var user = await _db.Users
            .FirstOrDefaultAsync(item => item.Id == reservation.UserId, cancellationToken)
            ?? throw new AiQuotaException(404, "user_not_found", "User profile not found.");

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var sameUsageDay = user.TokenUsageDate == reservation.UsageDate && reservation.UsageDate == today;
        ResetDailyUsageIfNeeded(user, today);

        var actual = Math.Max(0, actualTokens);
        user.TokensUsedToday = sameUsageDay
            ? Math.Max(0, user.TokensUsedToday - reservation.ReservedTokens + actual)
            : user.TokensUsedToday + actual;
        user.TotalTokensUsed += actual;
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task ReleaseAsync(
        AiQuotaReservation reservation,
        CancellationToken cancellationToken = default)
    {
        var user = await _db.Users
            .FirstOrDefaultAsync(item => item.Id == reservation.UserId, cancellationToken);
        if (user is null)
        {
            return;
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (user.TokenUsageDate == reservation.UsageDate && reservation.UsageDate == today)
        {
            user.TokensUsedToday = Math.Max(0, user.TokensUsedToday - reservation.ReservedTokens);
            await _db.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<AiQuotaSnapshotDto> GetSnapshotAsync(
        Guid supabaseUserId,
        CancellationToken cancellationToken = default)
    {
        var user = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.SupabaseUserId == supabaseUserId, cancellationToken)
            ?? throw new AiQuotaException(404, "user_not_found", "User profile not found.");

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var used = user.TokenUsageDate == today ? user.TokensUsedToday : 0;
        return new AiQuotaSnapshotDto(
            user.DailyTokenQuota,
            used,
            Math.Max(0, user.DailyTokenQuota - used),
            today);
    }

    private static void ResetDailyUsageIfNeeded(Data.Entities.User user, DateOnly today)
    {
        if (user.TokenUsageDate == today)
        {
            return;
        }
        user.TokenUsageDate = today;
        user.TokensUsedToday = 0;
    }
}
