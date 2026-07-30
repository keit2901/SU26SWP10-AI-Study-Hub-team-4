using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AI_Study_Hub_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddFolderShareFeedbackTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE folders ADD COLUMN IF NOT EXISTS share_review_source character varying(32);
                ALTER TABLE folders ADD COLUMN IF NOT EXISTS ai_review_reason character varying(2000);
                ALTER TABLE folders ADD COLUMN IF NOT EXISTS ai_review_confidence double precision;
                ALTER TABLE folders ADD COLUMN IF NOT EXISTS ai_review_failure_count integer;
                ALTER TABLE folders ADD COLUMN IF NOT EXISTS share_submission_count integer;
                ALTER TABLE folders ADD COLUMN IF NOT EXISTS share_failure_count integer;
                ALTER TABLE folders ADD COLUMN IF NOT EXISTS human_review_reason character varying(2000);
                ALTER TABLE folders ADD COLUMN IF NOT EXISTS student_feedback_reason character varying(200);
                ALTER TABLE folders ADD COLUMN IF NOT EXISTS requires_human_review boolean;
                ALTER TABLE folders ADD COLUMN IF NOT EXISTS appeal_requested_at timestamp with time zone;
                ALTER TABLE folders ADD COLUMN IF NOT EXISTS appeal_message character varying(2000);

                UPDATE folders SET ai_review_failure_count = 0 WHERE ai_review_failure_count IS NULL;
                UPDATE folders SET share_submission_count = 0 WHERE share_submission_count IS NULL;
                UPDATE folders SET share_failure_count = 0 WHERE share_failure_count IS NULL;
                UPDATE folders SET requires_human_review = FALSE WHERE requires_human_review IS NULL;

                ALTER TABLE folders ALTER COLUMN ai_review_failure_count SET DEFAULT 0;
                ALTER TABLE folders ALTER COLUMN share_submission_count SET DEFAULT 0;
                ALTER TABLE folders ALTER COLUMN share_failure_count SET DEFAULT 0;
                ALTER TABLE folders ALTER COLUMN requires_human_review SET DEFAULT FALSE;

                ALTER TABLE folders ALTER COLUMN ai_review_failure_count SET NOT NULL;
                ALTER TABLE folders ALTER COLUMN share_submission_count SET NOT NULL;
                ALTER TABLE folders ALTER COLUMN share_failure_count SET NOT NULL;
                ALTER TABLE folders ALTER COLUMN requires_human_review SET NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE folders DROP COLUMN IF EXISTS appeal_message;
                ALTER TABLE folders DROP COLUMN IF EXISTS appeal_requested_at;
                ALTER TABLE folders DROP COLUMN IF EXISTS requires_human_review;
                ALTER TABLE folders DROP COLUMN IF EXISTS student_feedback_reason;
                ALTER TABLE folders DROP COLUMN IF EXISTS human_review_reason;
                ALTER TABLE folders DROP COLUMN IF EXISTS share_failure_count;
                ALTER TABLE folders DROP COLUMN IF EXISTS share_submission_count;
                ALTER TABLE folders DROP COLUMN IF EXISTS ai_review_failure_count;
                ALTER TABLE folders DROP COLUMN IF EXISTS ai_review_confidence;
                ALTER TABLE folders DROP COLUMN IF EXISTS ai_review_reason;
                ALTER TABLE folders DROP COLUMN IF EXISTS share_review_source;
                """);
        }
    }
}
