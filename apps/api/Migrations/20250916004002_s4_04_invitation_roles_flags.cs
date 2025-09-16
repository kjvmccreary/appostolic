using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Appostolic.Api.Migrations
{
    /// <inheritdoc />
    public partial class s4_04_invitation_roles_flags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "roles",
                schema: "app",
                table: "invitations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AlterColumn<DateTime>(
                name: "changed_at",
                schema: "app",
                table: "audits",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "timezone('utc', now())",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.CreateIndex(
                name: "IX_audits_user_id",
                schema: "app",
                table: "audits",
                column: "user_id");

            migrationBuilder.AddForeignKey(
                name: "FK_audits_tenants_tenant_id",
                schema: "app",
                table: "audits",
                column: "tenant_id",
                principalSchema: "app",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_audits_users_user_id",
                schema: "app",
                table: "audits",
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
                name: "FK_audits_tenants_tenant_id",
                schema: "app",
                table: "audits");

            migrationBuilder.DropForeignKey(
                name: "FK_audits_users_user_id",
                schema: "app",
                table: "audits");

            migrationBuilder.DropIndex(
                name: "IX_audits_user_id",
                schema: "app",
                table: "audits");

            migrationBuilder.DropColumn(
                name: "roles",
                schema: "app",
                table: "invitations");

            migrationBuilder.AlterColumn<DateTime>(
                name: "changed_at",
                schema: "app",
                table: "audits",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldDefaultValueSql: "timezone('utc', now())");
        }
    }
}
