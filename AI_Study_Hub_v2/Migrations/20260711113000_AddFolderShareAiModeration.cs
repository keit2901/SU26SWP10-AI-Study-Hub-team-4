using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AI_Study_Hub_v2.Migrations
{
    public partial class AddFolderShareAiModeration : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ai_review_reason",
                table: "folders",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "ai_review_confidence",
                table: "folders",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "appeal_requested_at",
                table: "folders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "appeal_message",
                table: "folders",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "human_review_reason",
                table: "folders",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "requires_human_review",
                table: "folders",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "share_review_source",
                table: "folders",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ai_review_reason",
                table: "folders");

            migrationBuilder.DropColumn(
                name: "ai_review_confidence",
                table: "folders");

            migrationBuilder.DropColumn(
                name: "appeal_requested_at",
                table: "folders");

            migrationBuilder.DropColumn(
                name: "appeal_message",
                table: "folders");

            migrationBuilder.DropColumn(
                name: "human_review_reason",
                table: "folders");

            migrationBuilder.DropColumn(
                name: "requires_human_review",
                table: "folders");

            migrationBuilder.DropColumn(
                name: "share_review_source",
                table: "folders");
        }
    }
}
