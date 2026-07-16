using System.Data;
using System.Collections.Concurrent;
using AI_Study_Hub_v2.Data;
using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Moq;
using Npgsql;

namespace AI_Study_Hub_v2.Tests.Services;

[TestFixture, Category("Postgres"), NonParallelizable]
public sealed class PlanCapacityPostgresTests
{
    private const string PreReSyncMigration = "20260706184528_AddDocumentEscalation";
    private const string ReSyncPlanMigration = "20260709165701_ReSyncPlanFkAndConstraints";
    private string _connectionString = null!;
    private NpgsqlDataSource? _dataSource;
    private readonly ConcurrentBag<Guid> _createdUserIds = [];
    private readonly ConcurrentBag<Guid> _createdAuthUserIds = [];
    private readonly ConcurrentBag<Guid> _createdPlanIds = [];
    private readonly ConcurrentBag<Guid> _createdUserPlanIds = [];
    private readonly ConcurrentBag<Guid> _createdFolderIds = [];
    private readonly ConcurrentBag<Guid> _createdDocumentIds = [];

    [SetUp]
    public async Task RequireDedicatedTestDatabaseAsync()
    {
        _connectionString = Environment.GetEnvironmentVariable("AI_STUDY_HUB_TEST_POSTGRES") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(_connectionString))
        {
            Assert.Ignore("AI_STUDY_HUB_TEST_POSTGRES is not configured.");
        }

        var database = new NpgsqlConnectionStringBuilder(_connectionString).Database;
        if (string.IsNullOrWhiteSpace(database) || !database.EndsWith("_test", StringComparison.OrdinalIgnoreCase))
        {
            Assert.Ignore("Refusing PostgreSQL capacity tests outside a database ending in _test.");
        }

        var builder = new NpgsqlDataSourceBuilder(_connectionString);
        builder.MapEnum<DocumentStatus>(pgName: "public.document_status");
        builder.UseVector();
        _dataSource = builder.Build();

        await BootstrapAuthPrerequisiteAsync();
        await using var db = CreateDb();
        await MigrateWithReSyncCompatibilityAsync(db);
        await ApplyFolderModelDriftCompatibilityAsync(db);
    }

    [TearDown]
    public async Task CleanCreatedRowsAsync()
    {
        try
        {
            if (_dataSource is not null)
            {
                await using var db = CreateDb();
                if (_createdDocumentIds.Count > 0)
                {
                    db.DocumentEscalationItems.RemoveRange(await db.DocumentEscalationItems
                        .Where(item => _createdDocumentIds.Contains(item.DocumentId)).ToListAsync());
                    db.DocumentChunks.RemoveRange(await db.DocumentChunks
                        .Where(chunk => _createdDocumentIds.Contains(chunk.DocumentId)).ToListAsync());
                    db.Documents.RemoveRange(await db.Documents
                        .Where(document => _createdDocumentIds.Contains(document.Id)).ToListAsync());
                }

                if (_createdFolderIds.Count > 0 || _createdUserIds.Count > 0)
                {
                    db.DocumentEscalations.RemoveRange(await db.DocumentEscalations
                        .Where(escalation => _createdFolderIds.Contains(escalation.FolderId)
                            || _createdUserIds.Contains(escalation.EscalatedByUserId))
                        .ToListAsync());
                }

                if (_createdUserPlanIds.Count > 0)
                {
                    db.UserPlans.RemoveRange(await db.UserPlans
                        .Where(userPlan => _createdUserPlanIds.Contains(userPlan.Id)).ToListAsync());
                }
                if (_createdFolderIds.Count > 0)
                {
                    db.Folders.RemoveRange(await db.Folders
                        .Where(folder => _createdFolderIds.Contains(folder.Id)).ToListAsync());
                }
                if (_createdPlanIds.Count > 0)
                {
                    db.Plans.RemoveRange(await db.Plans
                        .Where(plan => _createdPlanIds.Contains(plan.Id)).ToListAsync());
                }
                if (_createdUserIds.Count > 0)
                {
                    db.Users.RemoveRange(await db.Users
                        .Where(user => _createdUserIds.Contains(user.Id)).ToListAsync());
                }
                await db.SaveChangesAsync();

                foreach (var authUserId in _createdAuthUserIds)
                {
                    await using var connection = await _dataSource.OpenConnectionAsync();
                    await using var command = new NpgsqlCommand("DELETE FROM auth.users WHERE id = @id", connection);
                    command.Parameters.AddWithValue("id", authUserId);
                    await command.ExecuteNonQueryAsync();
                }
            }
        }
        finally
        {
            if (_dataSource is not null)
            {
                await _dataSource.DisposeAsync();
            }
            _dataSource = null;
        }
    }

    [Test]
    public async Task ConcurrentDocumentCapacity_OneRemainingSlot_CommitsExactlyOneDocument()
    {
        var scenario = await SeedScenarioAsync(maxDocuments: 1);
        var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var attempts = new[]
        {
            TryFinalizeDocumentAsync(scenario.User.Id, null, start.Task),
            TryFinalizeDocumentAsync(scenario.User.Id, null, start.Task),
        };
        start.SetResult(true);
        var results = await Task.WhenAll(attempts);

        results.Count(result => result.Committed).Should().Be(1);
        results.Count(result => !result.Committed).Should().Be(1);
        IsCapacityOrSerializationFailure(results.Single(result => !result.Committed).Failure).Should().BeTrue();
        await using var fresh = CreateDb();
        (await fresh.Documents.CountAsync(document => document.UserId == scenario.User.Id)).Should().Be(1);
    }

    [Test]
    public async Task ConcurrentFolderDocumentCapacity_OneRemainingSlot_CommitsExactlyOneDocument()
    {
        var scenario = await SeedScenarioAsync(maxDocumentsPerFolder: 1, createFolder: true);
        var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var attempts = new[]
        {
            TryFinalizeDocumentAsync(scenario.User.Id, scenario.Folder!.Id, start.Task),
            TryFinalizeDocumentAsync(scenario.User.Id, scenario.Folder.Id, start.Task),
        };
        start.SetResult(true);
        var results = await Task.WhenAll(attempts);

        results.Count(result => result.Committed).Should().Be(1);
        results.Count(result => !result.Committed).Should().Be(1);
        IsCapacityOrSerializationFailure(results.Single(result => !result.Committed).Failure).Should().BeTrue();
        await using var fresh = CreateDb();
        (await fresh.Documents.CountAsync(document => document.FolderId == scenario.Folder.Id)).Should().Be(1);
    }

    [Test]
    public async Task ConcurrentFolderCapacity_OneRemainingSlot_CommitsExactlyOneFolder()
    {
        var scenario = await SeedScenarioAsync(maxFolders: 1);
        var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var attempts = new[]
        {
            TryFinalizeFolderAsync(scenario.User.Id, start.Task),
            TryFinalizeFolderAsync(scenario.User.Id, start.Task),
        };
        start.SetResult(true);
        var results = await Task.WhenAll(attempts);

        results.Count(result => result.Committed).Should().Be(1);
        results.Count(result => !result.Committed).Should().Be(1);
        IsCapacityOrSerializationFailure(results.Single(result => !result.Committed).Failure).Should().BeTrue();
        await using var fresh = CreateDb();
        (await fresh.Folders.CountAsync(folder => folder.UserId == scenario.User.Id)).Should().Be(1);
    }

    [Test]
    public async Task TransactionRollback_AfterGuardAndInsertedRows_LeavesNoDocumentOrFolder()
    {
        var scenario = await SeedScenarioAsync(maxDocuments: 2, maxFolders: 2);
        var documentId = Guid.NewGuid();
        var folderId = Guid.NewGuid();

        await using (var db = CreateDb())
        await using (var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            var guard = CreateGuard();
            await guard.LockAndValidateAsync(db, scenario.User.Id, new PlanCapacityRequest(1, 1, null, 0), CancellationToken.None);
            db.Folders.Add(NewFolder(folderId, scenario.User.Id));
            db.Documents.Add(NewDocument(documentId, scenario.User.Id, null));
            await db.SaveChangesAsync();

            try
            {
                throw new InvalidOperationException("force rollback before commit");
            }
            catch
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }
        }

        await using var fresh = CreateDb();
        (await fresh.Documents.AnyAsync(document => document.Id == documentId)).Should().BeFalse();
        (await fresh.Folders.AnyAsync(folder => folder.Id == folderId)).Should().BeFalse();
    }

    [Test]
    public async Task UserRowLock_HeldByFirstFinalization_SecondCannotOverAllocate()
    {
        var scenario = await SeedScenarioAsync(maxDocuments: 1);
        var firstLocked = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = HoldLockedFinalizationAsync(scenario.User.Id, firstLocked, releaseFirst.Task);
        await firstLocked.Task.WaitAsync(TimeSpan.FromSeconds(20));

        var second = TryFinalizeDocumentAsync(scenario.User.Id, null, entered: secondEntered);
        await secondEntered.Task.WaitAsync(TimeSpan.FromSeconds(20));
        (await Task.WhenAny(second, Task.Delay(TimeSpan.FromMilliseconds(300)))).Should().NotBeSameAs(second,
            "the second finalization must wait on the first transaction's user row lock");

        releaseFirst.SetResult(true);
        var firstResult = await first;
        var secondResult = await second;

        firstResult.Committed.Should().BeTrue();
        secondResult.Committed.Should().BeFalse();
        IsCapacityOrSerializationFailure(secondResult.Failure).Should().BeTrue();
        await using var fresh = CreateDb();
        (await fresh.Documents.CountAsync(document => document.UserId == scenario.User.Id)).Should().Be(1);
    }

    private async Task<FinalizationResult> TryFinalizeDocumentAsync(
        Guid userId,
        Guid? folderId,
        Task? start = null,
        TaskCompletionSource<bool>? entered = null)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            if (start is not null)
            {
                await start.WaitAsync(timeout.Token);
            }
            await using var db = CreateDb();
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, timeout.Token);
            try
            {
                entered?.TrySetResult(true);
                await CreateGuard().LockAndValidateAsync(db, userId,
                    new PlanCapacityRequest(1, 0, folderId, folderId.HasValue ? 1 : 0), timeout.Token);
                var document = NewDocument(Guid.NewGuid(), userId, folderId);
                _createdDocumentIds.Add(document.Id);
                db.Documents.Add(document);
                await db.SaveChangesAsync(timeout.Token);
                await transaction.CommitAsync(timeout.Token);
                return new FinalizationResult(true, null);
            }
            catch (Exception exception)
            {
                try { await transaction.RollbackAsync(CancellationToken.None); } catch { }
                return new FinalizationResult(false, exception);
            }
        }
        catch (Exception exception)
        {
            return new FinalizationResult(false, exception);
        }
    }

    private async Task<FinalizationResult> TryFinalizeFolderAsync(Guid userId, Task start)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await start.WaitAsync(timeout.Token);
            await using var db = CreateDb();
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, timeout.Token);
            try
            {
                await CreateGuard().LockAndValidateAsync(db, userId, new PlanCapacityRequest(0, 1, null, 0), timeout.Token);
                var folder = NewFolder(Guid.NewGuid(), userId);
                _createdFolderIds.Add(folder.Id);
                db.Folders.Add(folder);
                await db.SaveChangesAsync(timeout.Token);
                await transaction.CommitAsync(timeout.Token);
                return new FinalizationResult(true, null);
            }
            catch (Exception exception)
            {
                try { await transaction.RollbackAsync(CancellationToken.None); } catch { }
                return new FinalizationResult(false, exception);
            }
        }
        catch (Exception exception)
        {
            return new FinalizationResult(false, exception);
        }
    }

    private async Task<FinalizationResult> HoldLockedFinalizationAsync(
        Guid userId,
        TaskCompletionSource<bool> locked,
        Task release)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await using var db = CreateDb();
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, timeout.Token);
            try
            {
                await CreateGuard().LockAndValidateAsync(db, userId, new PlanCapacityRequest(1, 0, null, 0), timeout.Token);
                locked.TrySetResult(true);
                await release.WaitAsync(timeout.Token);
                var document = NewDocument(Guid.NewGuid(), userId, null);
                _createdDocumentIds.Add(document.Id);
                db.Documents.Add(document);
                await db.SaveChangesAsync(timeout.Token);
                await transaction.CommitAsync(timeout.Token);
                return new FinalizationResult(true, null);
            }
            catch (Exception exception)
            {
                try { await transaction.RollbackAsync(CancellationToken.None); } catch { }
                return new FinalizationResult(false, exception);
            }
        }
        catch (Exception exception)
        {
            locked.TrySetException(exception);
            return new FinalizationResult(false, exception);
        }
    }

    private async Task<Scenario> SeedScenarioAsync(
        int? maxDocuments = null,
        int? maxFolders = null,
        int? maxDocumentsPerFolder = null,
        bool createFolder = false)
    {
        await using var db = CreateDb();
        var user = await SeedUserAsync(db);
        var plan = new Plan
        {
            Id = Guid.NewGuid(),
            PlanKey = $"capacity-{Guid.NewGuid():N}",
            DisplayName = "Capacity concurrency test",
            MaxDocumentCount = maxDocuments,
            MaxFolderCount = maxFolders,
            MaxDocsPerFolder = maxDocumentsPerFolder,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _createdPlanIds.Add(plan.Id);
        var userPlan = new UserPlan
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            PlanId = plan.Id,
            Status = "active",
            AssignedAt = DateTimeOffset.UtcNow,
        };
        _createdUserPlanIds.Add(userPlan.Id);
        db.AddRange(plan, userPlan);

        Folder? folder = null;
        if (createFolder)
        {
            folder = NewFolder(Guid.NewGuid(), user.Id);
            _createdFolderIds.Add(folder.Id);
            db.Folders.Add(folder);
        }
        await db.SaveChangesAsync();
        return new Scenario(user, folder);
    }

    private async Task<User> SeedUserAsync(AppDbContext db)
    {
        var userId = Guid.NewGuid();
        var authUserId = Guid.NewGuid();
        _createdUserIds.Add(userId);
        _createdAuthUserIds.Add(authUserId);
        await InsertAuthUserAsync(authUserId);
        var user = new User
        {
            Id = userId,
            RoleId = 2,
            SupabaseUserId = authUserId,
            Username = $"u{Guid.NewGuid():N}"[..15],
            FullName = "Capacity test user",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private async Task BootstrapAuthPrerequisiteAsync()
    {
        await using var connection = await (_dataSource ?? throw new InvalidOperationException("PostgreSQL data source is not initialized.")).OpenConnectionAsync();
        await using var command = new NpgsqlCommand("CREATE SCHEMA IF NOT EXISTS auth; CREATE TABLE IF NOT EXISTS auth.users (id uuid PRIMARY KEY);", connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task MigrateWithReSyncCompatibilityAsync(AppDbContext db)
    {
        var appliedMigrations = await db.Database.GetAppliedMigrationsAsync();
        if (!appliedMigrations.Contains(ReSyncPlanMigration))
        {
            if (!appliedMigrations.Contains(PreReSyncMigration))
            {
                await db.Database.GetService<IMigrator>().MigrateAsync(PreReSyncMigration);
            }

            // AddPlanSystem created this exact name, while ReSync attempts to add it
            // again. This is limited to the disposable test database migration path.
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE IF EXISTS public.payment_transactions DROP CONSTRAINT IF EXISTS \"FK_payment_transactions_users_user_id\"");
        }

        await db.Database.MigrateAsync();
    }

    private static Task ApplyFolderModelDriftCompatibilityAsync(AppDbContext db)
    {
        // Main currently has Folder model fields without a corresponding migration.
        // These idempotent columns only align disposable fixture databases with the
        // latest model and must be removed once a real application migration lands.
        return db.Database.ExecuteSqlRawAsync("""
            ALTER TABLE public.folders
                ADD COLUMN IF NOT EXISTS share_review_source varchar(32) NULL,
                ADD COLUMN IF NOT EXISTS ai_review_reason varchar(2000) NULL,
                ADD COLUMN IF NOT EXISTS ai_review_confidence double precision NULL,
                ADD COLUMN IF NOT EXISTS ai_review_failure_count integer NOT NULL DEFAULT 0,
                ADD COLUMN IF NOT EXISTS human_review_reason varchar(2000) NULL,
                ADD COLUMN IF NOT EXISTS requires_human_review boolean NOT NULL DEFAULT false,
                ADD COLUMN IF NOT EXISTS appeal_requested_at timestamp with time zone NULL,
                ADD COLUMN IF NOT EXISTS appeal_message varchar(2000) NULL;
            """);
    }

    private async Task InsertAuthUserAsync(Guid authUserId)
    {
        await using var connection = await (_dataSource ?? throw new InvalidOperationException("PostgreSQL data source is not initialized.")).OpenConnectionAsync();
        await using var command = new NpgsqlCommand("INSERT INTO auth.users (id) VALUES (@id)", connection);
        command.Parameters.AddWithValue("id", authUserId);
        await command.ExecuteNonQueryAsync();
    }

    private AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_dataSource ?? throw new InvalidOperationException("PostgreSQL data source is not initialized."), options => options.UseVector())
            .Options;
        return new AppDbContext(options);
    }

    private static IPlanCapacityGuard CreateGuard() => new PlanCapacityGuard(Mock.Of<IPlanService>());

    private static Document NewDocument(Guid id, Guid userId, Guid? folderId) => new()
    {
        Id = id,
        UserId = userId,
        FolderId = folderId,
        FileName = $"{id:N}.pdf",
        StoragePath = $"test/{id:N}",
        FileSizeBytes = 1,
        MimeType = "application/pdf",
        SubjectCode = "SWP391",
        Semester = "SU26",
        Status = DocumentStatus.Ready,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
    };

    private static Folder NewFolder(Guid id, Guid userId) => new()
    {
        Id = id,
        UserId = userId,
        Name = $"folder-{id:N}",
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
    };

    private static bool IsCapacityOrSerializationFailure(Exception? exception)
    {
        return (exception is PlanException planException
                && planException.Code is "document_count_exceeded" or "folder_count_exceeded")
            || (exception is DocumentException documentException && documentException.Code == "folder_full")
            || FindPostgresException(exception)?.SqlState == PostgresErrorCodes.SerializationFailure;
    }

    private static PostgresException? FindPostgresException(Exception? exception) => exception switch
    {
        PostgresException postgresException => postgresException,
        { InnerException: not null } => FindPostgresException(exception.InnerException),
        _ => null,
    };

    private sealed record Scenario(User User, Folder? Folder);
    private sealed record FinalizationResult(bool Committed, Exception? Failure);
}
