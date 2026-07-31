using System.ComponentModel.DataAnnotations;
using AI_Study_Hub_v2.Data.Entities;

namespace AI_Study_Hub_v2.Dtos;

// ── Enums ──

public enum ShareReviewSeverity { Low, Medium, High, NoAiReview }

public enum ShareReviewDecision { Keep, MarkEducational, Delete, Rename, HumanReview }

// ── Review Summary (Step 1 Landing) ──

public sealed record ShareReviewSummaryDto(
    Guid FolderId,
    string FolderName,
    int TotalFiles,
    int CleanFiles,
    int FlaggedFiles,
    int BlockedFiles,
    double HealthScore,
    int EstimatedMinutes,
    IReadOnlyList<ShareReviewFileDto> Files);

// ── Per-File Detail (Step 2 Wizard) ──

public sealed record ShareReviewFileDto(
    Guid DocumentId,
    string FileName,
    string SubjectCode,
    long FileSizeBytes,
    int PageCount,
    string OwnerName,
    ShareReviewSeverity Severity,
    string? AiReason,
    string? AiContextSnippet,
    double AiConfidence,
    bool IsBlocked);

// ── Moderator pending queue ──

public sealed record PendingShareDocumentDto(
    Guid Id,
    string FileName,
    DocumentStatus Status,
    DocumentReviewStatus ReviewStatus);

public sealed record PendingShareFolderDto(
    Guid Id,
    string Name,
    string OwnerName,
    string SubjectCode,
    string Semester,
    int DocumentCount,
    DateTimeOffset SubmittedAt,
    int ShareSubmissionCount,
    int ShareFailureCount,
    string? AppealMessage,
    string? StudentFeedbackReason,
    string? HumanReviewReason,
    IReadOnlyList<PendingShareDocumentDto> Documents);

// ── User Decisions ──

public sealed record ShareReviewVerdict(
    Guid DocumentId,
    ShareReviewDecision Decision,
    string? Note);

public sealed record ShareReviewVerdictModel(
    string Label,
    string Description,
    ShareReviewDecision Decision,
    string Icon);

// ── Submit Batch ──

public sealed class ApplyDecisionsRequest
{
    [Required]
    [MinLength(1)]
    public List<VerdictItem> Verdicts { get; set; } = new();
}

public sealed class VerdictItem
{
    [Required] public Guid DocumentId { get; set; }
    [Required] public string Decision { get; set; } = string.Empty;
    public string? Note { get; set; }
}

public sealed record ApplyDecisionsResponse(
    bool AllClean,
    int DeletedCount,
    int KeptCount,
    int HumanReviewCount,
    string Message);

// ── Undo / Rollback ──

public sealed record ShareRollbackResponse(
    bool CanUndo,
    int SecondsRemaining,
    bool IsPublished);

// ── User Rejection Action (from workflow) ──

public enum RejectionAction { Resubmit, Delete, Keep }

public sealed class UserRejectionActionRequest
{
    [Required] public string Action { get; set; } = string.Empty;
    public string? Note { get; set; }
}
