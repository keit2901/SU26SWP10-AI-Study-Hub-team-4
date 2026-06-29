using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Services;
using AI_Study_Hub_v2.Services.Supabase;
using AI_Study_Hub_v2.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Pgvector;
using System.Globalization;

namespace AI_Study_Hub_v2.Tests.Services;

[TestFixture]
public sealed class PublicHubServiceTests
{
    [Test]
    public async Task ListSharedAsync_AuthenticatedViewer_ReturnsCountsAndCurrentVote()
    {
        await using var db = TestDb.CreateInMemoryWithDocuments();
        var owner = SeedUser(db, "Owner");
        var viewer = SeedUser(db, "Viewer");
        var folder = SeedFolder(db, owner.Id, isShared: true);
        db.FolderReactions.Add(new FolderReaction
        {
            FolderId = folder.Id,
            UserId = viewer.Id,
            IsLike = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        db.SaveChanges();
        var sut = BuildSut(db);

        var result = await sut.ListSharedAsync(viewer.SupabaseUserId);

        result.Should().ContainSingle();
        result[0].OwnerName.Should().Be("Owner");
        result[0].LikeCount.Should().Be(1);
        result[0].CurrentUserVote.Should().BeTrue();
    }

    [Test]
    public async Task VoteAsync_PrivateFolder_IsNotExposed()
    {
        await using var db = TestDb.CreateInMemoryWithDocuments();
        var owner = SeedUser(db, "Owner");
        var viewer = SeedUser(db, "Viewer");
        var folder = SeedFolder(db, owner.Id, isShared: false);
        var sut = BuildSut(db);

        var act = () => sut.VoteAsync(viewer.SupabaseUserId, folder.Id, true);

        var exception = await act.Should().ThrowAsync<DocumentException>();
        exception.Which.StatusCode.Should().Be(404);
        exception.Which.Code.Should().Be("folder_not_found");
    }

    [Test]
    public async Task VoteAsync_SharedFolder_ReturnsPublisherIdentity()
    {
        await using var db = TestDb.CreateInMemoryWithDocuments();
        var owner = SeedUser(db, "Folder Owner");
        var viewer = SeedUser(db, "Voting Student");
        var folder = SeedFolder(db, owner.Id, isShared: true);
        var sut = BuildSut(db);

        var result = await sut.VoteAsync(viewer.SupabaseUserId, folder.Id, true);

        result.OwnerName.Should().Be("Folder Owner");
        result.LikeCount.Should().Be(1);
        result.CurrentUserVote.Should().BeTrue();
    }

    [Test]
    public async Task ToggleShareAsync_OwnerPublishesFolder_ReturnsPublishedFolder()
    {
        await using var db = TestDb.CreateInMemoryWithDocuments();
        var owner = SeedUser(db, "Owner");
        var folder = SeedFolder(db, owner.Id, isShared: false);
        var sut = BuildSut(db);

        var result = await sut.RequestShareAsync(owner.SupabaseUserId, folder.Id);

        result.ShareStatus.Should().Be(FolderStatus.PendingShare);
    }

    [Test]
    public async Task CopySharedFolderAsync_CopiesStorageDocumentsAndChunks()
    {
        await using var db = CreateDbWithChunks();
        var owner = SeedUser(db, "Owner");
        var viewer = SeedUser(db, "Viewer");
        var folder = SeedFolder(db, owner.Id, isShared: true);
        var sourceDocument = new Document
        {
            Id = Guid.NewGuid(),
            UserId = owner.Id,
            FolderId = folder.Id,
            FileName = "rag-notes.pdf",
            StoragePath = $"users/{owner.Id:N}/2026/rag-notes.pdf",
            FileSizeBytes = 128,
            MimeType = "application/pdf",
            SubjectCode = "SWP391",
            Semester = "SU26",
            Status = DocumentStatus.Ready,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.Documents.Add(sourceDocument);
        db.DocumentChunks.Add(new DocumentChunk
        {
            Id = Guid.NewGuid(),
            DocumentId = sourceDocument.Id,
            ChunkIndex = 0,
            PageNumber = 1,
            Content = "RAG uses retrieved source chunks.",
            TokenCount = 8,
            Embedding = new Vector(Enumerable.Repeat(0.1f, DocumentChunk.EmbeddingDimension).ToArray()),
            CreatedAt = DateTimeOffset.UtcNow,
        });
        db.SaveChanges();

        var storage = new Mock<ISupabaseStorageClient>(MockBehavior.Strict);
        storage
            .Setup(client => client.DownloadFileAsync(
                DocumentService.BucketName,
                sourceDocument.StoragePath,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => (
                (Stream)new MemoryStream(new byte[] { 1, 2, 3 }),
                "application/pdf"));
        storage
            .Setup(client => client.UploadAsync(
                DocumentService.BucketName,
                It.IsAny<string>(),
                It.IsAny<Stream>(),
                "application/pdf",
                false,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, string path, Stream _, string _, bool _, CancellationToken _) => path);
        var sut = new FolderService(db, NullLogger<FolderService>.Instance, storage.Object);

        var saved = await sut.CopySharedFolderAsync(viewer.SupabaseUserId, folder.Id);

        saved.DocumentCount.Should().Be(1);
        saved.ShareStatus.Should().Be(FolderStatus.None);
        var copiedDocument = db.Documents.Single(document => document.FolderId == saved.Id);
        copiedDocument.UserId.Should().Be(viewer.Id);
        copiedDocument.StoragePath.Should().NotBe(sourceDocument.StoragePath);
        db.DocumentChunks.Should().ContainSingle(chunk =>
            chunk.DocumentId == copiedDocument.Id
            && chunk.Content == "RAG uses retrieved source chunks.");
        storage.VerifyAll();
    }

    [Test]
    public async Task CopySharedFolderAsync_NameAlreadyExists_UsesNextAvailableSuffix()
    {
        await using var db = TestDb.CreateInMemoryWithDocuments();
        var owner = SeedUser(db, "Owner");
        var viewer = SeedUser(db, "Viewer");
        var source = SeedFolder(db, owner.Id, isShared: true);
        SeedFolderWithName(db, viewer.Id, source.Name);
        SeedFolderWithName(db, viewer.Id, $"{source.Name} (1)");
        var sut = BuildSut(db);

        var saved = await sut.CopySharedFolderAsync(viewer.SupabaseUserId, source.Id);

        saved.Name.Should().Be($"{source.Name} (2)");
        saved.ShareStatus.Should().Be(FolderStatus.None);
    }

    [Test]
    public async Task CopySharedFolderAsync_PrivateSource_IsNotExposed()
    {
        await using var db = TestDb.CreateInMemoryWithDocuments();
        var owner = SeedUser(db, "Owner");
        var viewer = SeedUser(db, "Viewer");
        var source = SeedFolder(db, owner.Id, isShared: false);
        var sut = BuildSut(db);

        var act = () => sut.CopySharedFolderAsync(viewer.SupabaseUserId, source.Id);

        var exception = await act.Should().ThrowAsync<DocumentException>();
        exception.Which.StatusCode.Should().Be(404);
        exception.Which.Code.Should().Be("folder_not_found");
    }

    [Test]
    public async Task CopySharedFolderAsync_StorageCopyFails_DoesNotPersistBrokenRecords()
    {
        await using var db = TestDb.CreateInMemoryWithDocuments();
        var owner = SeedUser(db, "Owner");
        var viewer = SeedUser(db, "Viewer");
        var source = SeedFolder(db, owner.Id, isShared: true);
        var sourceDocument = new Document
        {
            Id = Guid.NewGuid(),
            UserId = owner.Id,
            FolderId = source.Id,
            FileName = "unavailable.pdf",
            StoragePath = $"users/{owner.Id:N}/2026/unavailable.pdf",
            FileSizeBytes = 64,
            MimeType = "application/pdf",
            SubjectCode = "SWP391",
            Semester = "SU26",
            Status = DocumentStatus.Ready,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.Documents.Add(sourceDocument);
        db.SaveChanges();

        var storage = new Mock<ISupabaseStorageClient>(MockBehavior.Strict);
        storage
            .Setup(client => client.DownloadFileAsync(
                DocumentService.BucketName,
                sourceDocument.StoragePath,
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Storage unavailable."));
        var sut = new FolderService(db, NullLogger<FolderService>.Instance, storage.Object);

        var act = () => sut.CopySharedFolderAsync(viewer.SupabaseUserId, source.Id);

        var exception = await act.Should().ThrowAsync<DocumentException>();
        exception.Which.StatusCode.Should().Be(502);
        exception.Which.Code.Should().Be("folder_copy_failed");

        db.ChangeTracker.Clear();
        db.Folders.Should().NotContain(folder => folder.UserId == viewer.Id);
        db.Documents.Should().NotContain(document => document.UserId == viewer.Id);
        storage.VerifyAll();
    }

    [Test]
    public async Task CopySharedFolderAsync_LaterStorageCopyFails_CleansEarlierUpload()
    {
        await using var db = TestDb.CreateInMemoryWithDocuments();
        var owner = SeedUser(db, "Owner");
        var viewer = SeedUser(db, "Viewer");
        var source = SeedFolder(db, owner.Id, isShared: true);
        db.Documents.AddRange(
            CreateDocument(owner.Id, source.Id, "first.pdf"),
            CreateDocument(owner.Id, source.Id, "second.pdf"));
        db.SaveChanges();

        var storage = new Mock<ISupabaseStorageClient>(MockBehavior.Strict);
        storage
            .SetupSequence(client => client.DownloadFileAsync(
                DocumentService.BucketName,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(((Stream)new MemoryStream(new byte[] { 1, 2, 3 }), "application/pdf"))
            .ThrowsAsync(new HttpRequestException("Second download failed."));
        storage
            .Setup(client => client.UploadAsync(
                DocumentService.BucketName,
                It.IsAny<string>(),
                It.IsAny<Stream>(),
                "application/pdf",
                false,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, string path, Stream _, string _, bool _, CancellationToken _) => path);
        storage
            .Setup(client => client.DeleteAsync(
                DocumentService.BucketName,
                It.IsAny<string>(),
                CancellationToken.None))
            .Returns(Task.CompletedTask);
        var sut = new FolderService(db, NullLogger<FolderService>.Instance, storage.Object);

        var act = () => sut.CopySharedFolderAsync(viewer.SupabaseUserId, source.Id);

        await act.Should().ThrowAsync<DocumentException>();
        storage.Verify(client => client.DeleteAsync(
            DocumentService.BucketName,
            It.IsAny<string>(),
            CancellationToken.None), Times.Once);

        db.ChangeTracker.Clear();
        db.Folders.Should().NotContain(folder => folder.UserId == viewer.Id);
        db.Documents.Should().NotContain(document => document.UserId == viewer.Id);
        storage.VerifyAll();
    }

    private static FolderService BuildSut(Data.AppDbContext db)
        => new(
            db,
            NullLogger<FolderService>.Instance,
            Mock.Of<ISupabaseStorageClient>());

    private static Data.AppDbContext CreateDbWithChunks()
    {
        var options = new DbContextOptionsBuilder<Data.AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var db = new PublicHubTestDbContext(options);
        db.Roles.AddRange(
            new Role { Id = 1, RoleName = Role.AdminRoleName, Description = "Admin", CreatedAt = DateTimeOffset.UtcNow },
            new Role { Id = 2, RoleName = Role.StudentRoleName, Description = "Student", CreatedAt = DateTimeOffset.UtcNow });
        db.SaveChanges();
        return db;
    }

    private static User SeedUser(Data.AppDbContext db, string fullName)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            RoleId = 2,
            SupabaseUserId = Guid.NewGuid(),
            Username = $"u{Guid.NewGuid():N}"[..12],
            FullName = fullName,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.Users.Add(user);
        db.SaveChanges();
        return user;
    }

    private static Folder SeedFolder(Data.AppDbContext db, Guid userId, bool isShared)
    {
        var folder = new Folder
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = $"Folder {Guid.NewGuid():N}"[..20],
            ShareStatus = isShared ? FolderStatus.Approved : FolderStatus.None,
            SharedAt = isShared ? DateTimeOffset.UtcNow : null,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.Folders.Add(folder);
        db.SaveChanges();
        return folder;
    }

    private static Folder SeedFolderWithName(Data.AppDbContext db, Guid userId, string name)
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

    private static Document CreateDocument(Guid userId, Guid folderId, string fileName)
    {
        return new Document
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            FolderId = folderId,
            FileName = fileName,
            StoragePath = $"users/{userId:N}/2026/{Guid.NewGuid():N}-{fileName}",
            FileSizeBytes = 64,
            MimeType = "application/pdf",
            SubjectCode = "SWP391",
            Semester = "SU26",
            Status = DocumentStatus.Ready,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
    }

    private sealed class PublicHubTestDbContext : Data.AppDbContext
    {
        public PublicHubTestDbContext(DbContextOptions<Data.AppDbContext> options) : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<DocumentChunk>()
                .Property(chunk => chunk.Embedding)
                .HasConversion(
                    vector => string.Join(
                        ',',
                        vector.ToArray().Select(value => value.ToString("R", CultureInfo.InvariantCulture))),
                    value => new Vector(value
                        .Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(item => float.Parse(item, CultureInfo.InvariantCulture))
                        .ToArray()));
        }
    }
}
