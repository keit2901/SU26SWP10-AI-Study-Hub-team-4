using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using AI_Study_Hub_v2.Data;
using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Options;
using AI_Study_Hub_v2.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace AI_Study_Hub_v2.Tests.Services;

[TestFixture, Category("Postgres"), NonParallelizable]
public sealed class SystemConfigServicePostgresTests
{
    private const string PreReSyncMigration = "20260706184528_AddDocumentEscalation";
    private const string ReSyncPlanMigration = "20260709165701_ReSyncPlanFkAndConstraints";
    private const string OverlapKey = "rag.semantic_overlap_tokens";
    private const string MinKey = "rag.semantic_min_tokens";
    private const string TargetKey = "rag.semantic_target_tokens";
    private const string MaxKey = "rag.semantic_max_tokens";
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(15);
    private static readonly string[] Keys = [OverlapKey, MinKey, TargetKey, MaxKey];
    private static readonly IReadOnlySet<string> KeySet = Keys.ToHashSet(StringComparer.Ordinal);

    private NpgsqlDataSource? _dataSource;
    private DbContextOptions<AppDbContext>? _options;

    [OneTimeSetUp]
    public async Task SetUpAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable("AI_STUDY_HUB_TEST_POSTGRES") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Assert.Ignore("AI_STUDY_HUB_TEST_POSTGRES is not configured.");
        }

        var database = new NpgsqlConnectionStringBuilder(connectionString).Database;
        if (string.IsNullOrWhiteSpace(database) || !database.EndsWith("_test", StringComparison.OrdinalIgnoreCase))
        {
            Assert.Ignore("Refusing PostgreSQL config tests outside a database ending in _test.");
        }

        var builder = new NpgsqlDataSourceBuilder(connectionString);
        builder.MapEnum<DocumentStatus>(pgName: "public.document_status");
        builder.UseVector();
        _dataSource = builder.Build();
        _options = BuildOptions();

        await BootstrapAuthPrerequisiteAsync();
        await using var db = CreateDb();
        await MigrateWithReSyncCompatibilityAsync(db);
    }

    [SetUp]
    public async Task SeedAsync()
    {
        await using var db = CreateDb();
        await DeleteFixtureRowsAsync(db);
        db.SystemConfigs.AddRange(Configs());
        await db.SaveChangesAsync();
        (await db.SystemConfigs.CountAsync(item => Keys.Contains(item.Key))).Should().Be(4);
    }

    [TearDown]
    public async Task CleanupAsync()
    {
        if (_options is null)
        {
            return;
        }

        await using var db = CreateDb();
        await DeleteFixtureRowsAsync(db);
    }

    [OneTimeTearDown]
    public async Task DisposeAsync()
    {
        if (_dataSource is not null)
        {
            await _dataSource.DisposeAsync();
        }

        _dataSource = null;
    }

    [Test]
    public async Task ExistingSerializableTransaction_DoesNotNestAndCommitsValidUpdate()
    {
        await using var db = CreateDb();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var service = new SystemConfigService(db, new AuditLogService(db));

        await service.UpdateValueAsync(MinKey, "80", "pg@test");
        await transaction.CommitAsync();

        (await db.SystemConfigs.SingleAsync(item => item.Key == MinKey)).Value.Should().Be("80");
    }

    [Test]
    public async Task ExistingReadCommittedTransaction_RejectsWithoutMutation()
    {
        await using var db = CreateDb();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        var action = () => new SystemConfigService(db, new AuditLogService(db)).UpdateValueAsync(MinKey, "80", "pg@test");

        var error = await action.Should().ThrowAsync<AdminException>();

        error.Which.StatusCode.Should().Be(409);
        error.Which.Code.Should().Be("config_update_conflict");
        (await db.SystemConfigs.SingleAsync(item => item.Key == MinKey)).Value.Should().Be("72");
        await transaction.RollbackAsync();
    }

    [Test]
    public async Task ConcurrentSemanticUpdates_WriteSkewIsRejectedAndOnlyWinnerIsAudited()
    {
        var barrier = new SemanticSelectBarrier(Keys, TestTimeout);
        var interceptorA = new SemanticSelectBarrierInterceptor("A", barrier);
        var interceptorB = new SemanticSelectBarrierInterceptor("B", barrier);
        await using var dbA = CreateDb(interceptorA);
        await using var dbB = CreateDb(interceptorB);
        var serviceA = new SystemConfigService(dbA, new AuditLogService(dbA));
        var serviceB = new SystemConfigService(dbB, new AuditLogService(dbB));

        var updateA = CaptureAsync(() => serviceA.UpdateValueAsync(MinKey, "120", "race-a@test"));
        var updateB = CaptureAsync(() => serviceB.UpdateValueAsync(TargetKey, "100", "race-b@test"));

        try
        {
            await WaitForBothSelectionsOrEarlyCompletionAsync(barrier, updateA, updateB);
            barrier.HitCount.Should().Be(2);
            barrier.SerializableTransactionCount.Should().Be(2);
            barrier.ConnectionProcessIds.Should().OnlyHaveUniqueItems().And.HaveCount(2);
            interceptorA.HitCount.Should().Be(1);
            interceptorB.HitCount.Should().Be(1);
        }
        finally
        {
            barrier.Release();
        }

        var outcomes = await Task.WhenAll(updateA, updateB).WaitAsync(TestTimeout);
        var committed = outcomes.Where(outcome => outcome.Error is null).ToList();
        var rejected = outcomes.Where(outcome => outcome.Error is not null).ToList();

        committed.Should().ContainSingle();
        rejected.Should().ContainSingle();
        rejected[0].Error.Should().BeOfType<AdminException>();
        var conflict = (AdminException)rejected[0].Error!;
        conflict.StatusCode.Should().Be(409);
        conflict.Code.Should().Be("config_update_conflict");

        await using var verificationDb = CreateDb();
        var values = await verificationDb.SystemConfigs
            .AsNoTracking()
            .Where(item => Keys.Contains(item.Key))
            .ToDictionaryAsync(item => item.Key, item => int.Parse(item.Value));
        values.Should().HaveCount(4);
        var finalOptions = new RagOptions
        {
            SemanticOverlapTokens = values[OverlapKey],
            SemanticMinTokens = values[MinKey],
            SemanticTargetTokens = values[TargetKey],
            SemanticMaxTokens = values[MaxKey],
        };
        RagOptions.HasValidSemanticV2Bounds(finalOptions).Should().BeTrue();
        (values[MinKey] == 120 && values[TargetKey] == 100).Should().BeFalse();

        var audits = await FixtureAudits(verificationDb).ToListAsync();
        audits.Should().ContainSingle();
        audits[0].EntityId.Should().Be(committed[0].Result!.Key);
    }

    [Test]
    public async Task OwnedTransaction_CancellationAfterWriteCommand_RollsBackAndConnectionRemainsReusable()
    {
        var injectedCancellation = new OperationCanceledException("Injected after the semantic config write command.");
        var writeObserver = new SemanticWriteObservationInterceptor(MinKey);
        var cancellationInterceptor = new PostSaveCancellationInterceptor(MinKey, writeObserver, injectedCancellation);
        await using var db = CreateDb(writeObserver, cancellationInterceptor);
        var service = new SystemConfigService(db, new AuditLogService(db));

        var action = () => service.UpdateValueAsync(MinKey, "120", "cancel@test");

        var error = await action.Should().ThrowAsync<OperationCanceledException>();
        error.Which.Should().BeSameAs(injectedCancellation);
        writeObserver.UpdateHitCount.Should().Be(1);
        writeObserver.AuditHitCount.Should().Be(1);
        cancellationInterceptor.HitCount.Should().Be(1);

        await using (var reuseTransaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            await db.Database.ExecuteSqlRawAsync("SELECT 1");
            await reuseTransaction.RollbackAsync();
        }

        await using var verificationDb = CreateDb();
        (await verificationDb.SystemConfigs.AsNoTracking().SingleAsync(item => item.Key == MinKey)).Value.Should().Be("72");
        (await FixtureAudits(verificationDb).CountAsync()).Should().Be(0);
    }

    private AppDbContext CreateDb(params IInterceptor[] interceptors) =>
        new(interceptors.Length == 0 ? _options! : BuildOptions(interceptors));

    private DbContextOptions<AppDbContext> BuildOptions(params IInterceptor[] interceptors)
    {
        var builder = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_dataSource!, options => options.UseVector())
            .ConfigureWarnings(warnings => warnings.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));
        if (interceptors.Length > 0)
        {
            builder.AddInterceptors(interceptors);
        }

        return builder.Options;
    }

    private static IQueryable<AuditLog> FixtureAudits(AppDbContext db) =>
        db.AuditLogs.Where(log => log.Action == "CONFIG_UPDATE"
            && log.EntityType == "system_configs"
            && log.EntityId != null
            && Keys.Contains(log.EntityId));

    private static async Task DeleteFixtureRowsAsync(AppDbContext db)
    {
        db.AuditLogs.RemoveRange(await FixtureAudits(db).ToListAsync());
        db.SystemConfigs.RemoveRange(await db.SystemConfigs.Where(item => Keys.Contains(item.Key)).ToListAsync());
        await db.SaveChangesAsync();
    }

    private static async Task<UpdateOutcome> CaptureAsync(Func<Task<AI_Study_Hub_v2.Dtos.SystemConfigDto>> update)
    {
        try
        {
            return new UpdateOutcome(await update(), null);
        }
        catch (Exception exception)
        {
            return new UpdateOutcome(null, exception);
        }
    }

    private static async Task WaitForBothSelectionsOrEarlyCompletionAsync(
        SemanticSelectBarrier barrier,
        Task<UpdateOutcome> updateA,
        Task<UpdateOutcome> updateB)
    {
        var timeout = Task.Delay(TestTimeout);
        while (!barrier.BothSelectionsExecuted.IsCompleted)
        {
            var completed = await Task.WhenAny(barrier.BothSelectionsExecuted, updateA, updateB, timeout);
            if (completed == barrier.BothSelectionsExecuted)
            {
                await barrier.BothSelectionsExecuted;
                return;
            }

            if (completed == timeout)
            {
                Assert.Fail($"Timed out waiting for both semantic SELECTs. {barrier.DescribeState()}");
            }

            var outcome = await (Task<UpdateOutcome>)completed;
            Assert.Fail(
                $"An update completed before both semantic SELECTs reached the barrier. "
                + $"ResultKey={outcome.Result?.Key ?? "<none>"}; Error={outcome.Error?.GetType().Name ?? "<none>"}: {outcome.Error?.Message ?? "<none>"}. "
                + barrier.DescribeState());
        }
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

            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE IF EXISTS public.payment_transactions DROP CONSTRAINT IF EXISTS \"FK_payment_transactions_users_user_id\"");
        }

        await db.Database.MigrateAsync();
    }

    private static IEnumerable<SystemConfig> Configs() =>
    [
        new() { Key = OverlapKey, Value = "24", DefaultValue = "24", Category = "Retrieval", DisplayName = "overlap", ConfigType = "Number" },
        new() { Key = MinKey, Value = "72", DefaultValue = "72", Category = "Retrieval", DisplayName = "min", ConfigType = "Number" },
        new() { Key = TargetKey, Value = "144", DefaultValue = "144", Category = "Retrieval", DisplayName = "target", ConfigType = "Number" },
        new() { Key = MaxKey, Value = "192", DefaultValue = "192", Category = "Retrieval", DisplayName = "max", ConfigType = "Number" },
    ];

    private sealed record UpdateOutcome(AI_Study_Hub_v2.Dtos.SystemConfigDto? Result, Exception? Error);

    private sealed class SemanticSelectBarrier
    {
        private readonly HashSet<string> _keys;
        private readonly TimeSpan _timeout;
        private readonly TaskCompletionSource _bothSelectionsExecuted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ConcurrentDictionary<int, byte> _connectionProcessIds = new();
        private int _hitCount;
        private int _serializableTransactionCount;

        private readonly ConcurrentQueue<string> _diagnostics = new();

        public SemanticSelectBarrier(IEnumerable<string> keys, TimeSpan timeout)
        {
            _keys = keys.ToHashSet(StringComparer.Ordinal);
            _timeout = timeout;
        }

        public Task BothSelectionsExecuted => _bothSelectionsExecuted.Task;
        public int HitCount => Volatile.Read(ref _hitCount);
        public int SerializableTransactionCount => Volatile.Read(ref _serializableTransactionCount);
        public IReadOnlyCollection<int> ConnectionProcessIds => _connectionProcessIds.Keys.ToArray();

        public void Release() => _release.TrySetResult();

        public string DescribeState() =>
            $"Hits={HitCount}; SerializableHits={SerializableTransactionCount}; "
            + $"ProcessIds=[{string.Join(',', ConnectionProcessIds)}]; Commands=[{string.Join(" | ", _diagnostics)}]";

        public async ValueTask ArriveAsync(
            string lane,
            DbCommand command,
            CancellationToken cancellationToken)
        {
            var matched = IsSemanticFourKeySelect(command, _keys);
            _diagnostics.Enqueue(DescribeCommand(lane, command, matched));
            if (!matched)
            {
                return;
            }

            if (command.Transaction?.IsolationLevel == IsolationLevel.Serializable)
            {
                Interlocked.Increment(ref _serializableTransactionCount);
            }

            if (command.Connection is NpgsqlConnection npgsqlConnection)
            {
                _connectionProcessIds.TryAdd(npgsqlConnection.ProcessID, 0);
            }

            var hit = Interlocked.Increment(ref _hitCount);
            if (hit == 2)
            {
                _bothSelectionsExecuted.TrySetResult();
            }
            else if (hit > 2)
            {
                throw new InvalidOperationException("Semantic SELECT barrier was hit more than twice.");
            }

            await _release.Task.WaitAsync(_timeout, cancellationToken);
        }
    }

    private sealed class SemanticSelectBarrierInterceptor : DbCommandInterceptor
    {
        private readonly string _lane;
        private readonly SemanticSelectBarrier _barrier;
        private int _hitCount;

        public SemanticSelectBarrierInterceptor(string lane, SemanticSelectBarrier barrier)
        {
            _lane = lane;
            _barrier = barrier;
        }

        public int HitCount => Volatile.Read(ref _hitCount);

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (IsSemanticFourKeySelect(command, KeySet))
            {
                Interlocked.Increment(ref _hitCount);
            }

            await _barrier.ArriveAsync(_lane, command, cancellationToken);
            return result;
        }
    }

    private static string DescribeCommand(string lane, DbCommand command, bool matched)
    {
        var parameterShapes = command.Parameters.Cast<DbParameter>().Select(parameter =>
        {
            var collectionCount = parameter.Value is IEnumerable<string> values && parameter.Value is not string
                ? values.Count().ToString()
                : "n/a";
            return $"{parameter.ParameterName}:{parameter.DbType}:{parameter.Value?.GetType().Name ?? "null"}:strings={collectionCount}";
        });
        var sql = string.Join(' ', NormalizeSql(command.CommandText).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return $"lane={lane},matched={matched},pid={(command.Connection as NpgsqlConnection)?.ProcessID},params=[{string.Join(',', parameterShapes)}],sql={sql}";
    }

    private sealed class SemanticWriteObservationInterceptor : DbCommandInterceptor
    {
        private readonly string _key;
        private int _updateHitCount;
        private int _auditHitCount;

        public SemanticWriteObservationInterceptor(string key)
        {
            _key = key;
        }

        public int UpdateHitCount => Volatile.Read(ref _updateHitCount);
        public int AuditHitCount => Volatile.Read(ref _auditHitCount);
        public bool HasObservedTargetWrite => UpdateHitCount == 1 && AuditHitCount == 1;

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            ObserveTargetWrite(command);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            ObserveTargetWrite(command);
            return ValueTask.FromResult(result);
        }

        private void ObserveTargetWrite(DbCommand command)
        {
            var values = CommandStringValues(command).ToHashSet(StringComparer.Ordinal);
            if (IsSystemConfigUpdate(command.CommandText)
                && values.Contains(_key))
            {
                Interlocked.Increment(ref _updateHitCount);
            }

            if (IsConfigAuditInsert(command.CommandText)
                && values.Contains(_key)
                && values.Contains("CONFIG_UPDATE")
                && values.Contains("system_configs"))
            {
                Interlocked.Increment(ref _auditHitCount);
            }
        }
    }

    private sealed class PostSaveCancellationInterceptor : SaveChangesInterceptor
    {
        private readonly string _key;
        private readonly SemanticWriteObservationInterceptor _writeObserver;
        private readonly OperationCanceledException _exception;
        private int _hitCount;

        public PostSaveCancellationInterceptor(
            string key,
            SemanticWriteObservationInterceptor writeObserver,
            OperationCanceledException exception)
        {
            _key = key;
            _writeObserver = writeObserver;
            _exception = exception;
        }

        public int HitCount => Volatile.Read(ref _hitCount);

        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData,
            int result,
            CancellationToken cancellationToken = default)
        {
            var context = eventData.Context;
            var isTargetSave = context is not null
                && _writeObserver.HasObservedTargetWrite
                && context.ChangeTracker.Entries<SystemConfig>().Any(entry => entry.Entity.Key == _key)
                && context.ChangeTracker.Entries<AuditLog>().Any(entry =>
                    entry.Entity.Action == "CONFIG_UPDATE"
                    && entry.Entity.EntityType == "system_configs"
                    && entry.Entity.EntityId == _key);
            if (!isTargetSave || Interlocked.CompareExchange(ref _hitCount, 1, 0) != 0)
            {
                return ValueTask.FromResult(result);
            }

            throw _exception;
        }
    }

    private static bool IsSemanticFourKeySelect(DbCommand command, IReadOnlySet<string> expectedKeys)
    {
        var sql = command.CommandText;
        if (!sql.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
            || !NormalizeSql(sql).Contains("system_configs", StringComparison.OrdinalIgnoreCase)
            || NormalizeSql(sql).Contains("audit_logs", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var parameterValues = CommandStringValues(command).ToHashSet(StringComparer.Ordinal);
        if (parameterValues.SetEquals(expectedKeys))
        {
            return true;
        }

        var inlineKeyCount = expectedKeys.Count(key =>
            sql.Contains($"'{key}'", StringComparison.Ordinal));
        return inlineKeyCount == expectedKeys.Count
            && CountOccurrences(sql, "'rag.semantic_") == expectedKeys.Count;
    }

    private static int CountOccurrences(string value, string fragment)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(fragment, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += fragment.Length;
        }

        return count;
    }

    private static bool IsSystemConfigUpdate(string sql) =>
        NormalizeSql(sql).Contains("UPDATE system_configs", StringComparison.OrdinalIgnoreCase)
        || NormalizeSql(sql).Contains("UPDATE public.system_configs", StringComparison.OrdinalIgnoreCase);

    private static bool IsConfigAuditInsert(string sql) =>
        NormalizeSql(sql).Contains("INSERT INTO audit_logs", StringComparison.OrdinalIgnoreCase)
        || NormalizeSql(sql).Contains("INSERT INTO public.audit_logs", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeSql(string sql) => sql.Replace("\"", string.Empty, StringComparison.Ordinal);

    private static IEnumerable<string> CommandStringValues(DbCommand command)
    {
        foreach (DbParameter parameter in command.Parameters)
        {
            switch (parameter.Value)
            {
                case string value:
                    yield return value;
                    break;
                case IEnumerable<string> values:
                    foreach (var item in values)
                    {
                        yield return item;
                    }
                    break;
            }
        }
    }
}
