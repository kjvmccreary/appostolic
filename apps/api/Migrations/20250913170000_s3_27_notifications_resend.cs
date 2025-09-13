using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Appostolic.Api.Migrations
{
    /// <inheritdoc />
    public partial class s3_27_notifications_resend : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "resend_of_notification_id",
                schema: "app",
                table: "notifications",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "resend_reason",
                schema: "app",
                table: "notifications",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "resend_count",
                schema: "app",
                table: "notifications",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_resend_at",
                schema: "app",
                table: "notifications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "throttle_until",
                schema: "app",
                table: "notifications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_notifications_resend_of",
                schema: "app",
                table: "notifications",
                column: "resend_of_notification_id");

            migrationBuilder.CreateIndex(
                name: "ix_notifications_to_kind_created",
                schema: "app",
                table: "notifications",
                columns: new[] { "to_email", "kind", "created_at" },
                descending: new[] { false, false, true });
            migrationBuilder.AddForeignKey(
                name: "fk_notifications_resend_of",
                schema: "app",
                table: "notifications",
                column: "resend_of_notification_id",
                principalSchema: "app",
                principalTable: "notifications",
                principalColumn: "id",
                onDelete: ReferentialAction.NoAction);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_notifications_resend_of",
                schema: "app",
                table: "notifications");
            migrationBuilder.DropForeignKey(
                name: "fk_notifications_resend_of",
                schema: "app",
                table: "notifications");

            migrationBuilder.DropIndex(
                name: "ix_notifications_to_kind_created",
                schema: "app",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "resend_of_notification_id",
                schema: "app",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "resend_reason",
                schema: "app",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "resend_count",
                schema: "app",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "last_resend_at",
                schema: "app",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "throttle_until",
                schema: "app",
                table: "notifications");
        }
    }
}
