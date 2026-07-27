using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Dtos;
using AI_Study_Hub_v2.Services;
using AI_Study_Hub_v2.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace AI_Study_Hub_v2.Tests.Services;

[TestFixture]
public sealed class EscalationServiceTests
{
    // ── P1: FK constraint — EscalatedByUserId MUST be User.Id (local PK), NOT SupabaseUserId ──

    [Test]
    public async Task CreateAsync_UsesLocalUserId_NotSupabaseUserId()
    {
        await using var db = TestDb.CreateInMemoryWithDocuments();
        var moderator = SeedUser(db, roleId: 3, "Moderator"); // role 3 = Moderator
        var folder = SeedFolder(db, moderator.Id);
        var doc = SeedDocument(db, moderator.Id, folder.Id);
        var sut = new EscalationService(db);

        var request = new CreateEscalationRequest
        {
            FolderId = folder.Id,
            Reason = "This rejection is incorrect.",
            Items = new List<EscalationItemRequest>
            {
                new() { DocumentId = doc.Id, RejectReason = "Content looks fine." }
            }
        };

        // Act — pass User.Id (local PK)
        var result = await sut.CreateAsync(moderator.Id, request);

        // Assert — EscalatedByUserId stored correctly as User.Id
        var persisted = await db.DocumentEscalations
            .Include(e => e.Items)
            .FirstAsync(e => e.Id == result.Id);
        persisted.EscalatedByUserId.Should().Be(moderator.Id);
        persisted.EscalatedByUserId.Should().NotBe(moderator.SupabaseUserId);
        persisted.EscalationStatus.Should().Be("Pending");
        persisted.Items.Should().HaveCount(1);
        persisted.Items.First().DocumentId.Should().Be(doc.Id);
    }

    [Test]
    public async Task CreateAsync_PersistsMultipleItems()
    {
        await using var db = TestDb.CreateInMemoryWithDocuments();
        var moderator = SeedUser(db, roleId: 3, "Moderator");
        var folder = SeedFolder(db, moderator.Id);
        var doc1 = SeedDocument(db, moderator.Id, folder.Id);
        var doc2 = SeedDocument(db, moderator.Id, folder.Id);
        var sut = new EscalationService(db);

        var request = new CreateEscalationRequest
        {
            FolderId = folder.Id,
            Reason = "Both docs are fine.",
            Items = new List<EscalationItemRequest>
            {
                new() { DocumentId = doc1.Id, RejectReason = "Doc1 reject reason" },
                new() { DocumentId = doc2.Id, RejectReason = "Doc2 reject reason" }
            }
        };

        var result = await sut.CreateAsync(moderator.Id, request);

        result.Items.Should().HaveCount(2);
        result.EscalatedByName.Should().Be(moderator.FullName);
    }

    // ── P1: GetAllAsync returns ALL statuses (fixed resolved-disappear bug) ──

    [Test]
    public async Task GetAllAsync_ReturnsAllStatuses_IncludingResolved()
    {
        await using var db = TestDb.CreateInMemoryWithDocuments();
        var moderator = SeedUser(db, roleId: 3, "Moderator");
        var folder = SeedFolder(db, moderator.Id);
        var doc = SeedDocument(db, moderator.Id, folder.Id);
        var sut = new EscalationService(db);

        // Create 2 escalations
        var req = new CreateEscalationRequest
        {
            FolderId = folder.Id,
            Reason = "Test",
            Items = new List<EscalationItemRequest>
            {
                new() { DocumentId = doc.Id, RejectReason = "Reason" }
            }
        };
        var e1 = await sut.CreateAsync(moderator.Id, req);
        var e2 = await sut.CreateAsync(moderator.Id, req);

        // Resolve one
        await sut.ResolveAsync(e1.Id, new ResolveEscalationRequest { Status = "Approved", AdminResponse = "Valid." });

        // Act
        var all = await sut.GetAllAsync();

        // Assert — includes both pending and resolved
        all.Should().HaveCount(2);
        all.Should().Contain(e => e.EscalationStatus == "Pending");
        all.Should().Contain(e => e.EscalationStatus == "Approved" && e.AdminResponse == "Valid.");
    }

    [Test]
    public async Task GetPendingAsync_OnlyReturnsPendingStatus()
    {
        await using var db = TestDb.CreateInMemoryWithDocuments();
        var moderator = SeedUser(db, roleId: 3, "Moderator");
        var folder = SeedFolder(db, moderator.Id);
        var doc = SeedDocument(db, moderator.Id, folder.Id);
        var sut = new EscalationService(db);

        var req = new CreateEscalationRequest
        {
            FolderId = folder.Id,
            Reason = "Test",
            Items = new List<EscalationItemRequest> { new() { DocumentId = doc.Id, RejectReason = "R" } }
        };
        var e1 = await sut.CreateAsync(moderator.Id, req);
        var e2 = await sut.CreateAsync(moderator.Id, req);
        await sut.ResolveAsync(e1.Id, new ResolveEscalationRequest { Status = "Rejected", AdminResponse = "No." });

        var pending = await sut.GetPendingAsync();

        pending.Should().HaveCount(1);
        pending.First().EscalationStatus.Should().Be("Pending");
    }

    // ── P1: GetMyAsync filters by EscalatedByUserId ──

    [Test]
    public async Task GetMyAsync_FiltersOnlyOwnEscalations()
    {
        await using var db = TestDb.CreateInMemoryWithDocuments();
        var mod1 = SeedUser(db, roleId: 3, "Moderator One");
        var mod2 = SeedUser(db, roleId: 3, "Moderator Two");
        var folder = SeedFolder(db, mod1.Id);
        var doc = SeedDocument(db, mod1.Id, folder.Id);
        var sut = new EscalationService(db);

        var req = new CreateEscalationRequest
        {
            FolderId = folder.Id,
            Reason = "Test",
            Items = new List<EscalationItemRequest> { new() { DocumentId = doc.Id, RejectReason = "R" } }
        };
        await sut.CreateAsync(mod1.Id, req);
        await sut.CreateAsync(mod1.Id, req);
        await sut.CreateAsync(mod2.Id, req);

        var mod1Escalations = await sut.GetMyAsync(mod1.Id);
        var mod2Escalations = await sut.GetMyAsync(mod2.Id);

        mod1Escalations.Should().HaveCount(2);
        mod2Escalations.Should().HaveCount(1);
        mod1Escalations.All(e => e.EscalatedByName == mod1.FullName).Should().BeTrue();
    }

    [Test]
    public async Task GetMyAsync_UserWithNoEscalations_ReturnsEmpty()
    {
        await using var db = TestDb.CreateInMemoryWithDocuments();
        var user = SeedUser(db, roleId: 3, "No Escalator");
        var sut = new EscalationService(db);

        var result = await sut.GetMyAsync(user.Id);

        result.Should().BeEmpty();
    }

    // ── Resolve flow ──

    [Test]
    public async Task ResolveAsync_Approved_SetsStatusResponseAndResolvedAt()
    {
        await using var db = TestDb.CreateInMemoryWithDocuments();
        var moderator = SeedUser(db, roleId: 3, "Moderator");
        var folder = SeedFolder(db, moderator.Id);
        var doc = SeedDocument(db, moderator.Id, folder.Id);
        var sut = new EscalationService(db);

        var req = new CreateEscalationRequest
        {
            FolderId = folder.Id,
            Reason = "Please review.",
            Items = new List<EscalationItemRequest> { new() { DocumentId = doc.Id, RejectReason = "R" } }
        };
        var created = await sut.CreateAsync(moderator.Id, req);

        var resolveReq = new ResolveEscalationRequest { Status = "Approved", AdminResponse = "Escalation valid." };
        var resolved = await sut.ResolveAsync(created.Id, resolveReq);

        resolved.EscalationStatus.Should().Be("Approved");
        resolved.AdminResponse.Should().Be("Escalation valid.");
        resolved.ResolvedAt.Should().NotBeNull();
    }

    [Test]
    public async Task ResolveAsync_Rejected_SetsStatusCorrectly()
    {
        await using var db = TestDb.CreateInMemoryWithDocuments();
        var moderator = SeedUser(db, roleId: 3, "Moderator");
        var folder = SeedFolder(db, moderator.Id);
        var doc = SeedDocument(db, moderator.Id, folder.Id);
        var sut = new EscalationService(db);

        var req = new CreateEscalationRequest
        {
            FolderId = folder.Id,
            Reason = "Bad reject.",
            Items = new List<EscalationItemRequest> { new() { DocumentId = doc.Id, RejectReason = "R" } }
        };
        var created = await sut.CreateAsync(moderator.Id, req);

        var resolved = await sut.ResolveAsync(created.Id, new ResolveEscalationRequest { Status = "Rejected" });

        resolved.EscalationStatus.Should().Be("Rejected");
    }

    [Test]
    public async Task ResolveAsync_NotFoundId_Throws404()
    {
        await using var db = TestDb.CreateInMemoryWithDocuments();
        var sut = new EscalationService(db);

        var act = () => sut.ResolveAsync(Guid.NewGuid(), new ResolveEscalationRequest { Status = "Approved" });

        var ex = await act.Should().ThrowAsync<AdminException>();
        ex.Which.StatusCode.Should().Be(404);
    }

    // ── Helpers ──

    private static User SeedUser(Data.AppDbContext db, int roleId, string fullName)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            RoleId = roleId,
            SupabaseUserId = Guid.NewGuid(),
            Username = $"u{Guid.NewGuid():N}"[..12],
            FullName = fullName,
            IsActive = true,
            DailyTokenQuota = 25_000,
            TokenUsageDate = DateOnly.FromDateTime(DateTime.UtcNow),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.Users.Add(user);
        db.SaveChanges();
        return user;
    }

    private static Folder SeedFolder(Data.AppDbContext db, Guid userId)
    {
        var folder = new Folder
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = "Test Folder",
            ShareStatus = FolderStatus.PendingShare,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.Folders.Add(folder);
        db.SaveChanges();
        return folder;
    }

    private static Document SeedDocument(Data.AppDbContext db, Guid userId, Guid folderId)
    {
        var doc = new Document
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            FolderId = folderId,
            FileName = "test.pdf",
            Status = DocumentStatus.Ready,
            StoragePath = $"docs/{Guid.NewGuid():N}.pdf",
            FileSizeBytes = 1024,
            MimeType = "application/pdf",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.Documents.Add(doc);
        db.SaveChanges();
        return doc;
    }
}
