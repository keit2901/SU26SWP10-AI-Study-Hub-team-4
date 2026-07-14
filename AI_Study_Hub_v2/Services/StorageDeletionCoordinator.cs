using System.Data;
using AI_Study_Hub_v2.Data;
using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Services.Supabase;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace AI_Study_Hub_v2.Services;

public sealed class StorageDeletionCoordinator : IStorageDeletionCoordinator
{
    private readonly AppDbContext _db;
    private readonly ISupabaseStorageClient _storage;
    private readonly ILogger<StorageDeletionCoordinator> _logger;

    public StorageDeletionCoordinator(AppDbContext db, ISupabaseStorageClient storage, ILogger<StorageDeletionCoordinator> logger)
    {
        _db = db;
        _storage = storage;
        _logger = logger;
    }

    public async Task<bool> DeleteOwnedDocumentAsync(Guid documentId, Guid ownerUserId, CancellationToken ct)
    {
        var candidate = await FindDocumentAsync(documentId, ownerUserId, ct);
        return candidate is not null && await DeleteDocumentsAsync([candidate], ct);
    }

    public async Task<bool> DeletePrivilegedDocumentAsync(Guid documentId, CancellationToken ct)
    {
        var candidate = await _db.Documents.AsNoTracking().Where(d => d.Id == documentId)
            .Select(d => new DeletionCandidate(d.Id, d.UserId, d.StoragePath, d.FileSizeBytes)).SingleOrDefaultAsync(ct);
        return candidate is not null && await DeleteDocumentsAsync([candidate], ct);
    }

    public async Task<bool> DeleteOwnedFolderAsync(Guid folderId, Guid ownerUserId, CancellationToken ct)
    {
        if (!await _db.Folders.AsNoTracking().AnyAsync(f => f.Id == folderId && f.UserId == ownerUserId, ct)) return false;

        var candidates = await _db.Documents.AsNoTracking().Where(d => d.FolderId == folderId && d.UserId == ownerUserId)
            .Select(d => new DeletionCandidate(d.Id, d.UserId, d.StoragePath, d.FileSizeBytes)).ToListAsync(ct);
        foreach (var candidate in candidates)
        {
            ct.ThrowIfCancellationRequested();
            await _storage.DeleteAsync(DocumentService.BucketName, candidate.StoragePath, ct);
        }

        return await FinalizeFolderAsync(folderId, ownerUserId, candidates, ct);
    }

    private async Task<DeletionCandidate?> FindDocumentAsync(Guid documentId, Guid ownerUserId, CancellationToken ct) =>
        await _db.Documents.AsNoTracking().Where(d => d.Id == documentId && d.UserId == ownerUserId)
            .Select(d => new DeletionCandidate(d.Id, d.UserId, d.StoragePath, d.FileSizeBytes)).SingleOrDefaultAsync(ct);

    private async Task<bool> DeleteDocumentsAsync(IReadOnlyList<DeletionCandidate> candidates, CancellationToken ct)
    {
        foreach (var candidate in candidates)
        {
            ct.ThrowIfCancellationRequested();
            await _storage.DeleteAsync(DocumentService.BucketName, candidate.StoragePath, ct);
        }

        return await FinalizeDocumentsAsync(candidates, ct);
    }

    private async Task<bool> FinalizeDocumentsAsync(IReadOnlyList<DeletionCandidate> candidates, CancellationToken ct)
    {
        await using var transaction = await BeginTransactionAsync(ct);
        try
        {
            var matched = await FindMatchingDocumentsAsync(candidates, ct);
            if (matched.Count == 0)
            {
                await CommitAsync(transaction, ct);
                return false;
            }

            LogConflicts(candidates, matched);
            await RemoveAndChargeAsync(matched, ct);
            await CommitAsync(transaction, ct);
            return true;
        }
        catch
        {
            await RollbackPreservingOriginalAsync(transaction);
            throw;
        }
    }

    private async Task<bool> FinalizeFolderAsync(Guid folderId, Guid ownerUserId, IReadOnlyList<DeletionCandidate> candidates, CancellationToken ct)
    {
        await using var transaction = await BeginTransactionAsync(ct);
        try
        {
            var folder = await _db.Folders.SingleOrDefaultAsync(f => f.Id == folderId && f.UserId == ownerUserId, ct);
            if (folder is null)
            {
                await CommitAsync(transaction, ct);
                return false;
            }

            var matched = await FindMatchingDocumentsAsync(candidates, ct, ownerUserId);
            LogConflicts(candidates, matched);
            await RemoveAndChargeAsync(matched, ct, folder);
            await CommitAsync(transaction, ct);
            _logger.LogInformation("Storage-first folder deletion finalized: FolderId={FolderId} DocumentCount={DocumentCount}", folderId, matched.Count);
            return true;
        }
        catch
        {
            await RollbackPreservingOriginalAsync(transaction);
            throw;
        }
    }

    private async Task<List<Document>> FindMatchingDocumentsAsync(IReadOnlyList<DeletionCandidate> candidates, CancellationToken ct, Guid? expectedOwnerUserId = null)
    {
        if (candidates.Count == 0) return [];
        var byId = candidates.ToDictionary(c => c.DocumentId);
        var documents = await _db.Documents.Where(d => byId.Keys.Contains(d.Id)).ToListAsync(ct);
        return documents.Where(d => byId.TryGetValue(d.Id, out var candidate)
            && d.UserId == candidate.UserId
            && (!expectedOwnerUserId.HasValue || d.UserId == expectedOwnerUserId.Value)
            && d.StoragePath == candidate.StoragePath && d.FileSizeBytes == candidate.FileSizeBytes).ToList();
    }

    private async Task RemoveAndChargeAsync(IReadOnlyList<Document> documents, CancellationToken ct, Folder? folder = null)
    {
        var documentIds = documents.Select(d => d.Id).ToList();
        if (documentIds.Count > 0)
        {
            var escalationItems = await _db.DocumentEscalationItems.Where(item => documentIds.Contains(item.DocumentId)).ToListAsync(ct);
            _db.DocumentEscalationItems.RemoveRange(escalationItems);
            _db.Documents.RemoveRange(documents);
        }
        if (folder is not null) _db.Folders.Remove(folder);
        await _db.SaveChangesAsync(ct);

        foreach (var charge in documents.GroupBy(d => d.UserId).Select(group => new { UserId = group.Key, Bytes = group.Sum(d => d.FileSizeBytes) }))
        {
            if (charge.Bytes <= 0) continue;
            if (_db.Database.IsRelational())
            {
                await _db.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE users SET storage_used_bytes = GREATEST(0, storage_used_bytes - {charge.Bytes}) WHERE id = {charge.UserId}", ct);
            }
            else
            {
                var user = await _db.Users.SingleAsync(u => u.Id == charge.UserId, ct);
                user.StorageUsedBytes = Math.Max(0, user.StorageUsedBytes - charge.Bytes);
                await _db.SaveChangesAsync(ct);
            }
        }
    }

    private void LogConflicts(IReadOnlyList<DeletionCandidate> candidates, IReadOnlyList<Document> matched)
    {
        var matchedIds = matched.Select(d => d.Id).ToHashSet();
        foreach (var candidate in candidates.Where(candidate => !matchedIds.Contains(candidate.DocumentId)))
            _logger.LogWarning("Storage finalization skipped changed or missing document: DocumentId={DocumentId} UserId={UserId}", candidate.DocumentId, candidate.UserId);
    }

    private Task<IDbContextTransaction?> BeginTransactionAsync(CancellationToken ct) => _db.Database.IsRelational()
        ? BeginRelationalTransactionAsync(ct) : Task.FromResult<IDbContextTransaction?>(null);
    private async Task<IDbContextTransaction?> BeginRelationalTransactionAsync(CancellationToken ct) => await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
    private static Task CommitAsync(IDbContextTransaction? transaction, CancellationToken ct) => transaction?.CommitAsync(ct) ?? Task.CompletedTask;
    private async Task RollbackPreservingOriginalAsync(IDbContextTransaction? transaction)
    {
        if (transaction is null) return;
        try
        {
            await transaction.RollbackAsync(CancellationToken.None);
        }
        catch (Exception rollbackException)
        {
            _logger.LogError(rollbackException, "Storage deletion rollback failed after finalization failure.");
        }
    }

    private sealed record DeletionCandidate(Guid DocumentId, Guid UserId, string StoragePath, long FileSizeBytes);
}
