namespace AI_Study_Hub_v2.Data.Entities;

/// <summary>
/// Share lifecycle of a folder.
/// </summary>
public enum FolderStatus
{
    /// <summary>Default — folder is private, not being shared.</summary>
    None = 0,

    /// <summary>User requested sharing; awaiting moderator review.</summary>
    PendingShare = 1,

    /// <summary>Moderator approved; visible on Community page.</summary>
    Approved = 2,

    /// <summary>Moderator rejected; not visible on Community page.</summary>
    Rejected = 3,
}
