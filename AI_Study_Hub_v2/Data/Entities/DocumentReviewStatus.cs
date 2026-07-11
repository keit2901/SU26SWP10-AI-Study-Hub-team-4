namespace AI_Study_Hub_v2.Data.Entities;

/// <summary>
/// Moderator review outcome for a document in a pending-share folder.
/// None = awaiting review, Approved = moderator accepted, Rejected = moderator denied.
/// </summary>
public enum DocumentReviewStatus
{
    None = 0,
    Approved = 1,
    Rejected = 2,
    Escalated = 3,
}
