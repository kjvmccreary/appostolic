using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Appostolic.Api.Migrations
{
    /// <inheritdoc />
    public partial class s1_09_agent_runtime_ctx : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "request_tenant",
                schema: "app",
                table: "agent_tasks",
                type: "varchar(64)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "request_user",
                schema: "app",
                table: "agent_tasks",
                type: "varchar(200)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_agent_tasks_tenant_created",
                schema: "app",
                table: "agent_tasks",
                columns: new[] { "request_tenant", "created_at" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_agent_tasks_tenant_created",
                schema: "app",
                table: "agent_tasks");

            migrationBuilder.DropColumn(
                name: "request_tenant",
                schema: "app",
                table: "agent_tasks");

            migrationBuilder.DropColumn(
                name: "request_user",
                schema: "app",
                table: "agent_tasks");
        }
    }
}
