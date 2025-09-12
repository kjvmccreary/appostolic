using Appostolic.Api.Domain.Agents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Appostolic.Api.Infrastructure.Configurations;

public class AgentConfiguration : IEntityTypeConfiguration<Agent>
{
    public void Configure(EntityTypeBuilder<Agent> b)
    {
        b.ToTable("agents", "app");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.Name).HasColumnName("name").HasMaxLength(120).IsRequired();
        b.Property(x => x.SystemPrompt).HasColumnName("system_prompt").HasColumnType("text");
        b.Property(x => x.ToolAllowlist).HasColumnName("tool_allowlist").HasColumnType("jsonb");
        b.Property(x => x.Model).HasColumnName("model").HasMaxLength(80).IsRequired();
        b.Property(x => x.Temperature).HasColumnName("temperature");
        b.Property(x => x.MaxSteps).HasColumnName("max_steps");
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("timezone('utc', now())");
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at");
    b.Property(x => x.IsEnabled).HasColumnName("is_enabled").HasDefaultValue(true);

        // Indexes
        b.HasIndex(x => x.Name).IsUnique();

        // Check constraints
        b.ToTable(t =>
        {
            t.HasCheckConstraint("ck_agents_max_steps_range", "max_steps BETWEEN 1 AND 50");
            t.HasCheckConstraint("ck_agents_temperature_range", "temperature >= 0 AND temperature <= 2");
        });
    }
}
