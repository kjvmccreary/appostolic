using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Appostolic.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddRolesBitmaskConstraint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Ensure only defined Roles flags (bits 0-3) are set and at least one bit is set.
            // Equivalent predicate: roles <> 0 AND (roles & ~15) = 0
            // Add idempotently (guard in case of hotfix forward-deploy).
            migrationBuilder.Sql(@"DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint c
        JOIN pg_class t ON t.oid = c.conrelid
        WHERE t.relname = 'memberships' AND c.conname = 'ck_memberships_roles_valid'
    ) THEN
        ALTER TABLE app.memberships ADD CONSTRAINT ck_memberships_roles_valid CHECK (roles <> 0 AND (roles & ~15) = 0);
    END IF;
END$$;
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint c
        JOIN pg_class t ON t.oid = c.conrelid
        WHERE t.relname = 'invitations' AND c.conname = 'ck_invitations_roles_valid'
    ) THEN
        ALTER TABLE app.invitations ADD CONSTRAINT ck_invitations_roles_valid CHECK (roles <> 0 AND (roles & ~15) = 0);
    END IF;
END$$;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE app.memberships DROP CONSTRAINT IF EXISTS ck_memberships_roles_valid;");
            migrationBuilder.Sql("ALTER TABLE app.invitations DROP CONSTRAINT IF EXISTS ck_invitations_roles_valid;");
        }
    }
}
