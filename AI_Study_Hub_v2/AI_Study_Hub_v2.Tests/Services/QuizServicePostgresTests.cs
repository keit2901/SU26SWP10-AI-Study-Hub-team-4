using System.Collections.Concurrent;
using System.Text.Json;
using AI_Study_Hub_v2.Data;
using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Dtos;
using AI_Study_Hub_v2.Options;
using AI_Study_Hub_v2.Services;
using AI_Study_Hub_v2.Services.Rag;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Npgsql;

namespace AI_Study_Hub_v2.Tests.Services;

[TestFixture, Category("Postgres"), NonParallelizable]
public sealed class QuizServicePostgresTests
{
    private const string PreReSyncMigration = "20260706184528_AddDocumentEscalation";
    private const string ReSyncPlanMigration = "20260709165701_ReSyncPlanFkAndConstraints";
    private readonly ConcurrentBag<Guid> _quizIds = [];
    private readonly ConcurrentBag<Guid> _sessionIds = [];
    private readonly ConcurrentBag<Guid> _userIds = [];
    private readonly ConcurrentBag<Guid> _authUserIds = [];
    private NpgsqlDataSource? _dataSource;
    private string _connectionString = null!;

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
            Assert.Ignore("Refusing PostgreSQL quiz tests outside a database ending in _test.");
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
                db.Quizzes.RemoveRange(await db.Quizzes.Where(quiz => _quizIds.Contains(quiz.Id)).ToListAsync());
                db.ChatSessions.RemoveRange(await db.ChatSessions.Where(session => _sessionIds.Contains(session.Id)).ToListAsync());
                db.Users.RemoveRange(await db.Users.Where(user => _userIds.Contains(user.Id)).ToListAsync());
                await db.SaveChangesAsync();

                foreach (var authUserId in _authUserIds)
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
    public async Task SaveAsync_ConcurrentDifferentAnswersForSameQuestion_CommitsOneAndRejectsTheOther()
    {
        var scenario = await SeedScenarioAsync();
        var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var ready = new[]
        {
            new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously),
            new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously),
        };

        var saves = new[]
        {
            TrySaveAsync(scenario, new SaveQuizRequest(0, new Dictionary<int, string?> { [0] = "A" }), ready[0], start.Task),
            TrySaveAsync(scenario, new SaveQuizRequest(0, new Dictionary<int, string?> { [0] = "B" }), ready[1], start.Task),
        };
        await Task.WhenAll(ready.Select(signal => signal.Task)).WaitAsync(TestTimeout);
        start.SetResult(true);
        var results = await Task.WhenAll(saves).WaitAsync(TestTimeout);

        results.Count(result => result.Saved is not null).Should().Be(1);
        var loser = results.Single(result => result.Saved is null).Failure.Should().BeOfType<QuizException>().Subject;
        loser.StatusCode.Should().Be(409);
        loser.Code.Should().Be("answer_already_submitted");

        var winner = results.Single(result => result.Saved is not null);
        await using var fresh = CreateDb();
        var persisted = await fresh.Quizzes.AsNoTracking().SingleAsync(quiz => quiz.Id == scenario.QuizId);
        DeserializeAnswers(persisted.AnswersJson).Should().ContainSingle()
            .Which.Value.Should().Be(winner.OptionId);
    }

    [Test]
    public async Task SaveAsync_ConcurrentAnswersForDifferentQuestions_MergesBothAnswers()
    {
        var scenario = await SeedScenarioAsync();
        var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var ready = new[]
        {
            new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously),
            new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously),
        };

        var saves = new[]
        {
            TrySaveAsync(scenario, new SaveQuizRequest(0, new Dictionary<int, string?> { [0] = "A" }), ready[0], start.Task),
            TrySaveAsync(scenario, new SaveQuizRequest(1, new Dictionary<int, string?> { [1] = "B" }), ready[1], start.Task),
        };
        await Task.WhenAll(ready.Select(signal => signal.Task)).WaitAsync(TestTimeout);
        start.SetResult(true);
        var results = await Task.WhenAll(saves).WaitAsync(TestTimeout);

        results.All(result => result.Saved is not null).Should().BeTrue();
        await using var fresh = CreateDb();
        var persisted = await fresh.Quizzes.AsNoTracking().SingleAsync(quiz => quiz.Id == scenario.QuizId);
        DeserializeAnswers(persisted.AnswersJson).Should().BeEquivalentTo(new Dictionary<int, string?>
        {
            [0] = "A",
            [1] = "B",
        });
    }

    private async Task<SaveResult> TrySaveAsync(
        QuizScenario scenario,
        SaveQuizRequest request,
        TaskCompletionSource<bool> ready,
        Task start)
    {
        using var timeout = new CancellationTokenSource(TestTimeout);
        await using var db = CreateDb();
        var service = CreateService(db);
        ready.TrySetResult(true);
        await start.WaitAsync(timeout.Token);
        try
        {
            var saved = await service.SaveAsync(scenario.SupabaseUserId, scenario.QuizId, request, timeout.Token);
            return new SaveResult(request.Answers!.Single().Value!, saved, null);
        }
        catch (Exception exception)
        {
            return new SaveResult(request.Answers!.Single().Value!, null, exception);
        }
    }

    private async Task<QuizScenario> SeedScenarioAsync()
    {
        var userId = Guid.NewGuid();
        var authUserId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var quizId = Guid.NewGuid();
        _userIds.Add(userId);
        _authUserIds.Add(authUserId);
        _sessionIds.Add(sessionId);
        _quizIds.Add(quizId);
        await InsertAuthUserAsync(authUserId);

        await using var db = CreateDb();
        var now = DateTimeOffset.UtcNow;
        db.Users.Add(new User
        {
            Id = userId,
            RoleId = 2,
            SupabaseUserId = authUserId,
            Username = $"q{Guid.NewGuid():N}"[..15],
            FullName = "Quiz concurrency test user",
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.ChatSessions.Add(new ChatSession
        {
            Id = sessionId,
            UserId = userId,
            Title = "Quiz concurrency test session",
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.Quizzes.Add(new Quiz
        {
            Id = quizId,
            SessionId = sessionId,
            UserId = userId,
            Title = "Quiz concurrency test",
            Status = QuizStatus.InProgress,
            TotalQuestions = 3,
            QuestionsJson = QuestionsJson,
            AnswersJson = "{}",
            SubmittedJson = "{}",
            ScopeJson = "{}",
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();
        return new QuizScenario(authUserId, quizId);
    }

    private static QuizService CreateService(AppDbContext db) => new(
        db,
        Mock.Of<IRagSearchService>(),
        Mock.Of<IAiChatCompletionClientFactory>(),
        Microsoft.Extensions.Options.Options.Create(new GroqOptions()),
        Microsoft.Extensions.Options.Options.Create(new GeminiOptions()),
        Mock.Of<IChatPersistenceService>(),
        Mock.Of<IAiQuotaService>(),
        Mock.Of<ILogger<QuizService>>());

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

            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE IF EXISTS public.payment_transactions DROP CONSTRAINT IF EXISTS \"FK_payment_transactions_users_user_id\"");
        }

        await db.Database.MigrateAsync();
    }

    private static Task ApplyFolderModelDriftCompatibilityAsync(AppDbContext db) => db.Database.ExecuteSqlRawAsync("""
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

    private static Dictionary<int, string?> DeserializeAnswers(string json) =>
        JsonSerializer.Deserialize<Dictionary<int, string?>>(json) ?? new Dictionary<int, string?>();

    private const string QuestionsJson = """
        [{"index":0,"question":"Q1","options":[{"id":"A","text":"A"},{"id":"B","text":"B"}],"correctOptionId":"A","explanation":"E1"},
         {"index":1,"question":"Q2","options":[{"id":"A","text":"A"},{"id":"B","text":"B"}],"correctOptionId":"B","explanation":"E2"},
         {"index":2,"question":"Q3","options":[{"id":"A","text":"A"},{"id":"B","text":"B"}],"correctOptionId":"A","explanation":"E3"}]
        """;

    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(30);

    private sealed record QuizScenario(Guid SupabaseUserId, Guid QuizId);
    private sealed record SaveResult(string OptionId, QuizDto? Saved, Exception? Failure);
}
