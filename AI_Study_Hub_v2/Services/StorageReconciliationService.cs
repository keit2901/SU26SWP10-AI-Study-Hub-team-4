using System.Data;
using AI_Study_Hub_v2.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace AI_Study_Hub_v2.Services;

public sealed class StorageReconciliationService(IServiceScopeFactory scopeFactory, ILogger<StorageReconciliationService> logger) : IStorageReconciliationService
{
    public async Task<IReadOnlyList<StorageDiscrepancy>> ReconcileAllAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userIds = await db.Users.AsNoTracking().Select(user => user.Id).ToListAsync(ct);
        var results = new List<StorageDiscrepancy>();
        foreach (var userId in userIds)
        {
            var result = await ReconcileUserCoreAsync(userId, ct);
            if (result is not null) results.Add(result);
        }
        return results;
    }

    public async Task ReconcileUserAsync(Guid userId, CancellationToken ct) => await ReconcileUserCoreAsync(userId, ct);

    private async Task<StorageDiscrepancy?> ReconcileUserCoreAsync(Guid userId, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var transaction = await BeginTransactionAsync(db, ct);
        try
        {
            if (db.Database.IsRelational())
                await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM public.users WHERE id = {userId} FOR UPDATE", ct);

            var user = await db.Users.SingleOrDefaultAsync(item => item.Id == userId, ct);
            if (user is null)
            {
                if (transaction is not null) await transaction.CommitAsync(ct);
                logger.LogInformation("Storage reconciliation: user {UserId} is consistent — no fix needed.", userId);
                return null;
            }

            var documentBytes = await db.Documents.Where(document => document.UserId == userId)
                .Select(document => document.FileSizeBytes).ToListAsync(ct);
            var reservationBytes = await db.SharedFolderCopyOperations
                .Where(operation => operation.DestinationUserId == userId
                    && !db.Folders.Any(folder => folder.Id == operation.DestinationFolderId && folder.UserId == userId))
                .Select(operation => operation.ReservedStorageBytes).ToListAsync(ct);
            long expectedBytes = 0;
            checked
            {
                foreach (var bytes in documentBytes) expectedBytes += bytes;
                foreach (var bytes in reservationBytes) expectedBytes += bytes;
            }
            if (user.StorageUsedBytes == expectedBytes)
            {
                if (transaction is not null) await transaction.CommitAsync(ct);
                logger.LogInformation("Storage reconciliation: user {UserId} is consistent — no fix needed.", userId);
                return null;
            }

            var result = new StorageDiscrepancy(user.Id, user.Username, user.StorageUsedBytes, expectedBytes, checked(expectedBytes - user.StorageUsedBytes));
            user.StorageUsedBytes = expectedBytes;
            await db.SaveChangesAsync(ct);
            if (transaction is not null) await transaction.CommitAsync(ct);
            logger.LogInformation("Storage reconciliation: auto-fixed user {UserId}: cached={Cached}, actual={Actual}, delta={Delta}", user.Id, result.CachedBytes, expectedBytes, result.Delta);
            return result;
        }
        catch
        {
            if (transaction is not null)
            {
                try { await transaction.RollbackAsync(CancellationToken.None); } catch { }
            }
            throw;
        }
    }

    private static async Task<IDbContextTransaction?> BeginTransactionAsync(AppDbContext db, CancellationToken ct)
    {
        if (!db.Database.IsRelational()) return null;
        return await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
    }
}
