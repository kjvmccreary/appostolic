using Appostolic.Api.Domain.Agents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Appostolic.Api.Infrastructure.Configurations;

public class AgentTaskConfiguration : IEntityTypeConfiguration<AgentTask>
{
    public void Configure(EntityTypeBuilder<AgentTask> b)
    {
        b.ToTable("agent_tasks", "app");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.AgentId).HasColumnName("agent_id");
        b.Property(x => x.InputJson).HasColumnName("input_json").HasColumnType("text");
        b.Property(x => x.Status).HasColumnName("status").HasConversion<string>();
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("timezone('utc', now())");
        b.Property(x => x.StartedAt).HasColumnName("started_at");
        b.Property(x => x.FinishedAt).HasColumnName("finished_at");
        b.Property(x => x.ResultJson).HasColumnName("result_json").HasColumnType("text");
        b.Property(x => x.ErrorMessage).HasColumnName("error_message").HasColumnType("text");

        b.HasIndex(x => new { x.AgentId, x.CreatedAt })
          .IsDescending(false, true)
          .HasDatabaseName("ix_agent_tasks_agent_created");

        b.HasIndex(x => new { x.Status, x.CreatedAt })
          .IsDescending(false, true)
          .HasDatabaseName("ix_agent_tasks_status_created");
    }
}
