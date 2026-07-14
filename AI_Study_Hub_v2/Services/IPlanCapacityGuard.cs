using AI_Study_Hub_v2.Data;

namespace AI_Study_Hub_v2.Services;

public sealed record PlanCapacityRequest(
    int AdditionalDocumentCount,
    int AdditionalFolderCount,
    Guid? TargetFolderId,
    int AdditionalDocumentsInTargetFolder);

public interface IPlanCapacityGuard
{
    Task LockAndValidateAsync(AppDbContext db, Guid userId, PlanCapacityRequest request, CancellationToken ct);
}
