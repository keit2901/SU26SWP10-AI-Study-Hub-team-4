using AI_Study_Hub_v2.Data;
using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Dtos;
using AI_Study_Hub_v2.Services;
using AI_Study_Hub_v2.Services.Supabase;
using AI_Study_Hub_v2.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AI_Study_Hub_v2.Tests.Services;

[TestFixture]
public class FolderServiceTests
{
    private static FolderService BuildSut(AppDbContext db, IPlanCapacityGuard? capacityGuard = null)
    {
        var storage = Mock.Of<ISupabaseStorageClient>();
        return new FolderService(db, NullLogger<FolderService>.Instance,
            new StorageDeletionCoordinator(db, storage, NullLogger<StorageDeletionCoordinator>.Instance),
            new FolderShareAiModerator(), capacityGuard ?? Mock.Of<IPlanCapacityGuard>(), Mock.Of<ISharedFolderCopyCoordinator>());
    }

    private static User SeedActiveStudent(AppDbContext db, Guid? supabaseUserId = null, bool isActive = true)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            RoleId = 2,
            SupabaseUserId = supabaseUserId ?? Guid.NewGuid(),
            Username = $"u{Guid.NewGuid():N}"[..10],
            FullName = "Test User",
            IsActive = isActive,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.Users.Add(user);
        db.SaveChanges();
        return user;
    }

    private static Folder SeedFolder(AppDbContext db, Guid userId, string name = "Sprint notes")
    {
        var folder = new Folder
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = name,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.Folders.Add(folder);
        db.SaveChanges();
        return folder;
    }

    private static void SeedDocument(AppDbContext db, Guid userId, Guid folderId, string fileName = "doc.pdf")
    {
        db.Documents.Add(new Document
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            FolderId = folderId,
            FileName = fileName,
            StoragePath = $"users/{userId:N}/2026/{Guid.NewGuid():N}-{fileName}",
            FileSizeBytes = 1024,
            MimeType = "application/pdf",
            SubjectCode = "SWP391",
            Semester = "SU26",
            Status = DocumentStatus.Ready,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        db.SaveChanges();
    }

    [Test]
    public async Task ListAsync_ReturnsOnlyOwnFolders_OrderedByName_WithDocumentCounts()
    {
        using var db = TestDb.CreateInMemoryWithDocuments();
        var me = SeedActiveStudent(db);
        var other = SeedActiveStudent(db);
        var z = SeedFolder(db, me.Id, "Zeta");
        var a = SeedFolder(db, me.Id, "Alpha");
        SeedFolder(db, other.Id, "Foreign");
        SeedDocument(db, me.Id, z.Id, "one.pdf");
        SeedDocument(db, me.Id, z.Id, "two.pdf");
        SeedDocument(db, me.Id, a.Id, "three.pdf");

        var sut = BuildSut(db);

        var rows = await sut.ListAsync(me.SupabaseUserId);

        rows.Select(f => f.Name).Should().Equal("Alpha", "Zeta");
        rows.Single(f => f.Name == "Alpha").DocumentCount.Should().Be(1);
        rows.Single(f => f.Name == "Zeta").DocumentCount.Should().Be(2);
    }

    [Test]
    public async Task CreateAsync_TrimsName_AndPersistsFolder()
    {
        using var db = TestDb.CreateInMemoryWithDocuments();
        var me = SeedActiveStudent(db);
        var sut = BuildSut(db);

        var dto = await sut.CreateAsync(
            me.SupabaseUserId,
            new CreateFolderRequest { Name = "  Sprint demo  ", Description = "  PDF set  " });

        dto.Id.Should().NotBeEmpty();
        dto.Name.Should().Be("Sprint demo");
        dto.Description.Should().Be("PDF set");
        dto.DocumentCount.Should().Be(0);
        (await db.Folders.AsNoTracking().SingleAsync()).Name.Should().Be("Sprint demo");
    }

    [Test]
    public async Task CreateAsync_DuplicateNameCaseInsensitive_Throws409()
    {
        using var db = TestDb.CreateInMemoryWithDocuments();
        var me = SeedActiveStudent(db);
        SeedFolder(db, me.Id, "Sprint demo");
        var sut = BuildSut(db);

        var act = () => sut.CreateAsync(me.SupabaseUserId, new CreateFolderRequest { Name = "SPRINT DEMO" });

        var ex = await act.Should().ThrowAsync<DocumentException>();
        ex.Which.StatusCode.Should().Be(409);
        ex.Which.Code.Should().Be("folder_name_taken");
    }

    [Test]
    public async Task CreateAsync_FolderLimitFailure_DoesNotPersistFolder()
    {
        using var db = TestDb.CreateInMemoryWithDocuments();
        var me = SeedActiveStudent(db);
        var guard = new Mock<IPlanCapacityGuard>(MockBehavior.Strict);
        guard.Setup(g => g.LockAndValidateAsync(
                db,
                me.Id,
                It.Is<PlanCapacityRequest>(request => request.AdditionalFolderCount == 1),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new PlanException(402, "folder_count_exceeded", "limit"));
        var sut = BuildSut(db, guard.Object);

        var act = () => sut.CreateAsync(me.SupabaseUserId, new CreateFolderRequest { Name = "Blocked" });

        var exception = await act.Should().ThrowAsync<PlanException>();
        exception.Which.Code.Should().Be("folder_count_exceeded");
        (await db.Folders.CountAsync()).Should().Be(0);
    }

    [Test]
    public async Task CreateAsync_DuplicateNameAfterGuard_DoesNotPersistPartialFolder()
    {
        using var db = TestDb.CreateInMemoryWithDocuments();
        var me = SeedActiveStudent(db);
        var existing = SeedFolder(db, me.Id, "Existing");
        var guard = new Mock<IPlanCapacityGuard>(MockBehavior.Strict);
        guard.Setup(g => g.LockAndValidateAsync(db, me.Id, It.IsAny<PlanCapacityRequest>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var sut = BuildSut(db, guard.Object);

        var act = () => sut.CreateAsync(me.SupabaseUserId, new CreateFolderRequest { Name = " existing " });

        var exception = await act.Should().ThrowAsync<DocumentException>();
        exception.Which.Code.Should().Be("folder_name_taken");
        (await db.Folders.CountAsync()).Should().Be(1);
        (await db.Folders.SingleAsync()).Id.Should().Be(existing.Id);
    }

    [Test]
    public async Task CreateAsync_Success_UsesCapacityGuardAndCommitsFolder()
    {
        using var db = TestDb.CreateInMemoryWithDocuments();
        var me = SeedActiveStudent(db);
        var guard = new Mock<IPlanCapacityGuard>(MockBehavior.Strict);
        guard.Setup(g => g.LockAndValidateAsync(
                db,
                me.Id,
                It.Is<PlanCapacityRequest>(request => request == new PlanCapacityRequest(0, 1, null, 0, 0)),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var sut = BuildSut(db, guard.Object);

        var result = await sut.CreateAsync(me.SupabaseUserId, new CreateFolderRequest { Name = "Committed" });

        result.Name.Should().Be("Committed");
        (await db.Folders.SingleAsync()).Id.Should().Be(result.Id);
        guard.VerifyAll();
    }

    [Test]
    public async Task CreateAsync_InactiveUser_Throws403()
    {
        using var db = TestDb.CreateInMemoryWithDocuments();
        var me = SeedActiveStudent(db, isActive: false);
        var sut = BuildSut(db);

        var act = () => sut.CreateAsync(me.SupabaseUserId, new CreateFolderRequest { Name = "Blocked" });

        var ex = await act.Should().ThrowAsync<DocumentException>();
        ex.Which.StatusCode.Should().Be(403);
        ex.Which.Code.Should().Be("user_inactive");
    }

    [Test]
    public async Task UpdateAsync_OwnFolder_RenamesAndKeepsDocumentCount()
    {
        using var db = TestDb.CreateInMemoryWithDocuments();
        var me = SeedActiveStudent(db);
        var folder = SeedFolder(db, me.Id, "Old");
        SeedDocument(db, me.Id, folder.Id);
        var sut = BuildSut(db);

        var dto = await sut.UpdateAsync(
            me.SupabaseUserId,
            folder.Id,
            new UpdateFolderRequest { Name = "  New name  " });

        dto.Name.Should().Be("New name");
        dto.DocumentCount.Should().Be(1);
        (await db.Folders.AsNoTracking().SingleAsync(f => f.Id == folder.Id)).Name.Should().Be("New name");
    }

    [Test]
    public async Task UpdateAsync_ForeignFolder_Throws404()
    {
        using var db = TestDb.CreateInMemoryWithDocuments();
        var me = SeedActiveStudent(db);
        var other = SeedActiveStudent(db);
        var foreign = SeedFolder(db, other.Id, "Foreign");
        var sut = BuildSut(db);

        var act = () => sut.UpdateAsync(me.SupabaseUserId, foreign.Id, new UpdateFolderRequest { Name = "Nope" });

        var ex = await act.Should().ThrowAsync<DocumentException>();
        ex.Which.StatusCode.Should().Be(404);
        ex.Which.Code.Should().Be("folder_not_found");
    }

    [Test]
    public async Task DeleteAsync_OwnFolder_RemovesFolder()
    {
        using var db = TestDb.CreateInMemoryWithDocuments();
        var me = SeedActiveStudent(db);
        var folder = SeedFolder(db, me.Id);
        var sut = BuildSut(db);

        await sut.DeleteAsync(me.SupabaseUserId, folder.Id);

        (await db.Folders.CountAsync()).Should().Be(0);
    }

    private static void SeedDocumentWithStatus(AppDbContext db, Guid userId, Guid folderId, DocumentStatus status, string fileName = "doc.pdf")
    {
        db.Documents.Add(new Document
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            FolderId = folderId,
            FileName = fileName,
            StoragePath = $"users/{userId:N}/2026/{Guid.NewGuid():N}-{fileName}",
            FileSizeBytes = 1024,
            MimeType = "application/pdf",
            SubjectCode = "SWP391",
            Semester = "SU26",
            Status = status,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        db.SaveChanges();
    }

    [Test]
    public async Task ListPersonalSharedAsync_CalculatesFolderStatusCorrectly()
    {
        using var db = TestDb.CreateInMemoryWithDocuments();
        var me = SeedActiveStudent(db);
        var sut = BuildSut(db);

        // 1. Empty folder
        var folderEmpty = SeedFolder(db, me.Id, "Empty Folder");
        folderEmpty.ShareStatus = FolderStatus.PendingShare;

        // 2. Processing folder (contains a processing document)
        var folderProc = SeedFolder(db, me.Id, "Processing Folder");
        folderProc.ShareStatus = FolderStatus.PendingShare;
        db.SaveChanges();
        SeedDocumentWithStatus(db, me.Id, folderProc.Id, DocumentStatus.Processing);

        // 3. Rejected folder (ShareStatus is Rejected)
        var folderRej = SeedFolder(db, me.Id, "Rejected Folder");
        folderRej.ShareStatus = FolderStatus.Rejected;
        db.SaveChanges();
        SeedDocumentWithStatus(db, me.Id, folderRej.Id, DocumentStatus.Ready);

        // 4. Pending Share folder (contains ready documents, but folder is not shared)
        var folderPending = SeedFolder(db, me.Id, "Pending Folder");
        folderPending.ShareStatus = FolderStatus.PendingShare;
        db.SaveChanges();
        SeedDocumentWithStatus(db, me.Id, folderPending.Id, DocumentStatus.Ready);

        // 5. Shared folder (contains ready documents, and folder is shared)
        var folderShared = SeedFolder(db, me.Id, "Shared Folder");
        folderShared.ShareStatus = FolderStatus.Approved;
        db.SaveChanges();
        SeedDocumentWithStatus(db, me.Id, folderShared.Id, DocumentStatus.Ready);

        var list = await sut.ListPersonalSharedAsync(me.SupabaseUserId);

        list.Single(f => f.Id == folderEmpty.Id).Status.Should().Be("Empty");
        list.Single(f => f.Id == folderProc.Id).Status.Should().Be("Processing");
        list.Single(f => f.Id == folderRej.Id).Status.Should().Be("Rejected");
        list.Single(f => f.Id == folderPending.Id).Status.Should().Be("Pending Share");
        list.Single(f => f.Id == folderShared.Id).Status.Should().Be("Shared");
    }

    [Test]
    public async Task ListPersonalSharedAsync_ExcludesNoneShareStatusFolders()
    {
        using var db = TestDb.CreateInMemoryWithDocuments();
        var me = SeedActiveStudent(db);
        var sut = BuildSut(db);

        var folderNone = SeedFolder(db, me.Id, "None Folder");
        folderNone.ShareStatus = FolderStatus.None;
        db.SaveChanges();

        var list = await sut.ListPersonalSharedAsync(me.SupabaseUserId);
        list.Should().BeEmpty();
    }
}
