using Microsoft.EntityFrameworkCore.Migrations;
using System;

#nullable disable

namespace Appostolic.Api.Migrations
{
    /// <inheritdoc />
    public partial class s3_17_fix_dedupe_table_hotfix2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Idempotent safety: create schema/table/index only if missing
            migrationBuilder.Sql(@"
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.schemata WHERE schema_name = 'app') THEN
        EXECUTE 'CREATE SCHEMA app';
    END IF;
END$$;

-- Create table if missing
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM   information_schema.tables
        WHERE  table_schema = 'app'
        AND    table_name   = 'notification_dedupes'
    ) THEN
        EXECUTE $$
            CREATE TABLE app.notification_dedupes (
                dedupe_key varchar(200) PRIMARY KEY,
                expires_at timestamptz NOT NULL,
                created_at timestamptz NOT NULL DEFAULT timezone('utc', now()),
                updated_at timestamptz NOT NULL DEFAULT timezone('utc', now())
            );
        $$;
    END IF;
END$$;

-- Create index if missing
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_class c
        JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE c.relkind = 'i'
          AND c.relname = 'ix_notification_dedupes_expires'
          AND n.nspname = 'app'
    ) THEN
        EXECUTE 'CREATE INDEX ix_notification_dedupes_expires ON app.notification_dedupes(expires_at)';
    END IF;
END$$;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop safely (no-op if missing)
            migrationBuilder.Sql(@"
DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM   information_schema.tables
        WHERE  table_schema = 'app'
        AND    table_name   = 'notification_dedupes'
    ) THEN
        EXECUTE 'DROP TABLE app.notification_dedupes';
    END IF;
END$$;
            ");
        }
    }
}
