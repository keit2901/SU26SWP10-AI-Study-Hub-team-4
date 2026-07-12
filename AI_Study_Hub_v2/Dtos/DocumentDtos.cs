using System.ComponentModel.DataAnnotations;
using AI_Study_Hub_v2.Data.Entities;

namespace AI_Study_Hub_v2.Dtos;

/// <summary>
/// Multipart upload request for a single study document.
/// The actual file is sent as <c>IFormFile</c> on the controller; this DTO carries
/// the metadata fields the user fills in alongside it (subject, semester, folder).
/// </summary>
public sealed class UploadDocumentRequest
{
    /// <summary>FPT subject code, e.g. "SWP391", "PRN232".</summary>
    [Required]
    [RegularExpression(@"^[A-Z]{3}[0-9]{3}$",
        ErrorMessage = "Subject code must follow the FPT pattern of 3 uppercase letters + 3 digits, e.g. SWP391.")]
    public string SubjectCode { get; set; } = string.Empty;

    /// <summary>Semester tag — Spring/Summer/Fall/Winter + 2-digit year, e.g. "SU26", "FA25".</summary>
    [Required]
    [RegularExpression(@"^(SP|SU|FA|WI)[0-9]{2}$",
        ErrorMessage = "Semester must be SP/SU/FA/WI followed by 2 digits, e.g. SU26.")]
    public string Semester { get; set; } = string.Empty;

    /// <summary>Optional folder this document belongs to. Null = loose document.</summary>
    public Guid? FolderId { get; set; }
}

/// <summary>
/// Read model for a stored document. <see cref="DownloadUrl"/> is a short-lived signed URL,
/// only populated by the read endpoints (not list endpoints) to limit URL leakage.
/// </summary>
public sealed class DocumentDto
{
    public Guid Id { get; set; }

    public Guid? FolderId { get; set; }

    public string FileName { get; set; } = string.Empty;

    public long FileSizeBytes { get; set; }

    public string MimeType { get; set; } = string.Empty;

    public string SubjectCode { get; set; } = string.Empty;

    public string Semester { get; set; } = string.Empty;

    public int? PageCount { get; set; }

    public DocumentStatus Status { get; set; }

    public AI_Study_Hub_v2.Data.Entities.DocumentReviewStatus ReviewStatus { get; set; }

    public string? ErrorMessage { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Optional Supabase Storage signed URL (5 min TTL). Only set on detail endpoint.</summary>
    public string? DownloadUrl { get; set; }

    /// <summary>Folder display name (populated by admin dashboard queries).</summary>
    public string? FolderName { get; set; }
}

/// <summary>
/// Optional filter set for listing documents. All fields are optional.
/// Pagination is done client-side for Sprint 1 (small corpora); add cursor pagination later.
/// </summary>
public sealed class DocumentListQuery
{
    public string? SubjectCode { get; set; }

    public string? Semester { get; set; }

    public Guid? FolderId { get; set; }

    /// <summary>Free-text search over <c>file_name</c>. Matched case-insensitive.</summary>
    public string? Q { get; set; }
}

public sealed class FileViewUrlResponse
{
    /// <summary>Microsoft Office Online Viewer URL (iframe src).</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>Direct proxy URL for downloading the file (fallback when the embedded viewer is unavailable).</summary>
    public string? DownloadUrl { get; set; }
}

public sealed class RenameDocumentRequest
{
    [Required]
    [StringLength(255, MinimumLength = 1)]
    public string FileName { get; set; } = string.Empty;
}

public sealed class MoveDocumentFolderRequest
{
    /// <summary>Target folder id. Null moves the document back to loose documents.</summary>
    public Guid? FolderId { get; set; }
}

public sealed record DocumentContentChunkDto(
    int ChunkIndex,
    int? PageNumber,
    string Content,
    int? TokenCount);

public sealed record DocumentContentDto(
    Guid DocumentId,
    string FileName,
    string MimeType,
    int TotalChunks,
    int? PageCount,
    IReadOnlyList<DocumentContentChunkDto> Chunks);

public sealed class FolderDto
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public int DocumentCount { get; set; }

    public bool IsFavorite { get; set; }

    public AI_Study_Hub_v2.Data.Entities.FolderStatus ShareStatus { get; set; }

    public DateTimeOffset? SharedAt { get; set; }

    public string? Icon { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Display name of the folder owner (populated only by shared-list endpoint).</summary>
    public string? OwnerName { get; set; }

    public int LikeCount { get; set; }

    public int DislikeCount { get; set; }

    /// <summary>Reaction of the current authenticated user (null = no vote, true = like, false = dislike).</summary>
    public bool? CurrentUserVote { get; set; }

    public string? Status { get; set; }

    // UI-only computed properties (not from API)
    public string? Subject { get; set; }
    public string? Semester { get; set; }
    public string? Color { get; set; }
    public string? BorderColor { get; set; }
    public string? LastAccessText { get; set; }
}

public sealed class CreateFolderRequest
{
    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    [StringLength(500)]
    public string? Description { get; set; }
}

public sealed class UpdateFolderRequest
{
    [StringLength(100, MinimumLength = 1)]
    public string? Name { get; set; }

    [StringLength(500)]
    public string? Description { get; set; }

    public string? Icon { get; set; }

    public bool? IsFavorite { get; set; }
}

public sealed class VoteRequest
{
    [Required]
    public bool IsLike { get; set; }
}
