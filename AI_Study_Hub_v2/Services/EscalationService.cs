using System.Text.Json;
using AI_Study_Hub_v2.Data;
using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Dtos;
using Microsoft.EntityFrameworkCore;

namespace AI_Study_Hub_v2.Services;

public interface IEscalationService
{
    Task<DocumentEscalationDto> CreateAsync(Guid escalatedByUserId, CreateEscalationRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<DocumentEscalationDto>> GetPendingAsync(CancellationToken ct = default);
    Task<IReadOnlyList<DocumentEscalationDto>> GetAllAsync(CancellationToken ct = default);
    Task<IReadOnlyList<DocumentEscalationDto>> GetMyAsync(Guid userId, CancellationToken ct = default);
    Task<DocumentEscalationDto> ResolveAsync(Guid escalationId, ResolveEscalationRequest request, Guid resolvedByUserId, CancellationToken ct = default);
}

public sealed class EscalationService : IEscalationService
{
    private readonly AppDbContext _db;
    private readonly IAuditLogService _audit;

    public EscalationService(AppDbContext db, IAuditLogService audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<DocumentEscalationDto> CreateAsync(Guid escalatedByUserId, CreateEscalationRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var actor = await ResolveActiveReviewerAsync(escalatedByUserId, ct);
        var itemIds = ValidateCreateRequest(request);
        var folder = await _db.Folders
            .FirstOrDefaultAsync(f => f.Id == request.FolderId, ct)
            ?? throw new AdminException(404, "folder_not_found", "Folder not found.");

        if (folder.ShareStatus != FolderStatus.PendingShare)
        {
            throw new AdminException(409, "folder_not_pending_share",
                "Only folders pending share review can be escalated.");
        }

        if (await _db.DocumentEscalations.AnyAsync(
                e => e.FolderId == folder.Id && e.EscalationStatus == "Pending", ct))
        {
            throw new AdminException(409, "pending_escalation_exists",
                "This folder already has a pending escalation.");
        }

        var documents = await _db.Documents
            .Where(document => itemIds.Contains(document.Id))
            .ToListAsync(ct);
        if (documents.Count != itemIds.Count || documents.Any(document => document.FolderId != folder.Id))
        {
            throw new AdminException(400, "escalation_item_not_in_folder",
                "Every escalated document must belong to the selected folder.");
        }

        var reason = NormalizeRequired(request.Reason, "reason_required", "Escalation reason is required.");
        var escalation = new DocumentEscalation
        {
            Id = Guid.NewGuid(),
            FolderId = folder.Id,
            EscalatedByUserId = actor.Id,
            Reason = reason,
            EscalationStatus = "Pending",
            CreatedAt = DateTimeOffset.UtcNow
        };
        _db.DocumentEscalations.Add(escalation);

        foreach (var item in request.Items)
        {
            _db.DocumentEscalationItems.Add(new DocumentEscalationItem
            {
                Id = Guid.NewGuid(),
                EscalationId = escalation.Id,
                DocumentId = item.DocumentId,
                RejectReason = NormalizeRequired(item.RejectReason, "item_reason_required", "Each escalation item needs a reason.")
            });
        }

        foreach (var document in documents)
        {
            document.ReviewStatus = DocumentReviewStatus.Escalated;
            document.UpdatedAt = DateTimeOffset.UtcNow;
        }

        _audit.Add(
            actor.Id,
            "ESCALATION_CREATED",
            "DocumentEscalation",
            escalation.Id.ToString(),
            "Medium",
            afterJson: JsonSerializer.Serialize(new
            {
                escalation.FolderId,
                DocumentCount = request.Items.Count,
                ReasonLength = escalation.Reason.Length,
                ItemReasonLengths = request.Items.Select(item => item.RejectReason?.Trim().Length ?? 0).ToArray()
            }));

        await _db.SaveChangesAsync(ct);

        return await GetByIdAsync(escalation.Id, ct);
    }

    public async Task<IReadOnlyList<DocumentEscalationDto>> GetPendingAsync(CancellationToken ct = default)
    {
        var escalationIds = await _db.DocumentEscalations
            .Where(e => e.EscalationStatus == "Pending")
            .OrderByDescending(e => e.CreatedAt)
            .Select(e => e.Id)
            .ToListAsync(ct);

        var result = new List<DocumentEscalationDto>();
        foreach (var id in escalationIds)
        {
            result.Add(await GetByIdAsync(id, ct));
        }
        return result;
    }

    public async Task<IReadOnlyList<DocumentEscalationDto>> GetAllAsync(CancellationToken ct = default)
    {
        var escalationIds = await _db.DocumentEscalations
            .OrderByDescending(e => e.CreatedAt)
            .Select(e => e.Id)
            .ToListAsync(ct);

        var result = new List<DocumentEscalationDto>();
        foreach (var id in escalationIds)
        {
            result.Add(await GetByIdAsync(id, ct));
        }
        return result;
    }

    public async Task<IReadOnlyList<DocumentEscalationDto>> GetMyAsync(Guid userId, CancellationToken ct = default)
    {
        var escalationIds = await _db.DocumentEscalations
            .Where(e => e.EscalatedByUserId == userId)
            .OrderByDescending(e => e.CreatedAt)
            .Select(e => e.Id)
            .ToListAsync(ct);

        var result = new List<DocumentEscalationDto>();
        foreach (var id in escalationIds)
        {
            result.Add(await GetByIdAsync(id, ct));
        }
        return result;
    }

    public async Task<DocumentEscalationDto> ResolveAsync(Guid escalationId, ResolveEscalationRequest request, Guid resolvedByUserId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var resolver = await ResolveActiveAdminAsync(resolvedByUserId, ct);
        var resolutionStatus = NormalizeResolutionStatus(request.Status);
        var adminResponse = NormalizeOptional(request.AdminResponse);
        var escalation = await _db.DocumentEscalations
            .Include(e => e.Items)
            .Include(e => e.Folder)
            .FirstOrDefaultAsync(e => e.Id == escalationId, ct)
            ?? throw new AdminException(404, "escalation_not_found", "Escalation not found.");

        if (escalation.EscalationStatus != "Pending")
            throw new AdminException(409, "already_resolved", $"Escalation has already been resolved as '{escalation.EscalationStatus}'.");

        if (escalation.Folder.ShareStatus != FolderStatus.PendingShare)
        {
            throw new AdminException(409, "folder_not_pending_share",
                "The folder is no longer pending share review; the escalation cannot be resolved.");
        }

        var previousStatus = escalation.EscalationStatus;
        var itemIds = escalation.Items.Select(item => item.DocumentId).ToList();
        var documents = await _db.Documents
            .Where(document => itemIds.Contains(document.Id) && document.FolderId == escalation.FolderId)
            .ToListAsync(ct);
        if (documents.Count != itemIds.Count)
        {
            throw new AdminException(409, "escalation_items_changed",
                "Escalation documents no longer match the folder; no resolution was applied.");
        }

        var folderDocuments = await _db.Documents
            .Where(document => document.FolderId == escalation.FolderId)
            .ToListAsync(ct);

        var now = DateTimeOffset.UtcNow;
        escalation.EscalationStatus = resolutionStatus;
        escalation.AdminResponse = adminResponse;
        escalation.ResolvedByUserId = resolver.Id;
        escalation.ResolvedAt = now;
        foreach (var document in documents)
        {
            document.ReviewStatus = resolutionStatus == "Approved"
                ? DocumentReviewStatus.Approved
                : DocumentReviewStatus.Rejected;
            document.UpdatedAt = now;
        }

        escalation.Folder.UpdatedAt = now;
        if (resolutionStatus == "Approved")
        {
            var allDocumentsApproved = folderDocuments.All(document =>
                document.ReviewStatus == DocumentReviewStatus.Approved);
            if (allDocumentsApproved)
            {
                escalation.Folder.ShareStatus = FolderStatus.Approved;
                escalation.Folder.SharedAt = now;
                escalation.Folder.ShareReviewSource = "ADMIN_ESCALATION_APPROVED";
                escalation.Folder.RequiresHumanReview = false;
            }
        }
        else
        {
            escalation.Folder.ShareStatus = FolderStatus.Rejected;
            escalation.Folder.SharedAt = null;
            escalation.Folder.ShareReviewSource = "ADMIN_ESCALATION_REJECTED";
            escalation.Folder.HumanReviewReason = adminResponse;
            escalation.Folder.RequiresHumanReview = false;
            escalation.Folder.ShareFailureCount += 1;
        }

        var beforeJson = JsonSerializer.Serialize(new { Status = previousStatus });
        var afterJson = JsonSerializer.Serialize(new { escalation.EscalationStatus, escalation.AdminResponse });

        _audit.Add(
            resolver.Id,
            "ESCALATION_RESOLVED",
            "DocumentEscalation",
            escalation.Id.ToString(),
            "Medium",
            beforeJson: beforeJson,
            afterJson: afterJson);

        await _db.SaveChangesAsync(ct);

        return await GetByIdAsync(escalationId, ct);
    }

    private async Task<User> ResolveActiveReviewerAsync(Guid localUserId, CancellationToken ct)
    {
        var user = await _db.Users.Include(candidate => candidate.Role)
            .FirstOrDefaultAsync(candidate => candidate.Id == localUserId, ct)
            ?? throw new AdminException(404, "user_not_found", "Authenticated user has no profile in public.users.");
        if (!user.IsActive
            || (!string.Equals(user.Role?.RoleName, Role.AdminRoleName, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(user.Role?.RoleName, Role.ModeratorRoleName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new AdminException(403, "share_reviewer_role_required",
                "Only active Admin or Moderator profiles can create escalations.");
        }
        return user;
    }

    private async Task<User> ResolveActiveAdminAsync(Guid localUserId, CancellationToken ct)
    {
        var user = await _db.Users.Include(candidate => candidate.Role)
            .FirstOrDefaultAsync(candidate => candidate.Id == localUserId, ct)
            ?? throw new AdminException(404, "user_not_found", "Authenticated user has no profile in public.users.");
        if (!user.IsActive || !string.Equals(user.Role?.RoleName, Role.AdminRoleName, StringComparison.OrdinalIgnoreCase))
        {
            throw new AdminException(403, "admin_role_required", "Only active Admin profiles can resolve escalations.");
        }
        return user;
    }

    private static List<Guid> ValidateCreateRequest(CreateEscalationRequest request)
    {
        if (request.Items is null || request.Items.Count == 0)
        {
            throw new AdminException(400, "escalation_items_required", "At least one document must be escalated.");
        }
        if (request.FolderId == Guid.Empty || request.Items.Any(item => item.DocumentId == Guid.Empty))
        {
            throw new AdminException(400, "invalid_escalation_item", "Folder and document identifiers are required.");
        }
        var itemIds = request.Items.Select(item => item.DocumentId).ToList();
        if (itemIds.Distinct().Count() != itemIds.Count)
        {
            throw new AdminException(400, "duplicate_escalation_document", "Each document may appear only once in an escalation.");
        }
        return itemIds;
    }

    private static string NormalizeResolutionStatus(string? status)
    {
        if (string.Equals(status?.Trim(), "Approved", StringComparison.OrdinalIgnoreCase)) return "Approved";
        if (string.Equals(status?.Trim(), "Rejected", StringComparison.OrdinalIgnoreCase)) return "Rejected";
        throw new AdminException(400, "invalid_escalation_status", "Status must be 'Approved' or 'Rejected'.");
    }

    private static string NormalizeRequired(string? value, string code, string message)
    {
        var normalized = NormalizeOptional(value);
        return string.IsNullOrWhiteSpace(normalized)
            ? throw new AdminException(400, code, message)
            : normalized;
    }

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized)) return null;
        return normalized.Length <= 2000 ? normalized : normalized[..2000];
    }

    private async Task<DocumentEscalationDto> GetByIdAsync(Guid escalationId, CancellationToken ct)
    {
        var e = await _db.DocumentEscalations
            .Include(x => x.EscalatedByUser)
            .Include(x => x.ResolvedByUser)
            .Include(x => x.Items).ThenInclude(i => i.Document)
            .AsNoTracking()
            .FirstAsync(x => x.Id == escalationId, ct);

        return new DocumentEscalationDto(
            e.Id, e.FolderId,
            e.EscalatedByUser.FullName,
            e.Reason, e.EscalationStatus, e.AdminResponse,
            e.ResolvedByUser?.FullName,
            e.CreatedAt, e.ResolvedAt,
            e.Items.Select(i => new DocumentEscalationItemDto(i.DocumentId, i.Document.FileName, i.RejectReason)).ToList());
    }
}
