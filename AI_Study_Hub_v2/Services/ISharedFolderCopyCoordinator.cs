using AI_Study_Hub_v2.Dtos;

namespace AI_Study_Hub_v2.Services;

public interface ISharedFolderCopyCoordinator
{
    Task<FolderDto> CopyAsync(Guid destinationSupabaseUserId, Guid sourceFolderId, CancellationToken ct);
}
