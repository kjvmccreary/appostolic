using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Appostolic.Api.Migrations
{
    /// <inheritdoc />
    public partial class _20250912191500_s1_12_auth_invitations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "invitations",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "text", nullable: false),
                    role = table.Column<int>(type: "integer", nullable: false),
                    token = table.Column<string>(type: "text", nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    invited_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    accepted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_invitations", x => x.id);
                    table.ForeignKey(
                        name: "FK_invitations_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "app",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_invitations_users_invited_by_user_id",
                        column: x => x.invited_by_user_id,
                        principalSchema: "app",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_invitations_invited_by_user_id",
                schema: "app",
                table: "invitations",
                column: "invited_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_invitations_tenant_id",
                schema: "app",
                table: "invitations",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "IX_invitations_token",
                schema: "app",
                table: "invitations",
                column: "token",
                unique: true);

            // Case-insensitive uniqueness per tenant on email
            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM pg_indexes 
                        WHERE schemaname = 'app' AND tablename = 'invitations' AND indexname = 'UX_invitations_tenant_email_ci'
                    ) THEN
                        CREATE UNIQUE INDEX UX_invitations_tenant_email_ci
                        ON app.invitations(tenant_id, lower(email));
                    END IF;
                END$$;
            ");

            // Optional supporting index for cleanup by tenant + expiration
            migrationBuilder.CreateIndex(
                name: "IX_invitations_tenant_expires",
                schema: "app",
                table: "invitations",
                columns: new[] { "tenant_id", "expires_at" }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS app.\"UX_invitations_tenant_email_ci\";");
            migrationBuilder.DropIndex(
                name: "IX_invitations_tenant_expires",
                schema: "app",
                table: "invitations");
            migrationBuilder.DropTable(
                name: "invitations",
                schema: "app");
        }
    }
}
