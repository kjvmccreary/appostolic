using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Appostolic.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddRlsPolicies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE app.memberships ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE app.lessons ENABLE ROW LEVEL SECURITY;");

            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE schemaname = 'app' AND tablename = 'lessons' AND policyname = 'tenant_isolation_select') THEN
                        CREATE POLICY tenant_isolation_select ON app.lessons
                        FOR SELECT USING (tenant_id = current_setting('app.tenant_id', true)::uuid);
                    END IF;
                    IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE schemaname = 'app' AND tablename = 'lessons' AND policyname = 'tenant_isolation_mod') THEN
                        CREATE POLICY tenant_isolation_mod ON app.lessons
                        USING (tenant_id = current_setting('app.tenant_id', true)::uuid)
                        WITH CHECK (tenant_id = current_setting('app.tenant_id', true)::uuid);
                    END IF;
                END$$;
            ");

            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE schemaname = 'app' AND tablename = 'memberships' AND policyname = 'tenant_isolation_select') THEN
                        CREATE POLICY tenant_isolation_select ON app.memberships
                        FOR SELECT USING (tenant_id = current_setting('app.tenant_id', true)::uuid);
                    END IF;
                    IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE schemaname = 'app' AND tablename = 'memberships' AND policyname = 'tenant_isolation_mod') THEN
                        CREATE POLICY tenant_isolation_mod ON app.memberships
                        USING (tenant_id = current_setting('app.tenant_id', true)::uuid)
                        WITH CHECK (tenant_id = current_setting('app.tenant_id', true)::uuid);
                    END IF;
                END$$;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE app.memberships DISABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE app.lessons DISABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation_select ON app.lessons;");
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation_mod ON app.lessons;");
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation_select ON app.memberships;");
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation_mod ON app.memberships;");
        }
    }
}
