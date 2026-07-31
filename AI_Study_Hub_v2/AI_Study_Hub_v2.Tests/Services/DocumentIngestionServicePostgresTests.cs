using System.Data.Common;
using System.Text;
using AI_Study_Hub_v2.Data;
using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Options;
using AI_Study_Hub_v2.Services.Rag;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Pgvector;

namespace AI_Study_Hub_v2.Tests.Services;

[TestFixture, Category("Postgres"), NonParallelizable]
public sealed class DocumentIngestionServicePostgresTests
{
    private const string BeforeVnPayExpiryMigration = "20260710162831_AddUniqueTxnRefPerUser";
    private NpgsqlDataSource? _dataSource;
    private bool _schemaReady;
    private readonly List<Guid> _documents = [];
    private readonly List<Guid> _users = [];
    private readonly List<Guid> _authUsers = [];

    [SetUp]
    public async Task SetUpAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable("AI_STUDY_HUB_TEST_POSTGRES") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(connectionString)) Assert.Ignore("AI_STUDY_HUB_TEST_POSTGRES is not configured.");
        var database = new NpgsqlConnectionStringBuilder(connectionString).Database;
        if (string.IsNullOrWhiteSpace(database) || !database.EndsWith("_test", StringComparison.OrdinalIgnoreCase))
            Assert.Ignore("Refusing ingestion PostgreSQL tests outside a database ending in _test.");
        var builder = new NpgsqlDataSourceBuilder(connectionString);
        builder.MapEnum<DocumentStatus>(pgName: "public.document_status");
        builder.UseVector();
        _dataSource = builder.Build();
        await BootstrapAuthAsync();
        await using var db = CreateDb();
        await MigrateCompatibilityAsync(db);
        _schemaReady = true;
    }

    [TearDown]
    public async Task TearDownAsync()
    {
        try
        {
            if (!_schemaReady || _dataSource is null) return;
            await using var db = CreateDb();
            db.DocumentChunks.RemoveRange(await db.DocumentChunks.Where(x => _documents.Contains(x.DocumentId)).ToListAsync());
            db.Documents.RemoveRange(await db.Documents.Where(x => _documents.Contains(x.Id)).ToListAsync());
            db.Users.RemoveRange(await db.Users.Where(x => _users.Contains(x.Id)).ToListAsync());
            await db.SaveChangesAsync();
            foreach (var authUser in _authUsers) await DeleteAuthAsync(authUser);
        }
        finally
        {
            if (_dataSource is not null) await _dataSource.DisposeAsync();
            _dataSource = null;
            _schemaReady = false;
        }
    }

    [Test]
    public async Task TwoServices_NewerClaimWins_AndStalePublicationCannotReplaceChunks()
    {
        var document = await SeedDocumentAsync("prior");
        var lockBarrier = new PublicationLockBarrier();
        await using var firstDb = CreateDb(lockBarrier);
        await using var secondDb = CreateDb();
        var first = BuildService(firstDb, new StaticExtraction(), new FixedEmbedding(), "first");
        var second = BuildService(secondDb, new StaticExtraction(), new FixedEmbedding(), "second");

        var firstTask = first.IngestAsync(document.Id, document.User.SupabaseUserId);
        await lockBarrier.BeforeLock.Task.WaitAsync(TimeSpan.FromSeconds(15));
        var secondResult = await second.IngestAsync(document.Id, document.User.SupabaseUserId);
        lockBarrier.Release.TrySetResult(true);
        var firstResult = await firstTask;

        secondResult.Success.Should().BeTrue();
        firstResult.Success.Should().BeFalse();
        firstResult.ErrorMessage.Should().Contain("superseded");
        await AssertReadyChunkAsync(document.Id, "second");
    }

    [Test]
    public async Task TwoServices_StaleEmbeddingFailureCannotOverwriteNewerReady()
    {
        var document = await SeedDocumentAsync("prior");
        var firstBarrier = new ExtractionBarrier();
        await using var firstDb = CreateDb();
        await using var secondDb = CreateDb();
        var first = BuildService(firstDb, new WaitingExtraction(firstBarrier), new ThrowingEmbedding(), "first");
        var second = BuildService(secondDb, new StaticExtraction(), new FixedEmbedding(), "second");

        var firstTask = first.IngestAsync(document.Id, document.User.SupabaseUserId);
        await firstBarrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
        (await second.IngestAsync(document.Id, document.User.SupabaseUserId)).Success.Should().BeTrue();
        firstBarrier.Release.TrySetResult(true);
        var firstResult = await firstTask;

        firstResult.Success.Should().BeFalse();
        firstResult.ErrorMessage.Should().Contain("superseded");
        await AssertReadyChunkAsync(document.Id, "second");
    }

    [Test]
    public async Task PublicationFailure_RollsBackChunks_AndMarksCurrentOwnerFailed()
    {
        var document = await SeedDocumentAsync("prior");
        var trigger = await InstallPublicationFailureTriggerAsync();
        DocumentIngestionResult result;
        try
        {
            await using var db = CreateDb();
            result = await BuildService(db, new StaticExtraction(), new FixedEmbedding(), "replacement")
                .IngestAsync(document.Id, document.User.SupabaseUserId);
        }
        finally
        {
            await DropPublicationFailureTriggerAsync(trigger);
        }

        result.Success.Should().BeFalse();
        await using var fresh = CreateDb();
        var reloaded = await fresh.Documents.SingleAsync(x => x.Id == document.Id);
        reloaded.Status.Should().Be(DocumentStatus.Failed);
        reloaded.IngestionOperationId.Should().BeNull();
        (await fresh.DocumentChunks.SingleAsync(x => x.DocumentId == document.Id)).Content.Should().Be("prior");
    }

    [Test]
    public async Task Migration_AddsNullableToken_AndRejectsEmptyUuid()
    {
        await using var connection = await (_dataSource ?? throw new InvalidOperationException()).OpenConnectionAsync();
        await using var column = new NpgsqlCommand("SELECT is_nullable FROM information_schema.columns WHERE table_schema='public' AND table_name='documents' AND column_name='ingestion_operation_id'", connection);
        (await column.ExecuteScalarAsync()).Should().Be("YES");
        var document = await SeedDocumentAsync();
        await using var invalid = new NpgsqlCommand("UPDATE public.documents SET ingestion_operation_id='00000000-0000-0000-0000-000000000000'::uuid WHERE id=@id", connection);
        invalid.Parameters.AddWithValue("id", document.Id);
        await FluentActions.Awaiting(() => invalid.ExecuteNonQueryAsync()).Should().ThrowAsync<PostgresException>()
            .Where(x => x.SqlState == PostgresErrorCodes.CheckViolation);
    }

    private DocumentIngestionService BuildService(AppDbContext db, ITextExtractionService extraction, IEmbeddingService embedding, string content) => new(
        db, new MemoryStorage(), extraction, new FixedChunking(content), new ConservativeTokenEstimator(), embedding, new NoImages(),
        Microsoft.Extensions.Options.Options.Create(new RagOptions { EmbeddingDimensions = DocumentChunk.EmbeddingDimension }),
        Microsoft.Extensions.Options.Options.Create(new OllamaOptions { Model = "pg-test" }), Microsoft.Extensions.Options.Options.Create(new GroqOptions()), NullLogger<DocumentIngestionService>.Instance);

    private async Task<Document> SeedDocumentAsync(string? priorContent = null)
    {
        await using var db = CreateDb();
        var authId = Guid.NewGuid(); await InsertAuthAsync(authId);
        var user = new User { Id = Guid.NewGuid(), RoleId = 2, SupabaseUserId = authId, Username = $"i{Guid.NewGuid():N}"[..12], FullName = "Ingestion", IsActive = true, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        var document = new Document { Id = Guid.NewGuid(), UserId = user.Id, FileName = "x.pdf", StoragePath = $"test/{Guid.NewGuid():N}", FileSizeBytes = 1, MimeType = "application/pdf", SubjectCode = "SWP391", Semester = "SU26", Status = DocumentStatus.Ready, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow, User = user };
        _authUsers.Add(authId); _users.Add(user.Id); _documents.Add(document.Id); db.AddRange(user, document);
        if (priorContent is not null) db.DocumentChunks.Add(NewChunk(document.Id, priorContent));
        await db.SaveChangesAsync(); return document;
    }

    private async Task AssertReadyChunkAsync(Guid documentId, string content)
    {
        await using var fresh = CreateDb();
        var document = await fresh.Documents.SingleAsync(x => x.Id == documentId);
        document.Status.Should().Be(DocumentStatus.Ready); document.IngestionOperationId.Should().BeNull();
        (await fresh.DocumentChunks.SingleAsync(x => x.DocumentId == documentId)).Content.Should().Be(content);
    }

    private static DocumentChunk NewChunk(Guid documentId, string content) => new() { Id = Guid.NewGuid(), DocumentId = documentId, ChunkIndex = 0, PageNumber = 1, Content = content, TokenCount = 1, Embedding = new Vector(new float[DocumentChunk.EmbeddingDimension]), EmbeddingModel = "pg-test", CreatedAt = DateTimeOffset.UtcNow };

    private static async Task MigrateCompatibilityAsync(AppDbContext db)
    {
        var applied = await db.Database.GetAppliedMigrationsAsync();
        if (!applied.Contains("20260711085101_AddVnPayExpiryAndExpiredStatus"))
        {
            if (!applied.Contains(BeforeVnPayExpiryMigration)) await db.Database.GetService<IMigrator>().MigrateAsync(BeforeVnPayExpiryMigration);
            await db.Database.ExecuteSqlRawAsync("DO $$ BEGIN IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_payment_transactions_status') THEN ALTER TABLE public.payment_transactions ADD CONSTRAINT ck_payment_transactions_status CHECK (status IN ('pending', 'completed', 'failed', 'demo_completed', 'refunded')); END IF; END $$;");
        }
        if (!(await db.Database.GetAppliedMigrationsAsync()).Contains("20260709165701_ReSyncPlanFkAndConstraints"))
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE IF EXISTS public.payment_transactions DROP CONSTRAINT IF EXISTS \"FK_payment_transactions_users_user_id\"");
        await db.Database.MigrateAsync();
    }

    private async Task BootstrapAuthAsync() { await using var c = await (_dataSource ?? throw new InvalidOperationException()).OpenConnectionAsync(); await using var cmd = new NpgsqlCommand("CREATE SCHEMA IF NOT EXISTS auth; CREATE TABLE IF NOT EXISTS auth.users (id uuid PRIMARY KEY);", c); await cmd.ExecuteNonQueryAsync(); }
    private async Task InsertAuthAsync(Guid id) { await using var c = await (_dataSource ?? throw new InvalidOperationException()).OpenConnectionAsync(); await using var cmd = new NpgsqlCommand("INSERT INTO auth.users (id) VALUES (@id)", c); cmd.Parameters.AddWithValue("id", id); await cmd.ExecuteNonQueryAsync(); }
    private async Task DeleteAuthAsync(Guid id) { await using var c = await (_dataSource ?? throw new InvalidOperationException()).OpenConnectionAsync(); await using var cmd = new NpgsqlCommand("DELETE FROM auth.users WHERE id=@id", c); cmd.Parameters.AddWithValue("id", id); await cmd.ExecuteNonQueryAsync(); }
    private AppDbContext CreateDb(params IInterceptor[] interceptors) { var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_dataSource ?? throw new InvalidOperationException(), o => o.UseVector()); if (interceptors.Length > 0) options.AddInterceptors(interceptors); return new AppDbContext(options.Options); }

    private async Task<(string Trigger, string Function)> InstallPublicationFailureTriggerAsync()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var trigger = $"trg_test_ingestion_fail_{suffix}";
        var function = $"fn_test_ingestion_fail_{suffix}";
        await using var connection = await (_dataSource ?? throw new InvalidOperationException()).OpenConnectionAsync();
        await using var command = new NpgsqlCommand($"CREATE FUNCTION public.{function}() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'test publication failure'; END; $$; CREATE TRIGGER {trigger} AFTER INSERT ON public.document_chunks FOR EACH ROW EXECUTE FUNCTION public.{function}();", connection);
        await command.ExecuteNonQueryAsync();
        return (trigger, function);
    }

    private async Task DropPublicationFailureTriggerAsync((string Trigger, string Function) names)
    {
        if (_dataSource is null) return;
        await using var connection = await _dataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand($"DROP TRIGGER IF EXISTS {names.Trigger} ON public.document_chunks; DROP FUNCTION IF EXISTS public.{names.Function}();", connection);
        await command.ExecuteNonQueryAsync();
    }

    private sealed class MemoryStorage : IDocumentStorageReadService { public Task<Stream> OpenReadAsync(Document document, CancellationToken cancellationToken = default) => Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes("file"))); }
    private sealed class StaticExtraction : ITextExtractionService { public Task<IReadOnlyList<ExtractedPage>> ExtractPagesAsync(Stream stream, string mime, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ExtractedPage>>([new(1, "text")]); }
    private sealed class WaitingExtraction(ExtractionBarrier barrier) : ITextExtractionService { public async Task<IReadOnlyList<ExtractedPage>> ExtractPagesAsync(Stream stream, string mime, CancellationToken ct = default) { barrier.Entered.TrySetResult(true); await barrier.Release.Task.WaitAsync(TimeSpan.FromSeconds(15), ct); return [new ExtractedPage(1, "text")]; } }
    private sealed class ExtractionBarrier { public TaskCompletionSource<bool> Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously); public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously); }
    private sealed class FixedChunking(string content) : IChunkingService { public IReadOnlyList<DocumentChunkDraft> Chunk(Guid documentId, IReadOnlyList<ExtractedPage> pages) => [new(documentId, 0, 1, content)]; }
    private sealed class FixedEmbedding : IEmbeddingService { public Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken ct = default) => Task.FromResult(new float[DocumentChunk.EmbeddingDimension]); }
    private sealed class ThrowingEmbedding : IEmbeddingService { public Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken ct = default) => throw new InvalidOperationException("embedding failed"); }
    private sealed class NoImages : IImageDescriptionService { public Task<string> DescribeAsync(IReadOnlyList<ExtractedImage> images, CancellationToken ct = default) => Task.FromResult(string.Empty); }
    private sealed class PublicationLockBarrier : DbCommandInterceptor
    {
        public TaskCompletionSource<bool> BeforeLock { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FOR UPDATE", StringComparison.OrdinalIgnoreCase))
            {
                BeforeLock.TrySetResult(true);
                await Release.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
            }
            return result;
        }
    }
}
