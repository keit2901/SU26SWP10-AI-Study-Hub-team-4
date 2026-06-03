using AI_Study_Hub_v2.Dtos;

namespace AI_Study_Hub_v2.Services;

/// <summary>
/// Application-layer service for the document management pipeline.
/// Sprint 1 surface: upload, list (with filters), get-by-id (with signed URL),
/// soft delete. Background ingestion (chunk + embed) lives in a separate service
/// and runs after <see cref="UploadAsync"/> completes successfully.
/// </summary>
public interface IDocumentService
{
    /// <summary>
    /// Persist a new document: validate → upload bytes to Supabase Storage → insert row.
    /// </summary>
    /// <param name="supabaseUserId">Caller's <c>auth.users.id</c> (from JWT sub).</param>
    /// <param name="request">Subject + semester + folder metadata.</param>
    /// <param name="fileName">Original filename from the multipart upload.</param>
    /// <param name="contentType">MIME type from the multipart upload.</param>
    /// <param name="fileSizeBytes">Total bytes (validated against the 50 MB cap).</param>
    /// <param name="content">File stream — read once, do not seek.</param>
    Task<DocumentDto> UploadAsync(
        Guid supabaseUserId,
        UploadDocumentRequest request,
        string fileName,
        string contentType,
        long fileSizeBytes,
        Stream content,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// List documents owned by the caller, optionally filtered. Sorted newest-first.
    /// </summary>
    Task<IReadOnlyList<DocumentDto>> ListAsync(
        Guid supabaseUserId,
        DocumentListQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetch a single document. Caller must own it (404 otherwise to avoid id-leak).
    /// The returned DTO includes a 5-minute signed Storage URL.
    /// </summary>
    Task<DocumentDto> GetByIdAsync(
        Guid supabaseUserId,
        Guid documentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Move the document into a folder owned by the caller, or back to loose documents.
    /// </summary>
    Task<DocumentDto> MoveToFolderAsync(
        Guid supabaseUserId,
        Guid documentId,
        Guid? folderId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Hard-delete: removes the row + cascades chunks + deletes the storage object.
    /// </summary>
    Task DeleteAsync(
        Guid supabaseUserId,
        Guid documentId,
        CancellationToken cancellationToken = default);
}
