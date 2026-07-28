using MudBlazor;

namespace AI_Study_Hub_v2.Components.Pages;

public static class ShareReviewVerdicts
{
    public static readonly ShareReviewVerdictModel[] All = new[]
    {
        new ShareReviewVerdictModel(
            "Mark as Educational",
            "Keep & share — AI will learn this content is educational",
            Dtos.ShareReviewDecision.MarkEducational,
            Icons.Material.Filled.School),
        new ShareReviewVerdictModel(
            "Delete file",
            "Remove permanently from folder",
            Dtos.ShareReviewDecision.Delete,
            Icons.Material.Filled.Delete),
        new ShareReviewVerdictModel(
            "Rename file",
            "Change name & retry AI review",
            Dtos.ShareReviewDecision.Rename,
            Icons.Material.Filled.DriveFileRenameOutline),
        new ShareReviewVerdictModel(
            "Request Human Review",
            "Admin will review within 24h",
            Dtos.ShareReviewDecision.HumanReview,
            Icons.Material.Filled.PersonSearch),
    };
}

public sealed record ShareReviewVerdictModel(
    string Label,
    string Description,
    Dtos.ShareReviewDecision Decision,
    string Icon);
