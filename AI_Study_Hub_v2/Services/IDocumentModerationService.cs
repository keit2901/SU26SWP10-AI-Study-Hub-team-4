using AI_Study_Hub_v2.Dtos;

namespace AI_Study_Hub_v2.Services;

public interface IDocumentModerationService
{
    Task<IReadOnlyList<ModerationQueueDocumentDto>> GetQueueAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ModerationQueueDocumentDto>> GetEscalatedQueueAsync(CancellationToken ct = default);
    Task<bool> ApproveAsync(Guid documentId, CancellationToken ct = default);
    Task<bool> RejectAsync(Guid documentId, string? reason, CancellationToken ct = default);
    Task<bool> EscalateAsync(Guid documentId, CancellationToken ct = default);
    Task<bool> RestoreAsync(Guid documentId, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid documentId, CancellationToken ct = default);
}
