using System.Data;
using System.Text.Json;
using AI_Study_Hub_v2.Data;
using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Dtos;
using AI_Study_Hub_v2.Services.Supabase;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Pgvector;

namespace AI_Study_Hub_v2.Services;

/// <summary>Durable shared-folder copy including bounded recovery of durable operations.</summary>
public sealed class SharedFolderCopyCoordinator : ISharedFolderCopyCoordinator
{
    private const int ManifestVersion = 1;
    private const int CleanupErrorLimit = 1000;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ISupabaseStorageClient _storage;
    private readonly IPlanCapacityGuard _capacityGuard;
    private readonly ILogger<SharedFolderCopyCoordinator> _logger;

    public SharedFolderCopyCoordinator(IServiceScopeFactory scopeFactory, ISupabaseStorageClient storage,
        IPlanCapacityGuard capacityGuard, ILogger<SharedFolderCopyCoordinator> logger)
    {
        _scopeFactory = scopeFactory;
        _storage = storage;
        _capacityGuard = capacityGuard;
        _logger = logger;
    }

    public async Task<FolderDto> CopyAsync(Guid destinationSupabaseUserId, Guid sourceFolderId, CancellationToken ct)
    {
        var destination = await ResolveDestinationAsync(destinationSupabaseUserId, ct);
        var recovered = await RecoverExistingOperationAsync(destination.Id, sourceFolderId);
        if (recovered is not null) return recovered;
        if (!destination.IsActive)
            throw new DocumentException(403, "user_inactive", "User account is inactive and cannot copy folders.");

        var prepared = await PrepareAsync(destination, sourceFolderId, ct);
        try
        {
            if (!await TryTransitionAsync(prepared.OperationId, SharedFolderCopyOperation.Reserved, SharedFolderCopyOperation.Copying, null, ct))
                throw new CopyOwnershipLostException();
            await CopyStorageAsync(prepared, ct);
            return await FinalizeAsync(prepared, ct);
        }
        catch (CopyOwnershipLostException)
        {
            throw new DocumentException(409, "folder_copy_in_progress", "Copy ownership was transferred to recovery.");
        }
        catch (Exception error)
        {
            var cleanedUp = await CompensateAsync(prepared.OperationId, error);
            if (error is OperationCanceledException) throw;
            if (!cleanedUp)
                throw new DocumentException(503, "folder_copy_cleanup_pending", "Copy cleanup is pending.");
            if (error is PlanException or DocumentException) throw;
            throw new DocumentException(502, "folder_copy_failed", "The shared folder could not be copied safely.");
        }
    }

    private async Task<User> ResolveDestinationAsync(Guid destinationSupabaseUserId, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var destination = await db.Users.AsNoTracking().SingleOrDefaultAsync(user => user.SupabaseUserId == destinationSupabaseUserId, ct)
            ?? throw new DocumentException(404, "user_not_found", "User profile not found.");
        return destination;
    }

    private async Task<PreparedCopy> PrepareAsync(User destination, Guid sourceFolderId, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var source = await LoadSnapshotAsync(db, sourceFolderId, ct);
        var now = DateTimeOffset.UtcNow;
        var manifest = CreateManifest(source, destination.Id, now);
        var request = new PlanCapacityRequest(source.Documents.Count, 1, null, 0, source.Documents.Count);
        await using var transaction = await BeginTransactionAsync(db, ct);
        try
        {
            if (await db.SharedFolderCopyOperations.AnyAsync(operation => operation.DestinationUserId == destination.Id, ct))
                throw new DocumentException(409, "folder_copy_in_progress", "A folder copy is already in progress.");

            var name = await GetCopyNameAsync(db, destination.Id, source.Name, ct);
            await _capacityGuard.LockValidateAndReserveStorageAsync(db, destination.Id, request, source.TotalBytes, ct);
            var operationId = Guid.NewGuid();
            var folderId = Guid.NewGuid();
            db.SharedFolderCopyOperations.Add(new SharedFolderCopyOperation
            {
                Id = operationId, DestinationUserId = destination.Id, SourceFolderId = source.Id,
                DestinationFolderId = folderId, DestinationName = name, Status = SharedFolderCopyOperation.Reserved,
                ReservedStorageBytes = source.TotalBytes, ManifestJson = JsonSerializer.Serialize(manifest),
                CreatedAt = now, UpdatedAt = now,
            });
            await db.SaveChangesAsync(ct);
            if (transaction is not null)
            {
                try { await transaction.CommitAsync(ct); }
                catch (Exception commitError)
                {
                    if (await OperationExistsAsync(operationId))
                        return new PreparedCopy(operationId, destination.Id, folderId, name, now, source, manifest, request);
                    _logger.LogWarning(commitError, "Copy reservation commit outcome is not durable for operation {OperationId}.", operationId);
                    throw;
                }
            }
            return new PreparedCopy(operationId, destination.Id, folderId, name, now, source, manifest, request);
        }
        catch (DbUpdateException exception)
        {
            await RollbackAsync(transaction);
            if (await HasDestinationOperationAsync(destination.Id))
            {
                _logger.LogInformation(exception, "Copy reservation raced for destination {DestinationUserId}.", destination.Id);
                throw new DocumentException(409, "folder_copy_in_progress", "A folder copy is already in progress.");
            }
            throw;
        }
        catch
        {
            await RollbackAsync(transaction);
            throw;
        }
    }

    private async Task CopyStorageAsync(PreparedCopy prepared, CancellationToken ct)
    {
        foreach (var item in prepared.Manifest.Items)
        {
            var (stream, contentType) = await _storage.DownloadFileAsync(DocumentService.BucketName, item.SourceStoragePath, ct);
            await using (stream)
            {
                if (stream.CanSeek) stream.Position = 0;
                await _storage.UploadAsync(DocumentService.BucketName, item.DestinationStoragePath, stream, contentType, false, ct);
            }
            if (!await TryHeartbeatAsync(prepared.OperationId, ct))
                throw new CopyOwnershipLostException();
        }
    }

    private async Task<FolderDto?> RecoverExistingOperationAsync(Guid destinationUserId, Guid requestedSourceFolderId)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var operation = await db.SharedFolderCopyOperations.AsNoTracking()
            .SingleOrDefaultAsync(item => item.DestinationUserId == destinationUserId, CancellationToken.None);
        if (operation is null) return null;

        var committedFolder = await db.Folders.AsNoTracking()
            .SingleOrDefaultAsync(folder => folder.Id == operation.DestinationFolderId && folder.UserId == destinationUserId, CancellationToken.None);
        if (committedFolder is not null)
        {
            await RemoveOperationWithoutReleaseAsync(operation.Id);
            if (operation.SourceFolderId == requestedSourceFolderId)
                return await ToFolderDtoAsync(db, committedFolder, CancellationToken.None);
            return null;
        }

        var stale = operation.Status == SharedFolderCopyOperation.CompensationRequired
            || ((operation.Status == SharedFolderCopyOperation.Reserved
                 || operation.Status == SharedFolderCopyOperation.Copying
                 || operation.Status == SharedFolderCopyOperation.Finalizing)
                && operation.UpdatedAt <= DateTimeOffset.UtcNow.AddMinutes(-15));
        if (!stale)
            throw new DocumentException(409, "folder_copy_in_progress", "A folder copy is already in progress.");

        var claimed = await TryClaimForRecoveryAsync(operation);
        if (!claimed)
            throw new DocumentException(409, "folder_copy_in_progress", "A folder copy is already in progress.");

        var cleaned = await CleanupClaimedOperationAsync(operation.Id, DeserializeManifest(operation.ManifestJson),
            new DocumentException(503, "folder_copy_cleanup_pending", "Copy cleanup is pending."));
        if (!cleaned)
            throw new DocumentException(503, "folder_copy_cleanup_pending", "Copy cleanup is pending.");
        return null;
    }

    private async Task<bool> TryClaimForRecoveryAsync(SharedFolderCopyOperation snapshot)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (db.Database.IsRelational())
        {
            var changed = await db.SharedFolderCopyOperations
                .Where(item => item.Id == snapshot.Id && item.Status == snapshot.Status && item.UpdatedAt == snapshot.UpdatedAt)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.Status, SharedFolderCopyOperation.Compensating)
                    .SetProperty(item => item.UpdatedAt, DateTimeOffset.UtcNow), CancellationToken.None);
            return changed == 1;
        }

        var operation = await db.SharedFolderCopyOperations.SingleOrDefaultAsync(item => item.Id == snapshot.Id, CancellationToken.None);
        if (operation is null || operation.Status != snapshot.Status || operation.UpdatedAt != snapshot.UpdatedAt)
            return false;
        operation.Status = SharedFolderCopyOperation.Compensating;
        operation.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(CancellationToken.None);
        return true;
    }

    private async Task RemoveOperationWithoutReleaseAsync(Guid operationId)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var transaction = await BeginTransactionAsync(db, CancellationToken.None);
        try
        {
            var operation = await db.SharedFolderCopyOperations.SingleOrDefaultAsync(item => item.Id == operationId, CancellationToken.None);
            if (operation is not null)
            {
                db.SharedFolderCopyOperations.Remove(operation);
                await db.SaveChangesAsync(CancellationToken.None);
            }
            if (transaction is not null) await transaction.CommitAsync(CancellationToken.None);
        }
        catch
        {
            await RollbackAsync(transaction);
            throw;
        }
    }

    private static async Task<FolderDto> ToFolderDtoAsync(AppDbContext db, Folder folder, CancellationToken ct) => new()
    {
        Id = folder.Id,
        Name = folder.Name,
        Description = folder.Description,
        DocumentCount = await db.Documents.CountAsync(document => document.FolderId == folder.Id, ct),
        IsFavorite = folder.IsFavorite,
        ShareStatus = folder.ShareStatus,
        Icon = folder.Icon,
        CreatedAt = folder.CreatedAt,
        UpdatedAt = folder.UpdatedAt,
    };

    private async Task<FolderDto> FinalizeAsync(PreparedCopy prepared, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var transaction = await BeginTransactionAsync(db, ct);
        try
        {
            if (!await TryTransitionAsync(db, prepared.OperationId, SharedFolderCopyOperation.Copying, SharedFolderCopyOperation.Finalizing, null, ct))
                throw new CopyOwnershipLostException();
            var operation = await db.SharedFolderCopyOperations.SingleAsync(item => item.Id == prepared.OperationId, ct);
            if (operation.Status != SharedFolderCopyOperation.Finalizing)
                throw new CopyOwnershipLostException();
            SourceSnapshot current;
            try
            {
                current = await LoadSnapshotAsync(db, prepared.Source.Id, ct);
            }
            catch (DocumentException)
            {
                throw new DocumentException(409, "folder_copy_conflict", "Shared folder changed during copy.");
            }
            if (!Matches(prepared.Source, current))
                throw new DocumentException(409, "folder_copy_conflict", "Shared folder changed during copy.");

            await _capacityGuard.LockAndValidateAsync(db, prepared.DestinationUserId, prepared.CapacityRequest, ct);
            var folder = new Folder
            {
                Id = prepared.DestinationFolderId, UserId = prepared.DestinationUserId, Name = prepared.DestinationName,
                Description = prepared.Source.Description, Icon = prepared.Source.Icon, IsFavorite = false,
                ShareStatus = FolderStatus.None, SharedAt = null, ShareReviewSource = null, AiReviewReason = null,
                AiReviewConfidence = null, AiReviewFailureCount = 0, HumanReviewReason = null,
                RequiresHumanReview = false, AppealRequestedAt = null, AppealMessage = null,
                CreatedAt = prepared.CreatedAt, UpdatedAt = prepared.CreatedAt,
            };
            db.Folders.Add(folder);
            var paths = prepared.Manifest.Items.ToDictionary(item => item.SourceDocumentId);
            foreach (var document in prepared.Source.Documents)
            {
                var item = paths[document.Id];
                db.Documents.Add(new Document
                {
                    Id = item.DestinationDocumentId, UserId = prepared.DestinationUserId, FolderId = folder.Id,
                    FileName = document.FileName, StoragePath = item.DestinationStoragePath, FileSizeBytes = document.FileSizeBytes,
                    MimeType = document.MimeType, SubjectCode = document.SubjectCode, Semester = document.Semester,
                    PageCount = document.PageCount, Status = document.Status, ReviewStatus = DocumentReviewStatus.None,
                    ErrorMessage = document.ErrorMessage, CreatedAt = prepared.CreatedAt, UpdatedAt = prepared.CreatedAt,
                });
            }
            foreach (var chunk in prepared.Source.Chunks)
            {
                db.DocumentChunks.Add(new DocumentChunk
                {
                    Id = Guid.NewGuid(), DocumentId = paths[chunk.DocumentId].DestinationDocumentId,
                    ChunkIndex = chunk.ChunkIndex, PageNumber = chunk.PageNumber, Content = chunk.Content,
                    TokenCount = chunk.TokenCount, Embedding = new Vector(chunk.EmbeddingValues.ToArray()),
                    EmbeddingModel = chunk.EmbeddingModel, CreatedAt = prepared.CreatedAt,
                });
            }
            db.SharedFolderCopyOperations.Remove(operation);
            try
            {
                await db.SaveChangesAsync(ct);
                if (transaction is not null) await transaction.CommitAsync(ct);
            }
            catch (Exception ambiguity)
            {
                var resolution = await ResolveFinalizationAmbiguityAsync(prepared);
                if (resolution.CommittedFolder is not null) return resolution.CommittedFolder;
                if (resolution.OperationStillExists)
                    throw;
                _logger.LogError(ambiguity, "Finalization outcome is unsafe for operation {OperationId} and folder {FolderId}.",
                    prepared.OperationId, prepared.DestinationFolderId);
                throw new DocumentException(502, "folder_copy_finalization_ambiguous", "The shared folder copy finalization could not be confirmed.");
            }
            return new FolderDto { Id = folder.Id, Name = folder.Name, Description = folder.Description,
                DocumentCount = prepared.Source.Documents.Count, IsFavorite = false, ShareStatus = FolderStatus.None,
                Icon = folder.Icon, CreatedAt = folder.CreatedAt, UpdatedAt = folder.UpdatedAt };
        }
        catch
        {
            await RollbackAsync(transaction);
            throw;
        }
    }

    private async Task<bool> CompensateAsync(Guid operationId, Exception originalError)
    {
        var claim = await ClaimCompensationAsync(operationId);
        return claim.Kind switch
        {
            CompensationClaimKind.Missing => true,
            CompensationClaimKind.Claimed => await CleanupClaimedOperationAsync(operationId, claim.Manifest!, originalError),
            CompensationClaimKind.InProgress => false,
            _ => false,
        };
    }

    private async Task<bool> CleanupClaimedOperationAsync(Guid operationId, CopyManifest manifest, Exception originalError)
    {
        var allDeleted = true;
        foreach (var item in manifest.Items)
        {
            try { await _storage.DeleteAsync(DocumentService.BucketName, item.DestinationStoragePath, CancellationToken.None); }
            catch (Exception exception)
            {
                allDeleted = false;
                _logger.LogWarning(exception, "Copy cleanup failed for operation {OperationId}.", operationId);
            }
        }
        if (!allDeleted)
        {
            await MarkCleanupRequiredAsync(operationId, originalError);
            return false;
        }
        try
        {
            await ReleaseAndRemoveAsync(operationId);
            return true;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Copy reservation release failed for operation {OperationId}.", operationId);
            await MarkCleanupRequiredAsync(operationId, originalError);
            return false;
        }
    }

    private async Task<FinalizationResolution> ResolveFinalizationAmbiguityAsync(PreparedCopy prepared)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var folder = await db.Folders.AsNoTracking().SingleOrDefaultAsync(item => item.Id == prepared.DestinationFolderId, CancellationToken.None);
        var operation = await db.SharedFolderCopyOperations.AsNoTracking().SingleOrDefaultAsync(item => item.Id == prepared.OperationId, CancellationToken.None);
        if (folder is null) return new FinalizationResolution(null, operation is not null);
        if (operation is not null) await RemoveOperationWithoutReleaseAsync(operation.Id);
        return new FinalizationResolution(await ToFolderDtoAsync(db, folder, CancellationToken.None), false);
    }

    private async Task<CompensationClaim> ClaimCompensationAsync(Guid operationId)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var operation = await db.SharedFolderCopyOperations.SingleOrDefaultAsync(item => item.Id == operationId, CancellationToken.None);
        if (operation is null) return new CompensationClaim(CompensationClaimKind.Missing, null);
        if (operation.Status == SharedFolderCopyOperation.Compensating)
            return new CompensationClaim(CompensationClaimKind.InProgress, null);
        var manifest = DeserializeManifest(operation.ManifestJson);
        if (!await TryTransitionAsync(db, operation.Id, operation.Status, SharedFolderCopyOperation.Compensating, operation.UpdatedAt, CancellationToken.None))
            return new CompensationClaim(CompensationClaimKind.InProgress, null);
        return new CompensationClaim(CompensationClaimKind.Claimed, manifest);
    }

    private async Task ReleaseAndRemoveAsync(Guid operationId)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var transaction = await BeginTransactionAsync(db, CancellationToken.None);
        try
        {
            var operation = await db.SharedFolderCopyOperations.SingleOrDefaultAsync(item => item.Id == operationId, CancellationToken.None);
            if (operation is null) return;
            await _capacityGuard.LockAndReleaseReservedStorageAsync(db, operation.DestinationUserId, operation.ReservedStorageBytes, CancellationToken.None);
            db.SharedFolderCopyOperations.Remove(operation);
            try
            {
                await db.SaveChangesAsync(CancellationToken.None);
                if (transaction is not null) await transaction.CommitAsync(CancellationToken.None);
            }
            catch
            {
                await RollbackAsync(transaction);
                if (!await OperationExistsAsync(operationId)) return;
                throw;
            }
        }
        catch
        {
            await RollbackAsync(transaction);
            throw;
        }
    }

    private async Task MarkCleanupRequiredAsync(Guid operationId, Exception error)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var operation = await db.SharedFolderCopyOperations.SingleOrDefaultAsync(item => item.Id == operationId, CancellationToken.None);
        if (operation is null) return;
        operation.Status = SharedFolderCopyOperation.CompensationRequired;
        operation.LastError = SanitizeError(error);
        operation.UpdatedAt = DateTimeOffset.UtcNow;
        try
        {
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not persist shared-copy cleanup state for operation {OperationId}.", operationId);
        }
    }

    private async Task<bool> OperationExistsAsync(Guid operationId)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.SharedFolderCopyOperations.AnyAsync(item => item.Id == operationId, CancellationToken.None);
    }

    private async Task<bool> HasDestinationOperationAsync(Guid destinationUserId)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.SharedFolderCopyOperations.AnyAsync(item => item.DestinationUserId == destinationUserId, CancellationToken.None);
    }

    private async Task<bool> TryHeartbeatAsync(Guid operationId, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await TryTransitionAsync(db, operationId, SharedFolderCopyOperation.Copying, SharedFolderCopyOperation.Copying, null, ct);
    }

    private async Task<bool> TryTransitionAsync(Guid operationId, string expectedStatus, string nextStatus, DateTimeOffset? expectedUpdatedAt, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await TryTransitionAsync(db, operationId, expectedStatus, nextStatus, expectedUpdatedAt, ct);
    }

    private static async Task<bool> TryTransitionAsync(AppDbContext db, Guid operationId, string expectedStatus, string nextStatus, DateTimeOffset? expectedUpdatedAt, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        if (db.Database.IsRelational())
        {
            var query = db.SharedFolderCopyOperations.Where(item => item.Id == operationId && item.Status == expectedStatus);
            if (expectedUpdatedAt.HasValue) query = query.Where(item => item.UpdatedAt == expectedUpdatedAt.Value);
            return await query.ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.Status, nextStatus)
                .SetProperty(item => item.UpdatedAt, now), ct) == 1;
        }
        var operation = await db.SharedFolderCopyOperations.SingleOrDefaultAsync(item => item.Id == operationId, ct);
        if (operation is null || operation.Status != expectedStatus || (expectedUpdatedAt.HasValue && operation.UpdatedAt != expectedUpdatedAt.Value)) return false;
        operation.Status = nextStatus;
        operation.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return true;
    }

    private static async Task<SourceSnapshot> LoadSnapshotAsync(AppDbContext db, Guid folderId, CancellationToken ct)
    {
        var folder = await db.Folders.AsNoTracking().SingleOrDefaultAsync(item => item.Id == folderId && item.ShareStatus == FolderStatus.Approved, ct)
            ?? throw new DocumentException(404, "folder_not_found", "Shared folder not found.");
        var documents = await db.Documents.AsNoTracking().Where(item => item.FolderId == folder.Id).OrderBy(item => item.Id).ToListAsync(ct);
        if (documents.Any(item => item.UserId != folder.UserId || item.FolderId != folder.Id || string.IsNullOrWhiteSpace(item.StoragePath) || item.FileSizeBytes < 0)
            || documents.Select(item => item.StoragePath).Distinct(StringComparer.Ordinal).Count() != documents.Count)
            throw new DocumentException(409, "folder_copy_conflict", "Shared folder contents are inconsistent.");
        long total = 0;
        try { foreach (var document in documents) total = checked(total + document.FileSizeBytes); }
        catch (OverflowException) { throw new DocumentException(409, "folder_copy_conflict", "Shared folder size is invalid."); }
        var ids = documents.Select(item => item.Id).ToArray();
        var chunks = ids.Length == 0 || db.Model.FindEntityType(typeof(DocumentChunk)) is null
            ? []
            : await db.DocumentChunks.AsNoTracking().Where(item => ids.Contains(item.DocumentId))
                .OrderBy(item => item.DocumentId).ThenBy(item => item.ChunkIndex).ThenBy(item => item.Id).ToListAsync(ct);
        if (chunks.Any(item => item.Embedding is null))
            throw new DocumentException(409, "folder_copy_conflict", "Shared folder chunks are inconsistent.");
        return new SourceSnapshot(folder.Id, folder.UserId, folder.Name, folder.Description, folder.Icon, folder.ShareStatus, folder.UpdatedAt,
            documents.Select(item => new DocumentSnapshot(item.Id, item.UserId, item.FolderId, item.FileName, item.StoragePath,
                item.FileSizeBytes, item.MimeType, item.SubjectCode, item.Semester, item.PageCount, item.Status, item.ReviewStatus,
                item.ErrorMessage, item.CreatedAt, item.UpdatedAt)).ToArray(),
            chunks.Select(item => new ChunkSnapshot(item.Id, item.DocumentId, item.ChunkIndex, item.PageNumber, item.Content,
                item.TokenCount, item.Embedding.ToArray(), item.EmbeddingModel, item.CreatedAt)).ToArray(), total);
    }

    private static bool Matches(SourceSnapshot expected, SourceSnapshot actual) =>
        expected.Id == actual.Id && expected.UserId == actual.UserId && expected.Name == actual.Name
        && expected.Description == actual.Description && expected.Icon == actual.Icon && expected.ShareStatus == actual.ShareStatus
        && expected.UpdatedAt == actual.UpdatedAt && expected.Documents.SequenceEqual(actual.Documents)
        && expected.Chunks.Count == actual.Chunks.Count && expected.Chunks.Zip(actual.Chunks).All(pair => ChunkMatches(pair.First, pair.Second));

    private static bool ChunkMatches(ChunkSnapshot left, ChunkSnapshot right) => left.Id == right.Id && left.DocumentId == right.DocumentId
        && left.ChunkIndex == right.ChunkIndex && left.PageNumber == right.PageNumber && left.Content == right.Content
        && left.TokenCount == right.TokenCount && left.EmbeddingModel == right.EmbeddingModel && left.CreatedAt == right.CreatedAt
        && left.EmbeddingValues.SequenceEqual(right.EmbeddingValues);

    private static CopyManifest CreateManifest(SourceSnapshot source, Guid userId, DateTimeOffset now) => new(ManifestVersion,
        source.Documents.Select(document =>
        {
            var documentId = Guid.NewGuid();
            return new CopyManifestItem(document.Id, document.StoragePath, documentId,
                $"users/{userId:N}/{now.Year}/{documentId:N}-{SanitizeFileName(document.FileName)}");
        }).ToArray());

    private static async Task<string> GetCopyNameAsync(AppDbContext db, Guid userId, string name, CancellationToken ct)
    {
        var used = (await db.Folders.AsNoTracking().Where(item => item.UserId == userId).Select(item => item.Name).ToListAsync(ct))
            .Concat(await db.SharedFolderCopyOperations.AsNoTracking().Where(item => item.DestinationUserId == userId).Select(item => item.DestinationName).ToListAsync(ct))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!used.Contains(name)) return name;
        for (var index = 1; index < 10_000; index++)
        {
            var suffix = $" ({index})";
            var candidate = (name.Length > 100 - suffix.Length ? name[..(100 - suffix.Length)] : name) + suffix;
            if (!used.Contains(candidate)) return candidate;
        }
        throw new DocumentException(409, "folder_name_conflict", "Could not create a unique name for the saved folder.");
    }

    private static async Task<IDbContextTransaction?> BeginTransactionAsync(AppDbContext db, CancellationToken ct) =>
        db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;

    private static async Task RollbackAsync(IDbContextTransaction? transaction)
    {
        if (transaction is null) return;
        try { await transaction.RollbackAsync(CancellationToken.None); } catch { }
    }

    private static CopyManifest DeserializeManifest(string json)
    {
        var manifest = JsonSerializer.Deserialize<CopyManifest>(json);
        if (manifest is null || manifest.Version != ManifestVersion || manifest.Items.Any(item => item.SourceDocumentId == Guid.Empty
            || item.DestinationDocumentId == Guid.Empty || string.IsNullOrWhiteSpace(item.SourceStoragePath)
            || string.IsNullOrWhiteSpace(item.DestinationStoragePath)))
            throw new DocumentException(409, "folder_copy_conflict", "Copy operation manifest is invalid.");
        return manifest;
    }

    private static string SanitizeFileName(string fileName)
    {
        var safe = new string(Path.GetFileName(fileName).Trim().Select(character => char.IsLetterOrDigit(character) || character is '.' or '_' or '-' ? character : '_').ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "upload.bin" : safe[..Math.Min(80, safe.Length)];
    }

    private static string SanitizeError(Exception error)
    {
        var value = error.Message.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return value.Length <= CleanupErrorLimit ? value : value[..CleanupErrorLimit];
    }

    private sealed record PreparedCopy(Guid OperationId, Guid DestinationUserId, Guid DestinationFolderId, string DestinationName,
        DateTimeOffset CreatedAt, SourceSnapshot Source, CopyManifest Manifest, PlanCapacityRequest CapacityRequest);
    private sealed record FinalizationResolution(FolderDto? CommittedFolder, bool OperationStillExists);
    private sealed record SourceSnapshot(Guid Id, Guid UserId, string Name, string? Description, string? Icon, FolderStatus ShareStatus,
        DateTimeOffset UpdatedAt, IReadOnlyList<DocumentSnapshot> Documents, IReadOnlyList<ChunkSnapshot> Chunks, long TotalBytes);
    private sealed record DocumentSnapshot(Guid Id, Guid UserId, Guid? FolderId, string FileName, string StoragePath, long FileSizeBytes,
        string MimeType, string SubjectCode, string Semester, int? PageCount, DocumentStatus Status, DocumentReviewStatus ReviewStatus,
        string? ErrorMessage, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
    private sealed record ChunkSnapshot(Guid Id, Guid DocumentId, int ChunkIndex, int? PageNumber, string Content, int? TokenCount,
        float[] EmbeddingValues, string? EmbeddingModel, DateTimeOffset CreatedAt);
    private sealed record CopyManifest(int Version, IReadOnlyList<CopyManifestItem> Items);
    private sealed record CopyManifestItem(Guid SourceDocumentId, string SourceStoragePath, Guid DestinationDocumentId, string DestinationStoragePath);
    private enum CompensationClaimKind { Missing, Claimed, InProgress }
    private sealed record CompensationClaim(CompensationClaimKind Kind, CopyManifest? Manifest);
    private sealed class CopyOwnershipLostException : Exception { }
}
