using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Appostolic.Api.Migrations
{
    /// <inheritdoc />
    public partial class s3_18_adjust_dedupe_index : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS app.ux_notifications_dedupe_key_active;
CREATE UNIQUE INDEX ux_notifications_dedupe_key_active ON app.notifications(dedupe_key) WHERE dedupe_key IS NOT NULL AND status IN ('Queued','Sending');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS app.ux_notifications_dedupe_key_active;
CREATE UNIQUE INDEX ux_notifications_dedupe_key_active ON app.notifications(dedupe_key) WHERE dedupe_key IS NOT NULL AND status IN ('Queued','Sending','Sent');");
        }
    }
}
