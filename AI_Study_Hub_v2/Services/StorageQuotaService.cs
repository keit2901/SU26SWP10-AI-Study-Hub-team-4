using AI_Study_Hub_v2.Data;
using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Dtos;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

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

        // Load user to resolve internal ID (immutable mapping — safe to cache for this request).
        var user = await _db.Users
            .FirstOrDefaultAsync(u => u.SupabaseUserId == supabaseUserId, ct)
            ?? throw new PlanException(404, "user_not_found", "User profile not found.");

        var userId = user.Id;

        // Atomic UPDATE...WHERE...RETURNING — races are resolved by the database.
        var sql = """
            UPDATE users u
            SET storage_used_bytes = storage_used_bytes + {0}
            FROM user_plans up
            JOIN plans p ON p.id = up.plan_id
            WHERE u.id = {1}
              AND up.user_id = u.id
              AND up.status = 'active'
              AND (up.expires_at IS NULL OR up.expires_at > NOW())
              AND (p.storage_quota_bytes IS NULL OR u.storage_used_bytes + {0} <= p.storage_quota_bytes)
              AND (p.max_document_count IS NULL OR
                   (SELECT COUNT(*) FROM documents d WHERE d.user_id = u.id) < p.max_document_count)
            RETURNING u.id
            """;

        var result = await _db.Database
            .SqlQueryRaw<Guid>(sql, fileSizeBytes, userId)
            .ToListAsync(ct);

        if (result.Count == 0)
        {
            // Determine which limit was exceeded for a better error message.
            var plan = await GetEffectivePlanAsync(userId, ct);
            var currentCount = await _db.Documents.CountAsync(d => d.UserId == userId, ct);

            if (plan.MaxDocumentCount.HasValue && currentCount >= plan.MaxDocumentCount.Value)
            {
                throw new PlanException(
                    StatusCodes.Status402PaymentRequired,
                    "document_count_exceeded",
                    $"Document count exceeded. You have {currentCount} of {plan.MaxDocumentCount.Value} documents.");
            }

            throw new PlanException(
                StatusCodes.Status402PaymentRequired,
                "storage_quota_exceeded",
                $"Storage quota exceeded. Used: {user.StorageUsedBytes:N0} bytes, " +
                $"Quota: {plan.StorageQuotaBytes:N0} bytes, " +
                $"Attempted: {fileSizeBytes:N0} bytes.");
        }

        return new StorageReservation(userId, fileSizeBytes, DateTimeOffset.UtcNow);
    }

    public Task ConfirmReservationAsync(StorageReservation reservation, CancellationToken ct)
    {
        // Reservation is already applied in ReserveUploadAsync — no-op.
        return Task.CompletedTask;
    }

    public async Task ReleaseReservationAsync(StorageReservation reservation, CancellationToken ct)
    {
        var user = await _db.Users
            .FirstOrDefaultAsync(u => u.Id == reservation.UserId, ct);
        if (user is null)
        {
            return;
        }

        user.StorageUsedBytes = Math.Max(0, user.StorageUsedBytes - reservation.ReservedBytes);
        await _db.SaveChangesAsync(ct);
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

        var effectivePlan = await GetEffectivePlanAsync(user.Id, ct);

        return new StorageQuotaSnapshotDto(
            user.StorageUsedBytes,
            effectivePlan.StorageQuotaBytes,
            effectivePlan.PlanKey,
            effectivePlan.DisplayName);
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
