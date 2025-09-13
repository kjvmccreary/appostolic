using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Appostolic.Api.Migrations
{
    /// <inheritdoc />
    public partial class s3_17_notification_dedupes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "app");

            migrationBuilder.CreateTable(
                name: "notification_dedupes",
                schema: "app",
                columns: table => new
                {
                    dedupe_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_dedupes", x => x.dedupe_key);
                });

            migrationBuilder.CreateIndex(
                name: "ix_notification_dedupes_expires",
                schema: "app",
                table: "notification_dedupes",
                column: "expires_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "notification_dedupes",
                schema: "app");
        }
    }
}
