using AI_Study_Hub_v2.Data.Entities;

namespace AI_Study_Hub_v2.Services;

public interface IFolderShareAiModerator
{
    FolderShareModerationDecision Evaluate(Folder folder, IReadOnlyList<Document> documents);
}

public sealed record FolderShareModerationDecision(
    FolderShareModerationOutcome Outcome,
    string Reason,
    double Confidence);

public enum FolderShareModerationOutcome
{
    AutoApproved = 0,
    NeedsHumanReview = 1,
    AutoRejected = 2,
}
