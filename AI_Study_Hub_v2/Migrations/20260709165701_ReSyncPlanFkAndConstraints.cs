using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AI_Study_Hub_v2.Migrations
{
    /// <inheritdoc />
    public partial class ReSyncPlanFkAndConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Actual FK name in local DB follows PG lowercase convention,
            // not EF Core convention. Use IF EXISTS for safety.
            migrationBuilder.Sql(
                "ALTER TABLE payment_transactions DROP CONSTRAINT IF EXISTS \"payment_transactions_user_id_fkey\"");
            migrationBuilder.Sql(
                "ALTER TABLE payment_transactions DROP CONSTRAINT IF EXISTS \"FK_payment_transactions_users_user_id\"");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_plans_plan_key",
                table: "plans",
                column: "plan_key");

            migrationBuilder.CreateIndex(
                name: "IX_user_plans_user_id",
                table: "user_plans",
                column: "user_id",
                unique: true,
                filter: "status = 'active'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_user_plans_status",
                table: "user_plans",
                sql: "status IN ('active', 'deactivated', 'expired')");

            migrationBuilder.CreateIndex(
                name: "IX_payment_transactions_plan_key",
                table: "payment_transactions",
                column: "plan_key");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payment_transactions_amount_non_negative",
                table: "payment_transactions",
                sql: "amount_vnd >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payment_transactions_billing_cycle",
                table: "payment_transactions",
                sql: "billing_cycle IN ('monthly', 'yearly')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payment_transactions_status",
                table: "payment_transactions",
                sql: "status IN ('pending', 'completed', 'failed', 'demo_completed', 'refunded')");

            migrationBuilder.AddForeignKey(
                name: "FK_payment_transactions_plans_plan_key",
                table: "payment_transactions",
                column: "plan_key",
                principalTable: "plans",
                principalColumn: "plan_key",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_payment_transactions_users_user_id",
                table: "payment_transactions",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_payment_transactions_plans_plan_key",
                table: "payment_transactions");

            migrationBuilder.DropForeignKey(
                name: "FK_payment_transactions_users_user_id",
                table: "payment_transactions");

            migrationBuilder.DropIndex(
                name: "IX_user_plans_user_id",
                table: "user_plans");

            migrationBuilder.DropCheckConstraint(
                name: "ck_user_plans_status",
                table: "user_plans");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_plans_plan_key",
                table: "plans");

            migrationBuilder.DropIndex(
                name: "IX_payment_transactions_plan_key",
                table: "payment_transactions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payment_transactions_amount_non_negative",
                table: "payment_transactions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payment_transactions_billing_cycle",
                table: "payment_transactions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payment_transactions_status",
                table: "payment_transactions");

            migrationBuilder.AddForeignKey(
                name: "FK_payment_transactions_users_user_id",
                table: "payment_transactions",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
