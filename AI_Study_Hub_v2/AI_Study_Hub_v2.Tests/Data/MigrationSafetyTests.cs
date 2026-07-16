using System.Reflection;
using AI_Study_Hub_v2.Migrations;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace AI_Study_Hub_v2.Tests.Migrations;

[TestFixture]
public sealed class MigrationSafetyTests
{
    [Test]
    public void ReSyncPlanFk_DropsBothPossibleExistingUserForeignKeyNames()
    {
        var migration = new ReSyncPlanFkAndConstraints();
        var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
        var up = typeof(ReSyncPlanFkAndConstraints).GetMethod(
            "Up",
            BindingFlags.Instance | BindingFlags.NonPublic);

        up.Should().NotBeNull();
        up!.Invoke(migration, [builder]);

        var sql = string.Join(
            Environment.NewLine,
            builder.Operations.OfType<SqlOperation>().Select(operation => operation.Sql));

        sql.Should().Contain("payment_transactions_user_id_fkey");
        sql.Should().Contain("FK_payment_transactions_users_user_id");
    }
}
