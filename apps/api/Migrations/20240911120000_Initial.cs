using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Appostolic.Api.Migrations
{
    public partial class Initial : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Ensure schema
            migrationBuilder.EnsureSchema(
                name: "app");

            // Extensions (if permissions allow) – no-op here; created by initdb

            // Tenants
            migrationBuilder.CreateTable(
                name: "tenants",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tenants", x => x.id);
                });

            // Users
            migrationBuilder.CreateTable(
                name: "users",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_users_email",
                schema: "app",
                table: "users",
                column: "email",
                unique: true);

            // Memberships
            migrationBuilder.CreateTable(
                name: "memberships",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_memberships", x => x.id);
                    table.ForeignKey(
                        name: "FK_memberships_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "app",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_memberships_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "app",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_memberships_tenant_user",
                schema: "app",
                table: "memberships",
                columns: new[] { "tenant_id", "user_id" },
                unique: true);

            // Lessons
            migrationBuilder.CreateTable(
                name: "lessons",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    audience = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lessons", x => x.id);
                    table.ForeignKey(
                        name: "FK_lessons_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "app",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_lessons_tenant_status",
                schema: "app",
                table: "lessons",
                columns: new[] { "tenant_id", "status" });

            // RLS enablement & policies
            migrationBuilder.Sql("ALTER TABLE app.memberships ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE app.lessons ENABLE ROW LEVEL SECURITY;");

            // Policy using app.tenant_id GUC set via middleware
            migrationBuilder.Sql(@"
                CREATE POLICY tenant_isolation_select ON app.lessons
                FOR SELECT USING (tenant_id = current_setting('app.tenant_id', true)::uuid);
                CREATE POLICY tenant_isolation_mod ON app.lessons
                USING (tenant_id = current_setting('app.tenant_id', true)::uuid)
                WITH CHECK (tenant_id = current_setting('app.tenant_id', true)::uuid);
            ");

            migrationBuilder.Sql(@"
                CREATE POLICY tenant_isolation_select ON app.memberships
                FOR SELECT USING (tenant_id = current_setting('app.tenant_id', true)::uuid);
                CREATE POLICY tenant_isolation_mod ON app.memberships
                USING (tenant_id = current_setting('app.tenant_id', true)::uuid)
                WITH CHECK (tenant_id = current_setting('app.tenant_id', true)::uuid);
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "lessons",
                schema: "app");
            migrationBuilder.DropTable(
                name: "memberships",
                schema: "app");
            migrationBuilder.DropTable(
                name: "users",
                schema: "app");
            migrationBuilder.DropTable(
                name: "tenants",
                schema: "app");
        }
    }
}

// Deprecated: legacy hand-written migration kept only for historical reference. Do not use.
