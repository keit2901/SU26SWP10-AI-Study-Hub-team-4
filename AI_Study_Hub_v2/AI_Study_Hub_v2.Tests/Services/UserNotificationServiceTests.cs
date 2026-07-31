using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Dtos;
using AI_Study_Hub_v2.Services;
using AI_Study_Hub_v2.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace AI_Study_Hub_v2.Tests.Services;

[TestFixture]
public sealed class UserNotificationServiceTests
{
    [TestCase(FolderStatus.Approved, UserNotificationOutcome.Approved)]
    [TestCase(FolderStatus.Rejected, UserNotificationOutcome.Rejected)]
    public async Task Stage_final_moderation_adds_a_safe_owner_snapshot_without_saving(
        FolderStatus finalStatus,
        UserNotificationOutcome expectedOutcome)
    {
        await using var db = TestDb.CreateInMemoryWithDocuments();
        var (owner, folder) = await AddOwnerAndFolderAsync(db, shareSubmissionCount: 4);
        var service = new UserNotificationService(db);
        var occurredAt = new DateTimeOffset(2026, 7, 31, 10, 0, 0, TimeSpan.Zero);
        folder.ShareStatus = finalStatus;

        service.StageFolderModerationFinal(folder, FolderStatus.PendingShare, "Sensitive rejection reason", occurredAt);

        db.ChangeTracker.Entries<UserNotification>().Should().ContainSingle(entry => entry.State == EntityState.Added);
        (await db.UserNotifications.AsNoTracking().CountAsync()).Should().Be(0);
        var staged = db.UserNotifications.Local.Single();
        staged.RecipientUserId.Should().Be(owner.Id);
        staged.FolderId.Should().Be(folder.Id);
        staged.SubmissionNumber.Should().Be(4);
        staged.Kind.Should().Be(UserNotificationKind.FolderModerationFinal);
        staged.Outcome.Should().Be(expectedOutcome);
        staged.FolderName.Should().Be(folder.Name);
        staged.CreatedAt.Should().Be(occurredAt);
        staged.Message.Should().NotContain("Sensitive rejection reason");
    }

    [Test]
    public async Task Stage_ignores_non_final_and_repeated_transitions()
    {
        await using var db = TestDb.CreateInMemoryWithDocuments();
        var (_, folder) = await AddOwnerAndFolderAsync(db);
        var service = new UserNotificationService(db);

        service.StageFolderModerationFinal(folder, FolderStatus.PendingShare, null, DateTimeOffset.UtcNow);
        folder.ShareStatus = FolderStatus.Approved;
        service.StageFolderModerationFinal(folder, FolderStatus.Approved, null, DateTimeOffset.UtcNow);
        service.StageFolderModerationFinal(folder, FolderStatus.PendingShare, null, DateTimeOffset.UtcNow);
        service.StageFolderModerationFinal(folder, FolderStatus.PendingShare, null, DateTimeOffset.UtcNow);

        db.UserNotifications.Local.Should().ContainSingle();
    }

    [Test]
    public async Task Get_mine_is_owner_scoped_ordered_limited_and_counts_all_unread()
    {
        await using var db = TestDb.CreateInMemoryWithDocuments();
        var (owner, folder) = await AddOwnerAndFolderAsync(db);
        var (otherOwner, otherFolder) = await AddOwnerAndFolderAsync(db, "Other folder");
        var first = AddNotification(owner, folder, 1, DateTimeOffset.UtcNow.AddMinutes(-3));
        var second = AddNotification(owner, folder, 2, DateTimeOffset.UtcNow.AddMinutes(-2));
        var third = AddNotification(owner, folder, 3, DateTimeOffset.UtcNow.AddMinutes(-1));
        db.UserNotifications.AddRange(first, second, third);
        second.ReadAt = DateTimeOffset.UtcNow;
        db.UserNotifications.Add(AddNotification(otherOwner, otherFolder, 1, DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();
        var service = new UserNotificationService(db);

        var feed = await service.GetMineAsync(owner.SupabaseUserId, 2, CancellationToken.None);

        feed.Items.Select(item => item.Id).Should().Equal(third.Id, second.Id);
        feed.UnreadCount.Should().Be(2);
        feed.Items.Should().NotContain(item => item.FolderId == otherFolder.Id);
        typeof(UserNotificationFeedItemDto).GetProperties().Should().NotContain(property => property.Name == "RecipientUserId");
    }

    [Test]
    public async Task Mark_read_is_owner_scoped_and_idempotent()
    {
        await using var db = TestDb.CreateInMemoryWithDocuments();
        var (owner, folder) = await AddOwnerAndFolderAsync(db);
        var (otherOwner, _) = await AddOwnerAndFolderAsync(db, "Other folder");
        var notification = AddNotification(owner, folder, 1, DateTimeOffset.UtcNow);
        db.UserNotifications.Add(notification);
        await db.SaveChangesAsync();
        var service = new UserNotificationService(db);

        await service.MarkReadAsync(owner.SupabaseUserId, notification.Id, CancellationToken.None);
        var firstReadAt = (await db.UserNotifications.SingleAsync()).ReadAt;
        firstReadAt.Should().NotBeNull();
        await service.MarkReadAsync(owner.SupabaseUserId, notification.Id, CancellationToken.None);
        (await db.UserNotifications.SingleAsync()).ReadAt.Should().Be(firstReadAt);
        var act = () => service.MarkReadAsync(otherOwner.SupabaseUserId, notification.Id, CancellationToken.None);
        (await act.Should().ThrowAsync<UserNotificationException>()).Which.Code.Should().Be("notification_not_found");
    }

    [Test]
    public async Task Folder_delete_cascades_staged_notifications()
    {
        await using var db = TestDb.CreateInMemoryWithDocuments();
        var (owner, folder) = await AddOwnerAndFolderAsync(db);
        db.UserNotifications.Add(AddNotification(owner, folder, 1, DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();

        db.Folders.Remove(folder);
        await db.SaveChangesAsync();

        (await db.UserNotifications.CountAsync()).Should().Be(0);
    }

    private static async Task<(User Owner, Folder Folder)> AddOwnerAndFolderAsync(
        Data.AppDbContext db,
        string folderName = "Moderated folder",
        int shareSubmissionCount = 1)
    {
        var owner = new User
        {
            Id = Guid.NewGuid(),
            SupabaseUserId = Guid.NewGuid(),
            RoleId = 2,
            Username = Guid.NewGuid().ToString("N")[..15],
            FullName = "Notification owner",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        var folder = new Folder
        {
            Id = Guid.NewGuid(),
            UserId = owner.Id,
            User = owner,
            Name = folderName,
            ShareStatus = FolderStatus.PendingShare,
            ShareSubmissionCount = shareSubmissionCount,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.AddRange(owner, folder);
        await db.SaveChangesAsync();
        return (owner, folder);
    }

    private static UserNotification AddNotification(User owner, Folder folder, int submissionNumber, DateTimeOffset createdAt) =>
        new()
        {
            Id = Guid.NewGuid(),
            RecipientUserId = owner.Id,
            FolderId = folder.Id,
            SubmissionNumber = submissionNumber,
            Kind = UserNotificationKind.FolderModerationFinal,
            Outcome = UserNotificationOutcome.Approved,
            FolderName = folder.Name,
            Title = "Folder approved for sharing",
            Message = "Your folder is now available in the community.",
            CreatedAt = createdAt
        };
}
