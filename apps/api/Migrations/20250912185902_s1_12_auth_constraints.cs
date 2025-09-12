using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Appostolic.Api.Migrations
{
    /// <inheritdoc />
    public partial class s1_12_auth_constraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_tenants_name",
                schema: "app",
                table: "tenants",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_memberships_user_id",
                schema: "app",
                table: "memberships",
                column: "user_id");

            migrationBuilder.AddForeignKey(
                name: "FK_lessons_tenants_tenant_id",
                schema: "app",
                table: "lessons",
                column: "tenant_id",
                principalSchema: "app",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_memberships_tenants_tenant_id",
                schema: "app",
                table: "memberships",
                column: "tenant_id",
                principalSchema: "app",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_memberships_users_user_id",
                schema: "app",
                table: "memberships",
                column: "user_id",
                principalSchema: "app",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_lessons_tenants_tenant_id",
                schema: "app",
                table: "lessons");

            migrationBuilder.DropForeignKey(
                name: "FK_memberships_tenants_tenant_id",
                schema: "app",
                table: "memberships");

            migrationBuilder.DropForeignKey(
                name: "FK_memberships_users_user_id",
                schema: "app",
                table: "memberships");

            migrationBuilder.DropIndex(
                name: "IX_tenants_name",
                schema: "app",
                table: "tenants");

            migrationBuilder.DropIndex(
                name: "IX_memberships_user_id",
                schema: "app",
                table: "memberships");
        }
    }
}
