using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Appostolic.Api.Migrations
{
    /// <inheritdoc />
    public partial class s5_03_roles_converge_to_flags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
                        // Converge legacy role enum -> flags bitmask for any lingering mismatches or zero values.
                        // Mapping (historical): Owner/Admin -> 15, Editor -> 12, Viewer -> 8
                        // RLS Safe: temporarily disable RLS (if enabled) to ensure bulk UPDATE succeeds atomically.
                        migrationBuilder.Sql(@"DO $$
DECLARE rls_enabled boolean;
BEGIN
    SELECT relrowsecurity INTO rls_enabled
        FROM pg_class c
        JOIN pg_namespace n ON n.oid = c.relnamespace
     WHERE n.nspname = 'app' AND c.relname = 'memberships';

    IF rls_enabled THEN
        EXECUTE 'ALTER TABLE app.memberships DISABLE ROW LEVEL SECURITY';
    END IF;

    -- Perform convergence UPDATE (idempotent)
    UPDATE app.memberships m SET roles = 
        CASE m.role 
            WHEN 0 THEN 15 -- Owner
            WHEN 1 THEN 15 -- Admin
            WHEN 2 THEN 12 -- Editor
            WHEN 3 THEN 8  -- Viewer
        END
    WHERE (m.roles = 0) OR (
        (m.role IN (0,1) AND m.roles <> 15) OR
        (m.role = 2 AND m.roles <> 12) OR
        (m.role = 3 AND m.roles <> 8)
    );

    IF rls_enabled THEN
        EXECUTE 'ALTER TABLE app.memberships ENABLE ROW LEVEL SECURITY';
    END IF;
END $$;");

                        // Add simple non-zero constraint if not already present (future tightening in Story 8)
                        migrationBuilder.Sql(@"DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'ck_memberships_roles_nonzero'
    ) THEN
        EXECUTE 'ALTER TABLE app.memberships ADD CONSTRAINT ck_memberships_roles_nonzero CHECK (roles <> 0)';
    END IF;
END $$;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Down: best-effort remove constraint; data convergence is irreversible intentionally.
            migrationBuilder.Sql("ALTER TABLE app.memberships DROP CONSTRAINT IF EXISTS ck_memberships_roles_nonzero;");
        }
    }
}
