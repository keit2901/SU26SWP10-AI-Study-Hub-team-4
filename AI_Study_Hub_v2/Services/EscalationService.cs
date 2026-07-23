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
        var escalation = new DocumentEscalation
        {
            Id = Guid.NewGuid(),
            FolderId = request.FolderId,
            EscalatedByUserId = escalatedByUserId,
            Reason = request.Reason,
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
                RejectReason = item.RejectReason
            });
        }

        _audit.Add(
            escalatedByUserId,
            "ESCALATION_CREATED",
            "DocumentEscalation",
            escalation.Id.ToString(),
            "Medium",
            afterJson: JsonSerializer.Serialize(new
            {
                escalation.FolderId,
                escalation.Reason,
                DocumentCount = request.Items.Count
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
        var escalation = await _db.DocumentEscalations
            .FirstOrDefaultAsync(e => e.Id == escalationId, ct)
            ?? throw new AdminException(404, "escalation_not_found", "Escalation not found.");

        if (escalation.EscalationStatus != "Pending")
            throw new AdminException(409, "already_resolved", $"Escalation has already been resolved as '{escalation.EscalationStatus}'.");

        var previousStatus = escalation.EscalationStatus;
        escalation.EscalationStatus = request.Status;
        escalation.AdminResponse = request.AdminResponse;
        escalation.ResolvedByUserId = resolvedByUserId;
        escalation.ResolvedAt = DateTimeOffset.UtcNow;

        var beforeJson = JsonSerializer.Serialize(new { Status = previousStatus });
        var afterJson = JsonSerializer.Serialize(new { escalation.EscalationStatus, escalation.AdminResponse });

        _audit.Add(
            resolvedByUserId,
            "ESCALATION_RESOLVED",
            "DocumentEscalation",
            escalation.Id.ToString(),
            "Medium",
            beforeJson: beforeJson,
            afterJson: afterJson);

        await _db.SaveChangesAsync(ct);

        return await GetByIdAsync(escalationId, ct);
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
