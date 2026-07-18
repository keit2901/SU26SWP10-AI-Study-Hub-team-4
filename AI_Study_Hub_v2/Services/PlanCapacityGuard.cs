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
        var locked = await LockUserAndResolvePlanAsync(db, userId, ct);
        await ValidateCapacityAsync(db, locked.Plan, userId, request, ct);
    }

    public async Task LockValidateAndReserveStorageAsync(
        AppDbContext db,
        Guid userId,
        PlanCapacityRequest request,
        long additionalStorageBytes,
        CancellationToken ct)
    {
        if (additionalStorageBytes < 0) throw new ArgumentOutOfRangeException(nameof(additionalStorageBytes));

        var locked = await LockUserAndResolvePlanAsync(db, userId, ct);
        await ValidateCapacityAsync(db, locked.Plan, userId, request, ct);
        if (locked.Plan.StorageQuotaBytes.HasValue
            && (locked.User.StorageUsedBytes > locked.Plan.StorageQuotaBytes.Value - additionalStorageBytes))
        {
            throw new PlanException(StatusCodes.Status402PaymentRequired, "storage_quota_exceeded",
                $"Storage quota exceeded. Used: {locked.User.StorageUsedBytes:N0} bytes, Quota: {locked.Plan.StorageQuotaBytes:N0} bytes, Attempted: {additionalStorageBytes:N0} bytes.");
        }

        locked.User.StorageUsedBytes += additionalStorageBytes;
    }

    public async Task LockAndReleaseReservedStorageAsync(
        AppDbContext db,
        Guid userId,
        long reservedStorageBytes,
        CancellationToken ct)
    {
        if (reservedStorageBytes < 0) throw new ArgumentOutOfRangeException(nameof(reservedStorageBytes));

        var user = await LockExistingUserAsync(db, userId, ct);
        user.StorageUsedBytes = Math.Max(0, user.StorageUsedBytes - reservedStorageBytes);
    }

    private async Task<LockedUserPlan> LockUserAndResolvePlanAsync(AppDbContext db, Guid userId, CancellationToken ct)
    {
        var user = await LockExistingUserAsync(db, userId, ct);
        if (!user.IsActive) throw new PlanException(404, "user_not_found", "User profile not found.");

        var now = DateTimeOffset.UtcNow;
        var active = await db.UserPlans.AsNoTracking().Include(up => up.Plan)
            .Where(up => up.UserId == userId && up.Status == "active" && (up.ExpiresAt == null || up.ExpiresAt > now))
            .OrderByDescending(up => up.PaidAt ?? up.AssignedAt).FirstOrDefaultAsync(ct);
        var plan = active?.Plan ?? _plans.GetFreePlan();
        return new LockedUserPlan(user, plan);
    }

    private static async Task<User> LockExistingUserAsync(AppDbContext db, Guid userId, CancellationToken ct)
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
        return user ?? throw new PlanException(404, "user_not_found", "User profile not found.");
    }

    private static async Task ValidateCapacityAsync(AppDbContext db, Plan plan, Guid userId, PlanCapacityRequest request, CancellationToken ct)
    {
        ValidateNonNegativeDeltas(request);
        var documentCount = await db.Documents.CountAsync(d => d.UserId == userId, ct);
        if (plan.MaxDocumentCount.HasValue && documentCount + request.AdditionalDocumentCount > plan.MaxDocumentCount.Value)
            throw new PlanException(StatusCodes.Status402PaymentRequired, "document_count_exceeded", "Document count limit reached.");

        if (request.AdditionalFolderCount > 0)
        {
            var folderCount = await db.Folders.CountAsync(f => f.UserId == userId, ct);
            if (plan.MaxFolderCount.HasValue && folderCount + request.AdditionalFolderCount > plan.MaxFolderCount.Value)
                throw new PlanException(StatusCodes.Status402PaymentRequired, "folder_count_exceeded", "Folder count limit reached.");
        }

        if (request.TargetFolderId.HasValue)
        {
            var current = await db.Documents.CountAsync(d => d.FolderId == request.TargetFolderId.Value && d.UserId == userId, ct);
            var max = plan.MaxDocsPerFolder ?? DocumentService.MaxDocumentsPerFolder;
            if (current + request.AdditionalDocumentsInTargetFolder > max)
                throw new DocumentException(409, "folder_full", $"This folder already has {current} document(s), which is the maximum ({max}).");
        }

        var maxDocumentsPerFolder = plan.MaxDocsPerFolder ?? DocumentService.MaxDocumentsPerFolder;
        if (request.NewFolderDocumentCount > maxDocumentsPerFolder)
            throw new DocumentException(409, "folder_full", $"This folder would have {request.NewFolderDocumentCount} document(s), which exceeds the maximum ({maxDocumentsPerFolder}).");
    }

    private static void ValidateNonNegativeDeltas(PlanCapacityRequest request)
    {
        if (request.AdditionalDocumentCount < 0) throw new ArgumentOutOfRangeException(nameof(request.AdditionalDocumentCount));
        if (request.AdditionalFolderCount < 0) throw new ArgumentOutOfRangeException(nameof(request.AdditionalFolderCount));
        if (request.AdditionalDocumentsInTargetFolder < 0) throw new ArgumentOutOfRangeException(nameof(request.AdditionalDocumentsInTargetFolder));
        if (request.NewFolderDocumentCount < 0) throw new ArgumentOutOfRangeException(nameof(request.NewFolderDocumentCount));
    }

    private sealed record LockedUserPlan(User User, Plan Plan);
}
