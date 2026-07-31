using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Dtos;
using AI_Study_Hub_v2.Services;
using AI_Study_Hub_v2.Services.Supabase;
using AI_Study_Hub_v2.Tests.Support;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;

namespace AI_Study_Hub_v2.Tests.Services;

[TestFixture]
public class DashboardModerationStateTests
{
    [TestCase(Role.StudentRoleName, true)]
    [TestCase(Role.ModeratorRoleName, false)]
    [TestCase("Unknown", true)]
    public async Task Unauthorized_local_user_cannot_mutate_pending_document(string roleName, bool isActive)
    {
        await using var db = CreateModerationDb();
        var caller = AddUser(db, roleName, isActive);
        var document = AddDocument(db, caller, FolderStatus.PendingShare);
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var act = () => service.ApproveDocumentAsync(caller.SupabaseUserId, document.Id, CancellationToken.None);

        var exception = await act.Should().ThrowAsync<DashboardModerationException>();
        exception.Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        db.ChangeTracker.Clear();
        (await db.Documents.SingleAsync(item => item.Id == document.Id)).ReviewStatus
            .Should().Be(DocumentReviewStatus.None);
    }

    [Test]
    public async Task Missing_local_user_is_forbidden()
    {
        await using var db = CreateModerationDb();
        var owner = AddUser(db, Role.StudentRoleName, true);
        var document = AddDocument(db, owner, FolderStatus.PendingShare);
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var act = () => service.ApproveDocumentAsync(Guid.NewGuid(), document.Id, CancellationToken.None);

        var exception = await act.Should().ThrowAsync<DashboardModerationException>();
        exception.Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        db.ChangeTracker.Clear();
        (await db.Documents.SingleAsync(item => item.Id == document.Id)).ReviewStatus
            .Should().Be(DocumentReviewStatus.None);
    }

    [Test]
    public async Task Active_moderator_can_approve_pending_document()
    {
        await using var db = CreateModerationDb();
        var moderator = AddUser(db, Role.ModeratorRoleName, true);
        var document = AddDocument(db, moderator, FolderStatus.PendingShare);
        await db.SaveChangesAsync();
        var service = CreateService(db);

        await service.ApproveDocumentAsync(moderator.SupabaseUserId, document.Id, CancellationToken.None);

        db.ChangeTracker.Clear();
        (await db.Documents.SingleAsync(item => item.Id == document.Id)).ReviewStatus
            .Should().Be(DocumentReviewStatus.Approved);
        var notification = await db.UserNotifications.SingleAsync();
        notification.RecipientUserId.Should().Be(moderator.Id);
        notification.Outcome.Should().Be(UserNotificationOutcome.Approved);
    }

    [Test]
    public async Task Direct_decision_changes_one_file_and_recomputes_folder_publication_state()
    {
        await using var db = CreateModerationDb();
        var moderator = AddUser(db, Role.ModeratorRoleName, true);
        var approved = AddDocument(db, moderator, FolderStatus.PendingShare);
        var sibling = AddDocumentInFolder(db, approved.Folder!, moderator, DocumentReviewStatus.None);
        await db.SaveChangesAsync();
        var service = CreateService(db);

        await service.ApproveDocumentAsync(moderator.SupabaseUserId, approved.Id, CancellationToken.None);

        db.ChangeTracker.Clear();
        var afterApproval = await db.Documents.Include(item => item.Folder).Where(item => item.FolderId == approved.FolderId).ToListAsync();
        afterApproval.Single(item => item.Id == approved.Id).ReviewStatus.Should().Be(DocumentReviewStatus.Approved);
        afterApproval.Single(item => item.Id == sibling.Id).ReviewStatus.Should().Be(DocumentReviewStatus.None);
        afterApproval[0].Folder!.ShareStatus.Should().Be(FolderStatus.PendingShare);

        await service.RejectDocumentAsync(moderator.SupabaseUserId, sibling.Id, "duplicate", CancellationToken.None);

        db.ChangeTracker.Clear();
        var folder = await db.Folders.Include(item => item.Documents).SingleAsync(item => item.Id == approved.FolderId);
        folder.ShareStatus.Should().Be(FolderStatus.Approved);
        folder.Documents.Single(item => item.Id == approved.Id).ReviewStatus.Should().Be(DocumentReviewStatus.Approved);
        folder.Documents.Single(item => item.Id == sibling.Id).ReviewStatus.Should().Be(DocumentReviewStatus.Rejected);
    }

    [Test]
    public async Task Publication_stays_pending_for_escalated_file_and_becomes_public_after_terminal_remainder()
    {
        await using var db = CreateModerationDb();
        var owner = AddUser(db, Role.StudentRoleName, true);
        var folder = new Folder
        {
            Id = Guid.NewGuid(), UserId = owner.Id, User = owner, Name = "Lifecycle folder",
            ShareStatus = FolderStatus.PendingShare, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        };
        var approved = AddDocumentInFolder(db, folder, owner, DocumentReviewStatus.Approved);
        var escalated = AddDocumentInFolder(db, folder, owner, DocumentReviewStatus.Escalated);
        var rejected = AddDocumentInFolder(db, folder, owner, DocumentReviewStatus.Rejected);
        db.Folders.Add(folder);
        await db.SaveChangesAsync();
        var publicationState = new FolderPublicationStateService();

        publicationState.Recompute(folder, [approved, escalated, rejected], DateTimeOffset.UtcNow);
        folder.ShareStatus.Should().Be(FolderStatus.PendingShare);

        escalated.ReviewStatus = DocumentReviewStatus.Rejected;
        publicationState.Recompute(folder, [approved, escalated, rejected], DateTimeOffset.UtcNow);
        folder.ShareStatus.Should().Be(FolderStatus.Approved);
    }

    [Test]
    public async Task Active_admin_can_reject_pending_document()
    {
        await using var db = CreateModerationDb();
        var admin = AddUser(db, Role.AdminRoleName, true);
        var document = AddDocument(db, admin, FolderStatus.PendingShare);
        await db.SaveChangesAsync();
        var service = CreateService(db);

        await service.RejectDocumentAsync(admin.SupabaseUserId, document.Id, "Invalid material", CancellationToken.None);

        db.ChangeTracker.Clear();
        var stored = await db.Documents.SingleAsync(item => item.Id == document.Id);
        stored.ReviewStatus.Should().Be(DocumentReviewStatus.Rejected);
        stored.ErrorMessage.Should().Be("Invalid material");
        var notification = await db.UserNotifications.SingleAsync();
        notification.RecipientUserId.Should().Be(admin.Id);
        notification.Outcome.Should().Be(UserNotificationOutcome.Rejected);
    }

    [Test]
    public async Task Pending_list_only_returns_documents_from_requested_pending_share_folder()
    {
        await using var db = CreateModerationDb();
        var moderator = AddUser(db, Role.ModeratorRoleName, true);
        var requested = AddDocument(db, moderator, FolderStatus.PendingShare);
        AddDocument(db, moderator, FolderStatus.PendingShare);
        AddDocument(db, moderator, FolderStatus.Approved);
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var documents = await service.GetPendingModerationDocumentsAsync(
            moderator.SupabaseUserId, requested.FolderId, CancellationToken.None);

        documents.Should().ContainSingle().Which.Id.Should().Be(requested.Id);
    }

    [Test]
    public async Task Terminal_document_returns_conflict_without_mutation()
    {
        await using var db = CreateModerationDb();
        var moderator = AddUser(db, Role.ModeratorRoleName, true);
        var document = AddDocument(db, moderator, FolderStatus.Approved);
        document.ReviewStatus = DocumentReviewStatus.Approved;
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var act = () => service.RejectDocumentAsync(
            moderator.SupabaseUserId, document.Id, "Should not apply", CancellationToken.None);

        var exception = await act.Should().ThrowAsync<DashboardModerationException>();
        exception.Which.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        db.ChangeTracker.Clear();
        var stored = await db.Documents.SingleAsync(item => item.Id == document.Id);
        stored.ReviewStatus.Should().Be(DocumentReviewStatus.Approved);
        stored.ErrorMessage.Should().BeNull();
    }

    [Test]
    public async Task Unknown_document_returns_not_found()
    {
        await using var db = CreateModerationDb();
        var moderator = AddUser(db, Role.ModeratorRoleName, true);
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var act = () => service.ApproveDocumentAsync(
            moderator.SupabaseUserId, Guid.NewGuid(), CancellationToken.None);

        var exception = await act.Should().ThrowAsync<DashboardModerationException>();
        exception.Which.StatusCode.Should().Be(StatusCodes.Status404NotFound);
    }

    [TestCase(Role.StudentRoleName, true)]
    [TestCase(Role.ModeratorRoleName, false)]
    [TestCase(Role.AdminRoleName, false)]
    [TestCase("Unknown", true)]
    public async Task Unauthorized_profiles_are_denied_by_every_moderation_operation(string roleName, bool isActive)
    {
        await using var db = CreateModerationDb();
        var caller = AddUser(db, roleName, isActive);
        var document = AddDocument(db, caller, FolderStatus.PendingShare);
        await db.SaveChangesAsync();
        var service = CreateService(db);

        await AssertForbiddenAsync(() => service.GetPendingModerationDocumentsAsync(caller.SupabaseUserId, null, CancellationToken.None));
        await AssertForbiddenAsync(() => service.GetModerationAnalyticsAsync(caller.SupabaseUserId, null, 1, 1, CancellationToken.None));
        await AssertForbiddenAsync(() => service.GetModerationDocumentSignedUrlAsync(caller.SupabaseUserId, document.Id, CancellationToken.None));
        await AssertForbiddenAsync(() => service.AiReviewDocumentAsync(caller.SupabaseUserId, document.Id, CancellationToken.None));
        await AssertForbiddenAsync(() => service.ApproveDocumentAsync(caller.SupabaseUserId, document.Id, CancellationToken.None));
        await AssertForbiddenAsync(() => service.RejectDocumentAsync(caller.SupabaseUserId, document.Id, "No", CancellationToken.None));

        db.ChangeTracker.Clear();
        (await db.Documents.SingleAsync(item => item.Id == document.Id)).ReviewStatus.Should().Be(DocumentReviewStatus.None);
    }

    [Test]
    public async Task Missing_profile_is_denied_by_every_moderation_operation()
    {
        await using var db = CreateModerationDb();
        var owner = AddUser(db, Role.StudentRoleName, true);
        var document = AddDocument(db, owner, FolderStatus.PendingShare);
        await db.SaveChangesAsync();
        var service = CreateService(db);
        var missingCaller = Guid.NewGuid();

        await AssertForbiddenAsync(() => service.GetPendingModerationDocumentsAsync(missingCaller, null, CancellationToken.None));
        await AssertForbiddenAsync(() => service.GetModerationAnalyticsAsync(missingCaller, null, 1, 1, CancellationToken.None));
        await AssertForbiddenAsync(() => service.GetModerationDocumentSignedUrlAsync(missingCaller, document.Id, CancellationToken.None));
        await AssertForbiddenAsync(() => service.AiReviewDocumentAsync(missingCaller, document.Id, CancellationToken.None));
        await AssertForbiddenAsync(() => service.ApproveDocumentAsync(missingCaller, document.Id, CancellationToken.None));
        await AssertForbiddenAsync(() => service.RejectDocumentAsync(missingCaller, document.Id, "No", CancellationToken.None));
    }

    [Test]
    public async Task Active_admin_can_use_list_analytics_and_signed_url()
    {
        await using var db = CreateModerationDb();
        var admin = AddUser(db, Role.AdminRoleName, true);
        var document = AddDocument(db, admin, FolderStatus.PendingShare);
        await db.SaveChangesAsync();
        var storage = new Mock<ISupabaseStorageClient>();
        storage.Setup(client => client.CreateSignedUrlAsync(It.IsAny<string>(), document.StoragePath, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://example.test/signed");
        var service = CreateService(db, storage.Object);

        (await service.GetPendingModerationDocumentsAsync(admin.SupabaseUserId, null, CancellationToken.None))
            .Should().ContainSingle(item => item.Id == document.Id);
        (await service.GetModerationAnalyticsAsync(admin.SupabaseUserId, null, 1, 1, CancellationToken.None))
            .TotalDocuments.Should().Be(1);
        (await service.GetModerationDocumentSignedUrlAsync(admin.SupabaseUserId, document.Id, CancellationToken.None))
            .Should().Be("https://example.test/signed");
    }

    [TestCase(FolderShareModerationOutcome.AutoApproved, DocumentAiAdvisoryOutcome.Approve)]
    [TestCase(FolderShareModerationOutcome.NeedsHumanReview, DocumentAiAdvisoryOutcome.NeedsHumanReview)]
    [TestCase(FolderShareModerationOutcome.AutoRejected, DocumentAiAdvisoryOutcome.Reject)]
    public async Task Ai_review_returns_advisory_without_mutating_human_moderation_state(
        FolderShareModerationOutcome aiOutcome,
        DocumentAiAdvisoryOutcome expectedAdvisoryOutcome)
    {
        await using var db = CreateModerationDb();
        var moderator = AddUser(db, Role.ModeratorRoleName, true);
        var document = AddDocument(db, moderator, FolderStatus.PendingShare);
        document.ErrorMessage = "Existing document diagnostic";
        var folder = document.Folder!;
        folder.ShareReviewSource = "HUMAN_REQUEST";
        var sharedAt = DateTimeOffset.UtcNow.AddDays(-1);
        folder.SharedAt = sharedAt;
        var originalDocumentUpdatedAt = document.UpdatedAt;
        var originalFolderUpdatedAt = folder.UpdatedAt;
        await db.SaveChangesAsync();
        var ai = new Mock<IFolderShareAiModerator>();
        ai.Setup(moderatorService => moderatorService.Evaluate(It.IsAny<Folder>(), It.IsAny<IReadOnlyList<Document>>(), It.IsAny<IReadOnlyList<string>>()))
            .Returns(new FolderShareModerationDecision(aiOutcome, "AI recommendation", 0.9));
        var audit = new Mock<IAuditLogService>();
        var service = CreateService(db, aiModerator: ai.Object, audit: audit.Object);

        var result = await service.AiReviewDocumentAsync(moderator.SupabaseUserId, document.Id, CancellationToken.None);

        result!.ReviewStatus.Should().Be(DocumentReviewStatus.None);
        result.AdvisoryOutcome.Should().Be(expectedAdvisoryOutcome);
        result.ReviewSource.Should().Be("AI_ADVISORY");
        result.Message.Should().Be("AI recommendation");
        result.Confidence.Should().Be(0.9);
        audit.Verify(service => service.Add(moderator.Id, "DOCUMENT_AI_REVIEWED", "Document", document.Id.ToString(), "Low", null, null, null, null, null), Times.Once);
        db.ChangeTracker.Clear();
        var storedDocument = await db.Documents.SingleAsync(item => item.Id == document.Id);
        var storedFolder = await db.Folders.SingleAsync(item => item.Id == document.FolderId);
        storedDocument.ReviewStatus.Should().Be(DocumentReviewStatus.None);
        storedDocument.ErrorMessage.Should().Be("Existing document diagnostic");
        storedDocument.UpdatedAt.Should().Be(originalDocumentUpdatedAt);
        storedFolder.ShareStatus.Should().Be(FolderStatus.PendingShare);
        storedFolder.ShareReviewSource.Should().Be("HUMAN_REQUEST");
        storedFolder.SharedAt.Should().Be(sharedAt);
        storedFolder.UpdatedAt.Should().Be(originalFolderUpdatedAt);
        (await db.UserNotifications.CountAsync()).Should().Be(0);
    }

    [Test]
    public async Task Ai_review_non_pending_document_returns_conflict_without_mutation()
    {
        await using var db = CreateModerationDb();
        var moderator = AddUser(db, Role.ModeratorRoleName, true);
        var document = AddDocument(db, moderator, FolderStatus.Approved);
        document.ReviewStatus = DocumentReviewStatus.Approved;
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var act = () => service.AiReviewDocumentAsync(moderator.SupabaseUserId, document.Id, CancellationToken.None);

        (await act.Should().ThrowAsync<DashboardModerationException>()).Which.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        db.ChangeTracker.Clear();
        (await db.Documents.SingleAsync(item => item.Id == document.Id)).ReviewStatus.Should().Be(DocumentReviewStatus.Approved);
    }

    [Test]
    public async Task Ai_review_allows_ready_unreviewed_new_file_in_approved_folder_and_remains_advisory()
    {
        await using var db = CreateModerationDb();
        var moderator = AddUser(db, Role.ModeratorRoleName, true);
        var document = AddDocument(db, moderator, FolderStatus.Approved);
        await db.SaveChangesAsync();
        var ai = new Mock<IFolderShareAiModerator>();
        ai.Setup(service => service.Evaluate(It.IsAny<Folder>(), It.IsAny<IReadOnlyList<Document>>(), It.IsAny<IReadOnlyList<string>>()))
            .Returns(new FolderShareModerationDecision(FolderShareModerationOutcome.NeedsHumanReview, "Review manually", 0.7));

        var result = await CreateService(db, aiModerator: ai.Object)
            .AiReviewDocumentAsync(moderator.SupabaseUserId, document.Id, CancellationToken.None);

        result!.ReviewSource.Should().Be("AI_ADVISORY");
        result.ReviewStatus.Should().Be(DocumentReviewStatus.None);
        db.ChangeTracker.Clear();
        (await db.Documents.SingleAsync(item => item.Id == document.Id)).ReviewStatus.Should().Be(DocumentReviewStatus.None);
        (await db.Folders.SingleAsync(item => item.Id == document.FolderId)).ShareStatus.Should().Be(FolderStatus.Approved);
    }

    [Test]
    public async Task Escalated_preview_is_limited_to_admin_and_matching_pending_item_generation()
    {
        await using var db = CreateModerationDb();
        var moderator = AddUser(db, Role.ModeratorRoleName, true);
        var admin = AddUser(db, Role.AdminRoleName, true);
        var document = AddDocument(db, moderator, FolderStatus.Approved);
        document.ReviewStatus = DocumentReviewStatus.Escalated;
        document.ModerationGeneration = 3;
        var escalation = new DocumentEscalation
        {
            Id = Guid.NewGuid(), FolderId = document.FolderId!.Value, EscalatedByUserId = moderator.Id,
            Reason = "Needs review", EscalationStatus = "Pending", CreatedAt = DateTimeOffset.UtcNow
        };
        db.DocumentEscalations.Add(escalation);
        db.DocumentEscalationItems.Add(new DocumentEscalationItem
        {
            Id = Guid.NewGuid(), EscalationId = escalation.Id, DocumentId = document.Id,
            DocumentFileName = document.FileName, DocumentModerationGeneration = document.ModerationGeneration,
            RejectReason = "Needs review", ResolutionStatus = "Pending"
        });
        await db.SaveChangesAsync();
        var storage = new Mock<ISupabaseStorageClient>();
        storage.Setup(client => client.CreateSignedUrlAsync(It.IsAny<string>(), document.StoragePath, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://example.test/escalated");
        var service = CreateService(db, storage.Object);

        (await service.GetModerationDocumentSignedUrlAsync(admin.SupabaseUserId, document.Id, CancellationToken.None))
            .Should().Be("https://example.test/escalated");
        Func<Task> moderatorAttempt = () => service.GetModerationDocumentSignedUrlAsync(moderator.SupabaseUserId, document.Id, CancellationToken.None);
        (await moderatorAttempt.Should().ThrowAsync<DashboardModerationException>()).Which.StatusCode.Should().Be(StatusCodes.Status409Conflict);

        document.ModerationGeneration++;
        await db.SaveChangesAsync();
        Func<Task> staleAdminAttempt = () => service.GetModerationDocumentSignedUrlAsync(admin.SupabaseUserId, document.Id, CancellationToken.None);
        (await staleAdminAttempt.Should().ThrowAsync<DashboardModerationException>()).Which.StatusCode.Should().Be(StatusCodes.Status409Conflict);
    }

    [Test]
    public async Task Analytics_aggregates_are_independent_of_the_current_page()
    {
        await using var db = CreateModerationDb();
        var moderator = AddUser(db, Role.ModeratorRoleName, true);
        var first = AddDocument(db, moderator, FolderStatus.PendingShare);
        var second = AddDocument(db, moderator, FolderStatus.PendingShare);
        var third = AddDocument(db, moderator, FolderStatus.PendingShare);
        second.ReviewStatus = DocumentReviewStatus.Approved;
        third.ReviewStatus = DocumentReviewStatus.Rejected;
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var analytics = await service.GetModerationAnalyticsAsync(moderator.SupabaseUserId, null, 2, 1, CancellationToken.None);

        analytics.RecentDocuments.Should().ContainSingle();
        analytics.PendingUnreviewedCount.Should().Be(1);
        analytics.RejectedDocumentCount.Should().Be(1);
        analytics.AllDocumentsApproved.Should().BeFalse();
    }

    [Test]
    public async Task Reject_reason_over_limit_is_rejected_without_mutation()
    {
        await using var db = CreateModerationDb();
        var admin = AddUser(db, Role.AdminRoleName, true);
        var document = AddDocument(db, admin, FolderStatus.PendingShare);
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var act = () => service.RejectDocumentAsync(admin.SupabaseUserId, document.Id, new string('x', 2_001), CancellationToken.None);

        (await act.Should().ThrowAsync<DashboardModerationException>()).Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        db.ChangeTracker.Clear();
        (await db.Documents.SingleAsync(item => item.Id == document.Id)).ReviewStatus.Should().Be(DocumentReviewStatus.None);
    }

    [Test]
    public async Task Signed_url_propagates_cancellation()
    {
        await using var db = CreateModerationDb();
        var moderator = AddUser(db, Role.ModeratorRoleName, true);
        var document = AddDocument(db, moderator, FolderStatus.PendingShare);
        await db.SaveChangesAsync();
        var storage = new Mock<ISupabaseStorageClient>();
        storage.Setup(client => client.CreateSignedUrlAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());
        var service = CreateService(db, storage.Object);

        var act = () => service.GetModerationDocumentSignedUrlAsync(moderator.SupabaseUserId, document.Id, CancellationToken.None);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private static Data.AppDbContext CreateModerationDb()
    {
        var options = new DbContextOptionsBuilder<Data.AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var context = new ModerationDbContext(options);
        context.Roles.AddRange(
            new Role { Id = 1, RoleName = Role.AdminRoleName, CreatedAt = DateTimeOffset.UtcNow },
            new Role { Id = 2, RoleName = Role.StudentRoleName, CreatedAt = DateTimeOffset.UtcNow },
            new Role { Id = 3, RoleName = Role.ModeratorRoleName, CreatedAt = DateTimeOffset.UtcNow });
        context.SaveChanges();
        return context;
    }

    private sealed class ModerationDbContext(DbContextOptions<Data.AppDbContext> options) : Data.AppDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<DocumentChunk>().Ignore(chunk => chunk.Embedding);
        }
    }

    private static async Task AssertForbiddenAsync(Func<Task> action)
    {
        (await action.Should().ThrowAsync<DashboardModerationException>()).Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    private static async Task AssertForbiddenAsync<T>(Func<Task<T>> action)
    {
        (await action.Should().ThrowAsync<DashboardModerationException>()).Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    private static DashboardService CreateService(
        Data.AppDbContext db,
        ISupabaseStorageClient? storage = null,
        IFolderShareAiModerator? aiModerator = null,
        IAuditLogService? audit = null) =>
        new(
            db,
            storage ?? Mock.Of<ISupabaseStorageClient>(),
            audit ?? Mock.Of<IAuditLogService>(),
            aiModerator ?? Mock.Of<IFolderShareAiModerator>(),
            new UserNotificationService(db),
            new FolderPublicationStateService());

    private static User AddUser(Data.AppDbContext db, string roleName, bool isActive)
    {
        var role = db.Roles.SingleOrDefault(item => item.RoleName == roleName);
        if (role is null)
        {
            role = new Role { Id = 99, RoleName = roleName, CreatedAt = DateTimeOffset.UtcNow };
            db.Roles.Add(role);
        }

        var id = Guid.NewGuid();
        var user = new User
        {
            Id = id,
            SupabaseUserId = Guid.NewGuid(),
            RoleId = role.Id,
            Role = role,
            Username = id.ToString("N")[..15],
            FullName = "Moderation test user",
            IsActive = isActive,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.Users.Add(user);
        return user;
    }

    private static Document AddDocument(Data.AppDbContext db, User owner, FolderStatus folderStatus)
    {
        var folder = new Folder
        {
            Id = Guid.NewGuid(),
            UserId = owner.Id,
            User = owner,
            Name = $"Folder {Guid.NewGuid():N}",
            ShareStatus = folderStatus,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        var document = new Document
        {
            Id = Guid.NewGuid(),
            UserId = owner.Id,
            User = owner,
            FolderId = folder.Id,
            Folder = folder,
            FileName = "moderation.pdf",
            StoragePath = $"tests/{Guid.NewGuid():N}.pdf",
            MimeType = "application/pdf",
            SubjectCode = "SWP391",
            Semester = "SU26",
            Status = DocumentStatus.Ready,
            ReviewStatus = DocumentReviewStatus.None,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.Folders.Add(folder);
        db.Documents.Add(document);
        return document;
    }

    private static Document AddDocumentInFolder(Data.AppDbContext db, Folder folder, User owner, DocumentReviewStatus reviewStatus)
    {
        var document = new Document
        {
            Id = Guid.NewGuid(), UserId = owner.Id, User = owner, FolderId = folder.Id, Folder = folder,
            FileName = $"moderation-{Guid.NewGuid():N}.pdf", StoragePath = $"tests/{Guid.NewGuid():N}.pdf",
            MimeType = "application/pdf", SubjectCode = "SWP391", Semester = "SU26", Status = DocumentStatus.Ready,
            ReviewStatus = reviewStatus, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        };
        folder.Documents.Add(document);
        return document;
    }
}
