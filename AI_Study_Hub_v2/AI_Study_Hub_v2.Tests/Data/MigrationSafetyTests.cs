using System.Reflection;
using AI_Study_Hub_v2.Migrations;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace AI_Study_Hub_v2.Tests.Migrations;

[TestFixture]
public sealed class MigrationSafetyTests
{
    [Test]
    public void ReSyncPlanFk_IsHistoricalNoOp()
    {
        var migration = new ReSyncPlanFkAndConstraints();
        var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
        var up = typeof(ReSyncPlanFkAndConstraints).GetMethod(
            "Up",
            BindingFlags.Instance | BindingFlags.NonPublic);

        up.Should().NotBeNull();
        up!.Invoke(migration, [builder]);

        builder.Operations.Should().BeEmpty();
    }

    [Test]
    public void CompatibilityMigration_IsOrderedBetweenHistoricalNoOpAndUniqueTxnRefMigration()
    {
        var migrations = typeof(ReSyncPlanFkAndConstraints).Assembly
            .GetTypes()
            .Select(type => type.GetCustomAttribute<MigrationAttribute>())
            .Where(attribute => attribute is not null)
            .Select(attribute => attribute!.Id)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

        migrations.IndexOf("20260709165701_ReSyncPlanFkAndConstraints").Should()
            .BeLessThan(migrations.IndexOf("20260709165702_ReapplyPlanFkAndConstraintsCompatibility"));
        migrations.IndexOf("20260709165702_ReapplyPlanFkAndConstraintsCompatibility").Should()
            .BeLessThan(migrations.IndexOf("20260710162831_AddUniqueTxnRefPerUser"));
    }

    [Test]
    public void CompatibilityMigration_UsesGuardedPostgresSqlForRequiredObjectsAndBothLegacyForeignKeyNames()
    {
        var migration = new ReapplyPlanFkAndConstraintsCompatibility();
        var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
        var up = typeof(ReapplyPlanFkAndConstraintsCompatibility).GetMethod("Up", BindingFlags.Instance | BindingFlags.NonPublic);

        up!.Invoke(migration, [builder]);

        var sql = string.Join(Environment.NewLine, builder.Operations.OfType<SqlOperation>().Select(operation => operation.Sql));
        sql.Should().Contain("DO $$").And.Contain("pg_index").And.Contain("pg_constraint");
        sql.Should().Contain("IX_plans_plan_key").And.Contain("IX_user_plans_user_id")
            .And.Contain("WHERE status = 'active'").And.Contain("ck_user_plans_status")
            .And.Contain("IX_payment_transactions_plan_key")
            .And.Contain("ck_payment_transactions_amount_non_negative")
            .And.Contain("ck_payment_transactions_billing_cycle")
            .And.Contain("ck_payment_transactions_status");
        sql.Should().Contain("FK_payment_transactions_plans_plan_key").And.Contain("FK_payment_transactions_users_user_id")
            .And.Contain("payment_transactions_user_id_fkey").And.Contain("ON DELETE RESTRICT");
        sql.Should().Contain("lower(pg_get_expr").And.Contain("%status%active%")
            .And.Contain("confdeltype IN ('r', 'a')");
    }

    [Test]
    public void CompatibilityMigration_PreservesBothPreAndPostAddVnPayStatusConstraintShapes()
    {
        var migration = new ReapplyPlanFkAndConstraintsCompatibility();
        var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
        var up = typeof(ReapplyPlanFkAndConstraintsCompatibility).GetMethod("Up", BindingFlags.Instance | BindingFlags.NonPublic);

        up!.Invoke(migration, [builder]);

        var sql = string.Join(Environment.NewLine, builder.Operations.OfType<SqlOperation>().Select(operation => operation.Sql));
        sql.Should().Contain("Five statuses are valid before AddVnPay; six are valid after it")
            .And.Contain("'expired'")
            .And.Contain("IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = 'public.payment_transactions'::regclass AND conname = 'ck_payment_transactions_status')");
        sql.Should().NotContain("DROP CONSTRAINT IF EXISTS \"ck_payment_transactions_status\"");
    }

    [Test]
    public void CompatibilityMigration_IsNoOpOutsidePostgres()
    {
        var migration = new ReapplyPlanFkAndConstraintsCompatibility();
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");
        var up = typeof(ReapplyPlanFkAndConstraintsCompatibility).GetMethod("Up", BindingFlags.Instance | BindingFlags.NonPublic);

        up!.Invoke(migration, [builder]);

        builder.Operations.Should().BeEmpty();
    }

    [Test]
    public void CompatibilityMigration_FailsForInvalidDataWithoutDeletingOrRewritingRows()
    {
        var migration = new ReapplyPlanFkAndConstraintsCompatibility();
        var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
        var up = typeof(ReapplyPlanFkAndConstraintsCompatibility).GetMethod("Up", BindingFlags.Instance | BindingFlags.NonPublic);

        up!.Invoke(migration, [builder]);

        var sql = string.Join(Environment.NewLine, builder.Operations.OfType<SqlOperation>().Select(operation => operation.Sql));
        sql.Should().Contain("RAISE EXCEPTION 'Cannot apply");
        sql.Should().NotContain("DELETE FROM").And.NotContain("UPDATE public.").And.NotContain("TRUNCATE");
    }
}
