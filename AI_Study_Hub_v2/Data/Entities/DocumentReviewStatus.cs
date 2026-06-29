namespace AI_Study_Hub_v2.Data.Entities;

/// <summary>
/// Moderator review outcome for a document in a pending-share folder.
/// None = awaiting review, Approved = moderator accepted, Rejected = moderator denied.
/// </summary>
public enum DocumentReviewStatus
{
    /// <summary>Not yet reviewed by a moderator.</summary>
    None = 0,

    /// <summary>Explicitly approved by a moderator.</summary>
    Approved = 1,

    /// <summary>Explicitly rejected by a moderator.</summary>
    Rejected = 2,
}
