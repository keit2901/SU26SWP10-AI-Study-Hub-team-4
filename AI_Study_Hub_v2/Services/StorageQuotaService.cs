using AI_Study_Hub_v2.Data;
using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Dtos;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace AI_Study_Hub_v2.Services;

public sealed class StorageQuotaService : IStorageQuotaService
{
    private readonly AppDbContext _db;
    private readonly IPlanService _planService;

    public StorageQuotaService(AppDbContext db, IPlanService planService)
    {
        _db = db;
        _planService = planService;
    }

    public async Task<StorageReservation> ReserveUploadAsync(
        Guid supabaseUserId,
        long fileSizeBytes,
        CancellationToken ct)
    {
        if (fileSizeBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fileSizeBytes));
        }

        await using var tx = await _db.Database
            .BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            // Load user with tracking — row is locked within SERIALIZABLE transaction.
            var user = await _db.Users
                .FirstOrDefaultAsync(u => u.SupabaseUserId == supabaseUserId, ct)
                ?? throw new PlanException(404, "user_not_found", "User profile not found.");

            var userId = user.Id;

            var plan = await GetEffectivePlanAsync(userId, ct);

            // Check storage quota.
            if (plan.StorageQuotaBytes.HasValue
                && user.StorageUsedBytes + fileSizeBytes > plan.StorageQuotaBytes.Value)
            {
                throw new PlanException(
                    StatusCodes.Status402PaymentRequired,
                    "storage_quota_exceeded",
                    $"Storage quota exceeded. Used: {user.StorageUsedBytes:N0} bytes, " +
                    $"Quota: {plan.StorageQuotaBytes:N0} bytes, " +
                    $"Attempted: {fileSizeBytes:N0} bytes.");
            }

            user.StorageUsedBytes += fileSizeBytes;
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return new StorageReservation(userId, fileSizeBytes, DateTimeOffset.UtcNow);
        }
        catch
        {
            try
            {
                await tx.RollbackAsync(CancellationToken.None);
            }
            catch
            {
                // Preserve the original reservation failure; the transaction dispose path still runs.
            }
            throw;
        }
    }

    public Task ConfirmReservationAsync(StorageReservation reservation, CancellationToken ct)
    {
        // Reservation is already applied in ReserveUploadAsync — no-op.
        return Task.CompletedTask;
    }

    public async Task ReleaseReservationAsync(StorageReservation reservation, CancellationToken ct)
    {
        await _db.Database.ExecuteSqlRawAsync(
            "UPDATE users SET storage_used_bytes = GREATEST(0, storage_used_bytes - {0}) WHERE id = {1}",
            reservation.ReservedBytes, reservation.UserId);
    }

    public async Task RecordDeleteAsync(
        Guid supabaseUserId,
        long fileSizeBytes,
        CancellationToken ct)
    {
        if (fileSizeBytes <= 0)
        {
            return;
        }

        // Atomic UPDATE: no read-then-write race condition.
        // GREATEST(0, ...) ensures storage_used_bytes never goes negative.
        await _db.Database.ExecuteSqlRawAsync(
            $"UPDATE users SET storage_used_bytes = GREATEST(0, storage_used_bytes - {{0}}) WHERE supabase_user_id = {{1}}",
            fileSizeBytes, supabaseUserId);
    }

    public async Task<StorageQuotaSnapshotDto> GetSnapshotAsync(
        Guid supabaseUserId,
        CancellationToken ct)
    {
        var user = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.SupabaseUserId == supabaseUserId, ct)
            ?? throw new PlanException(404, "user_not_found", "User profile not found.");

        var activeUserPlan = await _db.UserPlans
            .AsNoTracking()
            .Include(up => up.Plan)
            .Where(up => up.UserId == user.Id
                         && up.Status == "active"
                         && (up.ExpiresAt == null || up.ExpiresAt > DateTimeOffset.UtcNow))
            .OrderByDescending(up => up.AssignedAt)
            .FirstOrDefaultAsync(ct);

        var effectivePlan = activeUserPlan?.Plan ?? _planService.GetFreePlan();
        var hasExpiredPaidPlan = await _db.UserPlans
            .AsNoTracking()
            .Include(up => up.Plan)
            .AnyAsync(up => up.UserId == user.Id
                && up.Status == "expired"
                && up.Plan.PlanKey != "free", ct);

        return new StorageQuotaSnapshotDto(
            user.StorageUsedBytes,
            effectivePlan.StorageQuotaBytes,
            effectivePlan.PlanKey,
            effectivePlan.DisplayName,
            activeUserPlan?.ExpiresAt,
            effectivePlan.MaxFileSizeBytes,
            effectivePlan.MaxDocumentCount,
            effectivePlan.MaxFolderCount,
            effectivePlan.MaxDocsPerFolder,
            hasExpiredPaidPlan);
    }

    public async Task ValidateDocumentCountAsync(
        Guid supabaseUserId,
        CancellationToken ct)
    {
        var user = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.SupabaseUserId == supabaseUserId, ct)
            ?? throw new PlanException(404, "user_not_found", "User profile not found.");

        var effectivePlan = await GetEffectivePlanAsync(user.Id, ct);

        if (!effectivePlan.MaxDocumentCount.HasValue)
        {
            return; // Unlimited documents.
        }

        var currentCount = await _db.Documents
            .CountAsync(d => d.UserId == user.Id, ct);

        if (currentCount >= effectivePlan.MaxDocumentCount.Value)
        {
            throw new PlanException(
                StatusCodes.Status402PaymentRequired,
                "document_count_exceeded",
                $"Document limit ({effectivePlan.MaxDocumentCount.Value}) reached. " +
                $"You have {currentCount} document(s).");
        }
    }

    public async Task ValidateFolderCountAsync(
        Guid supabaseUserId,
        CancellationToken ct)
    {
        var user = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.SupabaseUserId == supabaseUserId, ct)
            ?? throw new PlanException(404, "user_not_found", "User profile not found.");

        var effectivePlan = await GetEffectivePlanAsync(user.Id, ct);

        if (!effectivePlan.MaxFolderCount.HasValue)
        {
            return; // Unlimited folders.
        }

        var currentCount = await _db.Folders
            .CountAsync(f => f.UserId == user.Id, ct);

        if (currentCount >= effectivePlan.MaxFolderCount.Value)
        {
            throw new PlanException(
                StatusCodes.Status402PaymentRequired,
                "folder_count_exceeded",
                $"Folder limit ({effectivePlan.MaxFolderCount.Value}) reached. " +
                $"You have {currentCount} folder(s).");
        }
    }

    private async Task<Plan> GetEffectivePlanAsync(Guid userId, CancellationToken ct)
    {
        var activePlan = await _db.UserPlans
            .AsNoTracking()
            .Include(up => up.Plan)
            .Where(up => up.UserId == userId
                         && up.Status == "active"
                         && (up.ExpiresAt == null || up.ExpiresAt > DateTimeOffset.UtcNow))
            .Select(up => up.Plan)
            .FirstOrDefaultAsync(ct);

        return activePlan ?? _planService.GetFreePlan();
    }
}
