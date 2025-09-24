using Microsoft.EntityFrameworkCore.Migrations;
using System;

namespace Appostolic.Api.Migrations
{
    /// <summary>
    /// Story 17/18: Add device_name column to refresh_tokens table.
    /// </summary>
    public partial class s17_18_device_name : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "device_name",
                schema: "app",
                table: "refresh_tokens",
                type: "varchar(120)",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "device_name",
                schema: "app",
                table: "refresh_tokens");
        }
    }
}