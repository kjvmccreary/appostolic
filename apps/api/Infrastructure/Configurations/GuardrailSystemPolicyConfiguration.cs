using Appostolic.Api.Domain.Guardrails;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Appostolic.Api.Infrastructure.Configurations;

/// <summary>
/// Entity configuration for global system-level guardrail policies.
/// </summary>
public class GuardrailSystemPolicyConfiguration : IEntityTypeConfiguration<GuardrailSystemPolicy>
{
    public void Configure(EntityTypeBuilder<GuardrailSystemPolicy> b)
    {
        b.ToTable("guardrail_system_policies", "app");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.Slug).HasColumnName("slug").HasMaxLength(120).IsRequired();
        b.Property(x => x.Name).HasColumnName("name").HasMaxLength(160).IsRequired();
        b.Property(x => x.Description).HasColumnName("description").HasColumnType("text");
        b.Property(x => x.Definition).HasColumnName("definition").HasColumnType("jsonb").IsRequired();
        b.Property(x => x.Version).HasColumnName("version").HasDefaultValue(1);
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("timezone('utc', now())");
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at");

        b.HasIndex(x => x.Slug).IsUnique().HasDatabaseName("ux_guardrail_system_policies_slug");
    }
}
