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
    Task<DocumentEscalationDto> ResolveAsync(Guid escalationId, Guid resolvedByUserId, ResolveEscalationRequest request, CancellationToken ct = default);
}

public sealed class EscalationService : IEscalationService
{
    private readonly AppDbContext _db;

    public EscalationService(AppDbContext db) => _db = db;

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

    public async Task<DocumentEscalationDto> ResolveAsync(Guid escalationId, Guid resolvedByUserId, ResolveEscalationRequest request, CancellationToken ct = default)
    {
        var escalation = await _db.DocumentEscalations
            .FirstOrDefaultAsync(e => e.Id == escalationId, ct)
            ?? throw new AdminException(404, "escalation_not_found", "Escalation not found.");

        escalation.EscalationStatus = request.Status;
        escalation.AdminResponse = request.AdminResponse;
        escalation.ResolvedByUserId = resolvedByUserId;
        escalation.ResolvedAt = DateTimeOffset.UtcNow;
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
