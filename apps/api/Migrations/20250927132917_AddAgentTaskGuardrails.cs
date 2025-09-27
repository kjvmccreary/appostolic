using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Appostolic.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentTaskGuardrails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "guardrail_decision",
                schema: "app",
                table: "agent_tasks",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "guardrail_metadata_json",
                schema: "app",
                table: "agent_tasks",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "guardrail_decision",
                schema: "app",
                table: "agent_tasks");

            migrationBuilder.DropColumn(
                name: "guardrail_metadata_json",
                schema: "app",
                table: "agent_tasks");
        }
    }
}
