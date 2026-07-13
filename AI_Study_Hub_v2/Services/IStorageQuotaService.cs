using AI_Study_Hub_v2.Dtos;

namespace AI_Study_Hub_v2.Services;

/// <summary>
/// Reserve-then-commit pattern for storage quota enforcement.
/// </summary>
public interface IStorageQuotaService
{
    /// <summary>
    /// Atomically reserves storage bytes for an upload. Throws <see cref="PlanException"/>
    /// with status 402 and code "storage_quota_exceeded" if the user would exceed their
    /// plan quota.
    /// </summary>
    Task<StorageReservation> ReserveUploadAsync(Guid supabaseUserId, long fileSizeBytes, CancellationToken ct);

    /// <summary>
    /// No-op — the reservation is already applied in <see cref="ReserveUploadAsync"/>.
    /// Exists for symmetry with the reserve-then-commit pattern.
    /// </summary>
    Task ConfirmReservationAsync(StorageReservation reservation, CancellationToken ct);

    /// <summary>
    /// Releases previously reserved bytes (rolls back the storage increment).
    /// </summary>
    Task ReleaseReservationAsync(StorageReservation reservation, CancellationToken ct);

    /// <summary>
    /// Records a storage decrease after a successful document delete on Supabase.
    /// </summary>
    Task RecordDeleteAsync(Guid supabaseUserId, long fileSizeBytes, CancellationToken ct);

    /// <summary>
    /// Returns current storage usage and quota for a user.
    /// </summary>
    Task<StorageQuotaSnapshotDto> GetSnapshotAsync(Guid supabaseUserId, CancellationToken ct);

    /// <summary>
    /// Validates that the user has not exceeded their plan's max document count.
    /// Throws <see cref="PlanException"/> with status 402 and code "document_count_exceeded"
    /// if the user would exceed their plan limit.
    /// </summary>
    Task ValidateDocumentCountAsync(Guid supabaseUserId, CancellationToken ct);

    /// <summary>
    /// Validates that the user has not exceeded their plan's max folder count.
    /// Throws <see cref="PlanException"/> with status 402 and code "folder_count_exceeded"
    /// if the user would exceed their plan limit.
    /// </summary>
    Task ValidateFolderCountAsync(Guid supabaseUserId, CancellationToken ct);
}

public sealed record StorageReservation(Guid UserId, long ReservedBytes, DateTimeOffset ReservedAt);
