using AI_Study_Hub_v2.Data.Entities;
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
    public async Task Non_pending_document_returns_conflict_without_mutation()
    {
        await using var db = CreateModerationDb();
        var moderator = AddUser(db, Role.ModeratorRoleName, true);
        var document = AddDocument(db, moderator, FolderStatus.Approved);
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var act = () => service.RejectDocumentAsync(
            moderator.SupabaseUserId, document.Id, "Should not apply", CancellationToken.None);

        var exception = await act.Should().ThrowAsync<DashboardModerationException>();
        exception.Which.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        db.ChangeTracker.Clear();
        var stored = await db.Documents.SingleAsync(item => item.Id == document.Id);
        stored.ReviewStatus.Should().Be(DocumentReviewStatus.None);
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

    [Test]
    public async Task Ai_review_requires_pending_folder_and_persists_audit_with_the_final_save()
    {
        await using var db = CreateModerationDb();
        var moderator = AddUser(db, Role.ModeratorRoleName, true);
        var document = AddDocument(db, moderator, FolderStatus.PendingShare);
        await db.SaveChangesAsync();
        var ai = new Mock<IFolderShareAiModerator>();
        ai.Setup(moderatorService => moderatorService.Evaluate(It.IsAny<Folder>(), It.IsAny<IReadOnlyList<Document>>(), It.IsAny<IReadOnlyList<string>>()))
            .Returns(new FolderShareModerationDecision(FolderShareModerationOutcome.AutoApproved, "Safe", 0.9));
        var audit = new Mock<IAuditLogService>();
        var service = CreateService(db, aiModerator: ai.Object, audit: audit.Object);

        var result = await service.AiReviewDocumentAsync(moderator.SupabaseUserId, document.Id, CancellationToken.None);

        result!.ReviewStatus.Should().Be(DocumentReviewStatus.Approved);
        audit.Verify(service => service.Add(moderator.Id, "DOCUMENT_AI_APPROVED", "Document", document.Id.ToString(), "Low", null, null, null, null, null), Times.Once);
        db.ChangeTracker.Clear();
        (await db.Documents.SingleAsync(item => item.Id == document.Id)).ReviewStatus.Should().Be(DocumentReviewStatus.Approved);
    }

    [Test]
    public async Task Ai_review_non_pending_document_returns_conflict_without_mutation()
    {
        await using var db = CreateModerationDb();
        var moderator = AddUser(db, Role.ModeratorRoleName, true);
        var document = AddDocument(db, moderator, FolderStatus.Approved);
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var act = () => service.AiReviewDocumentAsync(moderator.SupabaseUserId, document.Id, CancellationToken.None);

        (await act.Should().ThrowAsync<DashboardModerationException>()).Which.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        db.ChangeTracker.Clear();
        (await db.Documents.SingleAsync(item => item.Id == document.Id)).ReviewStatus.Should().Be(DocumentReviewStatus.None);
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
            aiModerator ?? Mock.Of<IFolderShareAiModerator>());

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
}
