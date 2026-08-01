using AI_Study_Hub_v2.Data;
using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Tests.Support;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace AI_Study_Hub_v2.Tests.Services;

/// <summary>
/// Opt-in migration admission checks which operate only on freshly-created disposable *_test databases.
/// </summary>
[TestFixture, Category("Postgres"), NonParallelizable]
public sealed class PayOsMigrationAdmissionPostgresTests
{
    private const string PrePayOsMigration = "20260801140000_UpdateRoleDescriptionsToEnglish";
    private const string PayOsMigration = "20260801150000_AddPayOsProviderReconciliation";
    private const long PayOsMaximumOrderCode = 9_007_199_254_740_991;

    private string _baseConnectionString = null!;

    [OneTimeSetUp]
    public void RequireDedicatedPostgresDatabase()
    {
        _baseConnectionString = Environment.GetEnvironmentVariable("AI_STUDY_HUB_TEST_POSTGRES") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(_baseConnectionString))
        {
            Assert.Ignore("AI_STUDY_HUB_TEST_POSTGRES is not configured.");
        }

        var database = new NpgsqlConnectionStringBuilder(_baseConnectionString).Database;
        if (string.IsNullOrWhiteSpace(database) || !database.EndsWith("_test", StringComparison.OrdinalIgnoreCase))
        {
            Assert.Ignore("Refusing PayOS migration admission tests outside a database ending in _test.");
        }
    }

    [Test]
    public async Task ValidLegacyOrderCode_BackfillsAndInstallsPayOsSchema()
    {
        await RunIsolatedAsync(async (db, connection, cancellationToken) =>
        {
            var paymentId = await SeedLegacyPaymentsAsync(connection, new[] { "PO_1234567890123" }, cancellationToken);

            await MigrateToPayOsAsync(db, cancellationToken);

            (await ScalarAsync<long>(connection,
                "SELECT provider_order_code FROM public.payment_transactions WHERE id = @id",
                cancellationToken,
                ("id", paymentId.Single()))).Should().Be(1_234_567_890_123);
            (await IsMigrationAppliedAsync(connection, cancellationToken)).Should().BeTrue();
            (await HasProviderOrderCodeUniqueIndexAsync(connection, cancellationToken)).Should().BeTrue();
            (await HasProviderOrderCodeRangeConstraintAsync(connection, cancellationToken)).Should().BeTrue();
        });
    }

    [Test]
    public async Task DuplicateParsedLegacyOrderCodes_RejectPayOsMigrationWithoutPartialUniqueIndex()
    {
        await RunIsolatedAsync(async (db, connection, cancellationToken) =>
        {
            await SeedLegacyPaymentsAsync(connection, new[] { "PO_123", "PO_000123" }, cancellationToken);

            var migrate = () => MigrateToPayOsAsync(db, cancellationToken);
            var migrationFailure = await migrate.Should().ThrowAsync<Exception>();
            migrationFailure.Which!.ToString().Should().Contain("PayOS");

            (await IsMigrationAppliedAsync(connection, cancellationToken)).Should().BeFalse();
            (await HasProviderOrderCodeUniqueIndexAsync(connection, cancellationToken)).Should().BeFalse();
        });
    }

    [TestCase("PO_0")]
    [TestCase("PO_9007199254740992")]
    [TestCase("PO_999999999999999999999999999999999999999999999999999999999999")]
    public async Task InvalidLegacyOrderCode_RejectsPayOsMigrationWithoutPartialUniqueIndex(string legacyReference)
    {
        await RunIsolatedAsync(async (db, connection, cancellationToken) =>
        {
            await SeedLegacyPaymentsAsync(connection, new[] { legacyReference }, cancellationToken);

            var migrate = () => MigrateToPayOsAsync(db, cancellationToken);
            var migrationFailure = await migrate.Should().ThrowAsync<Exception>();
            migrationFailure.Which!.ToString().Should().Contain("PayOS");

            (await IsMigrationAppliedAsync(connection, cancellationToken)).Should().BeFalse();
            (await HasProviderOrderCodeUniqueIndexAsync(connection, cancellationToken)).Should().BeFalse();
        });
    }

    [Test]
    public async Task ProviderOrderCodeRangeConstraint_RejectsZeroAndValuesAbovePayOsMaximum()
    {
        await RunIsolatedAsync(async (db, connection, cancellationToken) =>
        {
            var paymentId = (await SeedLegacyPaymentsAsync(connection, new[] { "PO_987654321" }, cancellationToken)).Single();
            await MigrateToPayOsAsync(db, cancellationToken);

            await AssertRangeConstraintViolationAsync(connection, paymentId, 0, cancellationToken);
            await AssertRangeConstraintViolationAsync(connection, paymentId, PayOsMaximumOrderCode + 1, cancellationToken);
        });
    }

    private async Task RunIsolatedAsync(Func<AppDbContext, NpgsqlConnection, CancellationToken, Task> assertion)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        await using var temporaryDatabase = await TemporaryDatabase.CreateAsync(_baseConnectionString, timeout.Token);
        await temporaryDatabase.InitializeAuthSchemaAsync(timeout.Token);
        await using var dataSource = CreateDataSource(temporaryDatabase.ConnectionString);
        await using var db = CreateDb(dataSource);
        await using var connection = new NpgsqlConnection(temporaryDatabase.ConnectionString);
        await connection.OpenAsync(timeout.Token);

        await PostgresTestDatabase.BootstrapToMigrationAsync(db, PrePayOsMigration, timeout.Token);
        await assertion(db, connection, timeout.Token);
    }

    private static NpgsqlDataSource CreateDataSource(string connectionString)
    {
        var builder = new NpgsqlDataSourceBuilder(connectionString);
        builder.MapEnum<DocumentStatus>(pgName: "public.document_status");
        builder.UseVector();
        return builder.Build();
    }

    private static AppDbContext CreateDb(NpgsqlDataSource dataSource)
        => new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(dataSource, npgsql => npgsql.UseVector())
            .Options);

    private static async Task MigrateToPayOsAsync(AppDbContext db, CancellationToken cancellationToken)
        => await db.Database.GetService<IMigrator>().MigrateAsync(PayOsMigration, cancellationToken);

    private static async Task<IReadOnlyList<Guid>> SeedLegacyPaymentsAsync(
        NpgsqlConnection connection,
        IEnumerable<string> legacyReferences,
        CancellationToken cancellationToken)
    {
        await using (var role = new NpgsqlCommand(
            "INSERT INTO public.roles (role_name, description) VALUES (@roleName, @description) ON CONFLICT (role_name) DO NOTHING;",
            connection))
        {
            role.Parameters.AddWithValue("roleName", "PayOsMigrationAdmission");
            role.Parameters.AddWithValue("description", "Disposable PostgreSQL migration admission role.");
            await role.ExecuteNonQueryAsync(cancellationToken);
        }

        int roleId;
        await using (var roleLookup = new NpgsqlCommand(
            "SELECT id FROM public.roles WHERE role_name = @roleName;",
            connection))
        {
            roleLookup.Parameters.AddWithValue("roleName", "PayOsMigrationAdmission");
            roleId = Convert.ToInt32(await roleLookup.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
        }

        var paymentIds = new List<Guid>();
        foreach (var legacyReference in legacyReferences)
        {
            var userId = Guid.NewGuid();
            var authUserId = Guid.NewGuid();
            var paymentId = Guid.NewGuid();

            await using (var authUser = new NpgsqlCommand("INSERT INTO auth.users (id) VALUES (@id);", connection))
            {
                authUser.Parameters.AddWithValue("id", authUserId);
                await authUser.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var user = new NpgsqlCommand("""
                INSERT INTO public.users (id, role_id, supabase_user_id, username, full_name)
                VALUES (@id, @roleId, @supabaseUserId, @username, @fullName);
                """, connection))
            {
                user.Parameters.AddWithValue("id", userId);
                user.Parameters.AddWithValue("roleId", roleId);
                user.Parameters.AddWithValue("supabaseUserId", authUserId);
                user.Parameters.AddWithValue("username", $"p{userId:N}"[..15]);
                user.Parameters.AddWithValue("fullName", "PayOS Migration Test User");
                await user.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var payment = new NpgsqlCommand("""
                INSERT INTO public.payment_transactions
                    (id, user_id, txn_ref, plan_key, billing_cycle, amount_vnd, status)
                VALUES
                    (@id, @userId, @txnRef, @planKey, @billingCycle, @amountVnd, @status);
                """, connection))
            {
                payment.Parameters.AddWithValue("id", paymentId);
                payment.Parameters.AddWithValue("userId", userId);
                payment.Parameters.AddWithValue("txnRef", legacyReference);
                payment.Parameters.AddWithValue("planKey", "admission");
                payment.Parameters.AddWithValue("billingCycle", "monthly");
                payment.Parameters.AddWithValue("amountVnd", 1L);
                payment.Parameters.AddWithValue("status", "pending");
                await payment.ExecuteNonQueryAsync(cancellationToken);
            }

            paymentIds.Add(paymentId);
        }

        return paymentIds;
    }

    private static async Task AssertRangeConstraintViolationAsync(
        NpgsqlConnection connection,
        Guid paymentId,
        long providerOrderCode,
        CancellationToken cancellationToken)
    {
        var update = async () =>
        {
            await using var command = new NpgsqlCommand(
                "UPDATE public.payment_transactions SET provider_order_code = @providerOrderCode WHERE id = @id;",
                connection);
            command.Parameters.AddWithValue("providerOrderCode", providerOrderCode);
            command.Parameters.AddWithValue("id", paymentId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        };

        var exception = await update.Should().ThrowAsync<PostgresException>();
        exception.Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
    }

    private static async Task<bool> IsMigrationAppliedAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
        => await ExistsAsync(connection, "SELECT EXISTS (SELECT 1 FROM public.\"__EFMigrationsHistory\" WHERE \"MigrationId\" = @migrationId);", cancellationToken,
            ("migrationId", PayOsMigration));

    private static async Task<bool> HasProviderOrderCodeUniqueIndexAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
        => await ExistsAsync(connection, """
            SELECT EXISTS (
                SELECT 1
                FROM pg_index AS index_definition
                JOIN pg_class AS index_class ON index_class.oid = index_definition.indexrelid
                JOIN pg_class AS table_class ON table_class.oid = index_definition.indrelid
                JOIN pg_namespace AS table_schema ON table_schema.oid = table_class.relnamespace
                WHERE table_schema.nspname = 'public'
                  AND table_class.relname = 'payment_transactions'
                  AND index_class.relname = @indexName
                  AND index_definition.indisunique
                  AND index_definition.indpred IS NOT NULL);
            """, cancellationToken, ("indexName", "ux_payment_transactions_provider_order_code"));

    private static async Task<bool> HasProviderOrderCodeRangeConstraintAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
        => await ExistsAsync(connection, """
            SELECT EXISTS (
                SELECT 1
                FROM pg_constraint AS constraint_definition
                WHERE constraint_definition.conrelid = 'public.payment_transactions'::regclass
                  AND constraint_definition.conname = @constraintName
                  AND constraint_definition.contype = 'c');
            """, cancellationToken, ("constraintName", "ck_payment_transactions_provider_order_code_range"));

    private static async Task<bool> ExistsAsync(
        NpgsqlConnection connection,
        string sql,
        CancellationToken cancellationToken,
        params (string Name, object Value)[] parameters)
        => (bool)(await ScalarAsync<object>(connection, sql, cancellationToken, parameters))!;

    private static async Task<T?> ScalarAsync<T>(
        NpgsqlConnection connection,
        string sql,
        CancellationToken cancellationToken,
        params (string Name, object Value)[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        var result = await command.ExecuteScalarAsync(cancellationToken);
        var targetType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        return result is null or DBNull ? default : (T)Convert.ChangeType(result, targetType, System.Globalization.CultureInfo.InvariantCulture);
    }

    private sealed class TemporaryDatabase : IAsyncDisposable
    {
        private readonly string _serverConnectionString;
        private readonly string _databaseName;

        private TemporaryDatabase(string serverConnectionString, string connectionString, string databaseName)
        {
            _serverConnectionString = serverConnectionString;
            ConnectionString = connectionString;
            _databaseName = databaseName;
        }

        public string ConnectionString { get; }

        public static async Task<TemporaryDatabase> CreateAsync(string baseConnectionString, CancellationToken cancellationToken)
        {
            var baseBuilder = new NpgsqlConnectionStringBuilder(baseConnectionString);
            if (string.IsNullOrWhiteSpace(baseBuilder.Database) || !baseBuilder.Database.EndsWith("_test", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("PostgreSQL migration admission tests require a base database ending in _test.");
            }

            var databaseName = $"payos_admission_{Guid.NewGuid():N}_test";
            var serverBuilder = new NpgsqlConnectionStringBuilder(baseConnectionString)
            {
                Database = "postgres",
                Pooling = false
            };
            var databaseBuilder = new NpgsqlConnectionStringBuilder(baseConnectionString)
            {
                Database = databaseName,
                Pooling = false
            };

            NpgsqlConnection.ClearAllPools();
            await using var serverConnection = new NpgsqlConnection(serverBuilder.ConnectionString);
            await serverConnection.OpenAsync(cancellationToken);
            await using var createDatabase = new NpgsqlCommand($"CREATE DATABASE {QuoteIdentifier(databaseName)};", serverConnection);
            await createDatabase.ExecuteNonQueryAsync(cancellationToken);

            return new TemporaryDatabase(serverBuilder.ConnectionString, databaseBuilder.ConnectionString, databaseName);
        }

        public async Task InitializeAuthSchemaAsync(CancellationToken cancellationToken)
        {
            await using var connection = new NpgsqlConnection(ConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand("""
                CREATE SCHEMA IF NOT EXISTS auth;
                CREATE TABLE IF NOT EXISTS auth.users (id uuid PRIMARY KEY);
                """, connection);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            NpgsqlConnection.ClearAllPools();
            await using var serverConnection = new NpgsqlConnection(_serverConnectionString);
            await serverConnection.OpenAsync(CancellationToken.None);

            await using (var terminateConnections = new NpgsqlCommand("""
                SELECT pg_terminate_backend(pid)
                FROM pg_stat_activity
                WHERE datname = @databaseName
                  AND pid <> pg_backend_pid();
                """, serverConnection))
            {
                terminateConnections.Parameters.AddWithValue("databaseName", _databaseName);
                await terminateConnections.ExecuteNonQueryAsync(CancellationToken.None);
            }

            await using var dropDatabase = new NpgsqlCommand($"DROP DATABASE IF EXISTS {QuoteIdentifier(_databaseName)};", serverConnection);
            await dropDatabase.ExecuteNonQueryAsync(CancellationToken.None);
        }

        private static string QuoteIdentifier(string value) => $"\"{value.Replace("\"", "\"\"")}\"";
    }
}
