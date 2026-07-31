using AI_Study_Hub_v2.Data.Entities;

namespace AI_Study_Hub_v2.Services;

/// <summary>Derives folder publication from its document-level moderation outcomes.</summary>
public interface IFolderPublicationStateService
{
    /// <summary>
    /// Stages the derived state on the tracked <paramref name="folder"/> only. This method never saves.
    /// </summary>
    void Recompute(Folder folder, IEnumerable<Document> documents, DateTimeOffset now);
}
