using System.IO;
using System.Text.RegularExpressions;
using AI_Study_Hub_v2.Data.Entities;

namespace AI_Study_Hub_v2.Services;

/// <summary>
/// Local-first "AI moderator" that allows folder sharing by default and blocks
/// only when there are clear policy-violation signals in metadata or extracted text.
/// This stays deterministic so the workflow is testable and can later be swapped
/// with a real LLM-backed reviewer without changing callers.
/// </summary>
public sealed partial class FolderShareAiModerator : IFolderShareAiModerator
{
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

        var harmfulHit = FindSignal(searchableText, HarmfulOrIllegalSignals);
        if (harmfulHit is not null)
        {
            return new FolderShareModerationDecision(
                FolderShareModerationOutcome.AutoRejected,
                $"AI rejected the folder because it detected harmful or illegal content signal '{harmfulHit}'.",
                0.98);
        }

        var copyrightHit = FindSignal(searchableText, CopyrightRiskSignals);
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
}
