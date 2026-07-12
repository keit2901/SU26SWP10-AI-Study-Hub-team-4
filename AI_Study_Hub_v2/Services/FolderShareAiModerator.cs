using System.Text.RegularExpressions;
using AI_Study_Hub_v2.Data.Entities;

namespace AI_Study_Hub_v2.Services;

/// <summary>
/// Local-first "AI moderator" that turns folder share requests into
/// approve / reject / human-review decisions with explainable reasons.
/// This is intentionally deterministic so the workflow is testable and can
/// later be replaced by a real LLM-backed reviewer without changing callers.
/// </summary>
public sealed partial class FolderShareAiModerator : IFolderShareAiModerator
{
    private static readonly string[] AcademicSignals =
    [
        "study", "note", "notes", "lecture", "slides", "lab", "assignment",
        "report", "tutorial", "quiz", "exam", "reference", "chapter",
        "syllabus", "revision", "summary", "research", "swp", "prn", "se"
    ];

    private static readonly string[] HardRejectSignals =
    [
        "torrent", "crack", "warez", "pirated", "piracy", "nsfw", "18+",
        "cheat", "leak", "answer key", "hack tool", "movie", "music pack", "game mod"
    ];

    public FolderShareModerationDecision Evaluate(Folder folder, IReadOnlyList<Document> documents)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(documents);

        if (documents.Count == 0)
        {
            return new FolderShareModerationDecision(
                FolderShareModerationOutcome.AutoRejected,
                "Folder has no documents, so it cannot be shared to the community yet.",
                0.99);
        }

        if (documents.Any(document => document.Status != DocumentStatus.Ready))
        {
            return new FolderShareModerationDecision(
                FolderShareModerationOutcome.AutoRejected,
                "Folder contains documents that are not fully ready, so the share request was blocked by a hard rule.",
                0.97);
        }

        if (documents.Any(document => document.ReviewStatus == DocumentReviewStatus.Rejected))
        {
            return new FolderShareModerationDecision(
                FolderShareModerationOutcome.AutoRejected,
                "Folder contains a document that was already rejected during document moderation.",
                0.98);
        }

        var combinedText = string.Join(
            ' ',
            new[]
            {
                folder.Name,
                folder.Description ?? string.Empty,
            }.Concat(documents.Select(document => document.FileName)));

        var normalized = combinedText.ToLowerInvariant();
        var hardRejectHit = HardRejectSignals.FirstOrDefault(signal => normalized.Contains(signal, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(hardRejectHit))
        {
            return new FolderShareModerationDecision(
                FolderShareModerationOutcome.AutoRejected,
                $"Detected blocked content signal '{hardRejectHit}', so the request needs to stay rejected unless a moderator overrides it.",
                0.98);
        }

        var missingDescription = string.IsNullOrWhiteSpace(folder.Description) || folder.Description!.Trim().Length < 20;
        var subjectCodes = documents
            .Select(document => document.SubjectCode?.Trim().ToUpperInvariant())
            .Where(subject => !string.IsNullOrWhiteSpace(subject))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var semesters = documents
            .Select(document => document.Semester?.Trim().ToUpperInvariant())
            .Where(semester => !string.IsNullOrWhiteSpace(semester))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var academicSignalCount = AcademicSignals.Count(signal => normalized.Contains(signal, StringComparison.OrdinalIgnoreCase));
        var containsSubjectCode = SubjectCodeRegex().IsMatch(combinedText);
        var consistentSubject = subjectCodes.Count == 1;
        var consistentSemester = semesters.Count == 1;

        if (consistentSubject
            && consistentSemester
            && !missingDescription
            && (containsSubjectCode || academicSignalCount >= 2))
        {
            var subject = subjectCodes[0];
            var semester = semesters[0];
            return new FolderShareModerationDecision(
                FolderShareModerationOutcome.AutoApproved,
                $"Academic signals look consistent for {subject} {semester}, so AI approved the folder automatically.",
                0.93);
        }

        var reasons = new List<string>();
        if (missingDescription)
        {
            reasons.Add("folder description is missing or too short");
        }
        if (!consistentSubject)
        {
            reasons.Add("documents do not share one clear subject code");
        }
        if (!consistentSemester)
        {
            reasons.Add("documents do not share one clear semester");
        }
        if (!containsSubjectCode && academicSignalCount < 2)
        {
            reasons.Add("metadata does not strongly signal academic relevance");
        }

        var reasonText = reasons.Count == 0
            ? "AI could not reach a confident decision, so a human moderator should review the folder."
            : $"AI is not confident enough because {string.Join(", ", reasons)}, so the request was sent to human review.";

        return new FolderShareModerationDecision(
            FolderShareModerationOutcome.NeedsHumanReview,
            reasonText,
            0.64);
    }

    [GeneratedRegex(@"\b[A-Z]{2,4}[0-9]{3}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SubjectCodeRegex();
}
