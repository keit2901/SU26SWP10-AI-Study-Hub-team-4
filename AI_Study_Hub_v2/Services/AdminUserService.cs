using System.Text.Json;
using AI_Study_Hub_v2.Data;
using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Dtos;
using AI_Study_Hub_v2.Services.Supabase;
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

    Task<AdminUserDto> UpdateRoleAsync(
        Guid adminSupabaseUserId,
        Guid userId,
        string roleName,
        string? ipAddress,
        string? requestId,
        CancellationToken cancellationToken = default);

    Task<AdminUserDto> ToggleActiveAsync(
        Guid adminSupabaseUserId,
        Guid userId,
        bool activate,
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
    private readonly IGoTrueClient _goTrue;

    public AdminUserService(AppDbContext db, IAuditLogService audit, IGoTrueClient goTrue)
    {
        _db = db;
        _audit = audit;
        _goTrue = goTrue;
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
                user.IsActive && _db.AuditLogs.Any(log =>
                    log.EntityType == "users"
                    && log.EntityId == user.Id.ToString()
                    && log.Action == "USER_LOCK"),
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
        return await ToDtoAsync(user, documentCount, cancellationToken: cancellationToken);
    }

    public async Task<AdminUserDto> UpdateRoleAsync(
        Guid adminSupabaseUserId,
        Guid userId,
        string roleName,
        string? ipAddress,
        string? requestId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(roleName))
        {
            throw new AdminException(400, "invalid_role", "Role name is required.");
        }

        var admin = await ResolveActiveAdminAsync(adminSupabaseUserId, cancellationToken);
        var user = await _db.Users
            .Include(item => item.Role)
            .FirstOrDefaultAsync(item => item.Id == userId, cancellationToken)
            ?? throw new AdminException(404, "user_not_found", "User not found.");

        if (user.Id == admin.Id)
        {
            throw new AdminException(400, "cannot_change_own_role", "You cannot change your own role.");
        }

        var targetRole = await _db.Roles
            .FirstOrDefaultAsync(r => r.RoleName == roleName, cancellationToken)
            ?? throw new AdminException(400, "invalid_role", $"Role '{roleName}' does not exist in the database.");

        var previousRoleName = user.Role.RoleName;
        if (previousRoleName.Equals(roleName, StringComparison.OrdinalIgnoreCase))
        {
            var documentCount = await _db.Documents.CountAsync(
                document => document.UserId == user.Id,
                cancellationToken);
            return await ToDtoAsync(user, documentCount, cancellationToken: cancellationToken);
        }

        // Admin cannot grant or revoke Admin role (only seed/DB)
        if (roleName.Equals(Role.AdminRoleName, StringComparison.OrdinalIgnoreCase)
            || previousRoleName.Equals(Role.AdminRoleName, StringComparison.OrdinalIgnoreCase))
        {
            throw new AdminException(403, "cannot_change_admin_role",
                "Admin role cannot be assigned or revoked by another Admin.");
        }

        // Verify admin has role-assignment permission
        if (!Role.RoleAssignerNames.Contains(admin.Role.RoleName))
        {
            throw new AdminException(403, "admin_required",
                "Administrator access is required to assign roles.");
        }

        // Update role in DB
        user.RoleId = targetRole.Id;

        // Update GoTrue app_metadata so JWT claims reflect the new role
        try
        {
            await _goTrue.AdminUpdateUserByIdAsync(
                user.SupabaseUserId,
                new Dictionary<string, object?> { ["role"] = roleName },
                cancellationToken);
        }
        catch (Exception ex)
        {
            throw new AdminException(502, "gotrue_update_failed",
                $"Failed to update user role in auth provider: {ex.Message}");
        }

        // Force logout all user sessions so they re-authenticate with new role
        try
        {
            await _goTrue.AdminSignOutUserAsync(user.SupabaseUserId, cancellationToken);
        }
        catch (Exception ex)
        {
            // Log but don't block — role is already updated in DB
            // Next token refresh will pick up new role if force logout fails
            _audit.Add(
                admin.Id,
                "FORCE_LOGOUT_FAILED",
                "users",
                user.Id.ToString(),
                "Medium",
                null,
                null,
                JsonSerializer.Serialize(new { error = ex.Message }),
                ipAddress,
                requestId);
        }

        // Audit log ROLE_CHANGE
        _audit.Add(
            admin.Id,
            "ROLE_CHANGE",
            "users",
            user.Id.ToString(),
            "High",
            JsonSerializer.Serialize(new { role = previousRoleName }),
            JsonSerializer.Serialize(new { role = roleName }),
            JsonSerializer.Serialize(new { adminRole = admin.Role.RoleName }),
            ipAddress,
            requestId);

        await _db.SaveChangesAsync(cancellationToken);

        var newRoleName = await _db.Roles
            .Where(r => r.Id == user.RoleId)
            .Select(r => r.RoleName)
            .FirstAsync(cancellationToken);

        var docCount = await _db.Documents.CountAsync(
            document => document.UserId == user.Id,
            cancellationToken);
        return await ToDtoAsync(user, docCount, newRoleName, cancellationToken);
    }

    public async Task<AdminUserDto> ToggleActiveAsync(
        Guid adminSupabaseUserId,
        Guid userId,
        bool activate,
        string? ipAddress,
        string? requestId,
        CancellationToken cancellationToken = default)
    {
        var admin = await ResolveActiveAdminAsync(adminSupabaseUserId, cancellationToken);
        var user = await _db.Users
            .Include(item => item.Role)
            .FirstOrDefaultAsync(item => item.Id == userId, cancellationToken)
            ?? throw new AdminException(404, "user_not_found", "User not found.");

        if (user.Id == admin.Id)
        {
            throw new AdminException(400, "cannot_toggle_self", "You cannot lock or unlock your own account.");
        }

        if (user.IsActive == activate)
        {
            var docCount = await _db.Documents.CountAsync(
                document => document.UserId == user.Id,
                cancellationToken);
            return await ToDtoAsync(user, docCount, cancellationToken: cancellationToken);
        }

        var action = activate ? "USER_UNLOCK" : "USER_LOCK";
        user.IsActive = activate;

        // If locking, also ban on GoTrue to prevent login
        if (!activate)
        {
            try
            {
                await _goTrue.AdminUpdateUserByIdAsync(
                    user.SupabaseUserId,
                    new Dictionary<string, object?> { ["banned"] = true },
                    cancellationToken);
                await _goTrue.AdminSignOutUserAsync(user.SupabaseUserId, cancellationToken);
            }
            catch (Exception ex)
            {
                _audit.Add(admin.Id, "FORCE_LOGOUT_FAILED", "users", user.Id.ToString(), "Medium",
                    null, null, JsonSerializer.Serialize(new { error = ex.Message }), ipAddress, requestId);
            }
        }
        else
        {
            // Unlock: remove ban flag from GoTrue
            try
            {
                await _goTrue.AdminUpdateUserByIdAsync(
                    user.SupabaseUserId,
                    new Dictionary<string, object?> { ["banned"] = false },
                    cancellationToken);
            }
            catch
            {
                // GoTrue unlock failure is non-blocking; DB lock is already cleared
            }
        }

        _audit.Add(
            admin.Id,
            action,
            "users",
            user.Id.ToString(),
            "High",
            JsonSerializer.Serialize(new { active = !activate }),
            JsonSerializer.Serialize(new { active = activate }),
            JsonSerializer.Serialize(new { adminRole = admin.Role.RoleName }),
            ipAddress,
            requestId);

        await _db.SaveChangesAsync(cancellationToken);

        var documentCount = await _db.Documents.CountAsync(
            document => document.UserId == user.Id,
            cancellationToken);
        return await ToDtoAsync(user, documentCount, cancellationToken: cancellationToken);
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

    private async Task<AdminUserDto> ToDtoAsync(User user, int documentCount, string? roleName = null, CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var isPreviouslyBanned = await _db.AuditLogs.AnyAsync(
            log => log.EntityType == "users"
                   && log.EntityId == user.Id.ToString()
                   && log.Action == "USER_LOCK",
            cancellationToken);
        return new AdminUserDto(
            user.Id,
            user.SupabaseUserId,
            user.Username,
            user.FullName,
            roleName ?? user.Role.RoleName,
            user.IsActive,
            user.DailyTokenQuota,
            user.TokenUsageDate == today ? user.TokensUsedToday : 0,
            today,
            user.TotalTokensUsed,
            documentCount,
            isPreviouslyBanned,
            user.CreatedAt);
    }
}
