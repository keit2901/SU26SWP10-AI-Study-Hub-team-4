using AI_Study_Hub_v2.Components.Admin.Documents;
using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Dtos;

namespace AI_Study_Hub_v2.Tests.Components.Admin.Documents;

[TestFixture]
public sealed class EscalationDecisionGuardTests
{
    [Test]
    public void CanReview_AllPendingCurrentActionableItems_ReturnsTrue()
    {
        var escalation = CreateEscalation(CreateActionableItem(), CreateActionableItem());

        EscalationDecisionGuard.CanReview(escalation).Should().BeTrue();
    }

    [Test]
    public void CanReview_OneStalePendingSibling_ReturnsFalse()
    {
        var stale = CreateActionableItem() with { CurrentModerationGeneration = 8 };
        var escalation = CreateEscalation(CreateActionableItem(), stale);

        EscalationDecisionGuard.CanReview(escalation).Should().BeFalse();
    }

    [TestCase(true)]
    [TestCase(false)]
    public void CanReview_OneUnavailableOrDeletedSibling_ReturnsFalse(bool deleted)
    {
        var unavailable = deleted
            ? CreateActionableItem() with { DocumentId = null, CurrentModerationGeneration = null }
            : CreateActionableItem() with { ProcessingStatus = DocumentStatus.Processing };
        var escalation = CreateEscalation(CreateActionableItem(), unavailable);

        EscalationDecisionGuard.CanReview(escalation).Should().BeFalse();
    }

    [Test]
    public void CanReview_OneResolvedItem_ReturnsFalse()
    {
        var resolved = CreateActionableItem() with
        {
            ItemStatus = "Approved",
            CurrentReviewStatus = DocumentReviewStatus.Approved
        };
        var escalation = CreateEscalation(CreateActionableItem(), resolved);

        EscalationDecisionGuard.CanReview(escalation).Should().BeFalse();
    }

    [Test]
    public void CanReview_ResolvedEscalationWithOtherwiseActionableItems_ReturnsFalse()
    {
        var pending = CreateEscalation(CreateActionableItem());
        var resolved = pending with { EscalationStatus = "Approved" };

        EscalationDecisionGuard.CanReview(resolved).Should().BeFalse();
    }

    private static DocumentEscalationDto CreateEscalation(params DocumentEscalationItemDto[] items) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Moderator",
            "Needs administrator review.",
            "Pending",
            null,
            null,
            DateTimeOffset.UtcNow,
            null,
            items)
        {
            FolderName = "Review folder",
            ShareReviewSource = "HUMAN_REQUEST"
        };

    private static DocumentEscalationItemDto CreateActionableItem()
    {
        const int generation = 7;
        return new DocumentEscalationItemDto(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "lecture.pdf",
            generation,
            "Needs administrator review.",
            "Pending",
            null,
            null,
            null)
        {
            ProcessingStatus = DocumentStatus.Ready,
            CurrentReviewStatus = DocumentReviewStatus.Escalated,
            CurrentModerationGeneration = generation
        };
    }
}
