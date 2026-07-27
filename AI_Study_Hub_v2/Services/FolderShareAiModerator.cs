using System.IO;
using System.Text.RegularExpressions;
using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Dtos;

namespace AI_Study_Hub_v2.Services;

/// <summary>
/// Local-first "AI moderator" that allows folder sharing by default and blocks
/// only when there are clear policy-violation signals in metadata or extracted text.
/// This stays deterministic so the workflow is testable and can later be swapped
/// with a real LLM-backed reviewer without changing callers.
/// </summary>
public sealed partial class FolderShareAiModerator : IFolderShareAiModerator
{
    private const int ContextWindowRadius = 96;

    private static readonly string[] HarmfulOrIllegalSignals =
    [
        "exam leak", "answer key", "cheat sheet", "hack tool", "ddos tool",
        "malware", "virus", "trojan", "ransomware", "keylogger", "phishing",
        "payload", "stealer", "exploit kit", "botnet", "nsfw", "18+"
    ];

    private static readonly string[] CopyrightRiskSignals =
    [
        "torrent", "warez", "pirated", "piracy", "cracked", "keygen",
        "paid course dump", "full textbook pdf", "textbook scan",
        "movie pack", "music pack", "software crack"
    ];

    private static readonly string[] DangerousUrlSignals =
    [
        "bit.ly/", "tinyurl.com/", "grabify", "pastebin.com/raw"
    ];

    private static readonly string[] MalwareDeliverySignals =
    [
        "download crack", "download payload", "download malware", "activation bypass",
        "disable antivirus", "run this script", "execute this file"
    ];

    private static readonly string[] BenignEducationalContextSignals =
    [
        "tac hai", "tac dong", "nguy co", "phong tranh", "phong ngua", "canh bao",
        "dao duc", "ethic", "ethics", "harm", "harmful", "risk", "risks", "warning",
        "awareness", "prevent", "prevention", "mitigation", "defense", "defence",
        "detection", "analysis", "research", "case study", "education", "educational",
        "do not", "don't", "avoid", "forbidden", "khong nen", "khong duoc"
    ];

    private static readonly string[] ExplicitAbuseContextSignals =
    [
        "download", "download here", "sell", "buy", "steal", "bypass", "crack",
        "use this", "run this", "execute", "tutorial", "step by step", "how to",
        "dump", "leaked exam", "answer key", "full textbook pdf", "torrent", "keygen"
    ];

    private static readonly HashSet<string> DangerousFileExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".bat", ".cmd", ".ps1", ".js", ".vbs", ".scr", ".msi", ".dll", ".jar", ".apk", ".reg", ".hta"
    };

    public FolderShareModerationDecision Evaluate(
        Folder folder,
        IReadOnlyList<Document> documents,
        IReadOnlyList<string> extractedTexts)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(extractedTexts);

        if (documents.Count == 0)
        {
            return new FolderShareModerationDecision(
                FolderShareModerationOutcome.AutoRejected,
                "Folder has no documents, so it cannot be shared to the community yet.",
                0.99);
        }

        if (documents.Any(document => document.ReviewStatus == DocumentReviewStatus.Rejected))
        {
            return new FolderShareModerationDecision(
                FolderShareModerationOutcome.AutoRejected,
                "Folder contains a document that was already rejected during document moderation.",
                0.98);
        }

        var dangerousFileName = documents
            .Select(document => document.FileName?.Trim())
            .FirstOrDefault(fileName =>
                !string.IsNullOrWhiteSpace(fileName)
                && DangerousFileExtensions.Contains(Path.GetExtension(fileName)));
        if (!string.IsNullOrWhiteSpace(dangerousFileName))
        {
            return new FolderShareModerationDecision(
                FolderShareModerationOutcome.AutoRejected,
                $"AI rejected the folder because the document name '{dangerousFileName}' looks like an executable or script file.",
                0.99);
        }

        var combinedText = string.Join(
            ' ',
            new[]
            {
                folder.Name,
                folder.Description ?? string.Empty,
            }
            .Concat(documents.Select(document => document.FileName))
            .Concat(documents.Select(document => document.SubjectCode))
            .Concat(documents.Select(document => document.Semester))
            .Concat(extractedTexts.Take(24)));

        var normalized = combinedText.ToLowerInvariant();
        var searchableText = ModerationTokenRegex().Replace(normalized, " ");

        var harmfulHit = FindRejectableSignal(searchableText, HarmfulOrIllegalSignals);
        if (harmfulHit is not null)
        {
            return new FolderShareModerationDecision(
                FolderShareModerationOutcome.AutoRejected,
                $"AI rejected the folder because it detected harmful or illegal content signal '{harmfulHit}'.",
                0.98);
        }

        var copyrightHit = FindRejectableSignal(searchableText, CopyrightRiskSignals);
        if (copyrightHit is not null)
        {
            return new FolderShareModerationDecision(
                FolderShareModerationOutcome.AutoRejected,
                $"AI rejected the folder because it detected a potential copyright-risk signal '{copyrightHit}'.",
                0.97);
        }

        var dangerousUrlHit = FindDangerousUrlSignal(combinedText, normalized);
        if (dangerousUrlHit is not null)
        {
            return new FolderShareModerationDecision(
                FolderShareModerationOutcome.AutoRejected,
                $"AI rejected the folder because it detected a dangerous-link or malware-delivery signal '{dangerousUrlHit}'.",
                0.98);
        }

        return new FolderShareModerationDecision(
            FolderShareModerationOutcome.AutoApproved,
            "AI found no strong violation signals in the folder metadata or extracted document text, so the share request was approved.",
            0.91);
    }

    private static string? FindSignal(string normalizedText, IEnumerable<string> signals)
        => signals.FirstOrDefault(signal => normalizedText.Contains(signal, StringComparison.Ordinal));

    private static string? FindRejectableSignal(string normalizedText, IEnumerable<string> signals)
    {
        foreach (var signal in signals)
        {
            var searchIndex = 0;
            while (searchIndex < normalizedText.Length)
            {
                var matchIndex = normalizedText.IndexOf(signal, searchIndex, StringComparison.Ordinal);
                if (matchIndex < 0)
                {
                    break;
                }

                var context = ExtractContext(normalizedText, matchIndex, signal.Length);
                var hasBenignContext = BenignEducationalContextSignals.Any(context.Contains);
                var hasAbuseContext = ExplicitAbuseContextSignals.Any(context.Contains);

                if (!hasBenignContext || hasAbuseContext)
                {
                    return signal;
                }

                searchIndex = matchIndex + signal.Length;
            }
        }

        return null;
    }

    private static string ExtractContext(string normalizedText, int startIndex, int signalLength)
    {
        var contextStart = Math.Max(0, startIndex - ContextWindowRadius);
        var contextEnd = Math.Min(normalizedText.Length, startIndex + signalLength + ContextWindowRadius);
        return normalizedText[contextStart..contextEnd];
    }

    private static string? FindDangerousUrlSignal(string combinedText, string normalizedText)
    {
        var shortenedUrlHit = FindSignal(normalizedText, DangerousUrlSignals);
        if (shortenedUrlHit is not null)
        {
            return shortenedUrlHit;
        }

        if (!UrlRegex().IsMatch(combinedText))
        {
            return null;
        }

        return FindSignal(normalizedText, MalwareDeliverySignals);
    }

    [GeneratedRegex(@"(?:https?://|www\.)\S+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UrlRegex();

    [GeneratedRegex(@"[\W_]+", RegexOptions.CultureInvariant)]
    private static partial Regex ModerationTokenRegex();

    // ── Per-Document Evaluation ──

    public ShareReviewFileDto EvaluateDocument(Document document, Folder folder)
    {
        var fileName = document.FileName.ToLowerInvariant();
        var severity = ShareReviewSeverity.Low;
        string? aiReason = null;
        string? aiContext = null;
        double confidence = 0.95;
        var blocked = false;

        if (BlockedExtensions.Any(ext => fileName.EndsWith(ext)))
        {
            return new ShareReviewFileDto(document.Id, document.FileName, document.SubjectCode,
                document.FileSizeBytes, document.PageCount ?? 0, folder.User?.FullName ?? "Unknown",
                ShareReviewSeverity.High, "Blocked file extension", "This file type is not allowed.", 0.99, true);
        }

        var normalized = fileName.Replace('_', ' ').Replace('-', ' ');
        if (ContainsSignal(normalized, ExamSignals))
        {
            severity = ShareReviewSeverity.Medium;
            aiReason = "Potential academic integrity concern";
            aiContext = $"The filename \"{document.FileName}\" contains keywords that may indicate exam-related content.";
            confidence = 0.72;
        }
        else if (ContainsSignal(normalized, IllegalSignals))
        {
            severity = ShareReviewSeverity.High;
            aiReason = "Potential illegal or unauthorized content";
            aiContext = $"The filename \"{document.FileName}\" suggests cracked/hacked software distribution.";
            confidence = 0.88;
        }

        return new ShareReviewFileDto(document.Id, document.FileName, document.SubjectCode,
            document.FileSizeBytes, document.PageCount ?? 0, folder.User?.FullName ?? "Unknown",
            severity, aiReason, aiContext, confidence, blocked);
    }

    private static readonly string[] BlockedExtensions = { ".exe", ".dll", ".bat", ".cmd", ".msi", ".apk" };
    private static readonly string[] ExamSignals = { "exam", "leak", "answer", "solution", "cheat" };
    private static readonly string[] IllegalSignals = { "crack", "hack", "keygen", "pirate", "warez" };

    private static bool ContainsSignal(string text, string[] signals)
        => signals.Any(s => text.Contains(s, StringComparison.OrdinalIgnoreCase));
}
