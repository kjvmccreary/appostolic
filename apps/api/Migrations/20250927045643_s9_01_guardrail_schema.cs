using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Appostolic.Api.Migrations
{
    /// <inheritdoc />
    public partial class s9_01_guardrail_schema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "guardrail_denomination_policies",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    notes = table.Column<string>(type: "text", nullable: true),
                    definition = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_guardrail_denomination_policies", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "guardrail_system_policies",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    slug = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    definition = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_guardrail_system_policies", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "guardrail_tenant_policies",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    layer = table.Column<int>(type: "integer", nullable: false),
                    policy_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    definition = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                    derived_from_preset_id = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    metadata = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    published_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_guardrail_tenant_policies", x => x.id);
                    table.ForeignKey(
                        name: "FK_guardrail_tenant_policies_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "app",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "guardrail_user_preferences",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    preferences = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_applied_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_guardrail_user_preferences", x => x.id);
                    table.ForeignKey(
                        name: "FK_guardrail_user_preferences_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "app",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_guardrail_user_preferences_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "app",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_guardrail_denomination_policies_name",
                schema: "app",
                table: "guardrail_denomination_policies",
                column: "name");

            migrationBuilder.CreateIndex(
                name: "ux_guardrail_system_policies_slug",
                schema: "app",
                table: "guardrail_system_policies",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_guardrail_tenant_policies_layer",
                schema: "app",
                table: "guardrail_tenant_policies",
                columns: new[] { "tenant_id", "layer" });

            migrationBuilder.CreateIndex(
                name: "ux_guardrail_tenant_policies_active_key",
                schema: "app",
                table: "guardrail_tenant_policies",
                columns: new[] { "tenant_id", "policy_key" },
                unique: true,
                filter: "is_active = true");

            migrationBuilder.CreateIndex(
                name: "IX_guardrail_user_preferences_user_id",
                schema: "app",
                table: "guardrail_user_preferences",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ux_guardrail_user_preferences_tenant_user",
                schema: "app",
                table: "guardrail_user_preferences",
                columns: new[] { "tenant_id", "user_id" },
                unique: true);

            migrationBuilder.Sql("ALTER TABLE app.guardrail_tenant_policies ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE app.guardrail_user_preferences ENABLE ROW LEVEL SECURITY;");

            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE schemaname = 'app' AND tablename = 'guardrail_tenant_policies' AND policyname = 'tenant_isolation_select') THEN
                        CREATE POLICY tenant_isolation_select ON app.guardrail_tenant_policies
                        FOR SELECT USING (tenant_id = current_setting('app.tenant_id', true)::uuid);
                    END IF;
                    IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE schemaname = 'app' AND tablename = 'guardrail_tenant_policies' AND policyname = 'tenant_isolation_mod') THEN
                        CREATE POLICY tenant_isolation_mod ON app.guardrail_tenant_policies
                        USING (tenant_id = current_setting('app.tenant_id', true)::uuid)
                        WITH CHECK (tenant_id = current_setting('app.tenant_id', true)::uuid);
                    END IF;
                END$$;
            ");

            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE schemaname = 'app' AND tablename = 'guardrail_user_preferences' AND policyname = 'tenant_isolation_select') THEN
                        CREATE POLICY tenant_isolation_select ON app.guardrail_user_preferences
                        FOR SELECT USING (tenant_id = current_setting('app.tenant_id', true)::uuid);
                    END IF;
                    IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE schemaname = 'app' AND tablename = 'guardrail_user_preferences' AND policyname = 'tenant_isolation_mod') THEN
                        CREATE POLICY tenant_isolation_mod ON app.guardrail_user_preferences
                        USING (tenant_id = current_setting('app.tenant_id', true)::uuid)
                        WITH CHECK (tenant_id = current_setting('app.tenant_id', true)::uuid);
                    END IF;
                END$$;
            ");

            migrationBuilder.Sql(@"
                INSERT INTO app.guardrail_system_policies (id, slug, name, description, definition, version)
                VALUES (
                    'a0b64fd3-15d6-4fd8-94ea-6ae2bc497acf',
                    'system-core',
                    'System Baseline Guardrail',
                    'Baseline doctrinal guardrail applied before denomination and tenant overrides.',
                    jsonb_build_object(
                        'version', 1,
                        'mergeOrder', jsonb_build_array('system','denomination','tenant','override','user'),
                        'deny', jsonb_build_array('deny:heretical-content','deny:hate-speech'),
                        'allow', jsonb_build_array('allow:creedal-core','allow:orthodox-scripture'),
                        'escalate', jsonb_build_array('escalate:ambiguous-case')
                    ),
                    1
                )
                ON CONFLICT (slug) DO NOTHING;
            ");

            migrationBuilder.Sql(@"
                INSERT INTO app.guardrail_denomination_policies (id, name, notes, definition, version)
                VALUES
                    ('mere-christianity', 'Mere Christianity (Core Orthodoxy)', $$Baseline orthodox Nicene/Chalcedonian consensus suitable as a neutral preset; emphasizes core creedal doctrines, excludes denominational distinctives.$$, jsonb_build_object('version', 1, 'inherits', 'system-core', 'allow', jsonb_build_array(), 'deny', jsonb_build_array(), 'escalate', jsonb_build_array()), 1),
                    ('baptist', 'Baptist', $$Believer's baptism by immersion, congregational governance, high view of Scripture, symbolic ordinances.$$, jsonb_build_object('version', 1, 'inherits', 'system-core', 'allow', jsonb_build_array('allow:baptism-believers'), 'deny', jsonb_build_array(), 'escalate', jsonb_build_array()), 1),
                    ('methodist', 'Methodist / Wesleyan', $$Emphasis on sanctification (holiness), prevenient grace, structured discipleship, connectional polity.$$, jsonb_build_object('version', 1, 'inherits', 'system-core', 'allow', jsonb_build_array('allow:holiness-emphasis'), 'deny', jsonb_build_array(), 'escalate', jsonb_build_array()), 1),
                    ('presbyterian', 'Presbyterian / Reformed', $$Eldership governance, covenant theology, confessional standards (e.g., Westminster), emphasis on God's sovereignty.$$, jsonb_build_object('version', 1, 'inherits', 'system-core', 'allow', jsonb_build_array('allow:confessional-standards'), 'deny', jsonb_build_array(), 'escalate', jsonb_build_array()), 1),
                    ('anglican', 'Anglican', $$Via media liturgical tradition, Book of Common Prayer, episcopal structure, creedal orthodoxy with broad evangelical/catholic/charismatic streams.$$, jsonb_build_object('version', 1, 'inherits', 'system-core', 'allow', jsonb_build_array('allow:liturgical-expression'), 'deny', jsonb_build_array(), 'escalate', jsonb_build_array()), 1),
                    ('pentecostal', 'Pentecostal', $$Continuing charismatic gifts, emphasis on Spirit baptism/empowerment, vibrant worship, global mission focus.$$, jsonb_build_object('version', 1, 'inherits', 'system-core', 'allow', jsonb_build_array('allow:charismatic-expression'), 'deny', jsonb_build_array(), 'escalate', jsonb_build_array()), 1),
                    ('lutheran', 'Lutheran', $$Justification by faith central, Law/Gospel distinction, sacramental (Word & Sacrament means of grace), liturgical continuity.$$, jsonb_build_object('version', 1, 'inherits', 'system-core', 'allow', jsonb_build_array('allow:law-gospel-distinction'), 'deny', jsonb_build_array(), 'escalate', jsonb_build_array()), 1),
                    ('adventist', 'Adventist (Seventh-day)', $$Seventh-day Sabbath observance, Second Advent expectancy, holistic health emphasis, great controversy metanarrative.$$, jsonb_build_object('version', 1, 'inherits', 'system-core', 'allow', jsonb_build_array('allow:sabbath-emphasis'), 'deny', jsonb_build_array(), 'escalate', jsonb_build_array()), 1),
                    ('anabaptist', 'Anabaptist', $$Believer's baptism, community discipleship, peace witness (nonviolence), simple living, counter-cultural ethic.$$, jsonb_build_object('version', 1, 'inherits', 'system-core', 'allow', jsonb_build_array('allow:peace-witness'), 'deny', jsonb_build_array(), 'escalate', jsonb_build_array()), 1),
                    ('eastern-orthodox', 'Eastern Orthodox', $$Historic liturgy, theosis (deification) emphasis, patristic continuity, sacramental life, icons as theology in color.$$, jsonb_build_object('version', 1, 'inherits', 'system-core', 'allow', jsonb_build_array('allow:theosis-language'), 'deny', jsonb_build_array(), 'escalate', jsonb_build_array()), 1)
                ON CONFLICT (id) DO NOTHING;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "guardrail_denomination_policies",
                schema: "app");

            migrationBuilder.DropTable(
                name: "guardrail_system_policies",
                schema: "app");

            migrationBuilder.DropTable(
                name: "guardrail_tenant_policies",
                schema: "app");

            migrationBuilder.DropTable(
                name: "guardrail_user_preferences",
                schema: "app");
        }
    }
}
