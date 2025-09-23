using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Appostolic.Api.Migrations;

/// <summary>
/// Story 8: Add session enumeration columns to refresh_tokens (fingerprint, last_used_at) and supporting indexes.
/// </summary>
public partial class s8_01_refresh_sessions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "fingerprint",
            schema: "app",
            table: "refresh_tokens",
            type: "varchar(128)",
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "last_used_at",
            schema: "app",
            table: "refresh_tokens",
            type: "timestamp with time zone",
            nullable: true);

        // Helpful composite index for active session listing (exclude revoked for small result set)
    migrationBuilder.Sql(@"CREATE INDEX IF NOT EXISTS ix_refresh_tokens_active_user ON app.refresh_tokens (user_id, created_at DESC) WHERE revoked_at IS NULL;");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
    migrationBuilder.Sql(@"DROP INDEX IF EXISTS ix_refresh_tokens_active_user;");
    migrationBuilder.DropColumn(name: "fingerprint", schema: "app", table: "refresh_tokens");
    migrationBuilder.DropColumn(name: "last_used_at", schema: "app", table: "refresh_tokens");
    }
}