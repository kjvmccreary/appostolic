using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Appostolic.Api.Migrations
{
    /// <inheritdoc />
    public partial class _20250912195000_s1_12_auth_user_password : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "password_hash",
                schema: "app",
                table: "users",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "password_salt",
                schema: "app",
                table: "users",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "password_updated_at",
                schema: "app",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "password_hash",
                schema: "app",
                table: "users");

            migrationBuilder.DropColumn(
                name: "password_salt",
                schema: "app",
                table: "users");

            migrationBuilder.DropColumn(
                name: "password_updated_at",
                schema: "app",
                table: "users");
        }
    }
}
