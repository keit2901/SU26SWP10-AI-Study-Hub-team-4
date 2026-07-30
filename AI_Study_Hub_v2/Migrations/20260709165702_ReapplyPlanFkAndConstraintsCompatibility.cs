using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AI_Study_Hub_v2.Migrations
{
    /// <inheritdoc />
    public partial class ReapplyPlanFkAndConstraintsCompatibility : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider != "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                return;
            }

            migrationBuilder.Sql("""
                DO $$
                DECLARE
                    normalized_active_predicate text;
                BEGIN
                    IF EXISTS (SELECT 1 FROM public.plans GROUP BY plan_key HAVING COUNT(*) > 1) THEN
                        RAISE EXCEPTION 'Cannot apply plan_key uniqueness: public.plans contains duplicate plan_key values.';
                    END IF;
                    IF NOT EXISTS (
                        SELECT 1 FROM pg_index i
                        JOIN pg_class t ON t.oid = i.indrelid
                        JOIN pg_namespace n ON n.oid = t.relnamespace
                        WHERE n.nspname = 'public' AND t.relname = 'plans' AND i.indisunique
                          AND i.indpred IS NULL AND i.indnkeyatts = 1
                          AND i.indkey[0] = (SELECT attnum FROM pg_attribute WHERE attrelid = t.oid AND attname = 'plan_key' AND NOT attisdropped)
                    ) THEN
                        CREATE UNIQUE INDEX "IX_plans_plan_key" ON public.plans (plan_key);
                    END IF;

                    IF EXISTS (SELECT 1 FROM public.user_plans WHERE status = 'active' GROUP BY user_id HAVING COUNT(*) > 1) THEN
                        RAISE EXCEPTION 'Cannot apply active user_plan uniqueness: public.user_plans contains multiple active plans for a user.';
                    END IF;
                    SELECT regexp_replace(lower(pg_get_expr(i.indpred, i.indrelid)), '[^a-z0-9_]+', '', 'g')
                    INTO normalized_active_predicate
                    FROM pg_index i
                    JOIN pg_class index_relation ON index_relation.oid = i.indexrelid
                    JOIN pg_class t ON t.oid = i.indrelid
                    JOIN pg_namespace n ON n.oid = t.relnamespace
                    WHERE n.nspname = 'public' AND t.relname = 'user_plans'
                      AND index_relation.relname = 'IX_user_plans_user_id';

                    IF normalized_active_predicate IS NOT NULL
                       AND NOT EXISTS (
                           SELECT 1 FROM pg_index i JOIN pg_class t ON t.oid = i.indrelid
                           JOIN pg_namespace n ON n.oid = t.relnamespace
                           WHERE n.nspname = 'public' AND t.relname = 'user_plans'
                             AND i.indisunique AND i.indpred IS NOT NULL AND i.indnkeyatts = 1
                             AND i.indkey[0] = (SELECT attnum FROM pg_attribute WHERE attrelid = t.oid AND attname = 'user_id' AND NOT attisdropped)
                             AND lower(pg_get_expr(i.indpred, i.indrelid)) LIKE '%status%active%'
                       ) THEN
                        RAISE EXCEPTION 'Cannot apply active user_plan uniqueness: IX_user_plans_user_id exists with an incompatible definition.';
                    END IF;
                    IF NOT EXISTS (
                        SELECT 1 FROM pg_index i JOIN pg_class t ON t.oid = i.indrelid
                        JOIN pg_namespace n ON n.oid = t.relnamespace
                        WHERE n.nspname = 'public' AND t.relname = 'user_plans'
                          AND i.indisunique AND i.indpred IS NOT NULL AND i.indnkeyatts = 1
                          AND i.indkey[0] = (SELECT attnum FROM pg_attribute WHERE attrelid = t.oid AND attname = 'user_id' AND NOT attisdropped)
                          AND lower(pg_get_expr(i.indpred, i.indrelid)) LIKE '%status%active%'
                    ) THEN
                        CREATE UNIQUE INDEX "IX_user_plans_user_id" ON public.user_plans (user_id) WHERE status = 'active';
                    END IF;

                    IF EXISTS (SELECT 1 FROM public.user_plans WHERE status NOT IN ('active', 'deactivated', 'expired')) THEN
                        RAISE EXCEPTION 'Cannot apply ck_user_plans_status: public.user_plans contains an unsupported status.';
                    END IF;
                    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = 'public.user_plans'::regclass AND conname = 'ck_user_plans_status') THEN
                        ALTER TABLE public.user_plans ADD CONSTRAINT ck_user_plans_status CHECK (status IN ('active', 'deactivated', 'expired'));
                    END IF;

                    IF NOT EXISTS (
                        SELECT 1 FROM pg_index i JOIN pg_class t ON t.oid = i.indrelid JOIN pg_namespace n ON n.oid = t.relnamespace
                        WHERE n.nspname = 'public' AND t.relname = 'payment_transactions' AND i.indnkeyatts = 1
                          AND i.indkey[0] = (SELECT attnum FROM pg_attribute WHERE attrelid = t.oid AND attname = 'plan_key' AND NOT attisdropped)
                    ) THEN
                        CREATE INDEX "IX_payment_transactions_plan_key" ON public.payment_transactions (plan_key);
                    END IF;

                    IF EXISTS (SELECT 1 FROM public.payment_transactions WHERE amount_vnd < 0) THEN RAISE EXCEPTION 'Cannot apply amount check: public.payment_transactions contains a negative amount.'; END IF;
                    IF EXISTS (SELECT 1 FROM public.payment_transactions WHERE billing_cycle NOT IN ('monthly', 'yearly')) THEN RAISE EXCEPTION 'Cannot apply billing cycle check: public.payment_transactions contains an unsupported billing cycle.'; END IF;
                    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = 'public.payment_transactions'::regclass AND conname = 'ck_payment_transactions_amount_non_negative') THEN ALTER TABLE public.payment_transactions ADD CONSTRAINT ck_payment_transactions_amount_non_negative CHECK (amount_vnd >= 0); END IF;
                    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = 'public.payment_transactions'::regclass AND conname = 'ck_payment_transactions_billing_cycle') THEN ALTER TABLE public.payment_transactions ADD CONSTRAINT ck_payment_transactions_billing_cycle CHECK (billing_cycle IN ('monthly', 'yearly')); END IF;

                    -- Five statuses are valid before AddVnPay; six are valid after it. Do not rewrite either shape.
                    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = 'public.payment_transactions'::regclass AND conname = 'ck_payment_transactions_status') THEN
                        IF EXISTS (SELECT 1 FROM public.payment_transactions WHERE status NOT IN ('pending', 'completed', 'failed', 'demo_completed', 'refunded', 'expired')) THEN
                            RAISE EXCEPTION 'Cannot apply status check: public.payment_transactions contains an unsupported status.';
                        END IF;
                        ALTER TABLE public.payment_transactions ADD CONSTRAINT ck_payment_transactions_status CHECK (status IN ('pending', 'completed', 'failed', 'demo_completed', 'refunded', 'expired'));
                    END IF;

                    IF EXISTS (SELECT 1 FROM public.payment_transactions tx LEFT JOIN public.plans p ON p.plan_key = tx.plan_key WHERE p.plan_key IS NULL) THEN RAISE EXCEPTION 'Cannot apply plan_key foreign key: public.payment_transactions contains an unknown plan_key.'; END IF;
                    IF EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = 'public.payment_transactions'::regclass AND contype = 'f' AND conkey::smallint[] = ARRAY[(SELECT attnum FROM pg_attribute WHERE attrelid = 'public.payment_transactions'::regclass AND attname = 'plan_key')]::smallint[] AND conname <> 'FK_payment_transactions_plans_plan_key') THEN RAISE EXCEPTION 'Cannot apply plan_key foreign key: an unexpected payment_transactions.plan_key foreign key exists.'; END IF;
                    IF EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = 'public.payment_transactions'::regclass AND conname = 'FK_payment_transactions_plans_plan_key' AND NOT (contype = 'f' AND confrelid = 'public.plans'::regclass AND conkey::smallint[] = ARRAY[(SELECT attnum FROM pg_attribute WHERE attrelid = 'public.payment_transactions'::regclass AND attname = 'plan_key')]::smallint[] AND confkey::smallint[] = ARRAY[(SELECT attnum FROM pg_attribute WHERE attrelid = 'public.plans'::regclass AND attname = 'plan_key')]::smallint[] AND confdeltype IN ('r', 'a'))) THEN ALTER TABLE public.payment_transactions DROP CONSTRAINT "FK_payment_transactions_plans_plan_key"; END IF;
                    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = 'public.payment_transactions'::regclass AND contype = 'f' AND confrelid = 'public.plans'::regclass AND conkey::smallint[] = ARRAY[(SELECT attnum FROM pg_attribute WHERE attrelid = 'public.payment_transactions'::regclass AND attname = 'plan_key')]::smallint[] AND confkey::smallint[] = ARRAY[(SELECT attnum FROM pg_attribute WHERE attrelid = 'public.plans'::regclass AND attname = 'plan_key')]::smallint[] AND confdeltype IN ('r', 'a')) THEN ALTER TABLE public.payment_transactions ADD CONSTRAINT "FK_payment_transactions_plans_plan_key" FOREIGN KEY (plan_key) REFERENCES public.plans (plan_key) ON DELETE RESTRICT; END IF;

                    IF EXISTS (SELECT 1 FROM public.payment_transactions tx LEFT JOIN public.users u ON u.id = tx.user_id WHERE u.id IS NULL) THEN RAISE EXCEPTION 'Cannot apply user_id foreign key: public.payment_transactions contains an unknown user_id.'; END IF;
                    IF EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = 'public.payment_transactions'::regclass AND contype = 'f' AND conkey::smallint[] = ARRAY[(SELECT attnum FROM pg_attribute WHERE attrelid = 'public.payment_transactions'::regclass AND attname = 'user_id')]::smallint[] AND conname NOT IN ('FK_payment_transactions_users_user_id', 'payment_transactions_user_id_fkey')) THEN RAISE EXCEPTION 'Cannot apply user_id foreign key: an unexpected payment_transactions.user_id foreign key exists.'; END IF;
                    IF EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = 'public.payment_transactions'::regclass AND conname IN ('FK_payment_transactions_users_user_id', 'payment_transactions_user_id_fkey') AND NOT (contype = 'f' AND confrelid = 'public.users'::regclass AND conkey::smallint[] = ARRAY[(SELECT attnum FROM pg_attribute WHERE attrelid = 'public.payment_transactions'::regclass AND attname = 'user_id')]::smallint[] AND confkey::smallint[] = ARRAY[(SELECT attnum FROM pg_attribute WHERE attrelid = 'public.users'::regclass AND attname = 'id')]::smallint[] AND confdeltype IN ('r', 'a'))) THEN ALTER TABLE public.payment_transactions DROP CONSTRAINT IF EXISTS "payment_transactions_user_id_fkey"; ALTER TABLE public.payment_transactions DROP CONSTRAINT IF EXISTS "FK_payment_transactions_users_user_id"; END IF;
                    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = 'public.payment_transactions'::regclass AND contype = 'f' AND confrelid = 'public.users'::regclass AND conkey::smallint[] = ARRAY[(SELECT attnum FROM pg_attribute WHERE attrelid = 'public.payment_transactions'::regclass AND attname = 'user_id')]::smallint[] AND confkey::smallint[] = ARRAY[(SELECT attnum FROM pg_attribute WHERE attrelid = 'public.users'::regclass AND attname = 'id')]::smallint[] AND confdeltype IN ('r', 'a')) THEN ALTER TABLE public.payment_transactions ADD CONSTRAINT "FK_payment_transactions_users_user_id" FOREIGN KEY (user_id) REFERENCES public.users (id) ON DELETE RESTRICT; END IF;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Compatibility state may predate this migration; do not remove it on rollback.
        }
    }
}
