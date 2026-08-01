using System.Runtime.CompilerServices;
using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Dtos;

[assembly: InternalsVisibleTo("AI_Study_Hub_v2.Tests")]

namespace AI_Study_Hub_v2.Components.Admin.Documents;

internal static class EscalationDecisionGuard
{
    internal static bool CanReview(DocumentEscalationDto? escalation) =>
        escalation is not null
        && string.Equals(escalation.EscalationStatus, "Pending", StringComparison.OrdinalIgnoreCase)
        && escalation.Items.Count > 0
        && escalation.Items.All(IsItemActionable);

    internal static bool IsItemActionable(DocumentEscalationItemDto item) =>
        string.Equals(item.ItemStatus, "Pending", StringComparison.OrdinalIgnoreCase)
        && item.DocumentId.HasValue
        && item.ProcessingStatus == DocumentStatus.Ready
        && item.CurrentModerationGeneration.HasValue
        && item.CurrentModerationGeneration.Value == item.ModerationGeneration
        && item.CurrentReviewStatus == DocumentReviewStatus.Escalated;
}
