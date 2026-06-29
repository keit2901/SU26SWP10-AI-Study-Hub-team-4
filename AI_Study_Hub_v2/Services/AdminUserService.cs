using System.Text.Json;
using AI_Study_Hub_v2.Data;
using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Dtos;
using Microsoft.EntityFrameworkCore;

namespace AI_Study_Hub_v2.Services;

public interface IAdminUserService
{
    Task<IReadOnlyList<AdminUserDto>> ListAsync(CancellationToken cancellationToken = default);

    Task<AdminUserDto> UpdateQuotaAsync(
        Guid adminSupabaseUserId,
        Guid userId,
        long dailyTokenQuota,
        string? ipAddress,
        string? requestId,
        CancellationToken cancellationToken = default);
}

public sealed class AdminUserService : IAdminUserService
{
    private const long MinimumQuota = 1_000;
    private const long MaximumQuota = 10_000_000;
    private readonly AppDbContext _db;
    private readonly IAuditLogService _audit;

    public AdminUserService(AppDbContext db, IAuditLogService audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<IReadOnlyList<AdminUserDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return await _db.Users
            .AsNoTracking()
            .OrderBy(user => user.FullName)
            .Select(user => new AdminUserDto(
                user.Id,
                user.SupabaseUserId,
                user.Username,
                user.FullName,
                user.Role.RoleName,
                user.IsActive,
                user.DailyTokenQuota,
                user.TokenUsageDate == today ? user.TokensUsedToday : 0,
                today,
                user.TotalTokensUsed,
                _db.Documents.Count(document => document.UserId == user.Id),
                user.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<AdminUserDto> UpdateQuotaAsync(
        Guid adminSupabaseUserId,
        Guid userId,
        long dailyTokenQuota,
        string? ipAddress,
        string? requestId,
        CancellationToken cancellationToken = default)
    {
        if (dailyTokenQuota is < MinimumQuota or > MaximumQuota)
        {
            throw new AdminException(400, "invalid_quota",
                $"Daily token quota must be between {MinimumQuota:N0} and {MaximumQuota:N0}.");
        }

        var admin = await ResolveActiveAdminAsync(adminSupabaseUserId, cancellationToken);
        var user = await _db.Users
            .Include(item => item.Role)
            .FirstOrDefaultAsync(item => item.Id == userId, cancellationToken)
            ?? throw new AdminException(404, "user_not_found", "User not found.");

        var previousQuota = user.DailyTokenQuota;
        user.DailyTokenQuota = dailyTokenQuota;

        _audit.Add(
            admin.Id,
            "QUOTA_UPDATE",
            "users",
            user.Id.ToString(),
            "Medium",
            JsonSerializer.Serialize(new { dailyTokenQuota = previousQuota }),
            JsonSerializer.Serialize(new { dailyTokenQuota }),
            JsonSerializer.Serialize(new
            {
                usageToday = user.TokenUsageDate == DateOnly.FromDateTime(DateTime.UtcNow)
                    ? user.TokensUsedToday
                    : 0,
            }),
            ipAddress,
            requestId);

        await _db.SaveChangesAsync(cancellationToken);
        var documentCount = await _db.Documents.CountAsync(
            document => document.UserId == user.Id,
            cancellationToken);
        return ToDto(user, documentCount);
    }

    private async Task<User> ResolveActiveAdminAsync(Guid supabaseUserId, CancellationToken cancellationToken)
    {
        var user = await _db.Users
            .Include(item => item.Role)
            .FirstOrDefaultAsync(item => item.SupabaseUserId == supabaseUserId, cancellationToken)
            ?? throw new AdminException(404, "user_not_found", "Administrator profile not found.");

        if (!user.IsActive)
        {
            throw new AdminException(403, "user_inactive", "Administrator account is inactive.");
        }
        if (!user.Role.RoleName.Equals(Role.AdminRoleName, StringComparison.OrdinalIgnoreCase))
        {
            throw new AdminException(403, "admin_required", "Administrator access is required.");
        }
        return user;
    }

    private static AdminUserDto ToDto(User user, int documentCount)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return new AdminUserDto(
            user.Id,
            user.SupabaseUserId,
            user.Username,
            user.FullName,
            user.Role.RoleName,
            user.IsActive,
            user.DailyTokenQuota,
            user.TokenUsageDate == today ? user.TokensUsedToday : 0,
            today,
            user.TotalTokensUsed,
            documentCount,
            user.CreatedAt);
    }
}
