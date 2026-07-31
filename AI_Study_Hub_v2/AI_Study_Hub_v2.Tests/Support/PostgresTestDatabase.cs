using AI_Study_Hub_v2.Data;
using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace AI_Study_Hub_v2.Tests.Support;

/// <summary>
/// Applies the application migration history to the opt-in PostgreSQL test database.
/// Compatibility repairs here are intentionally test-only until the historical migration
/// chain can be corrected without changing deployed databases.
/// </summary>
internal static class PostgresTestDatabase
{
    private const string PreReSyncMigration = "20260706184528_AddDocumentEscalation";
    private const string ReSyncPlanMigration = "20260709165701_ReSyncPlanFkAndConstraints";
    private const string PaymentConstraintPrerequisiteMigration = "20260710162831_AddUniqueTxnRefPerUser";
    private const string VnPayExpiryMigration = "20260711085101_AddVnPayExpiryAndExpiredStatus";

    // A stable bigint serializes all test-suite migration/bootstrap callers sharing this database.
    private const long BootstrapAdvisoryLockKey = 7_306_748_913_024_681;

    public static async Task BootstrapAsync(AppDbContext db, CancellationToken cancellationToken = default)
    {
        EnsureNoActiveTransaction(db);

        var lockConnection = new NpgsqlConnection(GetDedicatedTestConnectionString());
        var lockAcquired = false;

        try
        {
            await lockConnection.OpenAsync(cancellationToken);
            await ExecuteAdvisoryLockCommandAsync(lockConnection, "SELECT pg_advisory_lock(@lockKey)", cancellationToken);
            lockAcquired = true;

            var migrator = db.Database.GetService<IMigrator>();
            var appliedMigrations = await db.Database.GetAppliedMigrationsAsync(cancellationToken);
            await CloseBootstrapDatabaseConnectionIfOpenAsync(db, cancellationToken);
            if (!appliedMigrations.Contains(PaymentConstraintPrerequisiteMigration))
            {
                if (!appliedMigrations.Contains(ReSyncPlanMigration))
                {
                    if (!appliedMigrations.Contains(PreReSyncMigration))
                    {
                        await CloseBootstrapDatabaseConnectionIfOpenAsync(db, cancellationToken);
                        await migrator.MigrateAsync(PreReSyncMigration, cancellationToken);
                    }

                    // Historical test databases can retain this differently-cased FK. Keep the
                    // repair isolated to disposable test schemas before advancing migrations.
                    await db.Database.ExecuteSqlRawAsync(
                        "ALTER TABLE IF EXISTS public.payment_transactions DROP CONSTRAINT IF EXISTS \"FK_payment_transactions_users_user_id\"",
                        cancellationToken);
                    await CloseBootstrapDatabaseConnectionIfOpenAsync(db, cancellationToken);
                }

                await CloseBootstrapDatabaseConnectionIfOpenAsync(db, cancellationToken);
                await migrator.MigrateAsync(PaymentConstraintPrerequisiteMigration, cancellationToken);

            }

            appliedMigrations = await db.Database.GetAppliedMigrationsAsync(cancellationToken);
            await CloseBootstrapDatabaseConnectionIfOpenAsync(db, cancellationToken);
            if (!appliedMigrations.Contains(VnPayExpiryMigration))
            {
                await db.Database.ExecuteSqlRawAsync("""
                    DO $$
                    BEGIN
                        IF to_regclass('public.payment_transactions') IS NOT NULL
                            AND NOT EXISTS (
                                SELECT 1
                                FROM pg_constraint
                                WHERE conrelid = to_regclass('public.payment_transactions')
                                    AND conname = 'ck_payment_transactions_status') THEN
                            ALTER TABLE public.payment_transactions
                                ADD CONSTRAINT ck_payment_transactions_status
                                CHECK (status IN ('pending', 'completed', 'failed', 'demo_completed', 'refunded'));
                        END IF;
                    END $$;
                    """, cancellationToken);
                await CloseBootstrapDatabaseConnectionIfOpenAsync(db, cancellationToken);
            }

            await CloseBootstrapDatabaseConnectionIfOpenAsync(db, cancellationToken);
            await migrator.MigrateAsync(cancellationToken: cancellationToken);

            // The current model contains these folder fields before a production migration adds
            // them. This aligns only disposable test schemas with the EF model.
            await db.Database.ExecuteSqlRawAsync("""
                ALTER TABLE IF EXISTS public.folders
                    ADD COLUMN IF NOT EXISTS is_favorite boolean NOT NULL DEFAULT false,
                    ADD COLUMN IF NOT EXISTS share_review_source varchar(32) NULL,
                    ADD COLUMN IF NOT EXISTS ai_review_reason varchar(2000) NULL,
                    ADD COLUMN IF NOT EXISTS ai_review_confidence double precision NULL,
                    ADD COLUMN IF NOT EXISTS ai_review_failure_count integer NOT NULL DEFAULT 0,
                    ADD COLUMN IF NOT EXISTS share_submission_count integer NOT NULL DEFAULT 0,
                    ADD COLUMN IF NOT EXISTS share_failure_count integer NOT NULL DEFAULT 0,
                    ADD COLUMN IF NOT EXISTS human_review_reason varchar(2000) NULL,
                    ADD COLUMN IF NOT EXISTS student_feedback_reason character varying(200) NULL,
                    ADD COLUMN IF NOT EXISTS requires_human_review boolean NOT NULL DEFAULT false,
                    ADD COLUMN IF NOT EXISTS appeal_requested_at timestamp with time zone NULL,
                    ADD COLUMN IF NOT EXISTS appeal_message varchar(2000) NULL;
                """, cancellationToken);
        }
        finally
        {
            try
            {
                if (lockAcquired)
                {
                    await ExecuteAdvisoryLockCommandAsync(lockConnection, "SELECT pg_advisory_unlock(@lockKey)", CancellationToken.None);
                }
            }
            finally
            {
                await lockConnection.DisposeAsync();
            }
        }
    }

    private static void EnsureNoActiveTransaction(AppDbContext db)
    {
        if (db.Database.CurrentTransaction is not null)
        {
            throw new InvalidOperationException(
                "PostgreSQL test database bootstrap must run outside an active DbContext transaction.");
        }
    }

    private static async Task CloseBootstrapDatabaseConnectionIfOpenAsync(
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        EnsureNoActiveTransaction(db);

        if (db.Database.GetDbConnection().State != ConnectionState.Closed)
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    private static async Task ExecuteAdvisoryLockCommandAsync(
        NpgsqlConnection connection,
        string commandText,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(commandText, connection);
        command.Parameters.AddWithValue("lockKey", BootstrapAdvisoryLockKey);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string GetDedicatedTestConnectionString()
    {
        var connectionString = Environment.GetEnvironmentVariable("AI_STUDY_HUB_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("AI_STUDY_HUB_TEST_POSTGRES must be configured for PostgreSQL test migration bootstrap.");
        }

        var database = new NpgsqlConnectionStringBuilder(connectionString).Database;
        if (string.IsNullOrWhiteSpace(database) || !database.EndsWith("_test", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("PostgreSQL test migration bootstrap requires a database ending in _test.");
        }

        return connectionString;
    }
}
