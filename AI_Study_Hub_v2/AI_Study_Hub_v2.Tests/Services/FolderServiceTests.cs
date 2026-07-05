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
    private static FolderService BuildSut(AppDbContext db) =>
        new(db, NullLogger<FolderService>.Instance, Mock.Of<ISupabaseStorageClient>(), Mock.Of<IStorageQuotaService>());

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
}
