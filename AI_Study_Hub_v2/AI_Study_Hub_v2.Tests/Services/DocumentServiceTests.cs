using AI_Study_Hub_v2.Data;
using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Dtos;
using AI_Study_Hub_v2.Services;
using AI_Study_Hub_v2.Services.Rag;
using AI_Study_Hub_v2.Services.Supabase;
using AI_Study_Hub_v2.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AI_Study_Hub_v2.Tests.Services;

/// <summary>
/// D5 SCRUM-28 coverage cho <see cref="DocumentService"/>: 4 verbs (Upload / List / GetById / Delete)
/// + tất cả lỗi nhánh (404 user_not_found, 403 user_inactive, 400 empty_file, 413 file_too_large,
/// 415 unsupported_media_type, 404 folder_not_found, 500 upload_persist_failed, 404 document_not_found).
/// Storage REST + Postgres được mock — không đụng Supabase Local stack, chạy được offline.
/// </summary>
[TestFixture]
public class DocumentServiceTests
{
    // -------------------------------------------------------------------------
    // helpers
    // -------------------------------------------------------------------------

    private static DocumentService BuildSut(
        AppDbContext db,
        ISupabaseStorageClient storage,
        IPlanCapacityGuard? capacityGuard = null,
        IStorageQuotaService? quota = null,
        IDocumentIngestionService? ingestion = null) =>
        new(db, storage, quota ?? Mock.Of<IStorageQuotaService>(), NullLogger<DocumentService>.Instance, ingestion,
            new StorageDeletionCoordinator(db, storage, NullLogger<StorageDeletionCoordinator>.Instance), capacityGuard ?? Mock.Of<IPlanCapacityGuard>(), Mock.Of<IAuditLogService>());

    private static Mock<IStorageQuotaService> CreateQuota(StorageReservation reservation)
    {
        var quota = new Mock<IStorageQuotaService>(MockBehavior.Strict);
        quota.Setup(q => q.ReserveUploadAsync(It.IsAny<Guid>(), reservation.ReservedBytes, It.IsAny<CancellationToken>()))
            .ReturnsAsync(reservation);
        quota.Setup(q => q.ValidateDocumentCountAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        quota.Setup(q => q.ConfirmReservationAsync(reservation, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return quota;
    }

    private static User SeedActiveStudent(AppDbContext db, Guid? supabaseUserId = null, Guid? profileId = null, bool isActive = true)
    {
        var user = new User
        {
            Id = profileId ?? Guid.NewGuid(),
            RoleId = 2, // Student
            SupabaseUserId = supabaseUserId ?? Guid.NewGuid(),
            Username = $"u{Guid.NewGuid():N}".Substring(0, 10),
            FullName = "Test User",
            IsActive = isActive,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.Users.Add(user);
        db.SaveChanges();
        return user;
    }

    private static Document SeedDocument(
        AppDbContext db,
        Guid userId,
        string fileName = "doc.pdf",
        string subjectCode = "SWP391",
        string semester = "SU26",
        string? mime = "application/pdf",
        long size = 1024,
        Guid? folderId = null,
        string? storagePath = null,
        DateTimeOffset? createdAt = null)
    {
        var now = createdAt ?? DateTimeOffset.UtcNow;
        var doc = new Document
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            FolderId = folderId,
            FileName = fileName,
            StoragePath = storagePath ?? $"users/{userId:N}/2026/{Guid.NewGuid():N}-{fileName}",
            FileSizeBytes = size,
            MimeType = mime!,
            SubjectCode = subjectCode,
            Semester = semester,
            Status = DocumentStatus.Ready,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Documents.Add(doc);
        db.SaveChanges();
        return doc;
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

    private static UploadDocumentRequest UploadReq(string subject = "SWP391", string semester = "SU26", Guid? folderId = null) => new()
    {
        SubjectCode = subject,
        Semester = semester,
        FolderId = folderId,
    };

    private static MemoryStream Stream(int sizeBytes = 16) => new(new byte[sizeBytes]);

    // -------------------------------------------------------------------------
    // UploadAsync — happy path
    // -------------------------------------------------------------------------

    [Test]
    public async Task UploadAsync_HappyPath_PersistsRow_AndUploadsBytesToStorage()
    {
        using var db = TestDb.CreateInMemoryWithDocuments();
        var profile = SeedActiveStudent(db);

        var storage = new Mock<ISupabaseStorageClient>(MockBehavior.Strict);
        storage
            .Setup(s => s.UploadAsync(
                DocumentService.BucketName,
                It.IsAny<string>(),
                It.IsAny<Stream>(),
                "application/pdf",
                false,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, string path, Stream _, string _, bool _, CancellationToken _) => path);

        var sut = BuildSut(db, storage.Object);

        var dto = await sut.UploadAsync(
            profile.SupabaseUserId,
            UploadReq(subject: "swp391", semester: "su26"), // verify upper-casing
            fileName: "  notes.pdf  ",
            contentType: "application/pdf",
            fileSizeBytes: 2048,
            content: Stream(2048));

        dto.Id.Should().NotBeEmpty();
        dto.FileName.Should().Be("notes.pdf");                // trimmed
        dto.SubjectCode.Should().Be("SWP391");                 // upper-cased
        dto.Semester.Should().Be("SU26");                      // upper-cased
        dto.Status.Should().Be(DocumentStatus.Processing);
        dto.FileSizeBytes.Should().Be(2048);
        dto.MimeType.Should().Be("application/pdf");
        dto.DownloadUrl.Should().BeNull(); // Upload endpoint never returns the signed URL.

        var row = await db.Documents.AsNoTracking().SingleAsync();
        row.UserId.Should().Be(profile.Id);
        row.StoragePath.Should().StartWith($"users/{profile.Id:N}/");
        row.StoragePath.Should().EndWith("notes.pdf");
        row.FolderId.Should().BeNull();

        storage.VerifyAll();
    }

    [Test]
    public async Task UploadAsync_SanitizesFileName_AndIncludesGuidYearAndUserSegmentInPath()
    {
        using var db = TestDb.CreateInMemoryWithDocuments();
        var profile = SeedActiveStudent(db);
        string? capturedPath = null;
        var storage = new Mock<ISupabaseStorageClient>();
        storage
            .Setup(s => s.UploadAsync(DocumentService.BucketName, It.IsAny<string>(), It.IsAny<Stream>(),
                It.IsAny<string>(), false, It.IsAny<CancellationToken>()))
            .Callback<string, string, Stream, string, bool, CancellationToken>(
                (_, p, _, _, _, _) => capturedPath = p)
            .ReturnsAsync("ok");

        var sut = BuildSut(db, storage.Object);

        // Filename with path separators + unsafe chars must be stripped.
        await sut.UploadAsync(
            profile.SupabaseUserId,
            UploadReq(),
            fileName: "../../etc/passwd  weird name!.pdf",
            contentType: "application/pdf",
            fileSizeBytes: 100,
            content: Stream(100));

        capturedPath.Should().NotBeNull();
        capturedPath!.Should().StartWith($"users/{profile.Id:N}/");
        capturedPath.Should().Contain($"/{DateTimeOffset.UtcNow:yyyy}/");
        capturedPath.Should().NotContain("..");
        capturedPath.Should().NotContain(" ");
        capturedPath.Should().EndWith(".pdf");
    }

    // -------------------------------------------------------------------------
    // UploadAsync — error branches
    // -------------------------------------------------------------------------

    [Test]
    public async Task UploadAsync_NoProfile_Throws404_UserNotFound()
    {
        using var db = TestDb.CreateInMemoryWithDocuments();
        // intentionally no SeedActiveStudent call.
        var storage = new Mock<ISupabaseStorageClient>(MockBehavior.Strict); // strict → ensures no upload attempted
        var sut = BuildSut(db, storage.Object);

        var act = () => sut.UploadAsync(
            supabaseUserId: Guid.NewGuid(),
            UploadReq(),
            fileName: "x.pdf",
            contentType: "application/pdf",
            fileSizeBytes: 100,
            content: Stream(100));

        var ex = await act.Should().ThrowAsync<DocumentException>();
        ex.Which.StatusCode.Should().Be(404);
        ex.Which.Code.Should().Be("user_not_found");
    }

    [Test]
    public async Task UploadAsync_InactiveUser_Throws403_UserInactive()
    {
        using var db = TestDb.CreateInMemoryWithDocuments();
        var profile = SeedActiveStudent(db, isActive: false);
        var storage = new Mock<ISupabaseStorageClient>(MockBehavior.Strict);
        storage.Setup(s => s.UploadAsync(DocumentService.BucketName, It.IsAny<string>(), It.IsAny<Stream>(), "application/pdf", false, It.IsAny<CancellationToken>())).ReturnsAsync("ok");
        storage.Setup(s => s.DeleteAsync(DocumentService.BucketName, It.IsAny<string>(), CancellationToken.None)).Returns(Task.CompletedTask);
        var guard = new Mock<IPlanCapacityGuard>(MockBehavior.Strict);
        guard.Setup(g => g.LockAndValidateAsync(db, profile.Id, It.IsAny<PlanCapacityRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DocumentException(409, "folder_full", "full"));
        var sut = BuildSut(db, storage.Object, guard.Object);

        var act = () => sut.UploadAsync(profile.SupabaseUserId, UploadReq(),
            fileName: "x.pdf", contentType: "application/pdf",
            fileSizeBytes: 100, content: Stream(100));

        var ex = await act.Should().ThrowAsync<DocumentException>();
        ex.Which.StatusCode.Should().Be(403);
        ex.Which.Code.Should().Be("user_inactive");
    }

    [Test]
    public async Task UploadAsync_EmptyFile_Throws400_EmptyFile()
    {
        using var db = TestDb.CreateInMemoryWithDocuments();
        var profile = SeedActiveStudent(db);
        var storage = new Mock<ISupabaseStorageClient>(MockBehavior.Strict);
        storage.Setup(s => s.UploadAsync(DocumentService.BucketName, It.IsAny<string>(), It.IsAny<Stream>(), "application/pdf", false, It.IsAny<CancellationToken>())).ReturnsAsync("ok");
        storage.Setup(s => s.DeleteAsync(DocumentService.BucketName, It.IsAny<string>(), CancellationToken.None)).Returns(Task.CompletedTask);
        var sut = BuildSut(db, storage.Object);

        var act = () => sut.UploadAsync(profile.SupabaseUserId, UploadReq(),
            fileName: "x.pdf", contentType: "application/pdf",
            fileSizeBytes: 0, content: Stream(0));

        var ex = await act.Should().ThrowAsync<DocumentException>();
        ex.Which.StatusCode.Should().Be(400);
        ex.Which.Code.Should().Be("empty_file");
    }

    [Test]
    public async Task UploadAsync_OversizeFile_Throws413_FileTooLarge()
    {
        using var db = TestDb.CreateInMemoryWithDocuments();
        var profile = SeedActiveStudent(db);
        var storage = new Mock<ISupabaseStorageClient>(MockBehavior.Strict);
        storage.Setup(s => s.UploadAsync(DocumentService.BucketName, It.IsAny<string>(), It.IsAny<Stream>(), "application/pdf", false, It.IsAny<CancellationToken>())).ReturnsAsync("ok");
        storage.Setup(s => s.DeleteAsync(DocumentService.BucketName, It.IsAny<string>(), CancellationToken.None)).Returns(Task.CompletedTask);
        var guard = new Mock<IPlanCapacityGuard>(MockBehavior.Strict);
        guard.Setup(g => g.LockAndValidateAsync(db, profile.Id, It.IsAny<PlanCapacityRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DocumentException(409, "folder_full", "full"));
        var sut = BuildSut(db, storage.Object, guard.Object);

        var act = () => sut.UploadAsync(profile.SupabaseUserId, UploadReq(),
            fileName: "big.pdf", contentType: "application/pdf",
            fileSizeBytes: DocumentService.MaxFileSizeBytes + 1,
            content: Stream(16)); // stream length doesn't matter for the size-cap branch

        var ex = await act.Should().ThrowAsync<DocumentException>();
        ex.Which.StatusCode.Should().Be(413);
        ex.Which.Code.Should().Be("file_too_large");
    }

    [TestCase("text/plain")]
    [TestCase("image/png")]
    [TestCase("application/zip")]
    [TestCase("")]
    public async Task UploadAsync_DisallowedMime_Throws415_UnsupportedMediaType(string mime)
    {
        using var db = TestDb.CreateInMemoryWithDocuments();
        var profile = SeedActiveStudent(db);
        var storage = new Mock<ISupabaseStorageClient>(MockBehavior.Strict);
        storage.Setup(s => s.UploadAsync(DocumentService.BucketName, It.IsAny<string>(), It.IsAny<Stream>(), "application/pdf", false, It.IsAny<CancellationToken>())).ReturnsAsync("ok");
        storage.Setup(s => s.DeleteAsync(DocumentService.BucketName, It.IsAny<string>(), CancellationToken.None)).Returns(Task.CompletedTask);
        var sut = BuildSut(db, storage.Object);

        var act = () => sut.UploadAsync(profile.SupabaseUserId, UploadReq(),
            fileName: "x.bin", contentType: mime,
            fileSizeBytes: 100, content: Stream(100));

        var ex = await act.Should().ThrowAsync<DocumentException>();
        ex.Which.StatusCode.Should().Be(415);
        ex.Which.Code.Should().Be("unsupported_media_type");
    }

    [TestCase("application/pdf")]
    [TestCase("application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    [TestCase("application/vnd.openxmlformats-officedocument.presentationml.presentation")]
    [TestCase("application/msword")]
    [TestCase("application/vnd.ms-powerpoint")]
    public async Task UploadAsync_AllowedMime_AcceptedByPolicy(string mime)
    {
        using var db = TestDb.CreateInMemoryWithDocuments();
        var profile = SeedActiveStudent(db);
        var storage = new Mock<ISupabaseStorageClient>();
        storage
            .Setup(s => s.UploadAsync(DocumentService.BucketName, It.IsAny<string>(), It.IsAny<Stream>(),
                mime, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync("ok");
        var sut = BuildSut(db, storage.Object);

        var dto = await sut.UploadAsync(profile.SupabaseUserId, UploadReq(),
            fileName: "ok.bin", contentType: mime,
            fileSizeBytes: 100, content: Stream(100));

        dto.MimeType.Should().Be(mime);
    }

    [Test]
    public async Task UploadAsync_DocxWithOctetStream_StoresCanonicalMime()
    {
        using var db = TestDb.CreateInMemoryWithDocuments();
        var profile = SeedActiveStudent(db);
        const string docxMime = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

        var storage = new Mock<ISupabaseStorageClient>(MockBehavior.Strict);
        storage
            .Setup(s => s.UploadAsync(
                DocumentService.BucketName,
                It.IsAny<string>(),
                It.IsAny<Stream>(),
                docxMime,
                false,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, string path, Stream _, string _, bool _, CancellationToken _) => path);
        var sut = BuildSut(db, storage.Object);

        var dto = await sut.UploadAsync(profile.SupabaseUserId, UploadReq(),
            fileName: "slides-notes.docx", contentType: "application/octet-stream",
            fileSizeBytes: 100, content: Stream(100));

        dto.MimeType.Should().Be(docxMime);
        var row = await db.Documents.AsNoTracking().SingleAsync();
        row.MimeType.Should().Be(docxMime);
        storage.VerifyAll();
    }

    [Test]
    public async Task UploadAsync_UnknownExtensionWithOctetStream_Throws415_UnsupportedMediaType()
    {
        using var db = TestDb.CreateInMemoryWithDocuments();
        var profile = SeedActiveStudent(db);
        var storage = new Mock<ISupabaseStorageClient>(MockBehavior.Strict);
        var sut = BuildSut(db, storage.Object);

        var act = () => sut.UploadAsync(profile.SupabaseUserId, UploadReq(),
            fileName: "archive.bin", contentType: "application/octet-stream",
            fileSizeBytes: 100, content: Stream(100));

        var ex = await act.Should().ThrowAsync<DocumentException>();
        ex.Which.StatusCode.Should().Be(415);
        ex.Which.Code.Should().Be("unsupported_media_type");
    }

    [Test]
    public async Task UploadAsync_FolderNotOwned_Throws404_FolderNotFound()
    {
        using var db = TestDb.CreateInMemoryWithDocuments();
        var profile = SeedActiveStudent(db);
        // Folder belongs to a different user.
        var otherUser = SeedActiveStudent(db);
        var foreignFolder = new Folder
        {
            Id = Guid.NewGuid(),
            UserId = otherUser.Id,
            Name = "Foreign",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.Folders.Add(foreignFolder);
        db.SaveChanges();

        var storage = new Mock<ISupabaseStorageClient>(MockBehavior.Strict);
        var sut = BuildSut(db, storage.Object);

        var act = () => sut.UploadAsync(profile.SupabaseUserId,
            UploadReq(folderId: foreignFolder.Id),
            fileName: "x.pdf", contentType: "application/pdf",
            fileSizeBytes: 100, content: Stream(100));

        var ex = await act.Should().ThrowAsync<DocumentException>();
        ex.Which.StatusCode.Should().Be(404);
        ex.Which.Code.Should().Be("folder_not_found");
    }

    [Test]
    public async Task UploadAsync_OwnFolder_Accepted_AndPersistsFolderId()
    {
        using var db = TestDb.CreateInMemoryWithDocuments();
        var profile = SeedActiveStudent(db);
        var folder = new Folder
        {
            Id = Guid.NewGuid(),
            UserId = profile.Id,
            Name = "Mine",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.Folders.Add(folder);
        db.SaveChanges();

        var storage = new Mock<ISupabaseStorageClient>();
        storage.Setup(s => s.UploadAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(),
            It.IsAny<string>(), false, It.IsAny<CancellationToken>())).ReturnsAsync("ok");
        var sut = BuildSut(db, storage.Object);

        var dto = await sut.UploadAsync(profile.SupabaseUserId,
            UploadReq(folderId: folder.Id),
            fileName: "x.pdf", contentType: "application/pdf",
            fileSizeBytes: 100, content: Stream(100));

        dto.FolderId.Should().Be(folder.Id);
        var row = await db.Documents.AsNoTracking().SingleAsync();
        row.FolderId.Should().Be(folder.Id);
    }

    [Test]
    public async Task UploadAsync_FolderFull_Throws409()
    {
        using var db = TestDb.CreateInMemoryWithDocuments();
        var profile = SeedActiveStudent(db);
        var folder = SeedFolder(db, profile.Id);

        for (int i = 0; i < DocumentService.MaxDocumentsPerFolder; i++)
        {
            SeedDocument(db, profile.Id, folderId: folder.Id, fileName: $"doc{i}.pdf");
        }

        var storage = new Mock<ISupabaseStorageClient>(MockBehavior.Strict);
        var sut = BuildSut(db, storage.Object);

        var act = () => sut.UploadAsync(profile.SupabaseUserId,
            UploadReq(folderId: folder.Id), fileName: "overflow.pdf", contentType: "application/pdf",
            fileSizeBytes: 100, content: Stream(100));

        var ex = await act.Should().ThrowAsync<DocumentException>();
        ex.Which.StatusCode.Should().Be(409);
        ex.Which.Code.Should().Be("folder_full");
        storage.VerifyNoOtherCalls();
    }

    [Test]
    public async Task UploadAsync_DuplicateFileNameInFolder_Throws409()
    {
        using var db = TestDb.CreateInMemoryWithDocuments();
        var profile = SeedActiveStudent(db);
        var folder = SeedFolder(db, profile.Id);
        SeedDocument(db, profile.Id, folderId: folder.Id, fileName: "report.pdf");

        var storage = new Mock<ISupabaseStorageClient>(MockBehavior.Strict);
        var sut = BuildSut(db, storage.Object);

        var act = () => sut.UploadAsync(profile.SupabaseUserId,
            UploadReq(folderId: folder.Id),
            fileName: "Report.pdf", contentType: "application/pdf",
            fileSizeBytes: 100, content: Stream(100));

        var ex = await act.Should().ThrowAsync<DocumentException>();
        ex.Which.StatusCode.Should().Be(409);
        ex.Which.Code.Should().Be("duplicate_file");
        storage.VerifyNoOtherCalls();
    }

    [Test]
    public async Task UploadAsync_DuplicateFileNameOutsideFolder_Allowed()
    {
        using var db = TestDb.CreateInMemoryWithDocuments();
        var profile = SeedActiveStudent(db);
        var folderA = SeedFolder(db, profile.Id, "A");
        var folderB = SeedFolder(db, profile.Id, "B");
        SeedDocument(db, profile.Id, folderId: folderA.Id, fileName: "same.pdf");

        var storage = new Mock<ISupabaseStorageClient>();
        storage.Setup(s => s.UploadAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(),
            It.IsAny<string>(), false, It.IsAny<CancellationToken>())).ReturnsAsync("ok");
        var sut = BuildSut(db, storage.Object);

        var dto = await sut.UploadAsync(profile.SupabaseUserId,
            UploadReq(folderId: folderB.Id),
            fileName: "same.pdf", contentType: "application/pdf",
            fileSizeBytes: 100, content: Stream(100));

        dto.FolderId.Should().Be(folderB.Id);
        dto.FileName.Should().Be("same.pdf");
    }

    [Test]
    public async Task UploadAsync_StorageUploadThrows_Bubbles_AndDoesNotInsertRow()
    {
        using var db = TestDb.CreateInMemoryWithDocuments();
        var profile = SeedActiveStudent(db);
        var storage = new Mock<ISupabaseStorageClient>();
        storage.Setup(s => s.UploadAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(),
                It.IsAny<string>(), false, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("storage down"));
        var sut = BuildSut(db, storage.Object);

        var act = () => sut.UploadAsync(profile.SupabaseUserId, UploadReq(),
            fileName: "x.pdf", contentType: "application/pdf",
            fileSizeBytes: 100, content: Stream(100));

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await db.Documents.CountAsync()).Should().Be(0);
    }

    [Test]
    public async Task UploadAsync_TotalDocumentLimitAfterStorageUpload_CompensatesAndPreservesPlanException()
    {
        using var db = TestDb.CreateInMemoryWithDocuments();
        var profile = SeedActiveStudent(db);
        var reservation = new StorageReservation(profile.Id, 100, DateTimeOffset.UtcNow);
        var quota = CreateQuota(reservation);
        var storageUploaded = false;
        var storage = new Mock<ISupabaseStorageClient>(MockBehavior.Strict);
        storage.Setup(s => s.UploadAsync(DocumentService.BucketName, It.IsAny<string>(), It.IsAny<Stream>(), "application/pdf", false, It.IsAny<CancellationToken>()))
            .Callback(() => storageUploaded = true)
            .ReturnsAsync("uploaded");
        storage.Setup(s => s.DeleteAsync(DocumentService.BucketName, It.IsAny<string>(), CancellationToken.None))
            .Returns(Task.CompletedTask);
        var expected = new PlanException(402, "document_count_exceeded", "limit");
        var guard = new Mock<IPlanCapacityGuard>(MockBehavior.Strict);
        guard.Setup(g => g.LockAndValidateAsync(db, profile.Id, It.IsAny<PlanCapacityRequest>(), It.IsAny<CancellationToken>()))
            .Callback(() => storageUploaded.Should().BeTrue("capacity is finalized only after the object is uploaded"))
            .ThrowsAsync(expected);
        quota.Setup(q => q.ReleaseReservationAsync(reservation, CancellationToken.None)).Returns(Task.CompletedTask);
        var sut = BuildSut(db, storage.Object, guard.Object, quota.Object);

        var act = () => sut.UploadAsync(profile.SupabaseUserId, UploadReq(), "limit.pdf", "application/pdf", 100, Stream(100));

        var exception = await act.Should().ThrowAsync<PlanException>();
        exception.Which.Should().BeSameAs(expected);
        (await db.Documents.CountAsync()).Should().Be(0);
        storage.Verify(s => s.DeleteAsync(DocumentService.BucketName, It.IsAny<string>(), CancellationToken.None), Times.Once);
        quota.Verify(q => q.ReleaseReservationAsync(reservation, CancellationToken.None), Times.Once);
    }

    [Test]
    public async Task UploadAsync_DuplicateAfterStorageUpload_CompensatesReservation()
    {
        using var db = TestDb.CreateInMemoryWithDocuments();
        var profile = SeedActiveStudent(db);
        var folder = SeedFolder(db, profile.Id);
        var reservation = new StorageReservation(profile.Id, 100, DateTimeOffset.UtcNow);
        var quota = CreateQuota(reservation);
        quota.Setup(q => q.ReleaseReservationAsync(reservation, CancellationToken.None)).Returns(Task.CompletedTask);
        var storage = new Mock<ISupabaseStorageClient>(MockBehavior.Strict);
        storage.Setup(s => s.UploadAsync(DocumentService.BucketName, It.IsAny<string>(), It.IsAny<Stream>(), "application/pdf", false, It.IsAny<CancellationToken>()))
            .ReturnsAsync("uploaded");
        storage.Setup(s => s.DeleteAsync(DocumentService.BucketName, It.IsAny<string>(), CancellationToken.None))
            .Returns(Task.CompletedTask);
        var guard = new Mock<IPlanCapacityGuard>(MockBehavior.Strict);
        guard.Setup(g => g.LockAndValidateAsync(db, profile.Id, It.IsAny<PlanCapacityRequest>(), It.IsAny<CancellationToken>()))
            .Callback(() => SeedDocument(db, profile.Id, "report.pdf", folderId: folder.Id))
            .Returns(Task.CompletedTask);
        var sut = BuildSut(db, storage.Object, guard.Object, quota.Object);

        var act = () => sut.UploadAsync(profile.SupabaseUserId, UploadReq(folderId: folder.Id), "REPORT.pdf", "application/pdf", 100, Stream(100));

        var exception = await act.Should().ThrowAsync<DocumentException>();
        exception.Which.Code.Should().Be("duplicate_file");
        storage.Verify(s => s.DeleteAsync(DocumentService.BucketName, It.IsAny<string>(), CancellationToken.None), Times.Once);
        quota.Verify(q => q.ReleaseReservationAsync(reservation, CancellationToken.None), Times.Once);
    }

    [Test]
    public async Task UploadAsync_KnownDocumentLimit_FailsBeforeStorageUpload()
    {
        using var db = TestDb.CreateInMemoryWithDocuments();
        var profile = SeedActiveStudent(db);
        var expected = new PlanException(402, "document_count_exceeded", "limit");
        var quota = new Mock<IStorageQuotaService>(MockBehavior.Strict);
        quota.Setup(q => q.ValidateDocumentCountAsync(profile.SupabaseUserId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(expected);
        var storage = new Mock<ISupabaseStorageClient>(MockBehavior.Strict);
        var sut = BuildSut(db, storage.Object, quota: quota.Object);

        var act = () => sut.UploadAsync(profile.SupabaseUserId, UploadReq(), "limited.pdf", "application/pdf", 100, Stream(100));

        var exception = await act.Should().ThrowAsync<PlanException>();
        exception.Which.Should().BeSameAs(expected);
        storage.VerifyNoOtherCalls();
        quota.Verify(q => q.ReserveUploadAsync(It.IsAny<Guid>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task UploadAsync_CleanupFailureAfterFinalizationFailure_RetainsReservation()
    {
        using var db = TestDb.CreateInMemoryWithDocuments();
        var profile = SeedActiveStudent(db);
        var reservation = new StorageReservation(profile.Id, 100, DateTimeOffset.UtcNow);
        var quota = CreateQuota(reservation);
        var storage = new Mock<ISupabaseStorageClient>(MockBehavior.Strict);
        storage.Setup(s => s.UploadAsync(DocumentService.BucketName, It.IsAny<string>(), It.IsAny<Stream>(), "application/pdf", false, It.IsAny<CancellationToken>()))
            .ReturnsAsync("uploaded");
        storage.Setup(s => s.DeleteAsync(DocumentService.BucketName, It.IsAny<string>(), CancellationToken.None))
            .ThrowsAsync(new HttpRequestException("cleanup unavailable"));
        var expected = new DocumentException(409, "folder_full", "full");
        var guard = new Mock<IPlanCapacityGuard>(MockBehavior.Strict);
        guard.Setup(g => g.LockAndValidateAsync(db, profile.Id, It.IsAny<PlanCapacityRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(expected);
        var sut = BuildSut(db, storage.Object, guard.Object, quota.Object);

        var act = () => sut.UploadAsync(profile.SupabaseUserId, UploadReq(), "cleanup.pdf", "application/pdf", 100, Stream(100));

        var exception = await act.Should().ThrowAsync<DocumentException>();
        exception.Which.Should().BeSameAs(expected);
        quota.Verify(q => q.ReleaseReservationAsync(It.IsAny<StorageReservation>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task UploadAsync_UnexpectedFinalizationFailure_MapsToUploadPersistFailedAndCompensates()
    {
        using var db = TestDb.CreateInMemoryWithDocuments();
        var profile = SeedActiveStudent(db);
        var reservation = new StorageReservation(profile.Id, 100, DateTimeOffset.UtcNow);
        var quota = CreateQuota(reservation);
        quota.Setup(q => q.ReleaseReservationAsync(reservation, CancellationToken.None)).Returns(Task.CompletedTask);
        var storage = new Mock<ISupabaseStorageClient>(MockBehavior.Strict);
        storage.Setup(s => s.UploadAsync(DocumentService.BucketName, It.IsAny<string>(), It.IsAny<Stream>(), "application/pdf", false, It.IsAny<CancellationToken>()))
            .ReturnsAsync("uploaded");
        storage.Setup(s => s.DeleteAsync(DocumentService.BucketName, It.IsAny<string>(), CancellationToken.None))
            .Returns(Task.CompletedTask);
        var guard = new Mock<IPlanCapacityGuard>(MockBehavior.Strict);
        guard.Setup(g => g.LockAndValidateAsync(db, profile.Id, It.IsAny<PlanCapacityRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("persistence failed"));
        var sut = BuildSut(db, storage.Object, guard.Object, quota.Object);

        var act = () => sut.UploadAsync(profile.SupabaseUserId, UploadReq(), "persist.pdf", "application/pdf", 100, Stream(100));

        var exception = await act.Should().ThrowAsync<DocumentException>();
        exception.Which.StatusCode.Should().Be(500);
        exception.Which.Code.Should().Be("upload_persist_failed");
        quota.Verify(q => q.ReleaseReservationAsync(reservation, CancellationToken.None), Times.Once);
    }

    [Test]
    public async Task UploadAsync_StorageUploadFailure_AttemptsDeleteThenReleasesReservation()
    {
        using var db = TestDb.CreateInMemoryWithDocuments();
        var profile = SeedActiveStudent(db);
        var reservation = new StorageReservation(profile.Id, 100, DateTimeOffset.UtcNow);
        var quota = CreateQuota(reservation);
        quota.Setup(q => q.ReleaseReservationAsync(reservation, CancellationToken.None)).Returns(Task.CompletedTask);
        var storage = new Mock<ISupabaseStorageClient>(MockBehavior.Strict);
        storage.Setup(s => s.UploadAsync(DocumentService.BucketName, It.IsAny<string>(), It.IsAny<Stream>(), "application/pdf", false, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("storage down"));
        storage.Setup(s => s.DeleteAsync(DocumentService.BucketName, It.IsAny<string>(), CancellationToken.None))
            .Returns(Task.CompletedTask);
        var sut = BuildSut(db, storage.Object, quota: quota.Object);

        var act = () => sut.UploadAsync(profile.SupabaseUserId, UploadReq(), "failed.pdf", "application/pdf", 100, Stream(100));

        await act.Should().ThrowAsync<InvalidOperationException>();
        quota.Verify(q => q.ReleaseReservationAsync(reservation, CancellationToken.None), Times.Once);
        storage.Verify(s => s.DeleteAsync(DocumentService.BucketName, It.IsAny<string>(), CancellationToken.None), Times.Once);
        (await db.Documents.CountAsync()).Should().Be(0);
    }

    [Test]
    public async Task UploadAsync_StorageUploadFailureAndDeleteFailure_RetainsReservation()
    {
        using var db = TestDb.CreateInMemoryWithDocuments();
        var profile = SeedActiveStudent(db);
        var reservation = new StorageReservation(profile.Id, 100, DateTimeOffset.UtcNow);
        var quota = CreateQuota(reservation);
        var storage = new Mock<ISupabaseStorageClient>(MockBehavior.Strict);
        storage.Setup(s => s.UploadAsync(DocumentService.BucketName, It.IsAny<string>(), It.IsAny<Stream>(), "application/pdf", false, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("upload response lost"));
        storage.Setup(s => s.DeleteAsync(DocumentService.BucketName, It.IsAny<string>(), CancellationToken.None))
            .ThrowsAsync(new HttpRequestException("cleanup unavailable"));
        var sut = BuildSut(db, storage.Object, quota: quota.Object);

        var act = () => sut.UploadAsync(profile.SupabaseUserId, UploadReq(), "uncertain.pdf", "application/pdf", 100, Stream(100));

        await act.Should().ThrowAsync<InvalidOperationException>();
        storage.Verify(s => s.DeleteAsync(DocumentService.BucketName, It.IsAny<string>(), CancellationToken.None), Times.Once);
        quota.Verify(q => q.ReleaseReservationAsync(It.IsAny<StorageReservation>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task UploadAsync_CancelledAfterUploadAttempt_UsesNonCancelledDeleteToken()
    {
        using var db = TestDb.CreateInMemoryWithDocuments();
        var profile = SeedActiveStudent(db);
        var reservation = new StorageReservation(profile.Id, 100, DateTimeOffset.UtcNow);
        var quota = CreateQuota(reservation);
        quota.Setup(q => q.ReleaseReservationAsync(reservation, CancellationToken.None)).Returns(Task.CompletedTask);
        var storage = new Mock<ISupabaseStorageClient>(MockBehavior.Strict);
        storage.Setup(s => s.UploadAsync(DocumentService.BucketName, It.IsAny<string>(), It.IsAny<Stream>(), "application/pdf", false, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException("upload cancelled"));
        storage.Setup(s => s.DeleteAsync(DocumentService.BucketName, It.IsAny<string>(), CancellationToken.None))
            .Returns(Task.CompletedTask);
        var sut = BuildSut(db, storage.Object, quota: quota.Object);

        var act = () => sut.UploadAsync(profile.SupabaseUserId, UploadReq(), "cancelled.pdf", "application/pdf", 100, Stream(100));

        await act.Should().ThrowAsync<OperationCanceledException>();
        storage.Verify(s => s.DeleteAsync(DocumentService.BucketName, It.IsAny<string>(), CancellationToken.None), Times.Once);
        quota.Verify(q => q.ReleaseReservationAsync(reservation, CancellationToken.None), Times.Once);
    }

    [Test]
    public async Task UploadAsync_ConfirmationFailureAfterMetadataCommit_DoesNotCompensate()
    {
        using var db = TestDb.CreateInMemoryWithDocuments();
        var profile = SeedActiveStudent(db);
        var reservation = new StorageReservation(profile.Id, 100, DateTimeOffset.UtcNow);
        var quota = CreateQuota(reservation);
        quota.Setup(q => q.ConfirmReservationAsync(reservation, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("confirmation unavailable"));
        var storage = new Mock<ISupabaseStorageClient>(MockBehavior.Strict);
        storage.Setup(s => s.UploadAsync(DocumentService.BucketName, It.IsAny<string>(), It.IsAny<Stream>(), "application/pdf", false, It.IsAny<CancellationToken>()))
            .ReturnsAsync("uploaded");
        var sut = BuildSut(db, storage.Object, quota: quota.Object);

        var act = () => sut.UploadAsync(profile.SupabaseUserId, UploadReq(), "confirmed.pdf", "application/pdf", 100, Stream(100));

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await db.Documents.CountAsync()).Should().Be(1);
        quota.Verify(q => q.ReleaseReservationAsync(It.IsAny<StorageReservation>(), It.IsAny<CancellationToken>()), Times.Never);
        storage.Verify(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task UploadAsync_IngestionFailureAfterMetadataCommit_DoesNotCompensate()
    {
        using var db = TestDb.CreateInMemoryWithDocuments();
        var profile = SeedActiveStudent(db);
        var reservation = new StorageReservation(profile.Id, 100, DateTimeOffset.UtcNow);
        var quota = CreateQuota(reservation);
        var ingestion = new Mock<IDocumentIngestionService>(MockBehavior.Strict);
        ingestion.Setup(i => i.IngestAsync(It.IsAny<Guid>(), profile.SupabaseUserId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("ingestion unavailable"));
        var storage = new Mock<ISupabaseStorageClient>(MockBehavior.Strict);
        storage.Setup(s => s.UploadAsync(DocumentService.BucketName, It.IsAny<string>(), It.IsAny<Stream>(), "application/pdf", false, It.IsAny<CancellationToken>()))
            .ReturnsAsync("uploaded");
        var sut = BuildSut(db, storage.Object, quota: quota.Object, ingestion: ingestion.Object);

        var act = () => sut.UploadAsync(profile.SupabaseUserId, UploadReq(), "ingest.pdf", "application/pdf", 100, Stream(100));

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await db.Documents.CountAsync()).Should().Be(1);
        quota.Verify(q => q.ReleaseReservationAsync(It.IsAny<StorageReservation>(), It.IsAny<CancellationToken>()), Times.Never);
        storage.Verify(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // -------------------------------------------------------------------------
    // ListAsync
    // -------------------------------------------------------------------------

    [Test]
    public async Task ListAsync_NoFilter_ReturnsCallerDocsOnly_OrderedByCreatedAtDesc()
    {
        using var db = TestDb.CreateInMemoryWithDocuments();
        var me = SeedActiveStudent(db);
        var other = SeedActiveStudent(db);

        var older = SeedDocument(db, me.Id, fileName: "older.pdf",
            createdAt: DateTimeOffset.UtcNow.AddHours(-2));
        var newer = SeedDocument(db, me.Id, fileName: "newer.pdf",
            createdAt: DateTimeOffset.UtcNow.AddHours(-1));
        SeedDocument(db, other.Id, fileName: "other.pdf"); // must NOT appear

        var sut = BuildSut(db, Mock.Of<ISupabaseStorageClient>());

        var rows = await sut.ListAsync(me.SupabaseUserId, new DocumentListQuery());

        rows.Should().HaveCount(2);
        rows[0].Id.Should().Be(newer.Id);
        rows[1].Id.Should().Be(older.Id);
        rows.Should().AllSatisfy(d => d.DownloadUrl.Should().BeNull(),
            "list endpoint must never leak signed URLs");
    }

    [Test]
    public async Task ListAsync_FilterBySubjectAndSemester_CaseInsensitive()
    {
        using var db = TestDb.CreateInMemoryWithDocuments();
        var me = SeedActiveStudent(db);
        SeedDocument(db, me.Id, fileName: "swp.pdf", subjectCode: "SWP391", semester: "SU26");
        SeedDocument(db, me.Id, fileName: "prn.pdf", subjectCode: "PRN232", semester: "SU26");
        SeedDocument(db, me.Id, fileName: "swp-fa.pdf", subjectCode: "SWP391", semester: "FA25");

        var sut = BuildSut(db, Mock.Of<ISupabaseStorageClient>());

        var bySubjectLower = await sut.ListAsync(me.SupabaseUserId,
            new DocumentListQuery { SubjectCode = "swp391" });
        bySubjectLower.Should().HaveCount(2);

        var bySemester = await sut.ListAsync(me.SupabaseUserId,
            new DocumentListQuery { Semester = "su26" });
        bySemester.Should().HaveCount(2);

        var both = await sut.ListAsync(me.SupabaseUserId,
            new DocumentListQuery { SubjectCode = "SWP391", Semester = "SU26" });
        both.Should().ContainSingle().Which.FileName.Should().Be("swp.pdf");
    }

    [Test]
    public async Task ListAsync_FilterByFolderId_OnlyReturnsThatFolder()
    {
        using var db = TestDb.CreateInMemoryWithDocuments();
        var me = SeedActiveStudent(db);
        var folder = new Folder
        {
            Id = Guid.NewGuid(),
            UserId = me.Id,
            Name = "F1",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.Folders.Add(folder);
        db.SaveChanges();

        SeedDocument(db, me.Id, fileName: "loose.pdf");                       // no folder
        SeedDocument(db, me.Id, fileName: "infolder.pdf", folderId: folder.Id);

        var sut = BuildSut(db, Mock.Of<ISupabaseStorageClient>());

        var rows = await sut.ListAsync(me.SupabaseUserId,
            new DocumentListQuery { FolderId = folder.Id });

        rows.Should().ContainSingle().Which.FileName.Should().Be("infolder.pdf");
    }

    [Test]
    public async Task ListAsync_NoProfile_Throws404_UserNotFound()
    {
        using var db = TestDb.CreateInMemoryWithDocuments();
        var sut = BuildSut(db, Mock.Of<ISupabaseStorageClient>());

        var act = () => sut.ListAsync(Guid.NewGuid(), new DocumentListQuery());

        var ex = await act.Should().ThrowAsync<DocumentException>();
        ex.Which.StatusCode.Should().Be(404);
        ex.Which.Code.Should().Be("user_not_found");
    }

    // EF.Functions.ILike has no InMemory translation, so the Q-text branch can only be
    // exercised on a real Postgres connection. We document the gap rather than skip the
    // assertion silently — D5 still covers SubjectCode/Semester/FolderId text branches above.
    [Test]
    [Ignore("EF.Functions.ILike has no InMemory provider translation; covered by D2 live smoke instead.")]
    public Task ListAsync_FilterByQ_ILikeMatch_FileName() => Task.CompletedTask;

    // -------------------------------------------------------------------------
    // GetByIdAsync
    // -------------------------------------------------------------------------

    [Test]
    public async Task GetByIdAsync_Owner_ReturnsDtoWithSignedUrl()
    {
        using var db = TestDb.CreateInMemoryWithDocuments();
        var me = SeedActiveStudent(db);
        var doc = SeedDocument(db, me.Id);

        var storage = new Mock<ISupabaseStorageClient>(MockBehavior.Strict);
        storage
            .Setup(s => s.CreateSignedUrlAsync(
                DocumentService.BucketName,
                doc.StoragePath,
                DocumentService.SignedUrlTtlSeconds,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://storage.local/signed?token=abc");
        var sut = BuildSut(db, storage.Object);

        var dto = await sut.GetByIdAsync(me.SupabaseUserId, doc.Id);

        dto.Id.Should().Be(doc.Id);
        dto.DownloadUrl.Should().Be("https://storage.local/signed?token=abc");
        storage.VerifyAll();
    }

    [Test]
    public async Task GetByIdAsync_NotOwner_Throws404_DocumentNotFound()
    {
        using var db = TestDb.CreateInMemoryWithDocuments();
        var me = SeedActiveStudent(db);
        var owner = SeedActiveStudent(db);
        var doc = SeedDocument(db, owner.Id);

        var storage = new Mock<ISupabaseStorageClient>(MockBehavior.Strict);
        var sut = BuildSut(db, storage.Object);

        var act = () => sut.GetByIdAsync(me.SupabaseUserId, doc.Id);

        var ex = await act.Should().ThrowAsync<DocumentException>();
        ex.Which.StatusCode.Should().Be(404);
        ex.Which.Code.Should().Be("document_not_found");
    }

    [Test]
    public async Task GetByIdAsync_NoProfile_Throws404_UserNotFound()
    {
        using var db = TestDb.CreateInMemoryWithDocuments();
        var storage = new Mock<ISupabaseStorageClient>(MockBehavior.Strict);
        var sut = BuildSut(db, storage.Object);

        var act = () => sut.GetByIdAsync(Guid.NewGuid(), Guid.NewGuid());

        var ex = await act.Should().ThrowAsync<DocumentException>();
        ex.Which.StatusCode.Should().Be(404);
        ex.Which.Code.Should().Be("user_not_found");
    }

    // -------------------------------------------------------------------------
    // MoveToFolderAsync
    // -------------------------------------------------------------------------

    [Test]
    public async Task MoveToFolderAsync_OwnFolder_UpdatesFolderId()
    {
        using var db = TestDb.CreateInMemoryWithDocuments();
        var me = SeedActiveStudent(db);
        var folder = new Folder
        {
            Id = Guid.NewGuid(),
            UserId = me.Id,
            Name = "Sprint notes",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.Folders.Add(folder);
        var doc = SeedDocument(db, me.Id);

        var storage = new Mock<ISupabaseStorageClient>(MockBehavior.Strict);
        var sut = BuildSut(db, storage.Object);

        var dto = await sut.MoveToFolderAsync(me.SupabaseUserId, doc.Id, folder.Id);

        dto.FolderId.Should().Be(folder.Id);
        (await db.Documents.AsNoTracking().SingleAsync(d => d.Id == doc.Id)).FolderId.Should().Be(folder.Id);
    }

    [Test]
    public async Task MoveToFolderAsync_NullFolder_MovesBackToLooseDocuments()
    {
        using var db = TestDb.CreateInMemoryWithDocuments();
        var me = SeedActiveStudent(db);
        var folder = new Folder
        {
            Id = Guid.NewGuid(),
            UserId = me.Id,
            Name = "Sprint notes",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.Folders.Add(folder);
        var doc = SeedDocument(db, me.Id, folderId: folder.Id);

        var storage = new Mock<ISupabaseStorageClient>(MockBehavior.Strict);
        var sut = BuildSut(db, storage.Object);

        var dto = await sut.MoveToFolderAsync(me.SupabaseUserId, doc.Id, folderId: null);

        dto.FolderId.Should().BeNull();
        (await db.Documents.AsNoTracking().SingleAsync(d => d.Id == doc.Id)).FolderId.Should().BeNull();
    }

    [Test]
    public async Task MoveToFolderAsync_ForeignFolder_Throws404_FolderNotFound()
    {
        using var db = TestDb.CreateInMemoryWithDocuments();
        var me = SeedActiveStudent(db);
        var other = SeedActiveStudent(db);
        var foreignFolder = new Folder
        {
            Id = Guid.NewGuid(),
            UserId = other.Id,
            Name = "Private",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.Folders.Add(foreignFolder);
        var doc = SeedDocument(db, me.Id);

        var storage = new Mock<ISupabaseStorageClient>(MockBehavior.Strict);
        var sut = BuildSut(db, storage.Object);

        var act = () => sut.MoveToFolderAsync(me.SupabaseUserId, doc.Id, foreignFolder.Id);

        var ex = await act.Should().ThrowAsync<DocumentException>();
        ex.Which.StatusCode.Should().Be(404);
        ex.Which.Code.Should().Be("folder_not_found");
        (await db.Documents.AsNoTracking().SingleAsync(d => d.Id == doc.Id)).FolderId.Should().BeNull();
    }

    // -------------------------------------------------------------------------
    // DeleteAsync
    // -------------------------------------------------------------------------

    [Test]
    public async Task DeleteAsync_Owner_RemovesRow_AndCallsStorageDelete()
    {
        using var db = TestDb.CreateInMemoryWithDocuments();
        var me = SeedActiveStudent(db);
        var doc = SeedDocument(db, me.Id);
        var pathSnapshot = doc.StoragePath;

        var storage = new Mock<ISupabaseStorageClient>(MockBehavior.Strict);
        storage
            .Setup(s => s.DeleteAsync(DocumentService.BucketName, pathSnapshot, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var sut = BuildSut(db, storage.Object);

        await sut.DeleteAsync(me.SupabaseUserId, doc.Id);

        (await db.Documents.CountAsync()).Should().Be(0);
        storage.VerifyAll();
    }

    [Test]
    public async Task DeleteAsync_NotOwner_Throws404_DocumentNotFound_AndDoesNotCallStorage()
    {
        using var db = TestDb.CreateInMemoryWithDocuments();
        var me = SeedActiveStudent(db);
        var owner = SeedActiveStudent(db);
        var doc = SeedDocument(db, owner.Id);

        var storage = new Mock<ISupabaseStorageClient>(MockBehavior.Strict);
        var sut = BuildSut(db, storage.Object);

        var act = () => sut.DeleteAsync(me.SupabaseUserId, doc.Id);

        var ex = await act.Should().ThrowAsync<DocumentException>();
        ex.Which.StatusCode.Should().Be(404);
        ex.Which.Code.Should().Be("document_not_found");
        (await db.Documents.CountAsync()).Should().Be(1, "row must remain when not owner");
    }

    [Test]
    public async Task DeleteAsync_NoProfile_Throws404_UserNotFound()
    {
        using var db = TestDb.CreateInMemoryWithDocuments();
        var storage = new Mock<ISupabaseStorageClient>(MockBehavior.Strict);
        var sut = BuildSut(db, storage.Object);

        var act = () => sut.DeleteAsync(Guid.NewGuid(), Guid.NewGuid());

        var ex = await act.Should().ThrowAsync<DocumentException>();
        ex.Which.StatusCode.Should().Be(404);
        ex.Which.Code.Should().Be("user_not_found");
    }

    [Test]
    public async Task DeleteAsync_StorageDeleteThrows_RowAndQuotaRemain()
    {
        using var db = TestDb.CreateInMemoryWithDocuments();
        var me = SeedActiveStudent(db);
        var doc = SeedDocument(db, me.Id);

        var storage = new Mock<ISupabaseStorageClient>();
        storage
            .Setup(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("storage 503"));
        var sut = BuildSut(db, storage.Object);

        var act = () => sut.DeleteAsync(me.SupabaseUserId, doc.Id);

        await act.Should().ThrowAsync<HttpRequestException>();
        (await db.Documents.CountAsync()).Should().Be(1, "storage failure must retain durable metadata for retry");
        storage.Verify(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task ListAsync_ApprovedSharedFolderOfOtherUser_ReturnsDocuments()
    {
        using var db = TestDb.CreateInMemoryWithDocuments();
        var me = SeedActiveStudent(db);
        var other = SeedActiveStudent(db);
        var folder = SeedFolder(db, other.Id);
        folder.ShareStatus = FolderStatus.Approved;
        db.SaveChanges();

        var doc = SeedDocument(db, other.Id, folderId: folder.Id);

        var sut = BuildSut(db, Mock.Of<ISupabaseStorageClient>());

        var result = await sut.ListAsync(me.SupabaseUserId, new DocumentListQuery { FolderId = folder.Id });
        result.Should().ContainSingle().Which.Id.Should().Be(doc.Id);
    }

    [Test]
    public async Task ListAsync_NonApprovedSharedFolderOfOtherUser_ExcludesDocuments()
    {
        using var db = TestDb.CreateInMemoryWithDocuments();
        var me = SeedActiveStudent(db);
        var other = SeedActiveStudent(db);
        var folder = SeedFolder(db, other.Id);
        folder.ShareStatus = FolderStatus.None;
        db.SaveChanges();

        var doc = SeedDocument(db, other.Id, folderId: folder.Id);

        var sut = BuildSut(db, Mock.Of<ISupabaseStorageClient>());

        var result = await sut.ListAsync(me.SupabaseUserId, new DocumentListQuery { FolderId = folder.Id });
        result.Should().BeEmpty();
    }

    [Test]
    public async Task GetByIdAsync_DocumentInApprovedSharedFolderOfOtherUser_ReturnsDto()
    {
        using var db = TestDb.CreateInMemoryWithDocuments();
        var me = SeedActiveStudent(db);
        var other = SeedActiveStudent(db);
        var folder = SeedFolder(db, other.Id);
        folder.ShareStatus = FolderStatus.Approved;
        db.SaveChanges();

        var doc = SeedDocument(db, other.Id, folderId: folder.Id);

        var storage = new Mock<ISupabaseStorageClient>();
        storage.Setup(s => s.CreateSignedUrlAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://storage.local/signed");
        var sut = BuildSut(db, storage.Object);

        var dto = await sut.GetByIdAsync(me.SupabaseUserId, doc.Id);
        dto.Id.Should().Be(doc.Id);
    }

    [Test]
    public async Task GetByIdAsync_DocumentInPrivateFolderOfOtherUser_Throws404()
    {
        using var db = TestDb.CreateInMemoryWithDocuments();
        var me = SeedActiveStudent(db);
        var other = SeedActiveStudent(db);
        var folder = SeedFolder(db, other.Id);
        folder.ShareStatus = FolderStatus.None;
        db.SaveChanges();

        var doc = SeedDocument(db, other.Id, folderId: folder.Id);

        var sut = BuildSut(db, Mock.Of<ISupabaseStorageClient>());

        var act = () => sut.GetByIdAsync(me.SupabaseUserId, doc.Id);
        await act.Should().ThrowAsync<DocumentException>().Where(ex => ex.StatusCode == 404);
    }
}
