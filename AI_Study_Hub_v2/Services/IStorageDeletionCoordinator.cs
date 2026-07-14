namespace AI_Study_Hub_v2.Services;

public interface IStorageDeletionCoordinator
{
    Task<bool> DeleteOwnedDocumentAsync(Guid documentId, Guid ownerUserId, CancellationToken ct);
    Task<bool> DeletePrivilegedDocumentAsync(Guid documentId, CancellationToken ct);
    Task<bool> DeleteOwnedFolderAsync(Guid folderId, Guid ownerUserId, CancellationToken ct);
}
