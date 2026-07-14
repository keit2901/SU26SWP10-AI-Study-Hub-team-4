using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AI_Study_Hub_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddUniqueTxnRefPerUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Actual index name in local DB follows PG lowercase convention.
            migrationBuilder.Sql(
                "DROP INDEX IF EXISTS \"ix_payment_transactions_txn_ref\"");

            migrationBuilder.CreateIndex(
                name: "IX_payment_transactions_txn_ref_user_id",
                table: "payment_transactions",
                columns: new[] { "txn_ref", "user_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_payment_transactions_txn_ref_user_id",
                table: "payment_transactions");

            migrationBuilder.CreateIndex(
                name: "IX_payment_transactions_txn_ref",
                table: "payment_transactions",
                column: "txn_ref");
        }
    }
}
