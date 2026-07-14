using System.Text.Json;
using AI_Study_Hub_v2.Data;
using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Services;
using AI_Study_Hub_v2.Services.Supabase;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Pgvector;

namespace AI_Study_Hub_v2.Tests.Services;

[TestFixture]
public sealed class SharedFolderCopyCoordinatorTests
{
    [Test]
    public async Task CopyAsync_PrivateSource_ReturnsNonDisclosing404()
    {
        await using var env = new CopyEnvironment();
        var destination = env.AddUser();
        var source = env.AddFolder(env.AddUser().Id, FolderStatus.None);

        var error = await FluentActions.Awaiting(() => env.Sut.CopyAsync(destination.SupabaseUserId, source.Id, default))
            .Should().ThrowAsync<DocumentException>();

        error.Which.StatusCode.Should().Be(404);
        error.Which.Code.Should().Be("folder_not_found");
        env.Storage.Calls.Should().BeEmpty();
    }

    [Test]
    public async Task CopyAsync_InactiveDestination_Returns403()
    {
        await using var env = new CopyEnvironment();
        var destination = env.AddUser(active: false);
        var source = env.AddFolder(env.AddUser().Id, FolderStatus.Approved);

        var error = await FluentActions.Awaiting(() => env.Sut.CopyAsync(destination.SupabaseUserId, source.Id, default))
            .Should().ThrowAsync<DocumentException>();

        error.Which.StatusCode.Should().Be(403);
        error.Which.Code.Should().Be("user_inactive");
        env.Storage.Calls.Should().BeEmpty();
    }

    [Test]
    public async Task CopyAsync_InactiveDestination_RecoversCompensationThenRejectsNewCopy()
    {
        await using var env = new CopyEnvironment();
        var destination = env.AddUser(active: false, storageUsed: 19);
        var source = env.AddFolder(env.AddUser().Id, FolderStatus.Approved);
        env.AddOperation(destination.Id, Guid.NewGuid(), SharedFolderCopyOperation.CompensationRequired, 19, DateTimeOffset.UtcNow, paths: ["stale/object"]);

        var error = await FluentActions.Awaiting(() => env.Sut.CopyAsync(destination.SupabaseUserId, source.Id, default)).Should().ThrowAsync<DocumentException>();

        error.Which.StatusCode.Should().Be(403);
        env.Storage.DeletePaths.Should().Contain("stale/object");
        env.AssertReservationReleased(destination.Id, 0);
    }

    [Test]
    public async Task CopyAsync_InactiveDestination_WithCommittedStaleOperation_ReturnsExistingWithoutRelease()
    {
        await using var env = new CopyEnvironment();
        var destination = env.AddUser(active: false, storageUsed: 19);
        var source = env.AddFolder(env.AddUser().Id, FolderStatus.Approved);
        var committed = env.AddFolder(destination.Id, FolderStatus.None);
        env.AddOperation(destination.Id, source.Id, SharedFolderCopyOperation.CompensationRequired, 19, DateTimeOffset.UtcNow, committed.Id);

        var result = await env.Sut.CopyAsync(destination.SupabaseUserId, source.Id, default);

        result.Id.Should().Be(committed.Id);
        env.InFresh(db => { db.SharedFolderCopyOperations.Should().BeEmpty(); db.Users.Single(user => user.Id == destination.Id).StorageUsedBytes.Should().Be(19); });
    }

    [Test]
    public async Task CopyAsync_CrossOwnerCorruptSource_IsRejectedBeforeStorage()
    {
        await using var env = new CopyEnvironment();
        var destination = env.AddUser();
        var source = env.AddFolder(env.AddUser().Id, FolderStatus.Approved);
        env.AddDocument(source, env.AddUser().Id, "corrupt.pdf", 11);

        var error = await FluentActions.Awaiting(() => env.Sut.CopyAsync(destination.SupabaseUserId, source.Id, default))
            .Should().ThrowAsync<DocumentException>();

        error.Which.Code.Should().Be("folder_copy_conflict");
        env.Storage.Calls.Should().BeEmpty();
        env.InFresh(db => db.SharedFolderCopyOperations.Should().BeEmpty());
    }

    [Test]
    public async Task CopyAsync_ReservesExactBytesAndPersistsOperationBeforeFirstStorageCall()
    {
        await using var env = new CopyEnvironment();
        var destination = env.AddUser(storageUsed: 7);
        var source = env.AddFolder(env.AddUser().Id, FolderStatus.Approved);
        env.AddDocument(source, source.UserId, "one.pdf", 11);
        env.AddDocument(source, source.UserId, "two.pdf", 31);
        env.Storage.OnDownload = _ => env.InFresh(db =>
        {
            db.Users.Single(user => user.Id == destination.Id).StorageUsedBytes.Should().Be(49);
            db.SharedFolderCopyOperations.Should().ContainSingle(operation =>
                operation.DestinationUserId == destination.Id && operation.ReservedStorageBytes == 42 && operation.Status == SharedFolderCopyOperation.Copying);
        });

        await env.Sut.CopyAsync(destination.SupabaseUserId, source.Id, default);

        env.Storage.Calls.First().Kind.Should().Be(StorageCallKind.Download);
        env.InFresh(db => db.Users.Single(user => user.Id == destination.Id).StorageUsedBytes.Should().Be(49));
    }

    [Test]
    public async Task CopyAsync_EmptyFolder_SucceedsWithoutStorageAndRemovesOperationWithZeroCharge()
    {
        await using var env = new CopyEnvironment();
        var destination = env.AddUser(storageUsed: 9);
        var source = env.AddFolder(env.AddUser().Id, FolderStatus.Approved);

        var result = await env.Sut.CopyAsync(destination.SupabaseUserId, source.Id, default);

        result.DocumentCount.Should().Be(0);
        env.Storage.Calls.Should().BeEmpty();
        env.InFresh(db =>
        {
            db.Folders.Should().ContainSingle(folder => folder.Id == result.Id && folder.UserId == destination.Id);
            db.SharedFolderCopyOperations.Should().BeEmpty();
            db.Users.Single(user => user.Id == destination.Id).StorageUsedBytes.Should().Be(9);
        });
    }

    [Test]
    public async Task CopyAsync_HappyPath_CreatesFolderDocumentsChunksAndRetainsExactCharge()
    {
        await using var env = new CopyEnvironment();
        var destination = env.AddUser(storageUsed: 5);
        var source = env.SeedSharedFolderWithDocumentsAndChunks(2, 13, 17);

        var result = await env.Sut.CopyAsync(destination.SupabaseUserId, source.Id, default);

        env.Storage.UploadPaths.Should().HaveCount(2);
        env.InFresh(db =>
        {
            db.Folders.Single(folder => folder.Id == result.Id && folder.UserId == destination.Id).ShareStatus.Should().Be(FolderStatus.None);
            db.Documents.Count(document => document.FolderId == result.Id).Should().Be(2);
            db.DocumentChunks.Count(chunk => db.Documents.Any(document => document.Id == chunk.DocumentId && document.FolderId == result.Id)).Should().Be(2);
            db.SharedFolderCopyOperations.Should().BeEmpty();
            db.Users.Single(user => user.Id == destination.Id).StorageUsedBytes.Should().Be(35);
        });
    }

    [Test]
    public async Task CopyAsync_PreservesDocumentAndChunkFieldsWithIndependentVectorValue()
    {
        await using var env = new CopyEnvironment();
        var destination = env.AddUser();
        var source = env.AddFolder(env.AddUser().Id, FolderStatus.Approved);
        var document = env.AddDocument(source, source.UserId, "lecture.pdf", 29, pageCount: 12, status: DocumentStatus.Ready);
        var created = DateTimeOffset.UtcNow.AddDays(-2);
        var chunk = env.AddChunk(document, 3, 7, "precise chunk", 99, "model-x", new[] { 1f, 2f, 3f }, created);

        var result = await env.Sut.CopyAsync(destination.SupabaseUserId, source.Id, default);

        env.InFresh(db =>
        {
            var copied = db.Documents.Single(item => item.FolderId == result.Id);
            copied.FileName.Should().Be(document.FileName); copied.FileSizeBytes.Should().Be(document.FileSizeBytes);
            copied.MimeType.Should().Be(document.MimeType); copied.SubjectCode.Should().Be(document.SubjectCode);
            copied.Semester.Should().Be(document.Semester); copied.PageCount.Should().Be(document.PageCount);
            copied.Status.Should().Be(document.Status); copied.ReviewStatus.Should().Be(DocumentReviewStatus.None);
            copied.ErrorMessage.Should().Be(document.ErrorMessage); copied.CreatedAt.Should().Be(result.CreatedAt); copied.UpdatedAt.Should().Be(result.CreatedAt);
            copied.StoragePath.Should().NotBe(document.StoragePath);
            var copiedChunk = db.DocumentChunks.Single(item => item.DocumentId == copied.Id);
            copiedChunk.ChunkIndex.Should().Be(chunk.ChunkIndex); copiedChunk.PageNumber.Should().Be(chunk.PageNumber);
            copiedChunk.Content.Should().Be(chunk.Content); copiedChunk.TokenCount.Should().Be(chunk.TokenCount);
            copiedChunk.EmbeddingModel.Should().Be(chunk.EmbeddingModel); copiedChunk.CreatedAt.Should().Be(result.CreatedAt);
            copiedChunk.Embedding.ToArray().Should().Equal(chunk.Embedding.ToArray());
            ReferenceEquals(copiedChunk.Embedding, chunk.Embedding).Should().BeFalse();
        });
    }

    [Test]
    public async Task CopyAsync_FirstUploadAmbiguity_DeletesEveryPlannedDestinationPath()
    {
        await using var env = new CopyEnvironment();
        var destination = env.AddUser();
        var source = env.SeedSharedFolderWithDocuments(2, 10, 20);
        env.Storage.FailUploadNumber = 1;

        var error = await FluentActions.Awaiting(() => env.Sut.CopyAsync(destination.SupabaseUserId, source.Id, default))
            .Should().ThrowAsync<DocumentException>();

        error.Which.Code.Should().Be("folder_copy_failed");
        env.Storage.DeletePaths.Should().HaveCount(2);
        env.Storage.DeletePaths.Should().OnlyContain(path => path.StartsWith($"users/{destination.Id:N}/", StringComparison.Ordinal));
        env.AssertReservationReleased(destination.Id, 0);
    }

    [Test]
    public async Task CopyAsync_LaterUploadFailure_DeletesEveryPlannedDestinationPathNotOnlyUploaded()
    {
        await using var env = new CopyEnvironment();
        var destination = env.AddUser();
        var source = env.SeedSharedFolderWithDocuments(3, 10, 20, 30);
        env.Storage.FailUploadNumber = 2;

        await FluentActions.Awaiting(() => env.Sut.CopyAsync(destination.SupabaseUserId, source.Id, default))
            .Should().ThrowAsync<DocumentException>();

        env.Storage.UploadPaths.Should().HaveCount(2);
        env.Storage.DeletePaths.Should().HaveCount(3);
        env.AssertReservationReleased(destination.Id, 0);
    }

    [Test]
    public async Task CopyAsync_CleanupSuccess_ReleasesExactBytesAndRemovesOperationOnce()
    {
        await using var env = new CopyEnvironment();
        var destination = env.AddUser(storageUsed: 4);
        var source = env.SeedSharedFolderWithDocuments(1, 23);
        env.Storage.FailUploadNumber = 1;

        await FluentActions.Awaiting(() => env.Sut.CopyAsync(destination.SupabaseUserId, source.Id, default))
            .Should().ThrowAsync<DocumentException>();

        env.AssertReservationReleased(destination.Id, 4);
        env.Storage.DeleteCalls.Should().OnlyContain(call => !call.CancellationToken.CanBeCanceled);
    }

    [Test]
    public async Task CopyAsync_CleanupDeleteFailure_RetainsChargeAndOperationAndReturns503()
    {
        await using var env = new CopyEnvironment();
        var destination = env.AddUser(storageUsed: 3);
        var source = env.SeedSharedFolderWithDocuments(1, 23);
        env.Storage.FailUploadNumber = 1;
        env.Storage.FailDeleteNumber = 1;

        var error = await FluentActions.Awaiting(() => env.Sut.CopyAsync(destination.SupabaseUserId, source.Id, default))
            .Should().ThrowAsync<DocumentException>();

        error.Which.StatusCode.Should().Be(503); error.Which.Code.Should().Be("folder_copy_cleanup_pending");
        env.InFresh(db =>
        {
            db.Users.Single(user => user.Id == destination.Id).StorageUsedBytes.Should().Be(26);
            db.SharedFolderCopyOperations.Should().ContainSingle(operation => operation.Status == SharedFolderCopyOperation.CompensationRequired && operation.ReservedStorageBytes == 23);
        });
    }

    [Test]
    public async Task CopyAsync_CancelledDownload_UsesNonCancelledCleanupThenReleasesAndRethrows()
    {
        await using var env = new CopyEnvironment();
        var destination = env.AddUser(storageUsed: 1);
        var source = env.SeedSharedFolderWithDocuments(1, 8);
        using var cancelled = new CancellationTokenSource();
        env.Storage.OnDownload = _ => cancelled.Cancel();
        env.Storage.ThrowCancellationOnDownload = true;

        await FluentActions.Awaiting(() => env.Sut.CopyAsync(destination.SupabaseUserId, source.Id, cancelled.Token))
            .Should().ThrowAsync<OperationCanceledException>();

        env.Storage.DeleteCalls.Should().ContainSingle().Which.CancellationToken.CanBeCanceled.Should().BeFalse();
        env.AssertReservationReleased(destination.Id, 1);
    }

    [Test]
    public async Task CopyAsync_RecentActiveOperation_Returns409WithoutStorage()
    {
        await using var env = new CopyEnvironment();
        var destination = env.AddUser();
        var source = env.AddFolder(env.AddUser().Id, FolderStatus.Approved);
        env.AddOperation(destination.Id, source.Id, SharedFolderCopyOperation.Copying, 0, DateTimeOffset.UtcNow);

        var error = await FluentActions.Awaiting(() => env.Sut.CopyAsync(destination.SupabaseUserId, source.Id, default))
            .Should().ThrowAsync<DocumentException>();

        error.Which.StatusCode.Should().Be(409); error.Which.Code.Should().Be("folder_copy_in_progress");
        env.Storage.Calls.Should().BeEmpty();
    }

    [Test]
    public async Task CopyAsync_StaleOperation_CleansOldChargeThenContinuesWithOnlyNewCharge()
    {
        await using var env = new CopyEnvironment();
        var destination = env.AddUser(storageUsed: 50);
        var source = env.SeedSharedFolderWithDocuments(1, 9);
        env.AddOperation(destination.Id, Guid.NewGuid(), SharedFolderCopyOperation.Reserved, 30, DateTimeOffset.UtcNow.AddMinutes(-16), paths: ["stale/a"]);

        await env.Sut.CopyAsync(destination.SupabaseUserId, source.Id, default);

        env.Storage.DeletePaths.Should().Contain("stale/a");
        env.InFresh(db => { db.SharedFolderCopyOperations.Should().BeEmpty(); db.Users.Single(user => user.Id == destination.Id).StorageUsedBytes.Should().Be(29); });
    }

    [Test]
    public async Task CopyAsync_RecoveryCleanupFailure_Returns503AndRetainsOldChargeAndOperation()
    {
        await using var env = new CopyEnvironment();
        var destination = env.AddUser(storageUsed: 50);
        var source = env.AddFolder(env.AddUser().Id, FolderStatus.Approved);
        env.AddOperation(destination.Id, Guid.NewGuid(), SharedFolderCopyOperation.CompensationRequired, 30, DateTimeOffset.UtcNow, paths: ["stale/a"]);
        env.Storage.FailDeleteNumber = 1;

        var error = await FluentActions.Awaiting(() => env.Sut.CopyAsync(destination.SupabaseUserId, source.Id, default))
            .Should().ThrowAsync<DocumentException>();

        error.Which.StatusCode.Should().Be(503);
        env.InFresh(db => { db.Users.Single(user => user.Id == destination.Id).StorageUsedBytes.Should().Be(50); db.SharedFolderCopyOperations.Should().ContainSingle(operation => operation.Status == SharedFolderCopyOperation.CompensationRequired); });
    }

    [Test]
    public async Task CopyAsync_SourceApprovalRevokedDuringStorage_ReturnsConflictAndCompensates()
    {
        await using var env = new CopyEnvironment();
        var destination = env.AddUser(); var source = env.SeedSharedFolderWithDocuments(1, 12);
        env.Storage.OnUpload = _ => env.InFresh(db => { db.Folders.Single(folder => folder.Id == source.Id).ShareStatus = FolderStatus.None; db.SaveChanges(); });

        var error = await FluentActions.Awaiting(() => env.Sut.CopyAsync(destination.SupabaseUserId, source.Id, default))
            .Should().ThrowAsync<DocumentException>();

        error.Which.StatusCode.Should().Be(409); error.Which.Code.Should().Be("folder_copy_conflict");
        env.Storage.DeletePaths.Should().HaveCount(1); env.AssertReservationReleased(destination.Id, 0);
        env.InFresh(db => db.Folders.Count(folder => folder.UserId == destination.Id).Should().Be(0));
    }

    [Test]
    public async Task CopyAsync_SourceDocumentMutationDuringStorage_ReturnsConflictAndCompensates()
    {
        await using var env = new CopyEnvironment();
        var destination = env.AddUser(); var source = env.SeedSharedFolderWithDocuments(1, 12);
        env.Storage.OnUpload = _ => env.InFresh(db => { var document = db.Documents.Single(item => item.FolderId == source.Id); document.StoragePath = "changed/path"; document.UpdatedAt = document.UpdatedAt.AddSeconds(1); db.SaveChanges(); });

        var error = await FluentActions.Awaiting(() => env.Sut.CopyAsync(destination.SupabaseUserId, source.Id, default))
            .Should().ThrowAsync<DocumentException>();

        error.Which.Code.Should().Be("folder_copy_conflict"); env.Storage.DeletePaths.Should().HaveCount(1); env.AssertReservationReleased(destination.Id, 0);
    }

    [Test]
    public async Task CopyAsync_SourceChunkMutationDuringStorage_ReturnsConflictAndCompensates()
    {
        await using var env = new CopyEnvironment();
        var destination = env.AddUser(); var source = env.SeedSharedFolderWithDocuments(1, 12, chunks: true);
        env.Storage.OnUpload = _ => env.InFresh(db => { var chunk = db.DocumentChunks.Single(); chunk.Content = "changed"; chunk.EmbeddingModel = "changed-model"; chunk.Embedding = new Vector(new[] { 9f, 9f, 9f }); db.SaveChanges(); });

        var error = await FluentActions.Awaiting(() => env.Sut.CopyAsync(destination.SupabaseUserId, source.Id, default))
            .Should().ThrowAsync<DocumentException>();

        error.Which.Code.Should().Be("folder_copy_conflict"); env.Storage.DeletePaths.Should().HaveCount(1); env.AssertReservationReleased(destination.Id, 0);
    }

    [Test]
    public async Task CopyAsync_CapacityConsumedAfterReservation_CleansUpAndPreservesCapacityException()
    {
        await using var env = new CopyEnvironment(maxDocuments: 1);
        var destination = env.AddUser(); var source = env.SeedSharedFolderWithDocuments(1, 12);
        env.Storage.OnUpload = _ => env.InFresh(db => { db.Documents.Add(new Document { Id = Guid.NewGuid(), UserId = destination.Id, FileName = "other.pdf", StoragePath = "other", FileSizeBytes = 1, MimeType = "application/pdf", SubjectCode = "SWP", Semester = "SU", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow }); db.SaveChanges(); });

        var error = await FluentActions.Awaiting(() => env.Sut.CopyAsync(destination.SupabaseUserId, source.Id, default))
            .Should().ThrowAsync<PlanException>();

        error.Which.Code.Should().Be("document_count_exceeded"); env.Storage.DeletePaths.Should().HaveCount(1); env.AssertReservationReleased(destination.Id, 0);
        env.InFresh(db => db.Folders.Count(folder => folder.UserId == destination.Id).Should().Be(0));
    }

    [Test]
    public async Task CopyAsync_CommittedDestinationWithStaleOperation_ReturnsExistingWithoutRelease()
    {
        await using var env = new CopyEnvironment();
        var destination = env.AddUser(storageUsed: 44); var source = env.AddFolder(env.AddUser().Id, FolderStatus.Approved);
        var committed = env.AddFolder(destination.Id, FolderStatus.None);
        env.AddOperation(destination.Id, source.Id, SharedFolderCopyOperation.CompensationRequired, 20, DateTimeOffset.UtcNow, committed.Id);

        var result = await env.Sut.CopyAsync(destination.SupabaseUserId, source.Id, default);

        result.Id.Should().Be(committed.Id); env.Storage.Calls.Should().BeEmpty();
        env.InFresh(db => { db.SharedFolderCopyOperations.Should().BeEmpty(); db.Users.Single(user => user.Id == destination.Id).StorageUsedBytes.Should().Be(44); });
    }

    [Test]
    public async Task CopyAsync_RepeatedRecoveryDoesNotReleaseReservationTwice()
    {
        await using var env = new CopyEnvironment();
        var destination = env.AddUser(storageUsed: 40); var source = env.AddFolder(env.AddUser().Id, FolderStatus.Approved);
        env.AddOperation(destination.Id, Guid.NewGuid(), SharedFolderCopyOperation.CompensationRequired, 20, DateTimeOffset.UtcNow, paths: ["stale/a"]);

        await env.Sut.CopyAsync(destination.SupabaseUserId, source.Id, default);
        await env.Sut.CopyAsync(destination.SupabaseUserId, source.Id, default);

        env.InFresh(db => db.Users.Single(user => user.Id == destination.Id).StorageUsedBytes.Should().Be(20));
        env.Storage.DeletePaths.Count(path => path == "stale/a").Should().Be(1);
    }

    [Test]
    public async Task CopyAsync_NonCommittedFinalizationFailure_CompensatesAndMapsToFolderCopyFailed()
    {
        var fault = new FinalizationFaultInterceptor();
        await using var env = new CopyEnvironment(interceptor: fault);
        var destination = env.AddUser(); var source = env.SeedSharedFolderWithDocuments(1, 12);
        fault.Enabled = true;

        var error = await FluentActions.Awaiting(() => env.Sut.CopyAsync(destination.SupabaseUserId, source.Id, default))
            .Should().ThrowAsync<DocumentException>();

        error.Which.Code.Should().Be("folder_copy_failed");
        env.Storage.DeletePaths.Should().HaveCount(1);
        env.AssertReservationReleased(destination.Id, 0);
        env.InFresh(db => db.Folders.Count(folder => folder.UserId == destination.Id).Should().Be(0));
    }

    [Test]
    public async Task CopyAsync_NoDestinationAndMissingOperation_ReturnsAmbiguousWithoutSecondRelease()
    {
        var fault = new FinalizationFaultInterceptor();
        await using var env = new CopyEnvironment(interceptor: fault);
        var destination = env.AddUser(); var source = env.SeedSharedFolderWithDocuments(1, 12);
        fault.OnFolderAdd = () => env.InFresh(db => { db.SharedFolderCopyOperations.RemoveRange(db.SharedFolderCopyOperations); db.SaveChanges(); });
        fault.Enabled = true;

        var error = await FluentActions.Awaiting(() => env.Sut.CopyAsync(destination.SupabaseUserId, source.Id, default))
            .Should().ThrowAsync<DocumentException>();

        error.Which.Code.Should().Be("folder_copy_finalization_ambiguous");
        env.Storage.DeletePaths.Should().BeEmpty();
        env.InFresh(db => { db.SharedFolderCopyOperations.Should().BeEmpty(); db.Users.Single(user => user.Id == destination.Id).StorageUsedBytes.Should().Be(12); });
    }

    private sealed class CopyEnvironment : IAsyncDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly Plan _plan;
        public RecordingStorage Storage { get; } = new();
        public SharedFolderCopyCoordinator Sut { get; }

        public CopyEnvironment(int? maxDocuments = null, IInterceptor? interceptor = null)
        {
            var databaseName = $"shared-copy-{Guid.NewGuid():N}";
            _plan = new Plan { Id = Guid.NewGuid(), PlanKey = "test-free", DisplayName = "Test", StorageQuotaBytes = 1_000_000, MaxDocumentCount = maxDocuments ?? 100, MaxFolderCount = 100, MaxDocsPerFolder = 100, IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
            var plans = new Mock<IPlanService>(MockBehavior.Strict);
            plans.Setup(service => service.GetFreePlan()).Returns(_plan);
            var services = new ServiceCollection();
            var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(databaseName);
            if (interceptor is not null) options.AddInterceptors(interceptor);
            services.AddSingleton(options.Options);
            services.AddScoped<AppDbContext, TestCopyDbContext>();
            services.AddSingleton<IPlanService>(plans.Object);
            _provider = services.BuildServiceProvider(validateScopes: true);
            Sut = new SharedFolderCopyCoordinator(_provider.GetRequiredService<IServiceScopeFactory>(), Storage, new PlanCapacityGuard(plans.Object), NullLogger<SharedFolderCopyCoordinator>.Instance);
        }

        public User AddUser(bool active = true, long storageUsed = 0)
        {
            var user = new User { Id = Guid.NewGuid(), RoleId = 2, SupabaseUserId = Guid.NewGuid(), Username = Guid.NewGuid().ToString("N")[..12], IsActive = active, StorageUsedBytes = storageUsed, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
            InFresh(db => { db.Users.Add(user); db.SaveChanges(); }); return user;
        }

        public Folder AddFolder(Guid userId, FolderStatus status) { var folder = new Folder { Id = Guid.NewGuid(), UserId = userId, Name = $"folder-{Guid.NewGuid():N}"[..20], Description = "source description", Icon = "book", ShareStatus = status, CreatedAt = DateTimeOffset.UtcNow.AddDays(-1), UpdatedAt = DateTimeOffset.UtcNow.AddHours(-1) }; InFresh(db => { db.Folders.Add(folder); db.SaveChanges(); }); return folder; }
        public Document AddDocument(Folder folder, Guid ownerId, string name, long bytes, int? pageCount = 4, DocumentStatus status = DocumentStatus.Ready) { var document = new Document { Id = Guid.NewGuid(), UserId = ownerId, FolderId = folder.Id, FileName = name, StoragePath = $"source/{Guid.NewGuid():N}/{name}", FileSizeBytes = bytes, MimeType = "application/pdf", SubjectCode = "SWP391", Semester = "SU26", PageCount = pageCount, Status = status, ReviewStatus = DocumentReviewStatus.None, ErrorMessage = "original error", CreatedAt = DateTimeOffset.UtcNow.AddDays(-3), UpdatedAt = DateTimeOffset.UtcNow.AddDays(-2) }; InFresh(db => { db.Documents.Add(document); db.SaveChanges(); }); return document; }
        public DocumentChunk AddChunk(Document document, int index, int? page, string content, int? tokens, string? model, float[] vector, DateTimeOffset created) { var chunk = new DocumentChunk { Id = Guid.NewGuid(), DocumentId = document.Id, ChunkIndex = index, PageNumber = page, Content = content, TokenCount = tokens, Embedding = new Vector(vector), EmbeddingModel = model, CreatedAt = created }; InFresh(db => { db.DocumentChunks.Add(chunk); db.SaveChanges(); }); return chunk; }
        public Folder SeedSharedFolderWithDocuments(int count, params long[] sizes) => SeedSharedFolderWithDocuments(count, sizes, false);
        public Folder SeedSharedFolderWithDocumentsAndChunks(int count, params long[] sizes) => SeedSharedFolderWithDocuments(count, sizes, true);
        public Folder SeedSharedFolderWithDocuments(int count, long size, bool chunks) => SeedSharedFolderWithDocuments(count, [size], chunks);
        private Folder SeedSharedFolderWithDocuments(int count, long[] sizes, bool chunks) { var owner = AddUser(); var folder = AddFolder(owner.Id, FolderStatus.Approved); for (var index = 0; index < count; index++) { var document = AddDocument(folder, owner.Id, $"source-{index}.pdf", sizes[index]); if (chunks) AddChunk(document, index, index + 1, $"chunk-{index}", 10, "model", [1f, 2f, 3f], DateTimeOffset.UtcNow.AddDays(-1)); } return folder; }
        public void AddOperation(Guid userId, Guid sourceId, string status, long reserved, DateTimeOffset updated, Guid? destinationFolderId = null, string[]? paths = null) { var items = (paths ?? []).Select((path, index) => new { SourceDocumentId = Guid.NewGuid(), SourceStoragePath = $"source/{index}", DestinationDocumentId = Guid.NewGuid(), DestinationStoragePath = path }).ToArray(); var operation = new SharedFolderCopyOperation { Id = Guid.NewGuid(), DestinationUserId = userId, SourceFolderId = sourceId, DestinationFolderId = destinationFolderId ?? Guid.NewGuid(), DestinationName = "Recovery", Status = status, ReservedStorageBytes = reserved, ManifestJson = JsonSerializer.Serialize(new { Version = 1, Items = items }), CreatedAt = updated, UpdatedAt = updated }; InFresh(db => { db.SharedFolderCopyOperations.Add(operation); db.SaveChanges(); }); }
        public void AssertReservationReleased(Guid userId, long expectedStorage) => InFresh(db => { db.SharedFolderCopyOperations.Should().BeEmpty(); db.Users.Single(user => user.Id == userId).StorageUsedBytes.Should().Be(expectedStorage); });
        public void InFresh(Action<AppDbContext> assertion) { using var scope = _provider.CreateScope(); assertion(scope.ServiceProvider.GetRequiredService<AppDbContext>()); }
        public async ValueTask DisposeAsync() => await _provider.DisposeAsync();
    }

    private enum StorageCallKind { Download, Upload, Delete }
    private sealed record StorageCall(StorageCallKind Kind, string Path, CancellationToken CancellationToken);
    private sealed class RecordingStorage : ISupabaseStorageClient
    {
        public List<StorageCall> Calls { get; } = [];
        public IEnumerable<StorageCall> DeleteCalls => Calls.Where(call => call.Kind == StorageCallKind.Delete);
        public IReadOnlyList<string> UploadPaths => Calls.Where(call => call.Kind == StorageCallKind.Upload).Select(call => call.Path).ToArray();
        public IReadOnlyList<string> DeletePaths => Calls.Where(call => call.Kind == StorageCallKind.Delete).Select(call => call.Path).ToArray();
        public int? FailUploadNumber { get; set; }
        public int? FailDeleteNumber { get; set; }
        public bool ThrowCancellationOnDownload { get; set; }
        public Action<string>? OnDownload { get; set; }
        public Action<string>? OnUpload { get; set; }
        public Task<string> CreateSignedUrlAsync(string bucket, string objectPath, int ttlSeconds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<(Stream Content, string ContentType)> DownloadFileAsync(string bucket, string objectPath, CancellationToken cancellationToken = default)
        {
            Calls.Add(new(StorageCallKind.Download, objectPath, cancellationToken)); OnDownload?.Invoke(objectPath);
            if (ThrowCancellationOnDownload) throw new OperationCanceledException(cancellationToken);
            return Task.FromResult<(Stream, string)>((new MemoryStream([1, 2, 3]), "application/pdf"));
        }
        public Task<string> UploadAsync(string bucket, string objectPath, Stream content, string contentType, bool upsert = false, CancellationToken cancellationToken = default)
        {
            Calls.Add(new(StorageCallKind.Upload, objectPath, cancellationToken)); OnUpload?.Invoke(objectPath);
            if (FailUploadNumber == UploadPaths.Count) throw new InvalidOperationException("ambiguous upload failure");
            return Task.FromResult(objectPath);
        }
        public Task DeleteAsync(string bucket, string objectPath, CancellationToken cancellationToken = default)
        {
            Calls.Add(new(StorageCallKind.Delete, objectPath, cancellationToken));
            if (FailDeleteNumber == DeletePaths.Count) throw new InvalidOperationException("delete failed");
            return Task.CompletedTask;
        }
    }

    private sealed class TestCopyDbContext : AppDbContext
    {
        public TestCopyDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<DocumentChunk>().Property(chunk => chunk.Embedding)
                .HasConversion(vector => JsonSerializer.Serialize(vector.ToArray(), (JsonSerializerOptions?)null), values => new Vector(JsonSerializer.Deserialize<float[]>(values, (JsonSerializerOptions?)null)!));
        }
    }

    private sealed class FinalizationFaultInterceptor : SaveChangesInterceptor
    {
        public bool Enabled { get; set; }
        public Action? OnFolderAdd { get; set; }

        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            ThrowWhenArmed(eventData);
            return result;
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            ThrowWhenArmed(eventData);
            return ValueTask.FromResult(result);
        }

        private void ThrowWhenArmed(DbContextEventData eventData)
        {
            if (!Enabled || eventData.Context?.ChangeTracker.Entries<Folder>().Any(entry => entry.State == EntityState.Added) != true) return;
            OnFolderAdd?.Invoke();
            throw new InvalidOperationException("forced finalization failure");
        }
    }
}
