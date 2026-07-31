namespace AI_Study_Hub_v2.Services;

public interface IAdminModerationMetricsService
{
    Task<int> GetPendingEscalatedDocumentCountAsync(Guid supabaseUserId, CancellationToken ct = default);
}
