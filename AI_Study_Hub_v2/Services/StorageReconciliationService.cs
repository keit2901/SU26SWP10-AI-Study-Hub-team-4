using System.Runtime.CompilerServices;
using AI_Study_Hub_v2.Data;
using Microsoft.EntityFrameworkCore;

namespace AI_Study_Hub_v2.Services;

public sealed class StorageReconciliationService : IStorageReconciliationService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<StorageReconciliationService> _logger;

    public StorageReconciliationService(
        IServiceScopeFactory scopeFactory,
        ILogger<StorageReconciliationService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<IReadOnlyList<StorageDiscrepancy>> ReconcileAllAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var discrepancies = await db.Database
            .SqlQuery<DiscrepancyRow>(FormattableStringFactory.Create(
                @"SELECT u.id, u.username, u.storage_used_bytes AS ""CachedBytes"",
                         COALESCE(SUM(d.file_size_bytes), 0) AS ""ActualBytes""
                  FROM users u
                  LEFT JOIN documents d ON d.user_id = u.id
                  GROUP BY u.id
                  HAVING u.storage_used_bytes != COALESCE(SUM(d.file_size_bytes), 0)"))
            .ToListAsync(ct);

        var results = new List<StorageDiscrepancy>(discrepancies.Count);

        foreach (var row in discrepancies)
        {
            var delta = row.ActualBytes - row.CachedBytes;
            var result = new StorageDiscrepancy(
                row.Id,
                row.Username,
                row.CachedBytes,
                row.ActualBytes,
                delta);

            if (delta != 0)
            {
                // Auto-fix in both directions.
                await db.Users
                    .Where(u => u.Id == row.Id)
                    .ExecuteUpdateAsync(
                        s => s.SetProperty(u => u.StorageUsedBytes, row.ActualBytes),
                        ct);
                _logger.LogInformation(
                    "Storage reconciliation: auto-fixed user {UserId}: " +
                    "cached={Cached}, actual={Actual}, delta={Delta}",
                    row.Id, row.CachedBytes, row.ActualBytes, delta);
            }

            results.Add(result);
        }

        return results;
    }

    public async Task ReconcileUserAsync(Guid userId, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var row = await db.Database
            .SqlQuery<DiscrepancyRow>(FormattableStringFactory.Create(
                @"SELECT u.id, u.username, u.storage_used_bytes AS ""CachedBytes"",
                         COALESCE(SUM(d.file_size_bytes), 0) AS ""ActualBytes""
                  FROM users u
                  LEFT JOIN documents d ON d.user_id = u.id
                  WHERE u.id = {0}
                  GROUP BY u.id
                  HAVING u.storage_used_bytes != COALESCE(SUM(d.file_size_bytes), 0)", userId))
            .FirstOrDefaultAsync(ct);

        if (row is null)
        {
            _logger.LogInformation(
                "Storage reconciliation: user {UserId} is consistent — no fix needed.", userId);
            return;
        }

        var delta = row.ActualBytes - row.CachedBytes;

        if (delta != 0)
        {
            await db.Users
                .Where(u => u.Id == row.Id)
                .ExecuteUpdateAsync(
                    s => s.SetProperty(u => u.StorageUsedBytes, row.ActualBytes),
                    ct);
            _logger.LogInformation(
                "Storage reconciliation: auto-fixed user {UserId}: " +
                "cached={Cached}, actual={Actual}, delta={Delta}",
                row.Id, row.CachedBytes, row.ActualBytes, delta);
        }
    }

    // ReSharper disable once ClassNeverInstantiated.Local
    private sealed class DiscrepancyRow
    {
        public Guid Id { get; set; }
        public string Username { get; set; } = string.Empty;
        public long CachedBytes { get; set; }
        public long ActualBytes { get; set; }
    }
}
