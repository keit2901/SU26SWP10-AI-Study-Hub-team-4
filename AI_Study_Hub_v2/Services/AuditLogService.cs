using AI_Study_Hub_v2.Data;
using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Dtos;
using Microsoft.EntityFrameworkCore;

namespace AI_Study_Hub_v2.Services;

public interface IAuditLogService
{
    void Add(
        Guid? actorUserId,
        string action,
        string entityType,
        string? entityId = null,
        string severity = "Low",
        string? beforeJson = null,
        string? afterJson = null,
        string? contextJson = null,
        string? ipAddress = null,
        string? requestId = null);

    Task<IReadOnlyList<AuditLogDto>> ListAsync(
        string? action,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int limit,
        Guid? actorUserId = null,
        CancellationToken cancellationToken = default);
}

public sealed class AuditLogService : IAuditLogService
{
    private readonly AppDbContext _db;

    public AuditLogService(AppDbContext db)
    {
        _db = db;
    }

    public void Add(
        Guid? actorUserId,
        string action,
        string entityType,
        string? entityId = null,
        string severity = "Low",
        string? beforeJson = null,
        string? afterJson = null,
        string? contextJson = null,
        string? ipAddress = null,
        string? requestId = null)
    {
        _db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            ActorUserId = actorUserId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Severity = severity,
            BeforeJson = beforeJson,
            AfterJson = afterJson,
            ContextJson = contextJson,
            IpAddress = ipAddress,
            RequestId = requestId,
            CreatedAt = DateTimeOffset.UtcNow,
        });
    }

    public async Task<IReadOnlyList<AuditLogDto>> ListAsync(
        string? action,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int limit,
        Guid? actorUserId = null,
        CancellationToken cancellationToken = default)
    {
        var query = _db.AuditLogs.AsNoTracking();

        if (actorUserId.HasValue)
        {
            query = query.Where(log => log.ActorUserId == actorUserId.Value);
        }

        if (!string.IsNullOrWhiteSpace(action))
        {
            var normalized = action.Trim();
            query = query.Where(log => log.Action == normalized);
        }
        if (from.HasValue)
        {
            query = query.Where(log => log.CreatedAt >= from.Value);
        }
        if (to.HasValue)
        {
            query = query.Where(log => log.CreatedAt <= to.Value);
        }

        var take = Math.Clamp(limit, 1, 500);
        return await query
            .OrderByDescending(log => log.CreatedAt)
            .Take(take)
            .Select(log => new AuditLogDto(
                log.Id,
                log.ActorUserId,
                log.ActorUser == null
                    ? "System"
                    : (log.ActorUser.FullName ?? log.ActorUser.Username),
                log.Action,
                log.EntityType,
                log.EntityId,
                log.Severity,
                log.BeforeJson,
                log.AfterJson,
                log.ContextJson,
                log.IpAddress,
                log.RequestId,
                log.CreatedAt))
            .ToListAsync(cancellationToken);
    }
}
