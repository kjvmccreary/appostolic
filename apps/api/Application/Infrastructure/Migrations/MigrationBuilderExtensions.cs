using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Appostolic.Api.Application.Infrastructure.Migrations;

/// <summary>
/// Helper extensions to enforce explicit schema usage for migrations touching core tables.
/// Use in future migrations to avoid forgetting schema: "app".
/// Example: migrationBuilder.AddAppColumn("refresh_tokens", ...);
/// </summary>
public static class MigrationBuilderExtensions
{
    private const string AppSchema = "app";

    /// <summary>
    /// Adds a column to a table in the core app schema.
    /// </summary>
    public static void AddAppColumn<T>(this MigrationBuilder builder,
        string table,
        string name,
        string type,
        bool nullable = true)
    {
        builder.AddColumn<T>(
            name: name,
            schema: AppSchema,
            table: table,
            type: type,
            nullable: nullable);
    }

    /// <summary>
    /// Drops a column from a table in the core app schema.
    /// </summary>
    public static void DropAppColumn(this MigrationBuilder builder, string table, string name)
    {
        builder.DropColumn(name: name, schema: AppSchema, table: table);
    }

    /// <summary>
    /// Returns fully qualified table name (schema.table) for use in raw SQL (e.g., CREATE INDEX) to avoid search_path reliance.
    /// </summary>
    public static string AppTable(this string tableName) => $"{AppSchema}.{tableName}";
}
