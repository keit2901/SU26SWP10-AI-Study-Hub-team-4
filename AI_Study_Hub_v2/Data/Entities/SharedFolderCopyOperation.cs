namespace AI_Study_Hub_v2.Data.Entities;

public sealed class SharedFolderCopyOperation
{
    public const string Reserved = "Reserved";
    public const string Copying = "Copying";
    public const string Finalizing = "Finalizing";
    public const string Compensating = "Compensating";
    public const string CompensationRequired = "CompensationRequired";

    public Guid Id { get; set; }
    public Guid DestinationUserId { get; set; }
    public Guid SourceFolderId { get; set; }
    public Guid DestinationFolderId { get; set; }
    public string DestinationName { get; set; } = string.Empty;
    public string Status { get; set; } = Reserved;
    public long ReservedStorageBytes { get; set; }
    public string ManifestJson { get; set; } = string.Empty;
    public string? LastError { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
