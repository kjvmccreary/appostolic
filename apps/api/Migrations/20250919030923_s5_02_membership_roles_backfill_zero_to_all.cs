using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Appostolic.Api.Migrations
{
    /// <inheritdoc />
    public partial class s5_02_membership_roles_backfill_zero_to_all : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Data backfill: elevate any legacy-introduced zero-valued roles bitmask to full access (15).
            // Context: During transition from legacy Role enum to flags bitmask, a few memberships were created
            // with roles=0 (unset). The agreed remediation is to assign all four flags (TenantAdmin|Approver|Creator|Learner = 1|2|4|8 = 15)
            // to avoid inadvertently under‑privileging affected users before final legacy column removal.
            // Safe to run multiple times; idempotent because subsequent executions find no roles=0 rows.
            migrationBuilder.Sql("UPDATE app.memberships SET roles = 15 WHERE roles = 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentional no-op: data backfill is not reversed; reverting could reintroduce invalid zero roles.
        }
    }
}
