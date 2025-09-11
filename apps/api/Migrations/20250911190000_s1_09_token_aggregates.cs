using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Appostolic.Api.Migrations
{
    /// <inheritdoc />
    public partial class s1_09_token_aggregates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "total_prompt_tokens",
                schema: "app",
                table: "agent_tasks",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "total_completion_tokens",
                schema: "app",
                table: "agent_tasks",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "estimated_cost_usd",
                schema: "app",
                table: "agent_tasks",
                type: "numeric(12,4)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "total_prompt_tokens",
                schema: "app",
                table: "agent_tasks");

            migrationBuilder.DropColumn(
                name: "total_completion_tokens",
                schema: "app",
                table: "agent_tasks");

            migrationBuilder.DropColumn(
                name: "estimated_cost_usd",
                schema: "app",
                table: "agent_tasks");
        }
    }
}
