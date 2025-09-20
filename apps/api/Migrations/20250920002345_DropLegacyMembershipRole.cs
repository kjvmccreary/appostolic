using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Appostolic.Api.Migrations
{
    /// <inheritdoc />
    public partial class DropLegacyMembershipRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Defensive: ensure any rows with Roles == 0 are set to Learner (8) before adding constraint
            migrationBuilder.Sql("UPDATE app.memberships SET roles = 8 WHERE roles = 0;");
            migrationBuilder.Sql("UPDATE app.invitations SET roles = 8 WHERE roles = 0;");

            // Drop legacy single-role columns (already removed from model)
            migrationBuilder.DropColumn(
                name: "role",
                schema: "app",
                table: "memberships");

            migrationBuilder.DropColumn(
                name: "role",
                schema: "app",
                table: "invitations");

            // Enforce non-zero roles via CHECK constraints (guard if earlier hotfix already added)
            migrationBuilder.Sql(@"DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint c
        JOIN pg_class t ON t.oid = c.conrelid
        WHERE t.relname = 'memberships' AND c.conname = 'ck_memberships_roles_nonzero'
    ) THEN
        ALTER TABLE app.memberships ADD CONSTRAINT ck_memberships_roles_nonzero CHECK (roles <> 0);
    END IF;
END$$;");
            migrationBuilder.Sql(@"DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint c
        JOIN pg_class t ON t.oid = c.conrelid
        WHERE t.relname = 'invitations' AND c.conname = 'ck_invitations_roles_nonzero'
    ) THEN
        ALTER TABLE app.invitations ADD CONSTRAINT ck_invitations_roles_nonzero CHECK (roles <> 0);
    END IF;
END$$;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Remove CHECK constraints
            migrationBuilder.Sql("ALTER TABLE app.memberships DROP CONSTRAINT IF EXISTS ck_memberships_roles_nonzero;");
            migrationBuilder.Sql("ALTER TABLE app.invitations DROP CONSTRAINT IF EXISTS ck_invitations_roles_nonzero;");

            // Re-add legacy role columns (nullable to avoid forced backfill)
            migrationBuilder.AddColumn<int>(
                name: "role",
                schema: "app",
                table: "memberships",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "role",
                schema: "app",
                table: "invitations",
                type: "integer",
                nullable: true);

            // Heuristic backfill (Admin/Owner inference): TenantAdmin+Approver+Creator -> Admin (1), Creator+Learner -> Editor (2), Learner -> Viewer (3)
            migrationBuilder.Sql(@"UPDATE app.memberships SET role = CASE
                WHEN (roles & 1) <> 0 AND (roles & 2) <> 0 AND (roles & 4) <> 0 THEN 1
                WHEN (roles & 4) <> 0 AND (roles & 8) <> 0 THEN 2
                WHEN (roles & 8) <> 0 THEN 3
                ELSE NULL END WHERE role IS NULL;");
            migrationBuilder.Sql(@"UPDATE app.invitations SET role = CASE
                WHEN (roles & 1) <> 0 AND (roles & 2) <> 0 AND (roles & 4) <> 0 THEN 1
                WHEN (roles & 4) <> 0 AND (roles & 8) <> 0 THEN 2
                WHEN (roles & 8) <> 0 THEN 3
                ELSE NULL END WHERE role IS NULL;");
        }
    }
}
