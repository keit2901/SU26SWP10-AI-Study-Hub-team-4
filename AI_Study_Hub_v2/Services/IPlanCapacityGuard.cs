using AI_Study_Hub_v2.Data;

namespace AI_Study_Hub_v2.Services;

public sealed record PlanCapacityRequest(
    int AdditionalDocumentCount,
    int AdditionalFolderCount,
    Guid? TargetFolderId,
    int AdditionalDocumentsInTargetFolder,
    int NewFolderDocumentCount = 0);

public interface IPlanCapacityGuard
{
    Task LockAndValidateAsync(AppDbContext db, Guid userId, PlanCapacityRequest request, CancellationToken ct);
    Task LockValidateAndReserveStorageAsync(AppDbContext db, Guid userId, PlanCapacityRequest request, long additionalStorageBytes, CancellationToken ct);
    Task LockAndReleaseReservedStorageAsync(AppDbContext db, Guid userId, long reservedStorageBytes, CancellationToken ct);
}
