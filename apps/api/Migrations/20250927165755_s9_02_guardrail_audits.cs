using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Appostolic.Api.Migrations
{
    /// <inheritdoc />
    public partial class s9_02_guardrail_audits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "guardrail_policy_audits",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    scope = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_policy_id = table.Column<Guid>(type: "uuid", nullable: true),
                    system_policy_id = table.Column<Guid>(type: "uuid", nullable: true),
                    preset_id = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    policy_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    layer = table.Column<int>(type: "integer", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: true),
                    action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    snapshot_key = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    snapshot_url = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    snapshot_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    snapshot_content_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, defaultValue: "application/json"),
                    diff_summary = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                    occurred_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_guardrail_policy_audits", x => x.id);
                    table.ForeignKey(
                        name: "FK_guardrail_policy_audits_guardrail_denomination_policies_pre~",
                        column: x => x.preset_id,
                        principalSchema: "app",
                        principalTable: "guardrail_denomination_policies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_guardrail_policy_audits_guardrail_system_policies_system_po~",
                        column: x => x.system_policy_id,
                        principalSchema: "app",
                        principalTable: "guardrail_system_policies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_guardrail_policy_audits_guardrail_tenant_policies_tenant_po~",
                        column: x => x.tenant_policy_id,
                        principalSchema: "app",
                        principalTable: "guardrail_tenant_policies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_guardrail_policy_audits_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "app",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_guardrail_policy_audits_preset_id",
                schema: "app",
                table: "guardrail_policy_audits",
                column: "preset_id");

            migrationBuilder.CreateIndex(
                name: "ix_guardrail_policy_audits_scope",
                schema: "app",
                table: "guardrail_policy_audits",
                columns: new[] { "scope", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "IX_guardrail_policy_audits_system_policy_id",
                schema: "app",
                table: "guardrail_policy_audits",
                column: "system_policy_id");

            migrationBuilder.CreateIndex(
                name: "ix_guardrail_policy_audits_tenant",
                schema: "app",
                table: "guardrail_policy_audits",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_guardrail_policy_audits_tenant_policy",
                schema: "app",
                table: "guardrail_policy_audits",
                column: "tenant_policy_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "guardrail_policy_audits",
                schema: "app");
        }
    }
}
