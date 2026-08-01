using AI_Study_Hub_v2.Data;
using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Dtos;
using AI_Study_Hub_v2.Services;
using AI_Study_Hub_v2.Tests.Support;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Npgsql;

namespace AI_Study_Hub_v2.Tests.Services;

[TestFixture, Category("Postgres"), NonParallelizable]
public sealed class ModerationResubmitPostgresTests
{
    private string _connectionString = null!;
    private NpgsqlDataSource? _dataSource;
    private readonly List<Guid> _createdUserIds = [];
    private readonly List<Guid> _createdAuthUserIds = [];
    private readonly List<Guid> _createdFolderIds = [];
    private readonly List<Guid> _createdDocumentIds = [];

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
            Assert.Ignore("Refusing PostgreSQL moderation concurrency tests outside a database ending in _test.");
        }

        var builder = new NpgsqlDataSourceBuilder(_connectionString);
        builder.MapEnum<DocumentStatus>(pgName: "public.document_status");
        builder.UseVector();
        _dataSource = builder.Build();

        await BootstrapAuthPrerequisiteAsync();
        await using var db = CreateDb();
        await PostgresTestDatabase.BootstrapAsync(db);
    }

    [TearDown]
    public async Task CleanCreatedRowsAsync()
    {
        try
        {
            if (_dataSource is not null)
            {
                await using var db = CreateDb();
                db.UserNotifications.RemoveRange(await db.UserNotifications
                    .Where(notification => _createdFolderIds.Contains(notification.FolderId)).ToListAsync());
                db.DocumentEscalationItems.RemoveRange(await db.DocumentEscalationItems
                    .Where(item => item.DocumentId.HasValue && _createdDocumentIds.Contains(item.DocumentId.Value)).ToListAsync());
                db.DocumentEscalations.RemoveRange(await db.DocumentEscalations
                    .Where(escalation => _createdFolderIds.Contains(escalation.FolderId)).ToListAsync());
                db.Documents.RemoveRange(await db.Documents.Where(document => _createdDocumentIds.Contains(document.Id)).ToListAsync());
                db.Folders.RemoveRange(await db.Folders.Where(folder => _createdFolderIds.Contains(folder.Id)).ToListAsync());
                db.Users.RemoveRange(await db.Users.Where(user => _createdUserIds.Contains(user.Id)).ToListAsync());
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
    public async Task RejectedFolderResubmit_RacingAdminResolution_HasOneDomainWinnerAndNoMixedState()
    {
        var scenario = await SeedScenarioAsync();
        var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var attempts = new[]
        {
            TryResubmitAsync(scenario.Owner.SupabaseUserId, scenario.Folder.Id, start.Task),
            TryResolveAsync(scenario.Escalation.Id, scenario.Item.Id, scenario.Admin.Id, start.Task),
        };
        start.SetResult(true);

        var results = await Task.WhenAll(attempts).WaitAsync(TimeSpan.FromSeconds(30));
        results.Count(result => result.Succeeded).Should().Be(1);
        var loser = results.Single(result => !result.Succeeded).Exception;
        AssertDomainConflict(loser);

        await using var fresh = CreateDb();
        var folder = await fresh.Folders.SingleAsync(candidate => candidate.Id == scenario.Folder.Id);
        var document = await fresh.Documents.SingleAsync(candidate => candidate.Id == scenario.Document.Id);
        var escalation = await fresh.DocumentEscalations.SingleAsync(candidate => candidate.Id == scenario.Escalation.Id);
        var item = await fresh.DocumentEscalationItems.SingleAsync(candidate => candidate.Id == scenario.Item.Id);
        var notifications = await fresh.UserNotifications
            .Where(notification => notification.FolderId == scenario.Folder.Id)
            .ToListAsync();

        escalation.EscalationStatus.Should().Be("Resolved");
        if (item.ResolutionStatus == DocumentEscalationItem.SupersededResolutionStatus)
        {
            folder.ShareStatus.Should().Be(FolderStatus.PendingShare);
            document.ReviewStatus.Should().Be(DocumentReviewStatus.None);
            document.ModerationGeneration.Should().Be(scenario.Document.ModerationGeneration + 1);
            document.ErrorMessage.Should().BeNull();
            escalation.ResolvedByUserId.Should().BeNull();
            escalation.AdminResponse.Should().Be("Superseded by owner resubmission.");
            item.ResolvedByUserId.Should().BeNull();
            item.AdminResponse.Should().Be("Superseded by owner resubmission.");
            notifications.Should().BeEmpty();
        }
        else
        {
            item.ResolutionStatus.Should().Be("Approved");
            item.ResolvedByUserId.Should().Be(scenario.Admin.Id);
            document.ReviewStatus.Should().Be(DocumentReviewStatus.Approved);
            document.ModerationGeneration.Should().Be(scenario.Document.ModerationGeneration);
            folder.ShareStatus.Should().Be(FolderStatus.Approved);
            escalation.ResolvedByUserId.Should().Be(scenario.Admin.Id);
            escalation.AdminResponse.Should().BeNull();
            notifications.Should().ContainSingle(notification =>
                notification.Kind == UserNotificationKind.EscalationResolved &&
                notification.Outcome == UserNotificationOutcome.Approved &&
                notification.RecipientUserId == scenario.Owner.Id);
        }
    }

    private async Task<RaceResult> TryResubmitAsync(Guid ownerSupabaseUserId, Guid folderId, Task start)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        try
        {
            await start.WaitAsync(timeout.Token);
            await using var db = CreateDb();
            await CreateFolderService(db).RequestShareAsync(ownerSupabaseUserId, folderId, timeout.Token);
            return new RaceResult(true, null);
        }
        catch (Exception exception)
        {
            return new RaceResult(false, exception);
        }
    }

    private async Task<RaceResult> TryResolveAsync(Guid escalationId, Guid itemId, Guid adminId, Task start)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        try
        {
            await start.WaitAsync(timeout.Token);
            await using var db = CreateDb();
            await CreateEscalationService(db).ResolveItemsAsync(escalationId, new ResolveEscalationItemsRequest
            {
                Items = [new ResolveEscalationItemRequest { ItemId = itemId, Status = "Approved" }]
            }, adminId, timeout.Token);
            return new RaceResult(true, null);
        }
        catch (Exception exception)
        {
            return new RaceResult(false, exception);
        }
    }

    private async Task<Scenario> SeedScenarioAsync()
    {
        await using var db = CreateDb();
        var owner = await SeedUserAsync(db, roleId: 2, "Student");
        var admin = await SeedUserAsync(db, roleId: 1, "Admin");
        var folder = new Folder
        {
            Id = Guid.NewGuid(), UserId = owner.Id, Name = $"resubmit-{Guid.NewGuid():N}",
            ShareStatus = FolderStatus.Rejected, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        };
        var document = new Document
        {
            Id = Guid.NewGuid(), UserId = owner.Id, FolderId = folder.Id, FileName = "review.pdf",
            StoragePath = $"test/{Guid.NewGuid():N}", FileSizeBytes = 1, MimeType = "application/pdf",
            SubjectCode = "SWP391", Semester = "SU26", Status = DocumentStatus.Ready,
            ReviewStatus = DocumentReviewStatus.Escalated, ModerationGeneration = 7,
            ErrorMessage = "Awaiting admin resolution", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        };
        var escalation = new DocumentEscalation
        {
            Id = Guid.NewGuid(), FolderId = folder.Id, EscalatedByUserId = owner.Id,
            Reason = "Needs final review", EscalationStatus = "Pending", CreatedAt = DateTimeOffset.UtcNow
        };
        var item = new DocumentEscalationItem
        {
            Id = Guid.NewGuid(), EscalationId = escalation.Id, DocumentId = document.Id,
            DocumentFileName = document.FileName, DocumentModerationGeneration = document.ModerationGeneration,
            RejectReason = "Needs final review", ResolutionStatus = DocumentEscalationItem.PendingResolutionStatus
        };
        _createdFolderIds.Add(folder.Id);
        _createdDocumentIds.Add(document.Id);
        db.AddRange(folder, document, escalation, item);
        await db.SaveChangesAsync();
        return new Scenario(owner, admin, folder, document, escalation, item);
    }

    private async Task<User> SeedUserAsync(AppDbContext db, int roleId, string roleName)
    {
        var userId = Guid.NewGuid();
        var authUserId = Guid.NewGuid();
        _createdUserIds.Add(userId);
        _createdAuthUserIds.Add(authUserId);
        await InsertAuthUserAsync(authUserId);
        var user = new User
        {
            Id = userId, RoleId = roleId, SupabaseUserId = authUserId, Username = $"u{Guid.NewGuid():N}"[..15],
            FullName = $"{roleName} test", IsActive = true, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private static FolderService CreateFolderService(AppDbContext db)
    {
        var publicationState = new FolderPublicationStateService();
        return new FolderService(db, NullLogger<FolderService>.Instance, Mock.Of<IStorageDeletionCoordinator>(),
            Mock.Of<IAuditLogService>(), Mock.Of<IFolderShareAiModerator>(), Mock.Of<IPlanCapacityGuard>(),
            Mock.Of<ISharedFolderCopyCoordinator>(), new UserNotificationService(db), publicationState);
    }

    private static EscalationService CreateEscalationService(AppDbContext db) =>
        new(db, new AuditLogService(db), new UserNotificationService(db), new FolderPublicationStateService());

    private AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_dataSource ?? throw new InvalidOperationException("PostgreSQL data source is not initialized."), options => options.UseVector())
            .Options;
        return new AppDbContext(options);
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

    private static void AssertDomainConflict(Exception? exception)
    {
        exception.Should().NotBeNull();
        switch (exception)
        {
            case DocumentException documentException:
                documentException.StatusCode.Should().Be(409);
                documentException.Code.Should().BeOneOf("folder_resubmit_conflict", "folder_already_discoverable", "invalid_share_status");
                break;
            case AdminException adminException:
                adminException.StatusCode.Should().Be(409);
                adminException.Code.Should().BeOneOf("escalation_already_resolved", "escalation_item_stale", "escalation_item_set_changed");
                break;
            default:
                Assert.Fail($"Expected a mapped domain 409 conflict, but received {exception!.GetType().FullName}: {exception.Message}");
                break;
        }
    }

    private sealed record Scenario(User Owner, User Admin, Folder Folder, Document Document, DocumentEscalation Escalation, DocumentEscalationItem Item);
    private sealed record RaceResult(bool Succeeded, Exception? Exception);
}
