using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Appostolic.Api.Migrations
{
    /// <inheritdoc />
    public partial class s3_21_notifications_token_hash : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "token_hash",
                schema: "app",
                table: "notifications",
                type: "varchar(128)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "token_hash",
                schema: "app",
                table: "notifications");
        }
    }
}
