using AI_Study_Hub_v2.Data;
using AI_Study_Hub_v2.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AI_Study_Hub_v2.Services;

public sealed class AdminModerationMetricsService(AppDbContext db) : IAdminModerationMetricsService
{
    private readonly AppDbContext _db = db;

    public async Task<int> GetPendingEscalatedDocumentCountAsync(Guid supabaseUserId, CancellationToken ct = default)
    {
        var isActiveAdmin = await _db.Users
            .AsNoTracking()
            .Where(user => user.SupabaseUserId == supabaseUserId && user.IsActive)
            .AnyAsync(user => user.Role.RoleName == Role.AdminRoleName, ct);
        if (!isActiveAdmin)
        {
            throw new AdminException(403, "admin_required", "Administrator access is required.");
        }

        return await _db.DocumentEscalationItems
            .AsNoTracking()
            .Where(item => item.Escalation.EscalationStatus == "Pending" &&
                           item.ResolutionStatus == "Pending" &&
                           item.DocumentId != null)
            .Select(item => item.DocumentId)
            .Distinct()
            .CountAsync(ct);
    }
}
