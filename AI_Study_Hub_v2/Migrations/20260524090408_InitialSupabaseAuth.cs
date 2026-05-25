using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace AI_Study_Hub_v2.Migrations
{
    /// <inheritdoc />
    public partial class InitialSupabaseAuth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:pgcrypto", ",,");

            migrationBuilder.CreateTable(
                name: "roles",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    role_name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_roles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    role_id = table.Column<int>(type: "integer", nullable: false),
                    supabase_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    username = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: false),
                    full_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    total_tokens_used = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.id);
                    table.ForeignKey(
                        name: "FK_users_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "roles",
                columns: new[] { "id", "created_at", "description", "role_name" },
                values: new object[,]
                {
                    { 1, new DateTimeOffset(new DateTime(2026, 5, 23, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "Quản trị viên hệ thống, có quyền điều phối nhân sự, kiểm duyệt tài liệu và thay đổi tham số cấu hình AI", "Admin" },
                    { 2, new DateTimeOffset(new DateTime(2026, 5, 23, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "Sinh viên khai thác tài nguyên học tập cá nhân, thực hiện hội thoại RAG và tham gia kiểm tra ôn tập", "Student" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_roles_role_name",
                table: "roles",
                column: "role_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_users_role_id",
                table: "users",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "IX_users_supabase_user_id",
                table: "users",
                column: "supabase_user_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_users_username",
                table: "users",
                column: "username",
                unique: true);

            // Phase 1 deviations from plain EF scaffolding: pgvector for Phase 2,
            // FK from public.users.supabase_user_id -> auth.users(id) ON DELETE CASCADE,
            // and RLS bare bones (service role bypasses by default).
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS vector;");

            migrationBuilder.Sql(@"
                ALTER TABLE public.users
                ADD CONSTRAINT FK_users_auth_users_supabase_user_id
                FOREIGN KEY (supabase_user_id)
                REFERENCES auth.users(id)
                ON DELETE CASCADE;");

            migrationBuilder.Sql("ALTER TABLE public.users ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE public.roles ENABLE ROW LEVEL SECURITY;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE IF EXISTS public.users DISABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE IF EXISTS public.roles DISABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE IF EXISTS public.users DROP CONSTRAINT IF EXISTS FK_users_auth_users_supabase_user_id;");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "roles");
        }
    }
}
