using System.Data;
using System.Text.Json;
using AI_Study_Hub_v2.Data;
using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Dtos;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace AI_Study_Hub_v2.Services;

public interface IEscalationService
{
    Task<DocumentEscalationDto> CreateAsync(Guid escalatedByUserId, CreateEscalationRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<DocumentEscalationDto>> GetPendingAsync(CancellationToken ct = default);
    Task<IReadOnlyList<DocumentEscalationDto>> GetAllAsync(CancellationToken ct = default);
    Task<IReadOnlyList<DocumentEscalationDto>> GetMyAsync(Guid userId, CancellationToken ct = default);
    Task<DocumentEscalationDto> ResolveItemsAsync(Guid escalationId, ResolveEscalationItemsRequest request, Guid resolvedByUserId, CancellationToken ct = default);
}

public sealed class EscalationService : IEscalationService
{
    private readonly AppDbContext _db;
    private readonly IAuditLogService _audit;
    private readonly IUserNotificationService _notifications;
    private readonly IFolderPublicationStateService _publicationState;

    public EscalationService(
        AppDbContext db,
        IAuditLogService audit,
        IUserNotificationService notifications,
        IFolderPublicationStateService publicationState)
    {
        _db = db;
        _audit = audit;
        _notifications = notifications;
        _publicationState = publicationState;
    }

    public async Task<DocumentEscalationDto> CreateAsync(Guid escalatedByUserId, CreateEscalationRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var actor = await ResolveActiveReviewerAsync(escalatedByUserId, ct);
        var documentIds = ValidateCreateRequest(request);
        var reason = NormalizeRequired(request.Reason, "reason_required", "Escalation reason is required.");

        await using var transaction = _db.Database.IsRelational()
            ? await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct)
            : await _db.Database.BeginTransactionAsync(ct);
        try
        {
            var folder = await _db.Folders.FirstOrDefaultAsync(f => f.Id == request.FolderId, ct)
                ?? throw new AdminException(404, "folder_not_found", "Folder not found.");
            if (folder.ShareStatus is not (FolderStatus.PendingShare or FolderStatus.Approved))
            {
                throw new AdminException(409, "folder_not_actionable", "Only pending-share folders or public folders with a new private file can be escalated.");
            }

            // Ordering makes the serializable read set deterministic; the partial unique index added with the schema
            // remains the final arbiter when two moderators escalate the same document concurrently.
            var documents = await _db.Documents
                .Where(document => documentIds.Contains(document.Id))
                .OrderBy(document => document.Id)
                .ToListAsync(ct);
            if (documents.Count != documentIds.Count || documents.Any(document => document.FolderId != folder.Id))
            {
                throw new AdminException(400, "escalation_item_not_in_folder", "Every escalated document must belong to the selected folder.");
            }
            if (documents.Any(document => document.UserId != folder.UserId))
            {
                throw new AdminException(409, "escalation_document_owner_mismatch", "Every escalated document must belong to the folder owner.");
            }
            if (documents.Any(document => document.Status != DocumentStatus.Ready || document.ReviewStatus != DocumentReviewStatus.None))
            {
                throw new AdminException(409, "escalation_document_not_eligible", "Only ready, unreviewed documents can be escalated.");
            }

            var now = DateTimeOffset.UtcNow;
            var escalation = new DocumentEscalation
            {
                Id = Guid.NewGuid(),
                FolderId = folder.Id,
                EscalatedByUserId = actor.Id,
                Reason = reason,
                EscalationStatus = "Pending",
                CreatedAt = now
            };
            _db.DocumentEscalations.Add(escalation);

            var transitionedCount = _db.Database.IsRelational()
                ? await _db.Documents
                    .Where(document => documentIds.Contains(document.Id)
                        && document.FolderId == folder.Id
                        && document.UserId == folder.UserId
                        && document.Status == DocumentStatus.Ready
                        && document.ReviewStatus == DocumentReviewStatus.None)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(document => document.ReviewStatus, DocumentReviewStatus.Escalated)
                        .SetProperty(document => document.UpdatedAt, now), ct)
                : TransitionInMemoryDocuments(documents, now);
            if (transitionedCount != documents.Count)
            {
                await transaction.RollbackAsync(ct);
                _db.ChangeTracker.Clear();
                throw new AdminException(409, "document_moderation_changed", "One or more documents changed while the escalation was being created.");
            }

            foreach (var document in documents)
            {
                var itemRequest = request.Items.Single(item => item.DocumentId == document.Id);
                _db.DocumentEscalationItems.Add(new DocumentEscalationItem
                {
                    Id = Guid.NewGuid(),
                    EscalationId = escalation.Id,
                    DocumentId = document.Id,
                    DocumentFileName = document.FileName,
                    DocumentModerationGeneration = document.ModerationGeneration,
                    RejectReason = NormalizeRequired(itemRequest.RejectReason, "item_reason_required", "Each escalation item needs a reason."),
                    ResolutionStatus = "Pending"
                });
            }

            _audit.Add(actor.Id, "ESCALATION_CREATED", "DocumentEscalation", escalation.Id.ToString(), "Medium",
                afterJson: JsonSerializer.Serialize(new { escalation.FolderId, DocumentCount = documents.Count, ReasonLength = reason.Length }));
            await _db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return await GetByIdAsync(escalation.Id, ct);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            await transaction.RollbackAsync(ct);
            throw new AdminException(409, "escalation_document_already_pending", "One or more documents already have a pending escalation.");
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.SerializationFailure })
        {
            await transaction.RollbackAsync(ct);
            throw new AdminException(409, "document_moderation_changed", "Document moderation changed while the escalation was being created.");
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            await transaction.RollbackAsync(ct);
            throw new AdminException(409, "escalation_document_already_pending", "One or more documents already have a pending escalation.");
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.SerializationFailure)
        {
            await transaction.RollbackAsync(ct);
            throw new AdminException(409, "document_moderation_changed", "Document moderation changed while the escalation was being created.");
        }
    }

    public Task<IReadOnlyList<DocumentEscalationDto>> GetPendingAsync(CancellationToken ct = default) =>
        GetManyAsync(_db.DocumentEscalations.Where(e => e.EscalationStatus == "Pending"), ct);

    public Task<IReadOnlyList<DocumentEscalationDto>> GetAllAsync(CancellationToken ct = default) =>
        GetManyAsync(_db.DocumentEscalations, ct);

    public Task<IReadOnlyList<DocumentEscalationDto>> GetMyAsync(Guid userId, CancellationToken ct = default) =>
        GetManyAsync(_db.DocumentEscalations.Where(e => e.EscalatedByUserId == userId), ct);

    public async Task<DocumentEscalationDto> ResolveItemsAsync(Guid escalationId, ResolveEscalationItemsRequest request, Guid resolvedByUserId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var resolver = await ResolveActiveAdminAsync(resolvedByUserId, ct);
        ValidateResolutionRequest(request);

        IDbContextTransaction? transaction = null;
        try
        {
            transaction = _db.Database.IsRelational()
                ? await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct)
                : await _db.Database.BeginTransactionAsync(ct);
            var exists = await _db.DocumentEscalations.AsNoTracking().AnyAsync(escalation => escalation.Id == escalationId, ct);
            if (!exists)
                throw new AdminException(404, "escalation_not_found", "Escalation not found.");

            var now = DateTimeOffset.UtcNow;
            if (_db.Database.IsRelational())
            {
                var claimed = await _db.DocumentEscalations
                    .Where(escalation => escalation.Id == escalationId && escalation.EscalationStatus == "Pending")
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(escalation => escalation.EscalationStatus, "Resolved")
                        .SetProperty(escalation => escalation.ResolvedByUserId, resolver.Id)
                        .SetProperty(escalation => escalation.ResolvedAt, now), ct);
                if (claimed != 1)
                    throw new AdminException(409, "escalation_already_resolved", "This escalation has already been resolved.");
                _db.ChangeTracker.Clear();
            }

            var escalation = await _db.DocumentEscalations
                .Include(item => item.Folder)
                .FirstAsync(item => item.Id == escalationId, ct);
            if (!_db.Database.IsRelational() && escalation.EscalationStatus != "Pending")
                throw new AdminException(409, "escalation_already_resolved", "This escalation has already been resolved.");

            var items = await _db.DocumentEscalationItems
                .Where(item => item.EscalationId == escalationId)
                .OrderBy(item => item.Id)
                .ToListAsync(ct);
            var requested = request.Items.ToDictionary(item => item.ItemId);
            if (items.Count == 0 || items.Count != requested.Count || items.Any(item => item.ResolutionStatus != "Pending" || !requested.ContainsKey(item.Id)))
                throw new AdminException(409, "escalation_item_set_changed", "The submitted decisions must exactly match every pending escalation item.");

            var documentIds = items.Select(item => item.DocumentId).Where(id => id.HasValue).Select(id => id!.Value).OrderBy(id => id).ToList();
            var documents = await _db.Documents.Where(document => documentIds.Contains(document.Id)).OrderBy(document => document.Id).ToListAsync(ct);
            if (items.Any(item => !item.DocumentId.HasValue)
                || documents.Count != documentIds.Count
                || documents.Any(document => document.FolderId != escalation.FolderId))
                throw new AdminException(409, "escalation_items_changed", "Escalation documents no longer match the folder.");

            var documentsById = documents.ToDictionary(document => document.Id);
            foreach (var item in items)
            {
                var document = documentsById[item.DocumentId!.Value];
                if (document.Status != DocumentStatus.Ready
                    || document.ReviewStatus != DocumentReviewStatus.Escalated
                    || document.ModerationGeneration != item.DocumentModerationGeneration)
                    throw new AdminException(409, "escalation_item_stale", "An escalation item changed after it was submitted.");
            }

            var approvedCount = 0;
            var rejectedCount = 0;
            foreach (var item in items)
            {
                var decision = requested[item.Id];
                var status = NormalizeResolutionStatus(decision.Status);
                var response = NormalizeOptional(decision.AdminResponse);
                if (status == "Rejected" && string.IsNullOrWhiteSpace(response))
                    throw new AdminException(400, "rejection_response_required", "Each rejected escalation item requires an admin response.");

                var document = documentsById[item.DocumentId!.Value];
                document.ReviewStatus = status == "Approved" ? DocumentReviewStatus.Approved : DocumentReviewStatus.Rejected;
                document.ErrorMessage = status == "Rejected" ? response : null;
                document.UpdatedAt = now;
                item.ResolutionStatus = status;
                item.AdminResponse = response;
                item.ResolvedByUserId = resolver.Id;
                item.ResolvedAt = now;
                if (status == "Approved") approvedCount++; else rejectedCount++;
            }

            escalation.EscalationStatus = "Resolved";
            escalation.ResolvedByUserId = resolver.Id;
            escalation.ResolvedAt = now;
            escalation.AdminResponse = null;
            var folderDocuments = await _db.Documents.Where(document => document.FolderId == escalation.FolderId).ToListAsync(ct);
            if (escalation.Folder.ShareStatus == FolderStatus.Rejected)
            {
                escalation.Folder.ShareStatus = FolderStatus.PendingShare;
                escalation.Folder.SharedAt = null;
            }
            _publicationState.Recompute(escalation.Folder, folderDocuments, now);
            _notifications.StageEscalationResolved(escalation, escalation.Folder, approvedCount, rejectedCount, now);
            _audit.Add(resolver.Id, "ESCALATION_ITEMS_RESOLVED", "DocumentEscalation", escalation.Id.ToString(), "Medium",
                afterJson: JsonSerializer.Serialize(new { ApprovedCount = approvedCount, RejectedCount = rejectedCount }));
            await _db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            var result = await GetByIdAsync(escalationId, ct);
            await transaction.DisposeAsync();
            transaction = null;
            return result;
        }
        catch (AdminException)
        {
            await CleanupTransactionAsync(transaction);
            transaction = null;
            throw;
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            await CleanupTransactionAsync(transaction);
            transaction = null;
            throw new AdminException(409, "escalation_already_resolved", "The escalation changed while it was being resolved.");
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            await CleanupTransactionAsync(transaction);
            transaction = null;
            throw new AdminException(409, "escalation_already_resolved", "The escalation changed while it was being resolved.");
        }
        catch (Exception exception) when (HasPostgresConcurrencyFailure(exception))
        {
            await CleanupTransactionAsync(transaction);
            transaction = null;
            throw new AdminException(409, "escalation_already_resolved", "The escalation changed while it was being resolved.");
        }
        finally
        {
            if (transaction is not null)
                await transaction.DisposeAsync();
        }
    }

    private async Task<IReadOnlyList<DocumentEscalationDto>> GetManyAsync(IQueryable<DocumentEscalation> query, CancellationToken ct)
    {
        var ids = await query.OrderByDescending(e => e.CreatedAt).Select(e => e.Id).ToListAsync(ct);
        var result = new List<DocumentEscalationDto>(ids.Count);
        foreach (var id in ids) result.Add(await GetByIdAsync(id, ct));
        return result;
    }

    private static int TransitionInMemoryDocuments(IReadOnlyCollection<Document> documents, DateTimeOffset now)
    {
        if (documents.Any(document => document.Status != DocumentStatus.Ready || document.ReviewStatus != DocumentReviewStatus.None))
            return 0;

        foreach (var document in documents)
        {
            document.ReviewStatus = DocumentReviewStatus.Escalated;
            document.UpdatedAt = now;
        }
        return documents.Count;
    }

    private async Task<User> ResolveActiveReviewerAsync(Guid localUserId, CancellationToken ct)
    {
        var user = await _db.Users.Include(candidate => candidate.Role).FirstOrDefaultAsync(candidate => candidate.Id == localUserId, ct)
            ?? throw new AdminException(404, "user_not_found", "Authenticated user has no profile in public.users.");
        if (!user.IsActive || (!string.Equals(user.Role?.RoleName, Role.AdminRoleName, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(user.Role?.RoleName, Role.ModeratorRoleName, StringComparison.OrdinalIgnoreCase)))
            throw new AdminException(403, "share_reviewer_role_required", "Only active Admin or Moderator profiles can create escalations.");
        return user;
    }

    private async Task<User> ResolveActiveAdminAsync(Guid localUserId, CancellationToken ct)
    {
        var user = await _db.Users.Include(candidate => candidate.Role).FirstOrDefaultAsync(candidate => candidate.Id == localUserId, ct)
            ?? throw new AdminException(404, "user_not_found", "Authenticated user has no profile in public.users.");
        if (!user.IsActive || !string.Equals(user.Role?.RoleName, Role.AdminRoleName, StringComparison.OrdinalIgnoreCase))
            throw new AdminException(403, "admin_role_required", "Only active Admin profiles can resolve escalations.");
        return user;
    }

    private static List<Guid> ValidateCreateRequest(CreateEscalationRequest request)
    {
        if (request.FolderId == Guid.Empty || request.Items is null || request.Items.Count == 0 || request.Items.Any(item => item.DocumentId == Guid.Empty))
            throw new AdminException(400, "invalid_escalation_item", "A folder and at least one document are required.");
        var ids = request.Items.Select(item => item.DocumentId).ToList();
        if (ids.Distinct().Count() != ids.Count)
            throw new AdminException(400, "duplicate_escalation_document", "Each document may appear only once in an escalation.");
        return ids;
    }

    private static void ValidateResolutionRequest(ResolveEscalationItemsRequest request)
    {
        if (request.Items is null || request.Items.Count == 0 || request.Items.Any(item => item.ItemId == Guid.Empty))
            throw new AdminException(400, "escalation_item_decisions_required", "A decision is required for every escalation item.");
        if (request.Items.Select(item => item.ItemId).Distinct().Count() != request.Items.Count)
            throw new AdminException(400, "duplicate_escalation_item_decision", "Each escalation item may have only one decision.");
    }

    private static string NormalizeResolutionStatus(string? status) =>
        string.Equals(status?.Trim(), "Approved", StringComparison.OrdinalIgnoreCase) ? "Approved" :
        string.Equals(status?.Trim(), "Rejected", StringComparison.OrdinalIgnoreCase) ? "Rejected" :
        throw new AdminException(400, "invalid_escalation_status", "Status must be 'Approved' or 'Rejected'.");

    private static string NormalizeRequired(string? value, string code, string message) =>
        NormalizeOptional(value) ?? throw new AdminException(400, code, message);

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized.Length <= 2000 ? normalized : normalized[..2000];
    }

    private static bool HasPostgresConcurrencyFailure(Exception exception)
    {
        var pending = new Queue<Exception>();
        var visited = new HashSet<Exception>(ReferenceEqualityComparer.Instance);
        pending.Enqueue(exception);
        while (pending.Count > 0 && visited.Count < 32)
        {
            var current = pending.Dequeue();
            if (!visited.Add(current))
                continue;

            if (current is PostgresException { SqlState: PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected })
            {
                return true;
            }

            if (current.InnerException is not null)
                pending.Enqueue(current.InnerException);
            if (current is AggregateException aggregate)
            {
                foreach (var innerException in aggregate.InnerExceptions)
                    pending.Enqueue(innerException);
            }
        }

        return false;
    }

    private static async Task CleanupTransactionAsync(IDbContextTransaction? transaction)
    {
        if (transaction is null)
            return;

        try { await transaction.RollbackAsync(CancellationToken.None); } catch { }
        try { await transaction.DisposeAsync(); } catch { }
    }

    private async Task<DocumentEscalationDto> GetByIdAsync(Guid escalationId, CancellationToken ct)
    {
        var escalation = await _db.DocumentEscalations
            .Include(item => item.EscalatedByUser)
            .Include(item => item.ResolvedByUser)
            .Include(item => item.Items).ThenInclude(item => item.ResolvedByUser)
            .AsNoTracking()
            .FirstAsync(item => item.Id == escalationId, ct);
        return new DocumentEscalationDto(escalation.Id, escalation.FolderId, escalation.EscalatedByUser.FullName,
            escalation.Reason, escalation.EscalationStatus, escalation.AdminResponse, escalation.ResolvedByUser?.FullName,
            escalation.CreatedAt, escalation.ResolvedAt,
            escalation.Items.OrderBy(item => item.Id).Select(item => new DocumentEscalationItemDto(item.Id, item.DocumentId,
                item.DocumentFileName, item.DocumentModerationGeneration, item.RejectReason, item.ResolutionStatus,
                item.AdminResponse, item.ResolvedByUser?.FullName, item.ResolvedAt)).ToList());
    }
}
