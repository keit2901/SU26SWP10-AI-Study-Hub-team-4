namespace AI_Study_Hub_v2.Data.Entities;

/// <summary>
/// Metadata for a single uploaded study document (PDF / DOCX / PPTX).
/// The actual file bytes live in Supabase Storage at <see cref="StoragePath"/>.
/// </summary>
public sealed class Document
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid? FolderId { get; set; }

    /// <summary>Original file name as uploaded by the user.</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>Object key inside the Supabase Storage bucket (UNIQUE).</summary>
    public string StoragePath { get; set; } = string.Empty;

    public long FileSizeBytes { get; set; }

    public string MimeType { get; set; } = string.Empty;

    /// <summary>FPT subject code, e.g. "SWP391", "PRN232". Required (Sprint 1).</summary>
    public string SubjectCode { get; set; } = string.Empty;

    /// <summary>FPT semester tag, e.g. "SU26", "FA25". Required (Sprint 1).</summary>
    public string Semester { get; set; } = string.Empty;

    /// <summary>Total pages extracted from the document; <c>null</c> until ingestion completes.</summary>
    public int? PageCount { get; set; }

    public DocumentStatus Status { get; set; } = DocumentStatus.Uploading;

    /// <summary>Populated when <see cref="Status"/> = <c>Failed</c>.</summary>
    public string? ErrorMessage { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public User User { get; set; } = null!;

    public Folder? Folder { get; set; }

    public ICollection<DocumentChunk> Chunks { get; set; } = new List<DocumentChunk>();
}
