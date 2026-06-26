using AI_Study_Hub_v2.Dtos;

namespace AI_Study_Hub_v2.Services;

public interface IFolderService
{
    Task<IReadOnlyList<FolderDto>> ListAsync(
        Guid supabaseUserId,
        CancellationToken cancellationToken = default);

    Task<FolderDto> CreateAsync(
        Guid supabaseUserId,
        CreateFolderRequest request,
        CancellationToken cancellationToken = default);

    Task<FolderDto> UpdateAsync(
        Guid supabaseUserId,
        Guid folderId,
        UpdateFolderRequest request,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        Guid supabaseUserId,
        Guid folderId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FolderDto>> ListSharedAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FolderDto>> ListPersonalSharedAsync(
        Guid supabaseUserId,
        CancellationToken cancellationToken = default);

    Task<FolderDto> ToggleFavoriteAsync(
        Guid supabaseUserId,
        Guid folderId,
        CancellationToken cancellationToken = default);

    Task<FolderDto> ToggleShareAsync(
        Guid supabaseUserId,
        Guid folderId,
        CancellationToken cancellationToken = default);

    Task<FolderDto> VoteAsync(
        Guid supabaseUserId,
        Guid folderId,
        bool isLike,
        CancellationToken cancellationToken = default);

    Task<FolderDto> CopySharedFolderAsync(
        Guid supabaseUserId,
        Guid sharedFolderId,
        CancellationToken cancellationToken = default);
}
