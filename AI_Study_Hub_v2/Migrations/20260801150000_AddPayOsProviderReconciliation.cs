using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AI_Study_Hub_v2.Migrations
{
    public partial class AddPayOsProviderReconciliation : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(name: "ck_payment_transactions_status", table: "payment_transactions");
            migrationBuilder.AddColumn<long>(name: "provider_order_code", table: "payment_transactions", type: "bigint", nullable: true);
            migrationBuilder.AddColumn<string>(name: "provider_payment_link_id", table: "payment_transactions", type: "character varying(128)", maxLength: 128, nullable: true);
            migrationBuilder.AddColumn<string>(name: "provider_status", table: "payment_transactions", type: "character varying(64)", maxLength: 64, nullable: true);

            // Only exact legacy PO_<digits> references are eligible. Numeric is arbitrary
            // precision here, so a malformed/overflowing legacy value fails before bigint cast.
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (
                        SELECT 1 FROM payment_transactions
                        WHERE provider_order_code IS NULL
                          AND txn_ref ~ '^PO_[0-9]+$'
                          AND (substring(txn_ref from '^PO_([0-9]+)$')::numeric <= 0
                               OR substring(txn_ref from '^PO_([0-9]+)$')::numeric > 9007199254740991)
                    ) THEN
                        RAISE EXCEPTION 'Cannot backfill PayOS order code: PO_<digits> value is zero, negative, or exceeds 9007199254740991.';
                    END IF;
                    UPDATE payment_transactions
                    SET provider_order_code = substring(txn_ref from '^PO_([0-9]+)$')::bigint
                    WHERE provider_order_code IS NULL AND txn_ref ~ '^PO_[0-9]+$';
                    IF EXISTS (SELECT 1 FROM payment_transactions WHERE provider_order_code IS NOT NULL GROUP BY provider_order_code HAVING count(*) > 1) THEN
                        RAISE EXCEPTION 'Cannot add PayOS provider order-code uniqueness: duplicate parsed PO_<digits> transaction references exist.';
                    END IF;
                END $$;
                """);
            migrationBuilder.AddCheckConstraint(name: "ck_payment_transactions_provider_order_code_range", table: "payment_transactions", sql: "provider_order_code IS NULL OR provider_order_code BETWEEN 1 AND 9007199254740991");
            migrationBuilder.CreateIndex(name: "ux_payment_transactions_provider_order_code", table: "payment_transactions", column: "provider_order_code", unique: true, filter: "provider_order_code IS NOT NULL");
            migrationBuilder.AddCheckConstraint(name: "ck_payment_transactions_status", table: "payment_transactions", sql: "status IN ('pending', 'completed', 'failed', 'demo_completed', 'refunded', 'expired', 'cancelled')");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "ux_payment_transactions_provider_order_code", table: "payment_transactions");
            migrationBuilder.DropCheckConstraint(name: "ck_payment_transactions_provider_order_code_range", table: "payment_transactions");
            migrationBuilder.DropCheckConstraint(name: "ck_payment_transactions_status", table: "payment_transactions");
            // The pre-reconciliation schema has no cancelled status; rollback maps it to failed.
            migrationBuilder.Sql("UPDATE payment_transactions SET status = 'failed' WHERE status = 'cancelled';");
            migrationBuilder.DropColumn(name: "provider_order_code", table: "payment_transactions");
            migrationBuilder.DropColumn(name: "provider_payment_link_id", table: "payment_transactions");
            migrationBuilder.DropColumn(name: "provider_status", table: "payment_transactions");
            migrationBuilder.AddCheckConstraint(name: "ck_payment_transactions_status", table: "payment_transactions", sql: "status IN ('pending', 'completed', 'failed', 'demo_completed', 'refunded', 'expired')");
        }
    }
}
