using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Appostolic.Api.Migrations;

/// <summary>
/// Story 11: Add original_created_at column to refresh_tokens to support absolute max lifetime enforcement for sliding refresh expiration.
/// </summary>
public partial class s11_01_refresh_original_created : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(
            name: "original_created_at",
            schema: "app",
            table: "refresh_tokens",
            type: "timestamp with time zone",
            nullable: true);

        // Backfill existing rows setting original_created_at = created_at where null
        migrationBuilder.Sql("UPDATE app.refresh_tokens SET original_created_at = created_at WHERE original_created_at IS NULL;");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "original_created_at",
            schema: "app",
            table: "refresh_tokens");
    }
}
