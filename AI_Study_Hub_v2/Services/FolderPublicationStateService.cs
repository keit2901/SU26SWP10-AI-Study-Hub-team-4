using AI_Study_Hub_v2.Data.Entities;

namespace AI_Study_Hub_v2.Services;

public sealed class FolderPublicationStateService : IFolderPublicationStateService
{
    public void Recompute(Folder folder, IEnumerable<Document> documents, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(documents);

        var outcomes = documents.ToList();
        var hasApproved = outcomes.Any(document =>
            document.Status == DocumentStatus.Ready &&
            document.ReviewStatus == DocumentReviewStatus.Approved);
        var hasUnresolved = outcomes.Any(document => document.ReviewStatus is DocumentReviewStatus.None or DocumentReviewStatus.Escalated);
        var hasRejected = outcomes.Any(document => document.ReviewStatus == DocumentReviewStatus.Rejected);

        // A folder only enters the per-file moderation lifecycle after an explicit share
        // submission or while it is already public. Private and previously rejected folders
        // must not become Pending Share merely because their documents have ReviewStatus.None.
        if (folder.ShareStatus is FolderStatus.None or FolderStatus.Rejected)
        {
            folder.UpdatedAt = now;
            return;
        }

        // An already-public folder remains public while a new file is being moderated, but only
        // its approved subset is ever exposed by public queries.
        var wasPublic = folder.ShareStatus == FolderStatus.Approved;
        var next = wasPublic && hasApproved
            ? FolderStatus.Approved
            : hasUnresolved
                ? FolderStatus.PendingShare
                : hasApproved
                    ? FolderStatus.Approved
                : hasRejected
                    ? FolderStatus.Rejected
                    : FolderStatus.None;

        var willBePublic = next == FolderStatus.Approved;
        folder.ShareStatus = next;
        if (!wasPublic && willBePublic)
        {
            folder.SharedAt = now;
        }
        else if (wasPublic && !willBePublic)
        {
            folder.SharedAt = null;
        }

        folder.UpdatedAt = now;
    }
}
