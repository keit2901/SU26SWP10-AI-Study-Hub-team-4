using AI_Study_Hub_v2.Data;
using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Tests.Support;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace AI_Study_Hub_v2.Tests.Services;

/// <summary>Opt-in PostgreSQL admission checks. Requires a disposable *_test database.</summary>
[TestFixture, Category("Postgres"), NonParallelizable]
public sealed class PayOsReconciliationPostgresTests
{
    private string _connectionString = null!;

    [OneTimeSetUp]
    public void RequireDedicatedTestDatabase()
    {
        _connectionString = Environment.GetEnvironmentVariable("AI_STUDY_HUB_TEST_POSTGRES") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(_connectionString)) Assert.Ignore("AI_STUDY_HUB_TEST_POSTGRES is not configured.");
        var database = new NpgsqlConnectionStringBuilder(_connectionString).Database;
        if (string.IsNullOrWhiteSpace(database) || !database.EndsWith("_test", StringComparison.OrdinalIgnoreCase))
            Assert.Ignore("Refusing PayOS PostgreSQL admission tests outside a database ending in _test.");
    }

    [Test]
    public async Task MigrationDiscoveryAndCleanBootstrap_ContainOrderedPayOsSchema()
    {
        await using var dataSource = CreateDataSource();
        await using var db = CreateDb(dataSource);
        await db.Database.ExecuteSqlRawAsync("""
            CREATE SCHEMA IF NOT EXISTS auth;
            CREATE TABLE IF NOT EXISTS auth.users (id uuid PRIMARY KEY);
            """);
        var migrations = db.Database.GetService<IMigrationsAssembly>().Migrations.Keys.ToList();
        migrations.IndexOf("20260801140000_UpdateRoleDescriptionsToEnglish").Should().BeGreaterThanOrEqualTo(0);
        migrations.IndexOf("20260801150000_AddPayOsProviderReconciliation")
            .Should().BeGreaterThan(migrations.IndexOf("20260801140000_UpdateRoleDescriptionsToEnglish"));

        await PostgresTestDatabase.BootstrapAsync(db, new CancellationTokenSource(TimeSpan.FromSeconds(90)).Token);
        var applied = await db.Database.GetAppliedMigrationsAsync();
        applied.Should().Contain("20260801150000_AddPayOsProviderReconciliation");
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        foreach (var column in new[] { "provider_order_code", "provider_payment_link_id", "provider_status" })
            (await ScalarAsync(connection, "SELECT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='payment_transactions' AND column_name=@name)", column)).Should().Be("True");
        (await ScalarAsync(connection, "SELECT EXISTS (SELECT 1 FROM pg_indexes WHERE schemaname='public' AND indexname='ux_payment_transactions_provider_order_code')", null)).Should().Be("True");
        (await ScalarAsync(connection, "SELECT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid='public.payment_transactions'::regclass AND conname='ck_payment_transactions_provider_order_code_range')", null)).Should().Be("True");
    }

    private NpgsqlDataSource CreateDataSource()
    {
        var builder = new NpgsqlDataSourceBuilder(_connectionString);
        builder.MapEnum<DocumentStatus>(pgName: "public.document_status");
        builder.UseVector();
        return builder.Build();
    }

    private static AppDbContext CreateDb(NpgsqlDataSource dataSource)
        => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(dataSource, npgsql => npgsql.UseVector()).Options);

    private static async Task<string?> ScalarAsync(NpgsqlConnection connection, string sql, string? name)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        if (name is not null) command.Parameters.AddWithValue("name", name);
        return (await command.ExecuteScalarAsync())?.ToString();
    }
}
