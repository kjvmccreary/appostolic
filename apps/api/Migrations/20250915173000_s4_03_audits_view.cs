using Microsoft.EntityFrameworkCore.Migrations;

namespace Appostolic.Api.Migrations
{
    public partial class s4_03_audits_view : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"CREATE OR REPLACE VIEW app.vw_audits_recent AS
SELECT id,
       tenant_id,
       user_id,
       changed_by_user_id,
       changed_by_email,
       old_roles,
       new_roles,
       changed_at
FROM app.audits;");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP VIEW IF EXISTS app.vw_audits_recent;");
        }
    }
}
