namespace AI_Study_Hub_v2.Services;

/// <summary>
/// Periodically reconciles cached storage counters against actual document sizes.
/// </summary>
public interface IStorageReconciliationService
{
    /// <summary>
    /// Reconciles all users. For users where cached bytes &lt; actual bytes,
    /// auto-fixes the counter. For users where cached bytes &gt; actual bytes,
    /// logs a warning only (orphaned records may indicate incomplete deletes).
    /// </summary>
    Task<IReadOnlyList<StorageDiscrepancy>> ReconcileAllAsync(CancellationToken ct);

    /// <summary>
    /// Reconciles a single user.
    /// </summary>
    Task ReconcileUserAsync(Guid userId, CancellationToken ct);
}

public sealed record StorageDiscrepancy(
    Guid UserId,
    string Username,
    long CachedBytes,
    long ActualBytes,
    long Delta);
