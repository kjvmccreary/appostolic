using Appostolic.Api.Domain.Agents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Appostolic.Api.Infrastructure.Configurations;

public class AgentTraceConfiguration : IEntityTypeConfiguration<AgentTrace>
{
    public void Configure(EntityTypeBuilder<AgentTrace> b)
    {
        b.ToTable("agent_traces", "app");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.TaskId).HasColumnName("task_id");
        b.Property(x => x.StepNumber).HasColumnName("step_number");
        b.Property(x => x.Kind).HasColumnName("kind"); // int by default
        b.Property(x => x.Name).HasColumnName("name").HasMaxLength(120).IsRequired();
        b.Property(x => x.InputJson).HasColumnName("input_json").HasColumnType("text");
        b.Property(x => x.OutputJson).HasColumnName("output_json").HasColumnType("text");
        b.Property(x => x.DurationMs).HasColumnName("duration_ms");
        b.Property(x => x.PromptTokens).HasColumnName("prompt_tokens");
        b.Property(x => x.CompletionTokens).HasColumnName("completion_tokens");
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("timezone('utc', now())");

        b.HasIndex(x => new { x.TaskId, x.StepNumber }).IsUnique();

        b.ToTable(t =>
        {
            t.HasCheckConstraint("ck_agent_traces_step_number", "step_number >= 1");
            t.HasCheckConstraint("ck_agent_traces_duration_ms", "duration_ms >= 0");
        });
    }
}
