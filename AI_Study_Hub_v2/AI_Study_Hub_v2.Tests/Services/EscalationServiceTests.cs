using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Dtos;
using AI_Study_Hub_v2.Services;
using AI_Study_Hub_v2.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace AI_Study_Hub_v2.Tests.Services;

[TestFixture]
public sealed class EscalationServiceTests
{
    [Test]
    public async Task CreateAsync_SelectedReadyUnreviewedFiles_EscalatesAtomicallyAndSnapshotsEachFile()
    {
        await using var db = TestDb.CreateInMemoryWithDocuments();
        var moderator = SeedUser(db, 3, "Moderator");
        var folder = SeedFolder(db, moderator.Id);
        var first = SeedDocument(db, moderator.Id, folder.Id, "first.pdf", generation: 4);
        var second = SeedDocument(db, moderator.Id, folder.Id, "second.pdf", generation: 7);

        var result = await CreateSut(db).CreateAsync(moderator.Id, Request(folder.Id, first.Id, second.Id));

        result.EscalationStatus.Should().Be("Pending");
        result.Items.Should().BeEquivalentTo(new[]
        {
            new { DocumentId = (Guid?)first.Id, FileNameSnapshot = "first.pdf", ModerationGeneration = 4, ItemStatus = "Pending" },
            new { DocumentId = (Guid?)second.Id, FileNameSnapshot = "second.pdf", ModerationGeneration = 7, ItemStatus = "Pending" }
        });
        (await db.Documents.SingleAsync(document => document.Id == first.Id)).ReviewStatus.Should().Be(DocumentReviewStatus.Escalated);
        (await db.Documents.SingleAsync(document => document.Id == second.Id)).ReviewStatus.Should().Be(DocumentReviewStatus.Escalated);
        (await db.Folders.SingleAsync(item => item.Id == folder.Id)).ShareStatus.Should().Be(FolderStatus.PendingShare);
    }

    [Test]
    public async Task GetByIdAsync_ProjectsLiveFolderAndDocumentMetadata_AndPreservesDeletedHistory()
    {
        await using var db = TestDb.CreateInMemoryWithDocuments();
        var moderator = SeedUser(db, 3, "Moderator");
        var folder = SeedFolder(db, moderator.Id);
        folder.Name = "Live folder";
        folder.ShareReviewSource = "HUMAN_REQUEST";
        var document = SeedDocument(db, moderator.Id, folder.Id, "live.pdf", generation: 4);
        document.FileSizeBytes = 1234;
        document.MimeType = "application/pdf";
        await db.SaveChangesAsync();
        var sut = CreateSut(db);
        var created = await sut.CreateAsync(moderator.Id, Request(folder.Id, document.Id));

        var live = await sut.GetByIdAsync(created.Id);

        live.FolderId.Should().Be(folder.Id);
        live.FolderName.Should().Be("Live folder");
        live.ShareReviewSource.Should().Be("HUMAN_REQUEST");
        live.Items.Single().MimeType.Should().Be("application/pdf");
        live.Items.Single().FileSizeBytes.Should().Be(1234);
        live.Items.Single().CurrentReviewStatus.Should().Be(DocumentReviewStatus.Escalated);
        live.Items.Single().CurrentModerationGeneration.Should().Be(4);

        var item = await db.DocumentEscalationItems.SingleAsync();
        item.DocumentId = null;
        await db.SaveChangesAsync();
        var deleted = await sut.GetByIdAsync(created.Id);
        deleted.Items.Single().FileNameSnapshot.Should().Be("live.pdf");
        deleted.Items.Single().MimeType.Should().BeNull();
        deleted.Items.Single().CurrentReviewStatus.Should().BeNull();
    }

    [TestCase(DocumentReviewStatus.Approved)]
    [TestCase(DocumentReviewStatus.Rejected)]
    [TestCase(DocumentReviewStatus.Escalated)]
    public async Task CreateAsync_NonNoneDocument_FailsWithoutMutatingSelectedFiles(DocumentReviewStatus initialStatus)
    {
        await using var db = TestDb.CreateInMemoryWithDocuments();
        var moderator = SeedUser(db, 3, "Moderator");
        var folder = SeedFolder(db, moderator.Id);
        var eligible = SeedDocument(db, moderator.Id, folder.Id, "eligible.pdf");
        var invalid = SeedDocument(db, moderator.Id, folder.Id, "invalid.pdf");
        invalid.ReviewStatus = initialStatus;
        await db.SaveChangesAsync();

        Func<Task> act = () => CreateSut(db).CreateAsync(moderator.Id, Request(folder.Id, eligible.Id, invalid.Id));

        (await act.Should().ThrowAsync<AdminException>()).Which.Code.Should().Be("escalation_document_not_eligible");
        (await db.Documents.SingleAsync(document => document.Id == eligible.Id)).ReviewStatus.Should().Be(DocumentReviewStatus.None);
        (await db.Documents.SingleAsync(document => document.Id == invalid.Id)).ReviewStatus.Should().Be(initialStatus);
        (await db.DocumentEscalations.CountAsync()).Should().Be(0);
    }

    [Test]
    public async Task CreateAsync_StudentAndForeignDocumentAttempts_AreDeniedWithoutMutation()
    {
        await using var db = TestDb.CreateInMemoryWithDocuments();
        var student = SeedUser(db, 2, "Student");
        var moderator = SeedUser(db, 3, "Moderator");
        var folder = SeedFolder(db, moderator.Id);
        var foreign = SeedDocument(db, student.Id, folder.Id, "foreign.pdf");
        var sut = CreateSut(db);

        Func<Task> studentAttempt = () => sut.CreateAsync(student.Id, Request(folder.Id, foreign.Id));
        (await studentAttempt.Should().ThrowAsync<AdminException>()).Which.Code.Should().Be("share_reviewer_role_required");

        Func<Task> moderatorAttempt = () => sut.CreateAsync(moderator.Id, Request(folder.Id, foreign.Id));
        (await moderatorAttempt.Should().ThrowAsync<AdminException>()).Which.Code.Should().Be("escalation_document_owner_mismatch");
        (await db.DocumentEscalations.CountAsync()).Should().Be(0);
        (await db.Documents.SingleAsync(document => document.Id == foreign.Id)).ReviewStatus.Should().Be(DocumentReviewStatus.None);
    }

    [Test]
    public async Task ResolveItems_MixedDecisions_PersistsItemOutcomesRecomputesFolderAndStagesOneFinalPerFile()
    {
        await using var db = TestDb.CreateInMemoryWithDocuments();
        var moderator = SeedUser(db, 3, "Moderator");
        var admin = SeedUser(db, 1, "Admin");
        var folder = SeedFolder(db, moderator.Id);
        var first = SeedDocument(db, moderator.Id, folder.Id, "first.pdf");
        var second = SeedDocument(db, moderator.Id, folder.Id, "second.pdf");
        var sut = CreateSut(db);
        var pending = await sut.CreateAsync(moderator.Id, Request(folder.Id, first.Id, second.Id));

        var resolved = await sut.ResolveItemsAsync(pending.Id, Resolve(
            (pending.Items.Single(item => item.DocumentId == first.Id).Id, "Approved", null),
            (pending.Items.Single(item => item.DocumentId == second.Id).Id, "Rejected", "Not suitable")), admin.Id);

        resolved.EscalationStatus.Should().Be("Resolved");
        resolved.Items.Should().Contain(item => item.DocumentId == first.Id && item.ItemStatus == "Approved" && item.ResolvedByName == "Admin");
        resolved.Items.Should().Contain(item => item.DocumentId == second.Id && item.ItemStatus == "Rejected" && item.AdminResponse == "Not suitable");
        (await db.Documents.SingleAsync(document => document.Id == first.Id)).ReviewStatus.Should().Be(DocumentReviewStatus.Approved);
        (await db.Documents.SingleAsync(document => document.Id == second.Id)).ReviewStatus.Should().Be(DocumentReviewStatus.Rejected);
        (await db.Folders.SingleAsync(item => item.Id == folder.Id)).ShareStatus.Should().Be(FolderStatus.Approved);
        var notifications = await db.UserNotifications.OrderBy(notification => notification.DocumentId).ToListAsync();
        notifications.Should().HaveCount(2);
        notifications.Should().OnlyContain(notification => notification.Kind == UserNotificationKind.DocumentModerationFinal);
        notifications.Should().Contain(notification => notification.DocumentId == first.Id && notification.Outcome == UserNotificationOutcome.Approved && notification.Title.StartsWith("Admin approved"));
        notifications.Should().Contain(notification => notification.DocumentId == second.Id && notification.Outcome == UserNotificationOutcome.Rejected && notification.Message.Contains("Not suitable"));
    }

    [TestCase("Approved", FolderStatus.Approved, UserNotificationOutcome.Approved)]
    [TestCase("Rejected", FolderStatus.Rejected, UserNotificationOutcome.Rejected)]
    public async Task ResolveItems_ReactivatesRejectedFolderBeforeRecomputingFinalPublicationState(
        string decision,
        FolderStatus expectedFolderStatus,
        UserNotificationOutcome expectedNotificationOutcome)
    {
        await using var db = TestDb.CreateInMemoryWithDocuments();
        var moderator = SeedUser(db, 3, "Moderator");
        var admin = SeedUser(db, 1, "Admin");
        var folder = SeedFolder(db, moderator.Id);
        var document = SeedDocument(db, moderator.Id, folder.Id, "race.pdf");
        var sut = CreateSut(db);
        var pending = await sut.CreateAsync(moderator.Id, Request(folder.Id, document.Id));
        folder.ShareStatus = FolderStatus.Rejected;
        folder.SharedAt = DateTimeOffset.UtcNow.AddDays(-1);
        await db.SaveChangesAsync();

        await sut.ResolveItemsAsync(pending.Id, Resolve((pending.Items.Single().Id, decision,
            decision == "Rejected" ? "Not suitable" : null)), admin.Id);

        var persistedFolder = await db.Folders.SingleAsync(candidate => candidate.Id == folder.Id);
        persistedFolder.ShareStatus.Should().Be(expectedFolderStatus);
        if (decision == "Approved")
            persistedFolder.SharedAt.Should().NotBeNull();
        else
            persistedFolder.SharedAt.Should().BeNull();
        (await db.UserNotifications.SingleAsync(notification => notification.Kind == UserNotificationKind.DocumentModerationFinal))
            .Outcome.Should().Be(expectedNotificationOutcome);
    }

    [Test]
    public async Task ResolveItems_OmittedOrForeignDecision_FailsWithoutPartialMutation()
    {
        await using var db = TestDb.CreateInMemoryWithDocuments();
        var moderator = SeedUser(db, 3, "Moderator");
        var admin = SeedUser(db, 1, "Admin");
        var folder = SeedFolder(db, moderator.Id);
        var first = SeedDocument(db, moderator.Id, folder.Id, "first.pdf");
        var second = SeedDocument(db, moderator.Id, folder.Id, "second.pdf");
        var sut = CreateSut(db);
        var pending = await sut.CreateAsync(moderator.Id, Request(folder.Id, first.Id, second.Id));

        Func<Task> omitted = () => sut.ResolveItemsAsync(pending.Id, Resolve((pending.Items[0].Id, "Approved", null)), admin.Id);
        (await omitted.Should().ThrowAsync<AdminException>()).Which.Code.Should().Be("escalation_item_set_changed");

        Func<Task> foreign = () => sut.ResolveItemsAsync(pending.Id, Resolve(
            (pending.Items[0].Id, "Approved", null), (Guid.NewGuid(), "Rejected", "No")), admin.Id);
        (await foreign.Should().ThrowAsync<AdminException>()).Which.Code.Should().Be("escalation_item_set_changed");
        (await db.DocumentEscalations.SingleAsync()).EscalationStatus.Should().Be("Pending");
        (await db.Documents.SingleAsync(document => document.Id == first.Id)).ReviewStatus.Should().Be(DocumentReviewStatus.Escalated);
        (await db.Documents.SingleAsync(document => document.Id == second.Id)).ReviewStatus.Should().Be(DocumentReviewStatus.Escalated);
    }

    [Test]
    public async Task ResolveItems_StaleGenerationAndSecondResolve_ConflictWithoutPartialMutation()
    {
        await using var db = TestDb.CreateInMemoryWithDocuments();
        var moderator = SeedUser(db, 3, "Moderator");
        var admin = SeedUser(db, 1, "Admin");
        var secondAdmin = SeedUser(db, 1, "Second admin");
        var folder = SeedFolder(db, moderator.Id);
        var document = SeedDocument(db, moderator.Id, folder.Id, "stale.pdf", generation: 2);
        var sut = CreateSut(db);
        var pending = await sut.CreateAsync(moderator.Id, Request(folder.Id, document.Id));
        document.ModerationGeneration++;
        await db.SaveChangesAsync();

        Func<Task> stale = () => sut.ResolveItemsAsync(pending.Id, Resolve((pending.Items.Single().Id, "Approved", null)), admin.Id);
        (await stale.Should().ThrowAsync<AdminException>()).Which.Code.Should().Be("escalation_item_stale");
        (await db.Documents.SingleAsync(item => item.Id == document.Id)).ReviewStatus.Should().Be(DocumentReviewStatus.Escalated);

        document.ModerationGeneration--;
        await db.SaveChangesAsync();
        await sut.ResolveItemsAsync(pending.Id, Resolve((pending.Items.Single().Id, "Approved", null)), admin.Id);
        Func<Task> second = () => sut.ResolveItemsAsync(pending.Id, Resolve((pending.Items.Single().Id, "Approved", null)), secondAdmin.Id);
        (await second.Should().ThrowAsync<AdminException>()).Which.Code.Should().Be("escalation_already_resolved");
    }

    [Test]
    public async Task ResolveItems_FolderOwnerMismatch_ConflictsWithoutMutationOrNotifications()
    {
        await using var db = TestDb.CreateInMemoryWithDocuments();
        var moderator = SeedUser(db, 3, "Moderator");
        var otherOwner = SeedUser(db, 2, "Other owner");
        var admin = SeedUser(db, 1, "Admin");
        var folder = SeedFolder(db, moderator.Id);
        var document = SeedDocument(db, moderator.Id, folder.Id, "mismatch.pdf");
        var sut = CreateSut(db);
        var pending = await sut.CreateAsync(moderator.Id, Request(folder.Id, document.Id));
        document.UserId = otherOwner.Id;
        await db.SaveChangesAsync();

        Func<Task> act = () => sut.ResolveItemsAsync(pending.Id, Resolve((pending.Items.Single().Id, "Approved", null)), admin.Id);

        (await act.Should().ThrowAsync<AdminException>()).Which.Code.Should().Be("escalation_items_changed");
        (await db.Documents.SingleAsync(item => item.Id == document.Id)).ReviewStatus.Should().Be(DocumentReviewStatus.Escalated);
        (await db.UserNotifications.CountAsync()).Should().Be(0);
    }

    [Test]
    public async Task CreateAsync_WhenDirectDecisionWonFirst_DoesNotCreateAnEscalation()
    {
        await using var db = TestDb.CreateInMemoryWithDocuments();
        var moderator = SeedUser(db, 3, "Moderator");
        var folder = SeedFolder(db, moderator.Id);
        var document = SeedDocument(db, moderator.Id, folder.Id, "direct-won.pdf");
        document.ReviewStatus = DocumentReviewStatus.Approved;
        await db.SaveChangesAsync();

        Func<Task> act = () => CreateSut(db).CreateAsync(moderator.Id, Request(folder.Id, document.Id));

        (await act.Should().ThrowAsync<AdminException>()).Which.Code.Should().Be("escalation_document_not_eligible");
        (await db.DocumentEscalations.CountAsync()).Should().Be(0);
        (await db.Documents.SingleAsync(item => item.Id == document.Id)).ReviewStatus.Should().Be(DocumentReviewStatus.Approved);
    }

    private static EscalationService CreateSut(Data.AppDbContext db) =>
        new(db, new AuditLogService(db), new UserNotificationService(db), new FolderPublicationStateService());

    private static CreateEscalationRequest Request(Guid folderId, params Guid[] documentIds) => new()
    {
        FolderId = folderId,
        Reason = "Reason for escalation",
        Items = documentIds.Select(id => new EscalationItemRequest { DocumentId = id, RejectReason = "Needs admin review" }).ToList()
    };

    private static ResolveEscalationItemsRequest Resolve(params (Guid itemId, string status, string? response)[] decisions) => new()
    {
        Items = decisions.Select(decision => new ResolveEscalationItemRequest
        {
            ItemId = decision.itemId,
            Status = decision.status,
            AdminResponse = decision.response
        }).ToList()
    };

    private static User SeedUser(Data.AppDbContext db, int roleId, string name)
    {
        var user = new User { Id = Guid.NewGuid(), RoleId = roleId, SupabaseUserId = Guid.NewGuid(), Username = $"u{Guid.NewGuid():N}"[..12], FullName = name, IsActive = true, DailyTokenQuota = 25_000, TokenUsageDate = DateOnly.FromDateTime(DateTime.UtcNow), CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        db.Users.Add(user); db.SaveChanges(); return user;
    }

    private static Folder SeedFolder(Data.AppDbContext db, Guid userId)
    {
        var folder = new Folder { Id = Guid.NewGuid(), UserId = userId, Name = "Folder", ShareStatus = FolderStatus.PendingShare, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        db.Folders.Add(folder); db.SaveChanges(); return folder;
    }

    private static Document SeedDocument(Data.AppDbContext db, Guid userId, Guid folderId, string fileName, int generation = 1)
    {
        var document = new Document { Id = Guid.NewGuid(), UserId = userId, FolderId = folderId, FileName = fileName, StoragePath = $"docs/{Guid.NewGuid():N}.pdf", MimeType = "application/pdf", SubjectCode = "SWP391", Semester = "SU26", Status = DocumentStatus.Ready, ModerationGeneration = generation, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        db.Documents.Add(document); db.SaveChanges(); return document;
    }
}
