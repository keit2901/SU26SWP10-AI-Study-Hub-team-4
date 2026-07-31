using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Dtos;
using AI_Study_Hub_v2.Services;
using AI_Study_Hub_v2.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace AI_Study_Hub_v2.Tests.Services;

[TestFixture]
public sealed class ShareReviewServiceTests
{
    [Test]
    public async Task GetReviewerReviewAsync_ActiveModeratorCanReadPendingFolderButStudentCannot()
    {
        await using var db = TestDb.CreateInMemoryWithDocuments();
        var owner = SeedUser(db, 2, "Owner");
        var moderator = SeedUser(db, 3, "Moderator");
        var student = SeedUser(db, 2, "Student");
        var folder = new Folder { Id = Guid.NewGuid(), UserId = owner.Id, Name = "Pending", ShareStatus = FolderStatus.PendingShare, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        db.Folders.Add(folder);
        db.Documents.Add(new Document { Id = Guid.NewGuid(), UserId = owner.Id, FolderId = folder.Id, FileName = "notes.pdf", StoragePath = "docs/notes.pdf", FileSizeBytes = 1234, PageCount = 7, MimeType = "application/pdf", SubjectCode = "SWP391", Semester = "SU26", Status = DocumentStatus.Ready, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        var sut = new ShareReviewService(db);

        var summary = await sut.GetReviewerReviewAsync(folder.Id, moderator.SupabaseUserId);
        summary.FolderId.Should().Be(folder.Id);
        summary.TotalFiles.Should().Be(1);
        summary.CleanFiles.Should().Be(0);
        summary.FlaggedFiles.Should().Be(0);
        summary.BlockedFiles.Should().Be(0);
        summary.HealthScore.Should().Be(0);
        summary.Files.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            FileName = "notes.pdf",
            SubjectCode = "SWP391",
            FileSizeBytes = 1234L,
            PageCount = 7,
            OwnerName = "Owner",
            Severity = ShareReviewSeverity.NoAiReview,
            AiReason = (string?)null,
            AiContextSnippet = (string?)null,
            AiConfidence = 0d,
            IsBlocked = false
        });
        var ownerSummary = await sut.GetReviewAsync(folder.Id, owner.SupabaseUserId);
        ownerSummary.Should().BeEquivalentTo(summary);

        Func<Task> studentAttempt = () => sut.GetReviewerReviewAsync(folder.Id, student.SupabaseUserId);
        (await studentAttempt.Should().ThrowAsync<AdminException>())
            .Which.Code.Should().Be("share_reviewer_role_required");
    }

    [Test]
    public async Task GetReviewerReviewAsync_OnlyExposesPendingShareFolders()
    {
        await using var db = TestDb.CreateInMemoryWithDocuments();
        var owner = SeedUser(db, 2, "Owner");
        var moderator = SeedUser(db, 3, "Moderator");
        var folder = new Folder { Id = Guid.NewGuid(), UserId = owner.Id, Name = "Private", ShareStatus = FolderStatus.None, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        db.Folders.Add(folder);
        await db.SaveChangesAsync();
        var sut = new ShareReviewService(db);

        Func<Task> privateFolderAttempt = () => sut.GetReviewerReviewAsync(folder.Id, moderator.SupabaseUserId);
        (await privateFolderAttempt.Should().ThrowAsync<AdminException>())
            .Which.Code.Should().Be("pending_share_not_found");
    }

    [Test]
    public async Task GetPendingReviewerQueueAsync_StudentIsDenied()
    {
        await using var db = TestDb.CreateInMemoryWithDocuments();
        var student = SeedUser(db, 2, "Student");
        var sut = new ShareReviewService(db);

        Func<Task> act = () => sut.GetPendingReviewerQueueAsync(student.SupabaseUserId);

        (await act.Should().ThrowAsync<AdminException>())
            .Which.Code.Should().Be("share_reviewer_role_required");
    }

    [TestCase(1, "Admin")]
    [TestCase(3, "Moderator")]
    public async Task GetPendingReviewerQueueAsync_ActiveReviewerGetsOnlyOtherPendingFoldersWithReviewData(
        int roleId,
        string reviewerName)
    {
        await using var db = TestDb.CreateInMemoryWithDocuments();
        var reviewer = SeedUser(db, roleId, reviewerName);
        var owner = SeedUser(db, 2, "Folder owner");
        var pending = SeedFolder(db, owner, "Pending folder", FolderStatus.PendingShare, DateTimeOffset.UtcNow.AddMinutes(-5));
        var ownPending = SeedFolder(db, reviewer, "Own pending", FolderStatus.PendingShare, DateTimeOffset.UtcNow);
        SeedFolder(db, owner, "Approved folder", FolderStatus.Approved, DateTimeOffset.UtcNow.AddMinutes(1));
        db.Documents.AddRange(
            new Document
            {
                Id = Guid.NewGuid(), UserId = owner.Id, FolderId = pending.Id, FileName = "notes.pdf",
                StoragePath = "docs/notes.pdf", FileSizeBytes = 1234, MimeType = "application/pdf",
                SubjectCode = "SWP391", Semester = "SU26", Status = DocumentStatus.Ready,
                ReviewStatus = DocumentReviewStatus.None, CreatedAt = pending.CreatedAt, UpdatedAt = pending.UpdatedAt
            },
            new Document
            {
                Id = Guid.NewGuid(), UserId = owner.Id, FolderId = pending.Id, FileName = "slides.pptx",
                StoragePath = "docs/slides.pptx", FileSizeBytes = 4567, MimeType = "application/vnd.ms-powerpoint",
                SubjectCode = "SWP391", Semester = "SU26", Status = DocumentStatus.Failed,
                ReviewStatus = DocumentReviewStatus.Rejected, CreatedAt = pending.CreatedAt.AddMinutes(1), UpdatedAt = pending.UpdatedAt
            });
        await db.SaveChangesAsync();
        var sut = new ShareReviewService(db);

        var queue = await sut.GetPendingReviewerQueueAsync(reviewer.SupabaseUserId);

        queue.Should().ContainSingle();
        var item = queue.Single();
        item.Id.Should().Be(pending.Id);
        item.OwnerName.Should().Be("Folder owner");
        item.SubjectCode.Should().Be("SWP391");
        item.Semester.Should().Be("SU26");
        item.DocumentCount.Should().Be(2);
        item.SubmittedAt.Should().Be(pending.UpdatedAt);
        item.Documents.Should().BeEquivalentTo(new[]
        {
            new { FileName = "notes.pdf", Status = DocumentStatus.Ready, ReviewStatus = DocumentReviewStatus.None },
            new { FileName = "slides.pptx", Status = DocumentStatus.Failed, ReviewStatus = DocumentReviewStatus.Rejected }
        });
        queue.Select(folder => folder.Id).Should().NotContain(ownPending.Id);
    }

    [Test]
    public async Task RetryShareAfterResolveAsync_VerifiesSupabaseOwnerAndNeverSchedulesPublication()
    {
        await using var db = TestDb.CreateInMemoryWithDocuments();
        var owner = SeedUser(db, 2, "Owner");
        var folder = new Folder
        {
            Id = Guid.NewGuid(), UserId = owner.Id, Name = "Retry", ShareStatus = FolderStatus.Rejected,
            SharedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        };
        db.Folders.Add(folder);
        await db.SaveChangesAsync();
        var sut = new ShareReviewService(db);

        await sut.RetryShareAfterResolveAsync(folder.Id, owner.SupabaseUserId);

        var persisted = await db.Folders.AsNoTracking().SingleAsync(item => item.Id == folder.Id);
        persisted.ShareStatus.Should().Be(FolderStatus.PendingShare);
        persisted.SharedAt.Should().BeNull();
        persisted.ShareReviewSource.Should().Be("HUMAN_REQUEST");
        Func<Task> wrongOwner = () => sut.RetryShareAfterResolveAsync(folder.Id, Guid.NewGuid());
        (await wrongOwner.Should().ThrowAsync<AdminException>()).Which.Code.Should().Be("folder_not_found");
    }

    private static User SeedUser(Data.AppDbContext db, int roleId, string name) { var user = new User { Id = Guid.NewGuid(), RoleId = roleId, SupabaseUserId = Guid.NewGuid(), Username = $"u{Guid.NewGuid():N}"[..12], FullName = name, IsActive = true, DailyTokenQuota = 25_000, TokenUsageDate = DateOnly.FromDateTime(DateTime.UtcNow), CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow }; db.Users.Add(user); db.SaveChanges(); return user; }
    private static Folder SeedFolder(Data.AppDbContext db, User owner, string name, FolderStatus status, DateTimeOffset updatedAt)
    {
        var folder = new Folder
        {
            Id = Guid.NewGuid(), UserId = owner.Id, Name = name, ShareStatus = status,
            CreatedAt = updatedAt.AddMinutes(-1), UpdatedAt = updatedAt,
            ShareSubmissionCount = 2, ShareFailureCount = 1,
            AppealMessage = "Please reconsider", StudentFeedbackReason = "The material is educational.",
            HumanReviewReason = "Needs moderator review."
        };
        db.Folders.Add(folder);
        db.SaveChanges();
        return folder;
    }
}
