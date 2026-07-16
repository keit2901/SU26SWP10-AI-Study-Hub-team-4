using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using System.Text.Json;
using AI_Study_Hub_v2.Data;
using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Services;
using AI_Study_Hub_v2.Services.Supabase;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Npgsql;
using Pgvector;

namespace AI_Study_Hub_v2.Tests.Services;

[TestFixture, Category("Postgres"), NonParallelizable]
public sealed class SharedFolderCopyPostgresTests
{
    private const string PreReSyncMigration = "20260706184528_AddDocumentEscalation";
    private const string ReSyncPlanMigration = "20260709165701_ReSyncPlanFkAndConstraints";
    private NpgsqlDataSource? _dataSource;
    private readonly ConcurrentBag<Guid> _users = [];
    private readonly ConcurrentBag<Guid> _authUsers = [];

    [SetUp]
    public async Task SetUpAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable("AI_STUDY_HUB_TEST_POSTGRES") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(connectionString)) Assert.Ignore("AI_STUDY_HUB_TEST_POSTGRES is not configured.");
        var database = new NpgsqlConnectionStringBuilder(connectionString).Database;
        if (string.IsNullOrWhiteSpace(database) || !database.EndsWith("_test", StringComparison.OrdinalIgnoreCase))
            Assert.Ignore("Refusing shared-copy PostgreSQL tests outside a database ending in _test.");
        var builder = new NpgsqlDataSourceBuilder(connectionString);
        builder.MapEnum<DocumentStatus>(pgName: "public.document_status");
        builder.UseVector();
        _dataSource = builder.Build();
        await BootstrapAuthAsync();
        await using var db = CreateDb();
        await MigrateCompatibilityAsync(db);
        await ApplyFolderDriftColumnsAsync(db);
    }

    [TearDown]
    public async Task TearDownAsync()
    {
        try
        {
            if (_dataSource is null) return;
            await using var db = CreateDb();
            var users = _users.ToArray();
            var documents = await db.Documents.Where(item => users.Contains(item.UserId)).Select(item => item.Id).ToListAsync();
            db.DocumentEscalationItems.RemoveRange(await db.DocumentEscalationItems.Where(item => documents.Contains(item.DocumentId)).ToListAsync());
            db.DocumentChunks.RemoveRange(await db.DocumentChunks.Where(item => documents.Contains(item.DocumentId)).ToListAsync());
            db.SharedFolderCopyOperations.RemoveRange(await db.SharedFolderCopyOperations.Where(item => users.Contains(item.DestinationUserId)).ToListAsync());
            db.Documents.RemoveRange(await db.Documents.Where(item => users.Contains(item.UserId)).ToListAsync());
            db.DocumentEscalations.RemoveRange(await db.DocumentEscalations.Where(item => users.Contains(item.EscalatedByUserId)).ToListAsync());
            db.Folders.RemoveRange(await db.Folders.Where(item => users.Contains(item.UserId)).ToListAsync());
            db.Users.RemoveRange(await db.Users.Where(item => users.Contains(item.Id)).ToListAsync());
            await db.SaveChangesAsync();
            foreach (var authId in _authUsers) await DeleteAuthAsync(authId);
        }
        finally
        {
            if (_dataSource is not null) await _dataSource.DisposeAsync();
            _dataSource = null;
        }
    }

    [Test]
    public async Task OperationMigration_HasExpectedObjects_AndUniqueDestinationUser()
    {
        await using var db = CreateDb();
        var user = await SeedUserAsync(db);
        var table = await ScalarAsync("SELECT to_regclass('public.shared_folder_copy_operations') IS NOT NULL");
        var indexes = await ScalarAsync("SELECT count(*) = 3 FROM pg_indexes WHERE schemaname='public' AND tablename='shared_folder_copy_operations' AND indexname IN ('IX_shared_folder_copy_operations_destination_user_id', 'IX_shared_folder_copy_operations_status', 'IX_shared_folder_copy_operations_updated_at')");
        var constraint = await ScalarAsync("SELECT EXISTS (SELECT 1 FROM pg_constraint WHERE conname='ck_shared_folder_copy_operations_reserved_storage_non_negative')");
        table.Should().Be("True"); indexes.Should().Be("True"); constraint.Should().Be("True");
        db.SharedFolderCopyOperations.Add(NewOperation(user.Id, 1)); await db.SaveChangesAsync();
        db.SharedFolderCopyOperations.Add(NewOperation(user.Id, 1));
        await FluentActions.Awaiting(() => db.SaveChangesAsync()).Should().ThrowAsync<DbUpdateException>();
    }

    [Test]
    public async Task ReservationAndOperation_CommitAtomically()
    {
        await using var db = CreateDb(); var user = await SeedUserAsync(db);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await Guard().LockValidateAndReserveStorageAsync(db, user.Id, new PlanCapacityRequest(0, 0, null, 0), 41, default);
        db.SharedFolderCopyOperations.Add(NewOperation(user.Id, 41)); await db.SaveChangesAsync(); await transaction.CommitAsync();
        await AssertFreshAsync(user.Id, expectedBytes: 41, operations: 1);
    }

    [Test]
    public async Task ReservationRollback_LeavesNeitherOperationNorCharge()
    {
        await using var db = CreateDb(); var user = await SeedUserAsync(db);
        await using (var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            await Guard().LockValidateAndReserveStorageAsync(db, user.Id, new PlanCapacityRequest(0, 0, null, 0), 41, default);
            db.SharedFolderCopyOperations.Add(NewOperation(user.Id, 41)); await db.SaveChangesAsync(); await transaction.RollbackAsync();
        }
        await AssertFreshAsync(user.Id, expectedBytes: 0, operations: 0);
    }

    [Test]
    public async Task ConcurrentReservations_OneQuotaSlot_AllowsExactlyOne()
    {
        await using var seed = CreateDb(); var user = await SeedUserAsync(seed); var plan = FreePlan(quota: 10);
        var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = new[] { ReserveAsync(user.Id, plan, start.Task), ReserveAsync(user.Id, plan, start.Task) }; start.SetResult(true);
        var results = await Task.WhenAll(attempts);
        results.Count(item => item).Should().Be(1); await AssertFreshAsync(user.Id, expectedBytes: 10, operations: 1);
    }

    [Test]
    public async Task CoordinatorSuccess_CopiesMetadataChunksAndRetainsReservationCharge()
    {
        await using var db = CreateDb(); var sourceOwner = await SeedUserAsync(db); var destination = await SeedUserAsync(db);
        var source = await SeedSharedSourceAsync(db, sourceOwner.Id, 29, withChunk: true);
        var storage = new MemoryStorage(); storage.AddSource((await db.Documents.SingleAsync(item => item.FolderId == source.Id)).StoragePath);
        await using var host = CreateHost(storage, FreePlan());
        var result = await host.Coordinator.CopyAsync(destination.SupabaseUserId, source.Id, default);
        await using var fresh = CreateDb();
        var copied = await fresh.Documents.SingleAsync(item => item.FolderId == result.Id);
        var chunk = await fresh.DocumentChunks.SingleAsync(item => item.DocumentId == copied.Id);
        var expectedEmbedding = TestEmbedding();
        chunk.EmbeddingModel.Should().Be("pg-test");
        chunk.Embedding.ToArray().Should().Equal(expectedEmbedding);
        chunk.Embedding.ToArray()[0].Should().Be(1f);
        chunk.Embedding.ToArray()[DocumentChunk.EmbeddingDimension - 1].Should().Be(3f);
        (await fresh.Users.SingleAsync(item => item.Id == destination.Id)).StorageUsedBytes.Should().Be(29);
        (await fresh.SharedFolderCopyOperations.CountAsync(item => item.DestinationUserId == destination.Id)).Should().Be(0);
        storage.Destinations.Should().ContainSingle();
    }

    [Test]
    public async Task CapacityConsumedAfterStorage_CompensatesPathsMetadataAndCharge()
    {
        await using var db = CreateDb(); var owner = await SeedUserAsync(db); var destination = await SeedUserAsync(db);
        var source = await SeedSharedSourceAsync(db, owner.Id, 11); var storage = new MemoryStorage(); storage.AddSource((await db.Documents.SingleAsync(item => item.FolderId == source.Id)).StoragePath);
        storage.OnUpload = _ => { using var other = CreateDb(); other.Documents.Add(NewDocument(destination.Id, 1)); other.SaveChanges(); };
        await using var host = CreateHost(storage, FreePlan(maxDocuments: 1));
        await FluentActions.Awaiting(() => host.Coordinator.CopyAsync(destination.SupabaseUserId, source.Id, default)).Should().ThrowAsync<PlanException>();
        await AssertFreshAsync(destination.Id, expectedBytes: 0, operations: 0); storage.DeletedPaths.Should().HaveCount(1);
        await using var fresh = CreateDb(); (await fresh.Folders.CountAsync(item => item.UserId == destination.Id)).Should().Be(0);
    }

    [Test]
    public async Task ForcedFinalizationFailure_CompensatesAndLeavesNoMetadata()
    {
        await using var db = CreateDb(); var owner = await SeedUserAsync(db); var destination = await SeedUserAsync(db);
        var source = await SeedSharedSourceAsync(db, owner.Id, 12); var storage = new MemoryStorage(); storage.AddSource((await db.Documents.SingleAsync(item => item.FolderId == source.Id)).StoragePath);
        var fault = new FolderInsertFaultInterceptor(); await using var host = CreateHost(storage, FreePlan(), fault);
        var error = await FluentActions.Awaiting(() => host.Coordinator.CopyAsync(destination.SupabaseUserId, source.Id, default)).Should().ThrowAsync<DocumentException>();
        error.Which.Code.Should().Be("folder_copy_failed");
        await AssertFreshAsync(destination.Id, expectedBytes: 0, operations: 0); storage.DeletedPaths.Should().HaveCount(1);
    }

    [Test]
    public async Task CleanupFailure_PersistsCompensationRequired_ThenRetryReleasesOnce()
    {
        await using var db = CreateDb(); var owner = await SeedUserAsync(db); var destination = await SeedUserAsync(db);
        var source = await SeedSharedSourceAsync(db, owner.Id, 13); var storage = new MemoryStorage { FailUpload = true, FailDelete = true }; storage.AddSource((await db.Documents.SingleAsync(item => item.FolderId == source.Id)).StoragePath);
        await using var host = CreateHost(storage, FreePlan());
        await FluentActions.Awaiting(() => host.Coordinator.CopyAsync(destination.SupabaseUserId, source.Id, default)).Should().ThrowAsync<DocumentException>();
        await AssertFreshAsync(destination.Id, expectedBytes: 13, operations: 1, SharedFolderCopyOperation.CompensationRequired);
        storage.FailDelete = false;
        storage.FailUpload = false;
        await host.Coordinator.CopyAsync(destination.SupabaseUserId, source.Id, default);
        await AssertFreshAsync(destination.Id, expectedBytes: 13, operations: 0);
        await host.Coordinator.CopyAsync(destination.SupabaseUserId, source.Id, default);
        await AssertFreshAsync(destination.Id, expectedBytes: 26, operations: 0);
    }

    [Test]
    public async Task SourceMutationDuringStorage_ConflictsAndCompensates()
    {
        await using var db = CreateDb(); var owner = await SeedUserAsync(db); var destination = await SeedUserAsync(db);
        var source = await SeedSharedSourceAsync(db, owner.Id, 14, withChunk: true); var storage = new MemoryStorage(); storage.AddSource((await db.Documents.SingleAsync(item => item.FolderId == source.Id)).StoragePath);
        storage.OnUpload = _ => { using var other = CreateDb(); other.Folders.Single(item => item.Id == source.Id).ShareStatus = FolderStatus.None; other.SaveChanges(); };
        await using var host = CreateHost(storage, FreePlan());
        var error = await FluentActions.Awaiting(() => host.Coordinator.CopyAsync(destination.SupabaseUserId, source.Id, default)).Should().ThrowAsync<DocumentException>();
        error.Which.Code.Should().Be("folder_copy_conflict"); await AssertFreshAsync(destination.Id, 0, 0); storage.DeletedPaths.Should().HaveCount(1);
    }

    [Test]
    public async Task SourceDocumentMutationDuringStorage_ConflictsAndCompensates()
    {
        await using var db = CreateDb(); var owner = await SeedUserAsync(db); var destination = await SeedUserAsync(db);
        var source = await SeedSharedSourceAsync(db, owner.Id, 15); var sourceDocument = await db.Documents.SingleAsync(item => item.FolderId == source.Id); var storage = new MemoryStorage(); storage.AddSource(sourceDocument.StoragePath);
        storage.OnUpload = _ => { using var other = CreateDb(); var current = other.Documents.Single(item => item.Id == sourceDocument.Id); current.StoragePath = "mutated/source"; other.SaveChanges(); };
        await using var host = CreateHost(storage, FreePlan());
        var error = await FluentActions.Awaiting(() => host.Coordinator.CopyAsync(destination.SupabaseUserId, source.Id, default)).Should().ThrowAsync<DocumentException>();
        error.Which.Code.Should().Be("folder_copy_conflict"); await AssertFreshAsync(destination.Id, 0, 0); storage.DeletedPaths.Should().HaveCount(1);
    }

    [Test]
    public async Task SourceChunkMutationDuringStorage_ConflictsAndCompensates()
    {
        await using var db = CreateDb(); var owner = await SeedUserAsync(db); var destination = await SeedUserAsync(db);
        var source = await SeedSharedSourceAsync(db, owner.Id, 16, withChunk: true); var sourceDocument = await db.Documents.SingleAsync(item => item.FolderId == source.Id); var storage = new MemoryStorage(); storage.AddSource(sourceDocument.StoragePath);
        storage.OnUpload = _ => { using var other = CreateDb(); var current = other.DocumentChunks.Single(item => item.DocumentId == sourceDocument.Id); current.Content = "mutated chunk"; current.Embedding = new Vector(TestEmbedding(9f)); other.SaveChanges(); };
        await using var host = CreateHost(storage, FreePlan());
        var error = await FluentActions.Awaiting(() => host.Coordinator.CopyAsync(destination.SupabaseUserId, source.Id, default)).Should().ThrowAsync<DocumentException>();
        error.Which.Code.Should().Be("folder_copy_conflict"); await AssertFreshAsync(destination.Id, 0, 0); storage.DeletedPaths.Should().HaveCount(1);
    }

    [Test]
    public async Task RecoveryClaimsStaleCopying_OriginalCannotFinalizeOrDoubleRelease()
    {
        await using var db = CreateDb();
        var owner = await SeedUserAsync(db);
        var destination = await SeedUserAsync(db);
        var source = await SeedSharedSourceAsync(db, owner.Id, 31, withChunk: true);
        var sourceDocument = await db.Documents.SingleAsync(item => item.FolderId == source.Id);
        var storage = new MemoryStorage { PauseAfterDestinationUpload = true };
        storage.AddSource(sourceDocument.StoragePath);
        await using var originalHost = CreateHost(storage, FreePlan());

        var original = originalHost.Coordinator.CopyAsync(destination.SupabaseUserId, source.Id, default);
        await storage.DestinationUploadReached.Task.WaitAsync(TestTimeout);

        await using (var takeover = CreateDb())
        {
            var operation = await takeover.SharedFolderCopyOperations.SingleAsync(item => item.DestinationUserId == destination.Id).WaitAsync(TestTimeout);
            operation.Status.Should().Be(SharedFolderCopyOperation.Copying);
            operation.UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-16);
            takeover.Users.Single(item => item.Id == destination.Id).IsActive = false;
            await takeover.SaveChangesAsync().WaitAsync(TestTimeout);
        }

        await using var recoveryHost = CreateHost(storage, FreePlan());
        var recovery = recoveryHost.Coordinator.CopyAsync(destination.SupabaseUserId, source.Id, default);
        var recoveryError = await FluentActions.Awaiting(() => recovery).Should().ThrowAsync<DocumentException>().WaitAsync(TestTimeout);
        recoveryError.Which.Code.Should().Be("user_inactive");

        storage.ReleaseDestinationUpload();
        var originalError = await FluentActions.Awaiting(() => original).Should().ThrowAsync<DocumentException>().WaitAsync(TestTimeout);
        originalError.Which.Code.Should().Be("folder_copy_in_progress");

        await using var fresh = CreateDb();
        (await fresh.SharedFolderCopyOperations.CountAsync(item => item.DestinationUserId == destination.Id).WaitAsync(TestTimeout)).Should().Be(0);
        (await fresh.Folders.CountAsync(item => item.UserId == destination.Id).WaitAsync(TestTimeout)).Should().Be(0);
        (await fresh.Documents.CountAsync(item => item.UserId == destination.Id).WaitAsync(TestTimeout)).Should().Be(0);
        (await fresh.DocumentChunks.CountAsync(chunk => fresh.Documents.Any(document => document.Id == chunk.DocumentId && document.UserId == destination.Id)).WaitAsync(TestTimeout)).Should().Be(0);
        (await fresh.Users.SingleAsync(item => item.Id == destination.Id).WaitAsync(TestTimeout)).StorageUsedBytes.Should().Be(0);
        storage.Destinations.Should().BeEmpty();
        storage.DeletedPaths.Should().ContainSingle();
    }

    [Test]
    public async Task FinalizingClaimWins_RecoveryCannotDeleteCommittedObjects()
    {
        await using var db = CreateDb();
        var owner = await SeedUserAsync(db);
        var destination = await SeedUserAsync(db);
        var source = await SeedSharedSourceAsync(db, owner.Id, 37, withChunk: true);
        var sourceDocument = await db.Documents.SingleAsync(item => item.FolderId == source.Id);
        var storage = new MemoryStorage();
        storage.AddSource(sourceDocument.StoragePath);
        var finalizationBarrier = new FinalizationBarrierInterceptor(destination.Id);
        await using var originalHost = CreateHost(storage, FreePlan(), finalizationBarrier);

        var original = originalHost.Coordinator.CopyAsync(destination.SupabaseUserId, source.Id, default);
        await finalizationBarrier.DestinationFolderAdded.Task.WaitAsync(TestTimeout);

        await using var recoveryHost = CreateHost(storage, FreePlan());
        var recovery = recoveryHost.Coordinator.CopyAsync(destination.SupabaseUserId, source.Id, default);
        var recoveryError = await FluentActions.Awaiting(() => recovery).Should().ThrowAsync<DocumentException>().WaitAsync(TestTimeout);
        recoveryError.Which.Code.Should().Be("folder_copy_in_progress");
        storage.DeletedPaths.Should().BeEmpty();

        finalizationBarrier.Release();
        var result = await original.WaitAsync(TestTimeout);

        await using var fresh = CreateDb();
        (await fresh.SharedFolderCopyOperations.CountAsync(item => item.DestinationUserId == destination.Id).WaitAsync(TestTimeout)).Should().Be(0);
        var copiedFolder = await fresh.Folders.SingleAsync(item => item.Id == result.Id && item.UserId == destination.Id).WaitAsync(TestTimeout);
        copiedFolder.ShareStatus.Should().Be(FolderStatus.None);
        var copiedDocument = await fresh.Documents.SingleAsync(item => item.FolderId == result.Id).WaitAsync(TestTimeout);
        (await fresh.DocumentChunks.CountAsync(item => item.DocumentId == copiedDocument.Id).WaitAsync(TestTimeout)).Should().Be(1);
        (await fresh.Users.SingleAsync(item => item.Id == destination.Id).WaitAsync(TestTimeout)).StorageUsedBytes.Should().Be(37);
        storage.Destinations.Should().ContainSingle();
        storage.DeletedPaths.Should().BeEmpty();
    }

    [Test]
    public async Task ReconciliationVersusReservation_SerializesWithoutErasingCharge()
    {
        await using var db = CreateDb();
        var destination = await SeedUserAsync(db);
        var reconciliationBarrier = new ReconciliationUserLockBarrierInterceptor();
        await using var reconciliationHost = CreateHost(new MemoryStorage(), FreePlan(), reconciliationBarrier);

        var reconciliation = reconciliationHost.Reconciliation.ReconcileUserAsync(destination.Id, default);
        await reconciliationBarrier.UserLocked.Task.WaitAsync(TestTimeout);

        var reservationAttempted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reservation = ReserveOperationAfterLockAttemptAsync(destination.Id, 41, reservationAttempted);
        await reservationAttempted.Task.WaitAsync(TestTimeout);
        try
        {
            await AssertBlockedAsync(reservation);
        }
        finally
        {
            reconciliationBarrier.Release();
        }

        await reconciliation.WaitAsync(TestTimeout);
        await reservation.WaitAsync(TestTimeout);

        await using var fresh = CreateDb();
        (await fresh.Users.SingleAsync(item => item.Id == destination.Id).WaitAsync(TestTimeout)).StorageUsedBytes.Should().Be(41);
        (await fresh.SharedFolderCopyOperations.CountAsync(item => item.DestinationUserId == destination.Id).WaitAsync(TestTimeout)).Should().Be(1);
    }

    [Test]
    public async Task ReconciliationVersusFinalization_SeesOperationOrDocumentsNeverNeither()
    {
        await using var db = CreateDb();
        var owner = await SeedUserAsync(db);
        var destination = await SeedUserAsync(db);
        var source = await SeedSharedSourceAsync(db, owner.Id, 43, withChunk: true);
        var sourceDocument = await db.Documents.SingleAsync(item => item.FolderId == source.Id);
        var storage = new MemoryStorage();
        storage.AddSource(sourceDocument.StoragePath);
        var finalizationBarrier = new FinalizationBarrierInterceptor(destination.Id);
        await using var copyHost = CreateHost(storage, FreePlan(), finalizationBarrier);
        await using var reconciliationHost = CreateHost(storage, FreePlan());

        var copy = copyHost.Coordinator.CopyAsync(destination.SupabaseUserId, source.Id, default);
        await finalizationBarrier.DestinationFolderAdded.Task.WaitAsync(TestTimeout);
        var reconciliation = reconciliationHost.Reconciliation.ReconcileUserAsync(destination.Id, default);
        try
        {
            await AssertBlockedAsync(reconciliation);
        }
        finally
        {
            finalizationBarrier.Release();
        }

        var copied = await copy.WaitAsync(TestTimeout);
        await reconciliation.WaitAsync(TestTimeout);

        await using var fresh = CreateDb();
        (await fresh.SharedFolderCopyOperations.CountAsync(item => item.DestinationUserId == destination.Id).WaitAsync(TestTimeout)).Should().Be(0);
        (await fresh.Documents.CountAsync(item => item.FolderId == copied.Id && item.UserId == destination.Id).WaitAsync(TestTimeout)).Should().Be(1);
        (await fresh.Users.SingleAsync(item => item.Id == destination.Id).WaitAsync(TestTimeout)).StorageUsedBytes.Should().Be(43);
        storage.DeletedPaths.Should().BeEmpty();
    }

    [Test]
    public async Task ReconciliationVersusCompensationRelease_DoesNotRestoreReleasedBytes()
    {
        await using var db = CreateDb();
        var owner = await SeedUserAsync(db);
        var destination = await SeedUserAsync(db);
        var source = await SeedSharedSourceAsync(db, owner.Id, 47);
        var sourceDocument = await db.Documents.SingleAsync(item => item.FolderId == source.Id);
        var storage = new MemoryStorage { FailUpload = true, FailDelete = true };
        storage.AddSource(sourceDocument.StoragePath);
        await using (var failedCopyHost = CreateHost(storage, FreePlan()))
        {
            var failure = await FluentActions.Awaiting(() => failedCopyHost.Coordinator.CopyAsync(destination.SupabaseUserId, source.Id, default))
                .Should().ThrowAsync<DocumentException>().WaitAsync(TestTimeout);
            failure.Which.Code.Should().Be("folder_copy_cleanup_pending");
        }
        await AssertFreshAsync(destination.Id, 47, 1, SharedFolderCopyOperation.CompensationRequired);

        destination.IsActive = false;
        await db.SaveChangesAsync().WaitAsync(TestTimeout);
        storage.FailUpload = false;
        storage.FailDelete = false;
        var releaseBarrier = new CompensationReleaseBarrierInterceptor(destination.Id);
        await using var recoveryHost = CreateHost(storage, FreePlan(), releaseBarrier);
        await using var reconciliationHost = CreateHost(storage, FreePlan());

        var recovery = recoveryHost.Coordinator.CopyAsync(destination.SupabaseUserId, source.Id, default);
        await releaseBarrier.ReleaseSaveReached.Task.WaitAsync(TestTimeout);
        var reconciliation = reconciliationHost.Reconciliation.ReconcileUserAsync(destination.Id, default);
        try
        {
            await AssertBlockedAsync(reconciliation);
        }
        finally
        {
            releaseBarrier.Release();
        }

        var recoveryError = await FluentActions.Awaiting(() => recovery).Should().ThrowAsync<DocumentException>().WaitAsync(TestTimeout);
        recoveryError.Which.Code.Should().Be("user_inactive");
        await AwaitReconciliationCompletionAsync(reconciliation);

        await using var fresh = CreateDb();
        (await fresh.SharedFolderCopyOperations.CountAsync(item => item.DestinationUserId == destination.Id).WaitAsync(TestTimeout)).Should().Be(0);
        (await fresh.Documents.CountAsync(item => item.UserId == destination.Id).WaitAsync(TestTimeout)).Should().Be(0);
        (await fresh.Users.SingleAsync(item => item.Id == destination.Id).WaitAsync(TestTimeout)).StorageUsedBytes.Should().Be(0);
    }

    [Test]
    public async Task Reconciliation_CountsPendingReservation_AndFinalizedTotalStaysStable()
    {
        await using var db = CreateDb(); var user = await SeedUserAsync(db);
        db.SharedFolderCopyOperations.Add(NewOperation(user.Id, 22)); await db.SaveChangesAsync();
        await using var host = CreateHost(new MemoryStorage(), FreePlan());
        await host.Reconciliation.ReconcileUserAsync(user.Id, default); await AssertFreshAsync(user.Id, 22, 1);
        var operation = await db.SharedFolderCopyOperations.SingleAsync(); db.SharedFolderCopyOperations.Remove(operation); db.Documents.Add(NewDocument(user.Id, 22)); await db.SaveChangesAsync();
        await host.Reconciliation.ReconcileUserAsync(user.Id, default); await AssertFreshAsync(user.Id, 22, 0);
    }

    private async Task<bool> ReserveAsync(Guid userId, Plan plan, Task start)
    {
        await start; try { await using var db = CreateDb(); await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable); await new PlanCapacityGuard(Plans(plan).Object).LockValidateAndReserveStorageAsync(db, userId, new PlanCapacityRequest(0, 0, null, 0), 10, default); db.SharedFolderCopyOperations.Add(NewOperation(userId, 10)); await db.SaveChangesAsync(); await tx.CommitAsync(); return true; } catch { return false; }
    }

    private async Task ReserveOperationAfterLockAttemptAsync(Guid userId, long bytes, TaskCompletionSource<bool> lockAttempted)
    {
        await using var db = CreateDb();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable).WaitAsync(TestTimeout);
        lockAttempted.TrySetResult(true);
        await new PlanCapacityGuard(Plans(FreePlan()).Object)
            .LockValidateAndReserveStorageAsync(db, userId, new PlanCapacityRequest(0, 0, null, 0), bytes, default).WaitAsync(TestTimeout);
        db.SharedFolderCopyOperations.Add(NewOperation(userId, bytes));
        await db.SaveChangesAsync().WaitAsync(TestTimeout);
        await transaction.CommitAsync().WaitAsync(TestTimeout);
    }

    private static async Task AssertBlockedAsync(Task operation)
    {
        var completed = await Task.WhenAny(operation, Task.Delay(DiagnosticTimeout));
        completed.Should().NotBeSameAs(operation, "the user-row lock is held by the coordinated transaction");
        operation.IsCompleted.Should().BeFalse();
    }

    private static async Task AwaitReconciliationCompletionAsync(Task reconciliation)
    {
        try
        {
            await reconciliation.WaitAsync(TestTimeout);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.SerializationFailure)
        {
            // PostgreSQL can abort the stale reader after the release commits; it must never write the old charge back.
        }
    }

    private async Task<Folder> SeedSharedSourceAsync(AppDbContext db, Guid userId, long bytes, bool withChunk = false)
    {
        var folder = new Folder { Id = Guid.NewGuid(), UserId = userId, Name = $"shared-{Guid.NewGuid():N}", ShareStatus = FolderStatus.Approved, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        var document = NewDocument(userId, bytes, folder.Id); db.AddRange(folder, document);
        if (withChunk) db.DocumentChunks.Add(new DocumentChunk { Id = Guid.NewGuid(), DocumentId = document.Id, ChunkIndex = 0, Content = "postgres chunk", TokenCount = 3, Embedding = new Vector(TestEmbedding()), EmbeddingModel = "pg-test", CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync(); return folder;
    }

    private async Task<User> SeedUserAsync(AppDbContext db)
    {
        var authId = Guid.NewGuid(); var user = new User { Id = Guid.NewGuid(), SupabaseUserId = authId, RoleId = 2, Username = $"p{Guid.NewGuid():N}"[..14], IsActive = true, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        _users.Add(user.Id); _authUsers.Add(authId); await InsertAuthAsync(authId); db.Users.Add(user); await db.SaveChangesAsync(); return user;
    }

    private Host CreateHost(MemoryStorage storage, Plan plan, params IInterceptor[] interceptors)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(options => { options.UseNpgsql(_dataSource!, options => options.UseVector()); options.ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning)); if (interceptors.Length > 0) options.AddInterceptors(interceptors); }, ServiceLifetime.Transient, ServiceLifetime.Transient);
        var plans = Plans(plan); services.AddSingleton<IPlanService>(plans.Object); services.AddSingleton<ISupabaseStorageClient>(storage); services.AddSingleton<IPlanCapacityGuard>(new PlanCapacityGuard(plans.Object));
        services.AddScoped<SharedFolderCopyCoordinator>(); services.AddScoped<IStorageReconciliationService, StorageReconciliationService>();
        var provider = services.BuildServiceProvider(validateScopes: true); var scope = provider.CreateScope();
        return new Host(provider, scope, scope.ServiceProvider.GetRequiredService<SharedFolderCopyCoordinator>(), scope.ServiceProvider.GetRequiredService<IStorageReconciliationService>());
    }

    private AppDbContext CreateDb() => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_dataSource!, options => options.UseVector()).ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning)).Options);
    private static Mock<IPlanService> Plans(Plan plan) { var mock = new Mock<IPlanService>(MockBehavior.Strict); mock.Setup(item => item.GetFreePlan()).Returns(plan); return mock; }
    private static IPlanCapacityGuard Guard() => new PlanCapacityGuard(Plans(FreePlan()).Object);
    private static Plan FreePlan(long? quota = null, int? maxDocuments = null) => new() { Id = Guid.NewGuid(), PlanKey = Guid.NewGuid().ToString("N"), DisplayName = "test", StorageQuotaBytes = quota, MaxDocumentCount = maxDocuments, MaxFolderCount = 100, MaxDocsPerFolder = 100, IsActive = true };
    private static float[] TestEmbedding(float firstValue = 1f) { var values = new float[DocumentChunk.EmbeddingDimension]; values[0] = firstValue; values[1] = 2f; values[^1] = 3f; return values; }
    private static Document NewDocument(Guid userId, long bytes, Guid? folderId = null) => new() { Id = Guid.NewGuid(), UserId = userId, FolderId = folderId, FileName = "copy.pdf", StoragePath = $"copy-source/{Guid.NewGuid():N}", FileSizeBytes = bytes, MimeType = "application/pdf", SubjectCode = "SWP391", Semester = "SU26", Status = DocumentStatus.Ready, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
    private static SharedFolderCopyOperation NewOperation(Guid userId, long bytes) => new() { Id = Guid.NewGuid(), DestinationUserId = userId, SourceFolderId = Guid.NewGuid(), DestinationFolderId = Guid.NewGuid(), DestinationName = "pending", ReservedStorageBytes = bytes, ManifestJson = JsonSerializer.Serialize(new { Version = 1, Items = Array.Empty<object>() }), CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
    private async Task AssertFreshAsync(Guid userId, long expectedBytes, int operations, string? status = null) { await using var fresh = CreateDb(); (await fresh.Users.SingleAsync(item => item.Id == userId)).StorageUsedBytes.Should().Be(expectedBytes); var query = fresh.SharedFolderCopyOperations.Where(item => item.DestinationUserId == userId); if (status is not null) query = query.Where(item => item.Status == status); (await query.CountAsync()).Should().Be(operations); }
    private async Task BootstrapAuthAsync() { await using var connection = await _dataSource!.OpenConnectionAsync(); await using var command = new NpgsqlCommand("CREATE SCHEMA IF NOT EXISTS auth; CREATE TABLE IF NOT EXISTS auth.users (id uuid PRIMARY KEY);", connection); await command.ExecuteNonQueryAsync(); }
    private async Task InsertAuthAsync(Guid id) { await using var connection = await _dataSource!.OpenConnectionAsync(); await using var command = new NpgsqlCommand("INSERT INTO auth.users (id) VALUES (@id)", connection); command.Parameters.AddWithValue("id", id); await command.ExecuteNonQueryAsync(); }
    private async Task DeleteAuthAsync(Guid id) { await using var connection = await _dataSource!.OpenConnectionAsync(); await using var command = new NpgsqlCommand("DELETE FROM auth.users WHERE id=@id", connection); command.Parameters.AddWithValue("id", id); await command.ExecuteNonQueryAsync(); }
    private async Task<string> ScalarAsync(string sql) { await using var connection = await _dataSource!.OpenConnectionAsync(); await using var command = new NpgsqlCommand(sql, connection); return (await command.ExecuteScalarAsync())?.ToString() ?? string.Empty; }
    private static async Task MigrateCompatibilityAsync(AppDbContext db) { var applied = await db.Database.GetAppliedMigrationsAsync(); if (!applied.Contains(ReSyncPlanMigration)) { if (!applied.Contains(PreReSyncMigration)) await db.Database.GetService<IMigrator>().MigrateAsync(PreReSyncMigration); await db.Database.ExecuteSqlRawAsync("ALTER TABLE IF EXISTS public.payment_transactions DROP CONSTRAINT IF EXISTS \"FK_payment_transactions_users_user_id\""); } await db.Database.MigrateAsync(); }
    private static Task ApplyFolderDriftColumnsAsync(AppDbContext db) => db.Database.ExecuteSqlRawAsync("ALTER TABLE public.folders ADD COLUMN IF NOT EXISTS share_review_source varchar(32), ADD COLUMN IF NOT EXISTS ai_review_reason varchar(2000), ADD COLUMN IF NOT EXISTS ai_review_confidence double precision, ADD COLUMN IF NOT EXISTS ai_review_failure_count integer NOT NULL DEFAULT 0, ADD COLUMN IF NOT EXISTS human_review_reason varchar(2000), ADD COLUMN IF NOT EXISTS requires_human_review boolean NOT NULL DEFAULT false, ADD COLUMN IF NOT EXISTS appeal_requested_at timestamp with time zone, ADD COLUMN IF NOT EXISTS appeal_message varchar(2000)");

    private sealed class Host(ServiceProvider provider, IServiceScope scope, SharedFolderCopyCoordinator coordinator, IStorageReconciliationService reconciliation) : IAsyncDisposable { public SharedFolderCopyCoordinator Coordinator { get; } = coordinator; public IStorageReconciliationService Reconciliation { get; } = reconciliation; public async ValueTask DisposeAsync() { scope.Dispose(); await provider.DisposeAsync(); } }
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan DiagnosticTimeout = TimeSpan.FromMilliseconds(250);

    private sealed class MemoryStorage : ISupabaseStorageClient
    {
        private readonly HashSet<string> _objects = []; private int _destinationUploadPaused;
        public List<string> DeletedPaths { get; } = []; public Action<string>? OnUpload { get; set; } public bool FailUpload { get; set; } public bool FailDelete { get; set; }
        public bool PauseAfterDestinationUpload { get; set; }
        public TaskCompletionSource<bool> DestinationUploadReached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private TaskCompletionSource<bool> DestinationUploadRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IReadOnlyCollection<string> Destinations => _objects.Where(path => path.StartsWith("users/", StringComparison.Ordinal)).ToArray(); public void AddSource(string path) => _objects.Add(path);
        public Task<string> CreateSignedUrlAsync(string bucket, string objectPath, int ttlSeconds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<(Stream Content, string ContentType)> DownloadFileAsync(string bucket, string objectPath, CancellationToken cancellationToken = default) { if (!_objects.Contains(objectPath)) throw new FileNotFoundException(); return Task.FromResult<(Stream, string)>((new MemoryStream([1]), "application/pdf")); }
        public async Task<string> UploadAsync(string bucket, string objectPath, Stream content, string contentType, bool upsert = false, CancellationToken cancellationToken = default)
        {
            OnUpload?.Invoke(objectPath);
            if (FailUpload) throw new IOException("upload failure");
            _objects.Add(objectPath);
            if (PauseAfterDestinationUpload && Interlocked.CompareExchange(ref _destinationUploadPaused, 1, 0) == 0)
            {
                DestinationUploadReached.TrySetResult(true);
                await DestinationUploadRelease.Task.WaitAsync(TestTimeout);
            }
            return objectPath;
        }
        public Task DeleteAsync(string bucket, string objectPath, CancellationToken cancellationToken = default) { DeletedPaths.Add(objectPath); if (FailDelete) throw new IOException("delete failure"); _objects.Remove(objectPath); return Task.CompletedTask; }
        public void ReleaseDestinationUpload() => DestinationUploadRelease.TrySetResult(true);
    }

    private sealed class FinalizationBarrierInterceptor(Guid destinationUserId) : SaveChangesInterceptor
    {
        private int _paused;
        public TaskCompletionSource<bool> DestinationFolderAdded { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private TaskCompletionSource<bool> ReleaseFinalization { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context?.ChangeTracker.Entries<Folder>().Any(entry => entry.State == EntityState.Added
                && entry.Entity.UserId == destinationUserId) == true && Interlocked.CompareExchange(ref _paused, 1, 0) == 0)
            {
                DestinationFolderAdded.TrySetResult(true);
                await ReleaseFinalization.Task.WaitAsync(TestTimeout);
            }
            return result;
        }

        public void Release() => ReleaseFinalization.TrySetResult(true);
    }

    private sealed class ReconciliationUserLockBarrierInterceptor : DbCommandInterceptor
    {
        private int _paused;
        public TaskCompletionSource<bool> UserLocked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private TaskCompletionSource<bool> ReleaseUserLock { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            int result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FOR UPDATE", StringComparison.OrdinalIgnoreCase)
                && Interlocked.CompareExchange(ref _paused, 1, 0) == 0)
            {
                UserLocked.TrySetResult(true);
                await ReleaseUserLock.Task.WaitAsync(TestTimeout);
            }
            return result;
        }

        public void Release() => ReleaseUserLock.TrySetResult(true);
    }

    private sealed class CompensationReleaseBarrierInterceptor(Guid destinationUserId) : SaveChangesInterceptor
    {
        private int _paused;
        public TaskCompletionSource<bool> ReleaseSaveReached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private TaskCompletionSource<bool> ReleaseSave { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var entries = eventData.Context?.ChangeTracker;
            var removesOperation = entries?.Entries<SharedFolderCopyOperation>().Any(entry => entry.State == EntityState.Deleted) == true;
            var releasesUser = entries?.Entries<User>().Any(entry => entry.State == EntityState.Modified
                && entry.Entity.Id == destinationUserId && entry.Property(user => user.StorageUsedBytes).IsModified) == true;
            if (removesOperation && releasesUser && Interlocked.CompareExchange(ref _paused, 1, 0) == 0)
            {
                ReleaseSaveReached.TrySetResult(true);
                await ReleaseSave.Task.WaitAsync(TestTimeout);
            }
            return result;
        }

        public void Release() => ReleaseSave.TrySetResult(true);
    }
    private sealed class FolderInsertFaultInterceptor : SaveChangesInterceptor
    {
        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            ThrowIfDestinationFolderIsAdded(eventData);
            return result;
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            ThrowIfDestinationFolderIsAdded(eventData);
            return ValueTask.FromResult(result);
        }

        private static void ThrowIfDestinationFolderIsAdded(DbContextEventData eventData)
        {
            if (eventData.Context?.ChangeTracker.Entries<Folder>().Any(entry => entry.State == EntityState.Added) == true)
                throw new InvalidOperationException("forced finalization failure");
        }
    }
}
