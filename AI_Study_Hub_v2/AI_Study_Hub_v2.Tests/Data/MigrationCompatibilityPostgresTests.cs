using System.Reflection;
using AI_Study_Hub_v2.Migrations;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql;

namespace AI_Study_Hub_v2.Tests.Migrations;

[TestFixture, Category("Postgres"), NonParallelizable]
public sealed class MigrationCompatibilityPostgresTests
{
    [Test]
    public Task PreAddVnPay_CompatibilityConvergesTwice_AndLaterSixStatusConstraintCanApplyAsync() =>
        WithFixtureAsync(async (connection, transaction) =>
        {
            await AddCheckAsync(connection, transaction, "ck_payment_transactions_status", "status IN ('pending', 'completed', 'failed', 'demo_completed', 'refunded')");
            await ExecuteCompatibilityAsync(connection, transaction);
            await ExecuteCompatibilityAsync(connection, transaction);

            await ExecuteAsync(connection, transaction, "ALTER TABLE public.payment_transactions DROP CONSTRAINT ck_payment_transactions_status; ALTER TABLE public.payment_transactions ADD CONSTRAINT ck_payment_transactions_status CHECK (status IN ('pending', 'completed', 'failed', 'demo_completed', 'refunded', 'expired')); ");
            await ExecuteAsync(connection, transaction, "INSERT INTO public.payment_transactions (id, user_id, plan_key, amount_vnd, billing_cycle, status) VALUES ('00000000-0000-0000-0000-000000000002', '00000000-0000-0000-0000-000000000001', 'free', 0, 'monthly', 'expired');");

            Assert.That(await ScalarAsync<long>(connection, transaction, "SELECT COUNT(*) FROM public.payment_transactions WHERE status = 'expired';"), Is.EqualTo(1));
        });

    [Test]
    public Task PostAddVnPay_ExpiredRowAndLaterHistoryArePreservedAsync() =>
        WithFixtureAsync(async (connection, transaction) =>
        {
            await AddCheckAsync(connection, transaction, "ck_payment_transactions_status", "status IN ('pending', 'completed', 'failed', 'demo_completed', 'refunded', 'expired')");
            await ExecuteAsync(connection, transaction, "CREATE TABLE public.\"__EFMigrationsHistory\" (\"MigrationId\" varchar(150) PRIMARY KEY, \"ProductVersion\" varchar(32) NOT NULL); INSERT INTO public.\"__EFMigrationsHistory\" VALUES ('20260711085101_AddVnPayExpiryAndExpiredStatus', '8.0.10'); INSERT INTO public.payment_transactions (id, user_id, plan_key, amount_vnd, billing_cycle, status) VALUES ('00000000-0000-0000-0000-000000000002', '00000000-0000-0000-0000-000000000001', 'free', 0, 'monthly', 'expired');");

            await ExecuteCompatibilityAsync(connection, transaction);
            await ExecuteCompatibilityAsync(connection, transaction);

            Assert.That(await ScalarAsync<long>(connection, transaction, "SELECT COUNT(*) FROM public.payment_transactions WHERE status = 'expired';"), Is.EqualTo(1));
            Assert.That(await ScalarAsync<long>(connection, transaction, "SELECT COUNT(*) FROM public.\"__EFMigrationsHistory\" WHERE \"MigrationId\" = '20260711085101_AddVnPayExpiryAndExpiredStatus';"), Is.EqualTo(1));
            Assert.That(await ScalarAsync<string>(connection, transaction, "SELECT pg_get_constraintdef(oid) FROM pg_constraint WHERE conrelid = 'public.payment_transactions'::regclass AND conname = 'ck_payment_transactions_status';"), Does.Contain("expired"));
        });

    [Test]
    public Task PostgreSqlNormalizedActivePredicate_IsRecognizedOnRepeatExecutionAsync() =>
        WithFixtureAsync(async (connection, transaction) =>
        {
            await ExecuteAsync(connection, transaction, "CREATE UNIQUE INDEX \"IX_user_plans_user_id\" ON public.user_plans (user_id) WHERE status = 'active';");

            await ExecuteCompatibilityAsync(connection, transaction);
            await ExecuteCompatibilityAsync(connection, transaction);

            const string compatibleIndexCount = """
                SELECT COUNT(*)
                FROM pg_index i
                JOIN pg_class t ON t.oid = i.indrelid
                JOIN pg_namespace n ON n.oid = t.relnamespace
                WHERE n.nspname = 'public' AND t.relname = 'user_plans'
                  AND i.indisunique AND i.indpred IS NOT NULL AND i.indnkeyatts = 1
                  AND i.indkey[0] = (SELECT attnum FROM pg_attribute WHERE attrelid = t.oid AND attname = 'user_id')
                  AND lower(pg_get_expr(i.indpred, i.indrelid)) LIKE '%status%active%';
                """;
            Assert.That(await ScalarAsync<long>(connection, transaction, compatibleIndexCount), Is.EqualTo(1));
        });

    [TestCase("payment_transactions_user_id_fkey")]
    [TestCase("FK_payment_transactions_users_user_id")]
    public Task KnownLegacyUserForeignKeys_ConvergeWithoutDataLossAsync(string legacyName) =>
        WithFixtureAsync(async (connection, transaction) =>
        {
            await ExecuteAsync(connection, transaction, $"ALTER TABLE public.payment_transactions ADD CONSTRAINT \"{legacyName}\" FOREIGN KEY (user_id) REFERENCES public.users (id) ON DELETE NO ACTION;");
            await ExecuteCompatibilityAsync(connection, transaction);
            await ExecuteCompatibilityAsync(connection, transaction);

            const string userForeignKeyCount = """
                SELECT COUNT(*)
                FROM pg_constraint c
                WHERE c.conrelid = 'public.payment_transactions'::regclass
                  AND c.contype = 'f' AND c.confrelid = 'public.users'::regclass
                  AND c.confdeltype IN ('r', 'a')
                  AND c.conkey::smallint[] = ARRAY[(SELECT attnum FROM pg_attribute WHERE attrelid = 'public.payment_transactions'::regclass AND attname = 'user_id')]::smallint[]
                  AND c.confkey::smallint[] = ARRAY[(SELECT attnum FROM pg_attribute WHERE attrelid = 'public.users'::regclass AND attname = 'id')]::smallint[];
                """;
            Assert.That(await ScalarAsync<long>(connection, transaction, userForeignKeyCount), Is.EqualTo(1));
            Assert.That(await ScalarAsync<long>(connection, transaction, "SELECT COUNT(*) FROM public.payment_transactions;"), Is.EqualTo(1));
        });

    [Test]
    public Task RequiredObjectsAndPlanForeignKey_AreAddedWithoutRewritingInputRowsAsync() =>
        WithFixtureAsync(async (connection, transaction) =>
        {
            await ExecuteCompatibilityAsync(connection, transaction);

            Assert.That(await ScalarAsync<long>(connection, transaction, "SELECT COUNT(*) FROM public.payment_transactions;"), Is.EqualTo(1));
            Assert.That(await ScalarAsync<long>(connection, transaction, "SELECT COUNT(*) FROM pg_constraint WHERE conrelid = 'public.payment_transactions'::regclass AND conname IN ('ck_payment_transactions_amount_non_negative', 'ck_payment_transactions_billing_cycle', 'ck_payment_transactions_status', 'FK_payment_transactions_plans_plan_key');"), Is.EqualTo(4));
            Assert.That(await ScalarAsync<long>(connection, transaction, "SELECT COUNT(*) FROM pg_indexes WHERE schemaname = 'public' AND tablename = 'payment_transactions' AND indexname = 'IX_payment_transactions_plan_key';"), Is.EqualTo(1));
        });

    [Test]
    public Task InvalidData_FailsAndLeavesRowsUnchangedAsync() =>
        WithFixtureAsync(async (connection, transaction) =>
        {
            await ExecuteAsync(connection, transaction, "UPDATE public.payment_transactions SET amount_vnd = -1;");
            await ExecuteAsync(connection, transaction, "SAVEPOINT invalid_data;");

            Assert.ThrowsAsync<PostgresException>(async () => await ExecuteCompatibilityAsync(connection, transaction));
            await ExecuteAsync(connection, transaction, "ROLLBACK TO SAVEPOINT invalid_data;");
            Assert.That(await ScalarAsync<long>(connection, transaction, "SELECT COUNT(*) FROM public.payment_transactions WHERE amount_vnd = -1;"), Is.EqualTo(1));
        });

    private static async Task WithFixtureAsync(Func<NpgsqlConnection, NpgsqlTransaction, Task> test)
    {
        var connectionString = Environment.GetEnvironmentVariable("AI_STUDY_HUB_TEST_POSTGRES") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Assert.Ignore("AI_STUDY_HUB_TEST_POSTGRES is not configured.");
        }

        var database = new NpgsqlConnectionStringBuilder(connectionString).Database;
        if (string.IsNullOrWhiteSpace(database) || !database.EndsWith("_test", StringComparison.OrdinalIgnoreCase))
        {
            Assert.Ignore("Refusing compatibility migration tests outside a database ending in _test.");
        }

        await using var dataSource = new NpgsqlDataSourceBuilder(connectionString).Build();
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        try
        {
            await ExecuteAsync(connection, transaction, FixtureSql);
            await test(connection, transaction);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    private static async Task ExecuteCompatibilityAsync(NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        var migration = new ReapplyPlanFkAndConstraintsCompatibility();
        var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
        var up = typeof(ReapplyPlanFkAndConstraintsCompatibility).GetMethod("Up", BindingFlags.Instance | BindingFlags.NonPublic);
        up!.Invoke(migration, [builder]);
        await ExecuteAsync(connection, transaction, builder.Operations.OfType<SqlOperation>().Single().Sql);
    }

    private static Task AddCheckAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string name, string expression) =>
        ExecuteAsync(connection, transaction, $"ALTER TABLE public.payment_transactions ADD CONSTRAINT \"{name}\" CHECK ({expression});");

    private static async Task ExecuteAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<T> ScalarAsync<T>(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        return (T)(await command.ExecuteScalarAsync())!;
    }

    private const string FixtureSql = """
        DROP TABLE IF EXISTS public.payment_transactions CASCADE;
        DROP TABLE IF EXISTS public.user_plans CASCADE;
        DROP TABLE IF EXISTS public.plans CASCADE;
        DROP TABLE IF EXISTS public.users CASCADE;
        DROP TABLE IF EXISTS public."__EFMigrationsHistory" CASCADE;
        CREATE TABLE public.plans (id uuid PRIMARY KEY, plan_key varchar(50) NOT NULL);
        CREATE TABLE public.users (id uuid PRIMARY KEY);
        CREATE TABLE public.user_plans (id uuid PRIMARY KEY, user_id uuid NOT NULL, status varchar(32) NOT NULL);
        CREATE TABLE public.payment_transactions (id uuid PRIMARY KEY, user_id uuid NOT NULL, plan_key varchar(50) NOT NULL, amount_vnd bigint NOT NULL, billing_cycle varchar(32) NOT NULL, status varchar(32) NOT NULL);
        INSERT INTO public.plans VALUES ('00000000-0000-0000-0000-000000000001', 'free');
        INSERT INTO public.users VALUES ('00000000-0000-0000-0000-000000000001');
        INSERT INTO public.user_plans VALUES ('00000000-0000-0000-0000-000000000001', '00000000-0000-0000-0000-000000000001', 'active');
        INSERT INTO public.payment_transactions VALUES ('00000000-0000-0000-0000-000000000001', '00000000-0000-0000-0000-000000000001', 'free', 0, 'monthly', 'pending');
        """;
}
