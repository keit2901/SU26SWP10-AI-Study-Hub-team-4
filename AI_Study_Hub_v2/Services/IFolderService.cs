using AI_Study_Hub_v2.Dtos;

namespace AI_Study_Hub_v2.Services;

public interface IFolderService
{
    Task<IReadOnlyList<FolderDto>> ListAsync(
        Guid supabaseUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a single folder by ID. Returns the folder if the requesting user is the owner,
    /// or the folder is publicly shared (Approved), or the user is an Admin/Moderator.
    /// </summary>
    Task<FolderDto> GetFolderAsync(
        Guid supabaseUserId,
        Guid folderId,
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
        Guid? supabaseUserId = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FolderDto>> ListPersonalSharedAsync(
        Guid supabaseUserId,
        CancellationToken cancellationToken = default);

    Task<FolderDto> ToggleFavoriteAsync(
        Guid supabaseUserId,
        Guid folderId,
        CancellationToken cancellationToken = default);

    Task<FolderDto> RequestShareAsync(
        Guid supabaseUserId,
        Guid folderId,
        CancellationToken cancellationToken = default);

    Task<FolderDto> AppealShareReviewAsync(
        Guid supabaseUserId,
        Guid folderId,
        AppealFolderShareRequest request,
        CancellationToken cancellationToken = default);

    Task<FolderDto> ApproveFolderShareAsync(
        Guid folderId,
        CancellationToken cancellationToken = default);

    Task<FolderDto> RejectFolderShareAsync(
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
