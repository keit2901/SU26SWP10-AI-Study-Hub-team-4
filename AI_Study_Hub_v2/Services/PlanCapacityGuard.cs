using AI_Study_Hub_v2.Data;
using AI_Study_Hub_v2.Data.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace AI_Study_Hub_v2.Services;

public sealed class PlanCapacityGuard : IPlanCapacityGuard
{
    private readonly IPlanService _plans;
    public PlanCapacityGuard(IPlanService plans) => _plans = plans;

    public async Task LockAndValidateAsync(AppDbContext db, Guid userId, PlanCapacityRequest request, CancellationToken ct)
    {
        User? user;
        if (db.Database.IsRelational())
        {
            user = await db.Users
                .FromSqlRaw("SELECT * FROM users WHERE id = {0} FOR UPDATE", userId)
                .SingleOrDefaultAsync(ct);
        }
        else
        {
            user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId, ct);
        }
        if (user is null || !user.IsActive) throw new PlanException(404, "user_not_found", "User profile not found.");

        var now = DateTimeOffset.UtcNow;
        var active = await db.UserPlans.AsNoTracking().Include(up => up.Plan)
            .Where(up => up.UserId == userId && up.Status == "active" && (up.ExpiresAt == null || up.ExpiresAt > now))
            .OrderByDescending(up => up.PaidAt ?? up.AssignedAt).FirstOrDefaultAsync(ct);
        var plan = active?.Plan ?? _plans.GetFreePlan();

        var documentCount = await db.Documents.CountAsync(d => d.UserId == userId, ct);
        if (plan.MaxDocumentCount.HasValue && documentCount + request.AdditionalDocumentCount > plan.MaxDocumentCount.Value)
            throw new PlanException(StatusCodes.Status402PaymentRequired, "document_count_exceeded", "Document count limit reached.");

        var folderCount = await db.Folders.CountAsync(f => f.UserId == userId, ct);
        if (plan.MaxFolderCount.HasValue && folderCount + request.AdditionalFolderCount > plan.MaxFolderCount.Value)
            throw new PlanException(StatusCodes.Status402PaymentRequired, "folder_count_exceeded", "Folder count limit reached.");

        if (request.TargetFolderId.HasValue)
        {
            var current = await db.Documents.CountAsync(d => d.FolderId == request.TargetFolderId.Value && d.UserId == userId, ct);
            var max = plan.MaxDocsPerFolder ?? DocumentService.MaxDocumentsPerFolder;
            if (current + request.AdditionalDocumentsInTargetFolder > max)
                throw new DocumentException(409, "folder_full", $"This folder already has {current} document(s), which is the maximum ({max}).");
        }
    }
}
