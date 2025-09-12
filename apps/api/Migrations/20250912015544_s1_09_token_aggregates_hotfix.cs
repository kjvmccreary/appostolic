using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Appostolic.Api.Migrations
{
    /// <inheritdoc />
    public partial class s1_09_token_aggregates_hotfix : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add missing roll-up columns if they are not present yet
            migrationBuilder.Sql(@"DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'app' AND table_name = 'agent_tasks' AND column_name = 'total_prompt_tokens'
    ) THEN
        ALTER TABLE app.agent_tasks ADD COLUMN total_prompt_tokens integer NOT NULL DEFAULT 0;
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'app' AND table_name = 'agent_tasks' AND column_name = 'total_completion_tokens'
    ) THEN
        ALTER TABLE app.agent_tasks ADD COLUMN total_completion_tokens integer NOT NULL DEFAULT 0;
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'app' AND table_name = 'agent_tasks' AND column_name = 'estimated_cost_usd'
    ) THEN
        ALTER TABLE app.agent_tasks ADD COLUMN estimated_cost_usd numeric(12,4) NULL;
    END IF;
END $$;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop the columns if present (safe for down migration when rolling back)
            migrationBuilder.Sql(@"DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'app' AND table_name = 'agent_tasks' AND column_name = 'estimated_cost_usd'
    ) THEN
        ALTER TABLE app.agent_tasks DROP COLUMN estimated_cost_usd;
    END IF;

    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'app' AND table_name = 'agent_tasks' AND column_name = 'total_completion_tokens'
    ) THEN
        ALTER TABLE app.agent_tasks DROP COLUMN total_completion_tokens;
    END IF;

    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'app' AND table_name = 'agent_tasks' AND column_name = 'total_prompt_tokens'
    ) THEN
        ALTER TABLE app.agent_tasks DROP COLUMN total_prompt_tokens;
    END IF;
END $$;");
        }
    }
}
