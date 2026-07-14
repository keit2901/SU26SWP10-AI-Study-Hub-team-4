using AI_Study_Hub_v2.Data;
using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Services;
using AI_Study_Hub_v2.Services.Supabase;
using AI_Study_Hub_v2.Tests.Support;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AI_Study_Hub_v2.Tests.Services;

[TestFixture]
public sealed class StorageDeletionCoordinatorTests
{
    [Test]
    public async Task DeleteOwnedDocumentAsync_StorageFailure_RetainsRowAndQuota()
    {
        await using var db = TestDb.CreateInMemoryWithDocuments();
        var user = SeedUser(db, storageUsedBytes: 120);
        var document = SeedDocument(db, user.Id, 120);
        var storage = new Mock<ISupabaseStorageClient>(MockBehavior.Strict);
        storage.Setup(s => s.DeleteAsync(DocumentService.BucketName, document.StoragePath, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("storage unavailable"));
        var sut = new StorageDeletionCoordinator(db, storage.Object, NullLogger<StorageDeletionCoordinator>.Instance);

        var act = () => sut.DeleteOwnedDocumentAsync(document.Id, user.Id, CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>();
        (await db.Documents.CountAsync()).Should().Be(1);
        (await db.Users.SingleAsync()).StorageUsedBytes.Should().Be(120);
    }

    [Test]
    public async Task DeleteOwnedDocumentAsync_StorageRunsBeforeMetadataRemoval_AndSuccessChargesOnce()
    {
        await using var db = TestDb.CreateInMemoryWithDocuments();
        var user = SeedUser(db, storageUsedBytes: 200);
        var document = SeedDocument(db, user.Id, 200);
        var storage = new Mock<ISupabaseStorageClient>(MockBehavior.Strict);
        storage.Setup(s => s.DeleteAsync(DocumentService.BucketName, document.StoragePath, It.IsAny<CancellationToken>()))
            .Callback(() => db.Documents.Any(d => d.Id == document.Id).Should().BeTrue())
            .Returns(Task.CompletedTask);
        var sut = new StorageDeletionCoordinator(db, storage.Object, NullLogger<StorageDeletionCoordinator>.Instance);

        (await sut.DeleteOwnedDocumentAsync(document.Id, user.Id, CancellationToken.None)).Should().BeTrue();
        (await sut.DeleteOwnedDocumentAsync(document.Id, user.Id, CancellationToken.None)).Should().BeFalse();

        (await db.Documents.CountAsync()).Should().Be(0);
        (await db.Users.SingleAsync()).StorageUsedBytes.Should().Be(0);
        storage.Verify(s => s.DeleteAsync(DocumentService.BucketName, document.StoragePath, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task DeleteOwnedDocumentAsync_NotOwner_DoesNotInvokeStorage()
    {
        await using var db = TestDb.CreateInMemoryWithDocuments();
        var owner = SeedUser(db, 10);
        var other = SeedUser(db, 0);
        var document = SeedDocument(db, owner.Id, 10);
        var storage = new Mock<ISupabaseStorageClient>(MockBehavior.Strict);
        var sut = new StorageDeletionCoordinator(db, storage.Object, NullLogger<StorageDeletionCoordinator>.Instance);

        (await sut.DeleteOwnedDocumentAsync(document.Id, other.Id, CancellationToken.None)).Should().BeFalse();
        (await db.Documents.CountAsync()).Should().Be(1);
        storage.VerifyNoOtherCalls();
    }

    [Test]
    public async Task DeleteOwnedFolderAsync_SecondStorageFailure_RetainsFolderDocumentsAndQuota()
    {
        await using var db = TestDb.CreateInMemoryWithDocuments();
        var user = SeedUser(db, 300);
        var folder = SeedFolder(db, user.Id);
        var first = SeedDocument(db, user.Id, 100, folder.Id);
        var second = SeedDocument(db, user.Id, 200, folder.Id);
        var storage = new Mock<ISupabaseStorageClient>(MockBehavior.Strict);
        storage.Setup(s => s.DeleteAsync(DocumentService.BucketName, first.StoragePath, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        storage.Setup(s => s.DeleteAsync(DocumentService.BucketName, second.StoragePath, It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException("storage unavailable"));
        var sut = new StorageDeletionCoordinator(db, storage.Object, NullLogger<StorageDeletionCoordinator>.Instance);

        var act = () => sut.DeleteOwnedFolderAsync(folder.Id, user.Id, CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>();
        (await db.Folders.CountAsync()).Should().Be(1);
        (await db.Documents.CountAsync()).Should().Be(2);
        (await db.Users.SingleAsync()).StorageUsedBytes.Should().Be(300);
    }

    [Test]
    public async Task DeleteOwnedFolderAsync_Success_DeletesSnapshotDocumentsFolderAndAggregatesQuota()
    {
        await using var db = TestDb.CreateInMemoryWithDocuments();
        var user = SeedUser(db, 300);
        var folder = SeedFolder(db, user.Id);
        var first = SeedDocument(db, user.Id, 100, folder.Id);
        var second = SeedDocument(db, user.Id, 200, folder.Id);
        var storage = new Mock<ISupabaseStorageClient>(MockBehavior.Strict);
        storage.Setup(s => s.DeleteAsync(DocumentService.BucketName, It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var sut = new StorageDeletionCoordinator(db, storage.Object, NullLogger<StorageDeletionCoordinator>.Instance);

        (await sut.DeleteOwnedFolderAsync(folder.Id, user.Id, CancellationToken.None)).Should().BeTrue();

        (await db.Folders.CountAsync()).Should().Be(0);
        (await db.Documents.CountAsync()).Should().Be(0);
        (await db.Users.SingleAsync()).StorageUsedBytes.Should().Be(0);
        storage.Verify(s => s.DeleteAsync(DocumentService.BucketName, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Test]
    public async Task DeleteOwnedFolderAsync_RetryAfterStorageFailure_DeletesAndChargesOnce()
    {
        await using var db = TestDb.CreateInMemoryWithDocuments();
        var user = SeedUser(db, 100);
        var folder = SeedFolder(db, user.Id);
        var document = SeedDocument(db, user.Id, 100, folder.Id);
        var storage = new Mock<ISupabaseStorageClient>();
        storage.SetupSequence(s => s.DeleteAsync(DocumentService.BucketName, document.StoragePath, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException()).Returns(Task.CompletedTask);
        var sut = new StorageDeletionCoordinator(db, storage.Object, NullLogger<StorageDeletionCoordinator>.Instance);

        var act = () => sut.DeleteOwnedFolderAsync(folder.Id, user.Id, CancellationToken.None);
        await act.Should().ThrowAsync<HttpRequestException>();
        (await sut.DeleteOwnedFolderAsync(folder.Id, user.Id, CancellationToken.None)).Should().BeTrue();

        (await db.Users.SingleAsync()).StorageUsedBytes.Should().Be(0);
        (await db.Documents.CountAsync()).Should().Be(0);
    }

    [Test]
    public async Task DeleteOwnedFolderAsync_EmptyFolder_RemovesFolderWithoutStorageCall()
    {
        await using var db = TestDb.CreateInMemoryWithDocuments();
        var user = SeedUser(db, 0);
        var folder = SeedFolder(db, user.Id);
        var storage = new Mock<ISupabaseStorageClient>(MockBehavior.Strict);
        var sut = new StorageDeletionCoordinator(db, storage.Object, NullLogger<StorageDeletionCoordinator>.Instance);

        (await sut.DeleteOwnedFolderAsync(folder.Id, user.Id, CancellationToken.None)).Should().BeTrue();
        (await db.Folders.CountAsync()).Should().Be(0);
        storage.VerifyNoOtherCalls();
    }

    [Test]
    public async Task DeleteOwnedFolderAsync_ForeignDocumentReferencingFolder_IsNotDeletedOrSentToStorage()
    {
        await using var db = TestDb.CreateInMemoryWithDocuments();
        var owner = SeedUser(db, 100);
        var foreignOwner = SeedUser(db, 50);
        var folder = SeedFolder(db, owner.Id);
        var ownedDocument = SeedDocument(db, owner.Id, 100, folder.Id);
        var foreignDocument = SeedDocument(db, foreignOwner.Id, 50, folder.Id);
        var storage = new Mock<ISupabaseStorageClient>(MockBehavior.Strict);
        storage.Setup(s => s.DeleteAsync(DocumentService.BucketName, ownedDocument.StoragePath, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var sut = new StorageDeletionCoordinator(db, storage.Object, NullLogger<StorageDeletionCoordinator>.Instance);

        (await sut.DeleteOwnedFolderAsync(folder.Id, owner.Id, CancellationToken.None)).Should().BeTrue();

        (await db.Documents.AnyAsync(d => d.Id == ownedDocument.Id)).Should().BeFalse();
        var retainedForeignDocument = await db.Documents.SingleAsync(d => d.Id == foreignDocument.Id);
        retainedForeignDocument.FolderId.Should().BeNull();
        (await db.Users.SingleAsync(u => u.Id == owner.Id)).StorageUsedBytes.Should().Be(0);
        (await db.Users.SingleAsync(u => u.Id == foreignOwner.Id)).StorageUsedBytes.Should().Be(50);
        storage.Verify(s => s.DeleteAsync(DocumentService.BucketName, foreignDocument.StoragePath, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task DeleteOwnedDocumentAsync_CancelledAfterStorage_RetainsDocumentAndQuota()
    {
        await using var db = TestDb.CreateInMemoryWithDocuments();
        var user = SeedUser(db, 100);
        var document = SeedDocument(db, user.Id, 100);
        using var cts = new CancellationTokenSource();
        var storage = new Mock<ISupabaseStorageClient>(MockBehavior.Strict);
        storage.Setup(s => s.DeleteAsync(DocumentService.BucketName, document.StoragePath, It.IsAny<CancellationToken>()))
            .Callback(() => cts.Cancel())
            .Returns(Task.CompletedTask);
        var sut = new StorageDeletionCoordinator(db, storage.Object, NullLogger<StorageDeletionCoordinator>.Instance);

        var act = () => sut.DeleteOwnedDocumentAsync(document.Id, user.Id, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        (await db.Documents.CountAsync()).Should().Be(1);
        (await db.Users.SingleAsync()).StorageUsedBytes.Should().Be(100);
    }

    private static User SeedUser(AppDbContext db, long storageUsedBytes) {
        var user = new User { Id = Guid.NewGuid(), RoleId = 2, SupabaseUserId = Guid.NewGuid(), Username = Guid.NewGuid().ToString("N"), IsActive = true, StorageUsedBytes = storageUsedBytes, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        db.Users.Add(user); db.SaveChanges(); return user;
    }

    private static Folder SeedFolder(AppDbContext db, Guid userId) {
        var folder = new Folder { Id = Guid.NewGuid(), UserId = userId, Name = Guid.NewGuid().ToString("N"), CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        db.Folders.Add(folder); db.SaveChanges(); return folder;
    }

    private static Document SeedDocument(AppDbContext db, Guid userId, long size, Guid? folderId = null) {
        var document = new Document { Id = Guid.NewGuid(), UserId = userId, FolderId = folderId, FileName = "test.pdf", StoragePath = $"test/{Guid.NewGuid():N}", FileSizeBytes = size, MimeType = "application/pdf", SubjectCode = "SWP391", Semester = "SU26", Status = DocumentStatus.Ready, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        db.Documents.Add(document); db.SaveChanges(); return document;
    }
}
