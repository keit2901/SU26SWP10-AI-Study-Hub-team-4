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
    public async Task CreateAsync_ActiveModerator_ValidatesThenEscalatesOnlyListedFolderDocuments()
    {
        await using var db = TestDb.CreateInMemoryWithDocuments();
        var moderator = SeedUser(db, 3, "Moderator");
        var folder = SeedFolder(db, moderator.Id);
        var document = SeedDocument(db, moderator.Id, folder.Id);
        var sut = CreateSut(db);

        var result = await sut.CreateAsync(moderator.Id, Request(folder.Id, document.Id));

        result.EscalationStatus.Should().Be("Pending");
        (await db.Documents.SingleAsync(d => d.Id == document.Id)).ReviewStatus.Should().Be(DocumentReviewStatus.Escalated);
        db.AuditLogs.Should().ContainSingle(log => log.Action == "ESCALATION_CREATED" && !log.AfterJson!.Contains("Reason for"));
    }

    [Test]
    public async Task CreateAsync_StudentOrInvalidDocumentOwnership_DeniesWithoutMutation()
    {
        await using var db = TestDb.CreateInMemoryWithDocuments();
        var student = SeedUser(db, 2, "Student");
        var moderator = SeedUser(db, 3, "Moderator");
        var folder = SeedFolder(db, moderator.Id);
        var foreign = SeedDocument(db, student.Id, null);
        var sut = CreateSut(db);

        var studentAttempt = () => sut.CreateAsync(student.Id, Request(folder.Id, foreign.Id));
        (await studentAttempt.Should().ThrowAsync<AdminException>()).Which.Code.Should().Be("share_reviewer_role_required");

        var moderatorAttempt = () => sut.CreateAsync(moderator.Id, Request(folder.Id, foreign.Id));
        (await moderatorAttempt.Should().ThrowAsync<AdminException>()).Which.Code.Should().Be("escalation_item_not_in_folder");
        db.DocumentEscalations.Should().BeEmpty();
        (await db.Documents.SingleAsync(d => d.Id == foreign.Id)).ReviewStatus.Should().Be(DocumentReviewStatus.None);
    }

    [Test]
    public async Task CreateAsync_DuplicateDocumentOrExistingPendingEscalation_IsRejectedWithoutExtraMutation()
    {
        await using var db = TestDb.CreateInMemoryWithDocuments();
        var moderator = SeedUser(db, 3, "Moderator");
        var folder = SeedFolder(db, moderator.Id);
        var document = SeedDocument(db, moderator.Id, folder.Id);
        var sut = CreateSut(db);

        var duplicate = Request(folder.Id, document.Id);
        duplicate.Items.Add(new EscalationItemRequest { DocumentId = document.Id, RejectReason = "Duplicate" });
        Func<Task> duplicateAttempt = () => sut.CreateAsync(moderator.Id, duplicate);
        (await duplicateAttempt.Should().ThrowAsync<AdminException>())
            .Which.Code.Should().Be("duplicate_escalation_document");

        await sut.CreateAsync(moderator.Id, Request(folder.Id, document.Id));
        Func<Task> secondAttempt = () => sut.CreateAsync(moderator.Id, Request(folder.Id, document.Id));
        (await secondAttempt.Should().ThrowAsync<AdminException>())
            .Which.Code.Should().Be("pending_escalation_exists");
        db.DocumentEscalations.Should().ContainSingle();
    }

    [Test]
    public async Task ResolveAsync_OnlyActiveAdminCanResolveAndApprovedPublishesFolderImmediately()
    {
        await using var db = TestDb.CreateInMemoryWithDocuments();
        var moderator = SeedUser(db, 3, "Moderator");
        var admin = SeedUser(db, 1, "Admin");
        var folder = SeedFolder(db, moderator.Id);
        var document = SeedDocument(db, moderator.Id, folder.Id);
        var audit = new AuditLogService(db);
        var sut = new EscalationService(db, audit);
        var escalation = await sut.CreateAsync(moderator.Id, Request(folder.Id, document.Id));

        var moderatorAttempt = () => sut.ResolveAsync(escalation.Id, new ResolveEscalationRequest { Status = "Approved" }, moderator.Id);
        (await moderatorAttempt.Should().ThrowAsync<AdminException>()).Which.Code.Should().Be("admin_role_required");

        var resolved = await sut.ResolveAsync(escalation.Id, new ResolveEscalationRequest { Status = "Approved", AdminResponse = "Valid." }, admin.Id);

        resolved.EscalationStatus.Should().Be("Approved");
        var persistedFolder = await db.Folders.SingleAsync(f => f.Id == folder.Id);
        persistedFolder.ShareStatus.Should().Be(FolderStatus.Approved);
        persistedFolder.SharedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
        persistedFolder.ShareReviewSource.Should().Be("ADMIN_ESCALATION_APPROVED");
        (await db.Documents.SingleAsync(d => d.Id == document.Id)).ReviewStatus.Should().Be(DocumentReviewStatus.Approved);
    }

    [Test]
    public async Task ResolveAsync_InvalidOrStaleResolution_DoesNotMutateFolderOrDocuments()
    {
        await using var db = TestDb.CreateInMemoryWithDocuments();
        var moderator = SeedUser(db, 3, "Moderator");
        var admin = SeedUser(db, 1, "Admin");
        var folder = SeedFolder(db, moderator.Id);
        var document = SeedDocument(db, moderator.Id, folder.Id);
        var sut = CreateSut(db);
        var escalation = await sut.CreateAsync(moderator.Id, Request(folder.Id, document.Id));

        Func<Task> invalidAttempt = () => sut.ResolveAsync(escalation.Id, new ResolveEscalationRequest { Status = "Anything" }, admin.Id);
        (await invalidAttempt.Should().ThrowAsync<AdminException>())
            .Which.Code.Should().Be("invalid_escalation_status");
        (await db.Documents.SingleAsync(d => d.Id == document.Id)).ReviewStatus.Should().Be(DocumentReviewStatus.Escalated);

        await sut.ResolveAsync(escalation.Id, new ResolveEscalationRequest { Status = "Rejected", AdminResponse = "  Not valid.  " }, admin.Id);
        Func<Task> staleAttempt = () => sut.ResolveAsync(escalation.Id, new ResolveEscalationRequest { Status = "Approved" }, admin.Id);
        (await staleAttempt.Should().ThrowAsync<AdminException>())
            .Which.Code.Should().Be("already_resolved");
        var persistedFolder = await db.Folders.SingleAsync(f => f.Id == folder.Id);
        persistedFolder.ShareStatus.Should().Be(FolderStatus.Rejected);
        persistedFolder.HumanReviewReason.Should().Be("Not valid.");
        (await db.Documents.SingleAsync(d => d.Id == document.Id)).ReviewStatus.Should().Be(DocumentReviewStatus.Rejected);
    }

    private static EscalationService CreateSut(Data.AppDbContext db) => new(db, new AuditLogService(db));

    private static CreateEscalationRequest Request(Guid folderId, Guid documentId) => new()
    {
        FolderId = folderId,
        Reason = "Reason for escalation",
        Items = new List<EscalationItemRequest> { new() { DocumentId = documentId, RejectReason = "Needs admin review" } }
    };

    private static User SeedUser(Data.AppDbContext db, int roleId, string name) { var user = new User { Id = Guid.NewGuid(), RoleId = roleId, SupabaseUserId = Guid.NewGuid(), Username = $"u{Guid.NewGuid():N}"[..12], FullName = name, IsActive = true, DailyTokenQuota = 25_000, TokenUsageDate = DateOnly.FromDateTime(DateTime.UtcNow), CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow }; db.Users.Add(user); db.SaveChanges(); return user; }
    private static Folder SeedFolder(Data.AppDbContext db, Guid userId) { var folder = new Folder { Id = Guid.NewGuid(), UserId = userId, Name = "Folder", ShareStatus = FolderStatus.PendingShare, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow }; db.Folders.Add(folder); db.SaveChanges(); return folder; }
    private static Document SeedDocument(Data.AppDbContext db, Guid userId, Guid? folderId) { var document = new Document { Id = Guid.NewGuid(), UserId = userId, FolderId = folderId, FileName = "test.pdf", StoragePath = $"docs/{Guid.NewGuid():N}.pdf", MimeType = "application/pdf", SubjectCode = "SWP391", Semester = "SU26", Status = DocumentStatus.Ready, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow }; db.Documents.Add(document); db.SaveChanges(); return document; }
}
