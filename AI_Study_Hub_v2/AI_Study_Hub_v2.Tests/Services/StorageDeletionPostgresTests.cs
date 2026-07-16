using System.Data.Common;
using AI_Study_Hub_v2.Data;
using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Services;
using AI_Study_Hub_v2.Services.Supabase;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Npgsql;

namespace AI_Study_Hub_v2.Tests.Services;

[TestFixture, Category("Postgres"), NonParallelizable]
public sealed class StorageDeletionPostgresTests
{
    private string _connectionString = null!;
    private NpgsqlDataSource? _dataSource;
    private readonly List<Guid> _createdUserIds = [];
    private readonly List<Guid> _createdAuthUserIds = [];

    [SetUp]
    public async Task RequireDedicatedTestDatabase()
    {
        _connectionString = Environment.GetEnvironmentVariable("AI_STUDY_HUB_TEST_POSTGRES") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(_connectionString)) Assert.Ignore("AI_STUDY_HUB_TEST_POSTGRES is not configured.");
        var database = new NpgsqlConnectionStringBuilder(_connectionString).Database;
        if (string.IsNullOrWhiteSpace(database) || !database.EndsWith("_test", StringComparison.OrdinalIgnoreCase))
            Assert.Ignore("Refusing PostgreSQL deletion tests outside a database ending in _test.");
        var dataSourceBuilder = new NpgsqlDataSourceBuilder(_connectionString);
        dataSourceBuilder.MapEnum<DocumentStatus>(pgName: "public.document_status");
        dataSourceBuilder.UseVector();
        _dataSource = dataSourceBuilder.Build();
        await BootstrapAuthPrerequisiteAsync();
        await using var db = CreateDb();
        var pendingMigrations = await db.Database.GetPendingMigrationsAsync();
        if (pendingMigrations.Contains("20260709165701_ReSyncPlanFkAndConstraints"))
        {
            // The historical migration re-adds this named FK but only drops the
            // lowercase Supabase name. Isolate that compatibility repair to this
            // disposable test database; production migrations stay untouched.
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE IF EXISTS public.payment_transactions DROP CONSTRAINT IF EXISTS \"FK_payment_transactions_users_user_id\"");
        }
        await db.Database.MigrateAsync();
    }

    [TearDown]
    public async Task CleanCreatedRows()
    {
        try
        {
            if (_createdUserIds.Count > 0)
            {
                await using var db = CreateDb();
                var documents = await db.Documents.Where(d => _createdUserIds.Contains(d.UserId)).Select(d => d.Id).ToListAsync();
                db.DocumentEscalationItems.RemoveRange(await db.DocumentEscalationItems.Where(i => documents.Contains(i.DocumentId)).ToListAsync());
                db.DocumentEscalations.RemoveRange(await db.DocumentEscalations.Where(e => _createdUserIds.Contains(e.EscalatedByUserId)).ToListAsync());
                db.Documents.RemoveRange(await db.Documents.Where(d => _createdUserIds.Contains(d.UserId)).ToListAsync());
                db.Folders.RemoveRange(await db.Folders.Where(f => _createdUserIds.Contains(f.UserId)).ToListAsync());
                db.Users.RemoveRange(await db.Users.Where(u => _createdUserIds.Contains(u.Id)).ToListAsync());
                await db.SaveChangesAsync();
            }

            foreach (var authUserId in _createdAuthUserIds)
            {
                await using var connection = await (_dataSource ?? throw new InvalidOperationException("PostgreSQL data source is not initialized.")).OpenConnectionAsync();
                await using var command = new NpgsqlCommand("DELETE FROM auth.users WHERE id = @id", connection);
                command.Parameters.AddWithValue("id", authUserId);
                await command.ExecuteNonQueryAsync();
            }
        }
        finally
        {
            if (_dataSource is not null) await _dataSource.DisposeAsync();
            _dataSource = null;
        }
    }

    [Test]
    public async Task PrivilegedDelete_RemovesDocumentChunksEscalationAndExactQuota()
    {
        await using var db = CreateDb();
        var user = await SeedUserAsync(db, 123);
        var folder = await SeedFolderAsync(db, user.Id);
        var document = await SeedDocumentAsync(db, user.Id, 123, folder.Id);
        db.DocumentChunks.Add(new DocumentChunk { Id = Guid.NewGuid(), DocumentId = document.Id, ChunkIndex = 0, Content = "x", Embedding = new Pgvector.Vector(new float[384]), CreatedAt = DateTimeOffset.UtcNow });
        await SeedEscalationItemAsync(db, user.Id, folder.Id, document.Id);
        var storage = SuccessStorage(document.StoragePath);

        (await new StorageDeletionCoordinator(db, storage.Object, NullLogger<StorageDeletionCoordinator>.Instance).DeletePrivilegedDocumentAsync(document.Id, default)).Should().BeTrue();

        await using var fresh = CreateDb();
        (await fresh.Documents.AnyAsync(d => d.Id == document.Id)).Should().BeFalse();
        (await fresh.DocumentChunks.AnyAsync(c => c.DocumentId == document.Id)).Should().BeFalse();
        (await fresh.DocumentEscalationItems.AnyAsync(i => i.DocumentId == document.Id)).Should().BeFalse();
        (await fresh.Users.SingleAsync(u => u.Id == user.Id)).StorageUsedBytes.Should().Be(0);
    }

    [Test]
    public async Task PrivilegedDelete_QuotaSqlFailure_RollsBackDocumentEscalationAndQuota()
    {
        Guid documentId;
        Guid escalationItemId;
        Guid userId;
        await using (var seed = CreateDb())
        {
            var user = await SeedUserAsync(seed, 123);
            userId = user.Id;
            var folder = await SeedFolderAsync(seed, user.Id);
            var document = await SeedDocumentAsync(seed, user.Id, 123, folder.Id);
            documentId = document.Id;
            escalationItemId = await SeedEscalationItemAsync(seed, user.Id, folder.Id, document.Id);
        }

        await using (var deleting = CreateDb(new ThrowOnQuotaUpdateInterceptor()))
        {
            var storage = SuccessStorage((await deleting.Documents.SingleAsync(d => d.Id == documentId)).StoragePath);
            var act = () => new StorageDeletionCoordinator(deleting, storage.Object, NullLogger<StorageDeletionCoordinator>.Instance)
                .DeletePrivilegedDocumentAsync(documentId, default);
            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("test quota update failure");
        }

        await using var fresh = CreateDb();
        (await fresh.Documents.AnyAsync(d => d.Id == documentId)).Should().BeTrue();
        (await fresh.DocumentEscalationItems.AnyAsync(i => i.Id == escalationItemId)).Should().BeTrue();
        (await fresh.Users.SingleAsync(u => u.Id == userId)).StorageUsedBytes.Should().Be(123);
    }

    [Test]
    public async Task FolderDelete_SnapshotWinsAndLateDocumentIsSetNull()
    {
        await using var db = CreateDb();
        var user = await SeedUserAsync(db, 350);
        var folder = await SeedFolderAsync(db, user.Id);
        var first = await SeedDocumentAsync(db, user.Id, 100, folder.Id);
        var second = await SeedDocumentAsync(db, user.Id, 200, folder.Id);
        var lateDocumentId = Guid.NewGuid();
        var lateInserted = false;
        var deletedPaths = new List<string>();
        var storage = new Mock<ISupabaseStorageClient>(MockBehavior.Strict);
        storage.Setup(s => s.DeleteAsync(DocumentService.BucketName, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((_, path, _) =>
            {
                deletedPaths.Add(path);
                if (lateInserted) return;
                lateInserted = true;
                using var lateDb = CreateDb();
                lateDb.Documents.Add(NewDocument(lateDocumentId, user.Id, 50, folder.Id));
                lateDb.SaveChanges();
            })
            .Returns(Task.CompletedTask);

        (await new StorageDeletionCoordinator(db, storage.Object, NullLogger<StorageDeletionCoordinator>.Instance).DeleteOwnedFolderAsync(folder.Id, user.Id, default)).Should().BeTrue();

        await using var fresh = CreateDb();
        (await fresh.Documents.AnyAsync(d => d.Id == first.Id || d.Id == second.Id)).Should().BeFalse();
        var late = await fresh.Documents.SingleAsync(d => d.Id == lateDocumentId);
        late.FolderId.Should().BeNull();
        (await fresh.Folders.AnyAsync(f => f.Id == folder.Id)).Should().BeFalse();
        (await fresh.Users.SingleAsync(u => u.Id == user.Id)).StorageUsedBytes.Should().Be(50);
        deletedPaths.Should().BeEquivalentTo([first.StoragePath, second.StoragePath]);
        storage.Verify(s => s.DeleteAsync(DocumentService.BucketName, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Test]
    public async Task FolderDelete_MovedSnapshotCandidate_IsStillDeletedAndChargedOnce()
    {
        await using var db = CreateDb();
        var user = await SeedUserAsync(db, 100);
        var source = await SeedFolderAsync(db, user.Id);
        var destination = await SeedFolderAsync(db, user.Id);
        var document = await SeedDocumentAsync(db, user.Id, 100, source.Id);
        var moved = false;
        var storage = new Mock<ISupabaseStorageClient>(MockBehavior.Strict);
        storage.Setup(s => s.DeleteAsync(DocumentService.BucketName, document.StoragePath, It.IsAny<CancellationToken>()))
            .Callback(() =>
            {
                if (moved) return;
                moved = true;
                using var movingDb = CreateDb();
                var current = movingDb.Documents.Single(d => d.Id == document.Id);
                current.FolderId = destination.Id;
                movingDb.SaveChanges();
            })
            .Returns(Task.CompletedTask);

        (await new StorageDeletionCoordinator(db, storage.Object, NullLogger<StorageDeletionCoordinator>.Instance).DeleteOwnedFolderAsync(source.Id, user.Id, default)).Should().BeTrue();

        await using var fresh = CreateDb();
        (await fresh.Documents.AnyAsync(d => d.Id == document.Id)).Should().BeFalse();
        (await fresh.Users.SingleAsync(u => u.Id == user.Id)).StorageUsedBytes.Should().Be(0);
        storage.Verify(s => s.DeleteAsync(DocumentService.BucketName, document.StoragePath, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task FolderDelete_ForeignDocumentReferencingFolder_IsDetachedWithoutStorageOrQuotaChange()
    {
        await using var db = CreateDb();
        var owner = await SeedUserAsync(db, 100);
        var foreignOwner = await SeedUserAsync(db, 50);
        var folder = await SeedFolderAsync(db, owner.Id);
        var ownedDocument = await SeedDocumentAsync(db, owner.Id, 100, folder.Id);
        var foreignDocument = await SeedDocumentAsync(db, foreignOwner.Id, 50, folder.Id);
        var storage = new Mock<ISupabaseStorageClient>(MockBehavior.Strict);
        storage.Setup(s => s.DeleteAsync(DocumentService.BucketName, ownedDocument.StoragePath, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        (await new StorageDeletionCoordinator(db, storage.Object, NullLogger<StorageDeletionCoordinator>.Instance).DeleteOwnedFolderAsync(folder.Id, owner.Id, default)).Should().BeTrue();

        await using var fresh = CreateDb();
        (await fresh.Documents.AnyAsync(d => d.Id == ownedDocument.Id)).Should().BeFalse();
        var retainedForeignDocument = await fresh.Documents.SingleAsync(d => d.Id == foreignDocument.Id);
        retainedForeignDocument.FolderId.Should().BeNull();
        (await fresh.Users.SingleAsync(u => u.Id == owner.Id)).StorageUsedBytes.Should().Be(0);
        (await fresh.Users.SingleAsync(u => u.Id == foreignOwner.Id)).StorageUsedBytes.Should().Be(50);
        storage.Verify(s => s.DeleteAsync(DocumentService.BucketName, foreignDocument.StoragePath, It.IsAny<CancellationToken>()), Times.Never);
    }

    private AppDbContext CreateDb(params IInterceptor[] interceptors)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_dataSource ?? throw new InvalidOperationException("PostgreSQL data source is not initialized."), o => o.UseVector());
        if (interceptors.Length > 0) options.AddInterceptors(interceptors);
        return new AppDbContext(options.Options);
    }

    private async Task<User> SeedUserAsync(AppDbContext db, long storageUsedBytes)
    {
        var userId = Guid.NewGuid();
        var authUserId = Guid.NewGuid();
        _createdUserIds.Add(userId);
        _createdAuthUserIds.Add(authUserId);
        await InsertAuthUserAsync(authUserId);
        var user = new User { Id = userId, RoleId = 2, SupabaseUserId = authUserId, Username = Guid.NewGuid().ToString("N")[..12], IsActive = true, StorageUsedBytes = storageUsedBytes, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        db.Users.Add(user); await db.SaveChangesAsync(); return user;
    }

    private async Task BootstrapAuthPrerequisiteAsync()
    {
        await using var connection = await (_dataSource ?? throw new InvalidOperationException("PostgreSQL data source is not initialized.")).OpenConnectionAsync();
        await using var command = new NpgsqlCommand("CREATE SCHEMA IF NOT EXISTS auth; CREATE TABLE IF NOT EXISTS auth.users (id uuid PRIMARY KEY);", connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task InsertAuthUserAsync(Guid authUserId)
    {
        await using var connection = await (_dataSource ?? throw new InvalidOperationException("PostgreSQL data source is not initialized.")).OpenConnectionAsync();
        await using var command = new NpgsqlCommand("INSERT INTO auth.users (id) VALUES (@id)", connection);
        command.Parameters.AddWithValue("id", authUserId);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<Document> SeedDocumentAsync(AppDbContext db, Guid userId, long bytes, Guid? folderId) { var document = NewDocument(Guid.NewGuid(), userId, bytes, folderId); db.Documents.Add(document); await db.SaveChangesAsync(); return document; }
    private static Document NewDocument(Guid id, Guid userId, long bytes, Guid? folderId) => new() { Id = id, UserId = userId, FolderId = folderId, FileName = "test.pdf", StoragePath = $"test/{Guid.NewGuid():N}", FileSizeBytes = bytes, MimeType = "application/pdf", SubjectCode = "SWP391", Semester = "SU26", Status = DocumentStatus.Ready, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
    private static async Task<Folder> SeedFolderAsync(AppDbContext db, Guid userId) { var folder = new Folder { Id = Guid.NewGuid(), UserId = userId, Name = Guid.NewGuid().ToString("N"), CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow }; db.Folders.Add(folder); await db.SaveChangesAsync(); return folder; }
    private static async Task<Guid> SeedEscalationItemAsync(AppDbContext db, Guid userId, Guid folderId, Guid documentId) { var escalation = new DocumentEscalation { Id = Guid.NewGuid(), FolderId = folderId, EscalatedByUserId = userId, Reason = "test", CreatedAt = DateTimeOffset.UtcNow }; var item = new DocumentEscalationItem { Id = Guid.NewGuid(), EscalationId = escalation.Id, DocumentId = documentId, RejectReason = "test" }; db.AddRange(escalation, item); await db.SaveChangesAsync(); return item.Id; }
    private static Mock<ISupabaseStorageClient> SuccessStorage(string path) { var storage = new Mock<ISupabaseStorageClient>(MockBehavior.Strict); storage.Setup(s => s.DeleteAsync(DocumentService.BucketName, path, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask); return storage; }

    private sealed class ThrowOnQuotaUpdateInterceptor : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("UPDATE users SET storage_used_bytes", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("test quota update failure");
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
