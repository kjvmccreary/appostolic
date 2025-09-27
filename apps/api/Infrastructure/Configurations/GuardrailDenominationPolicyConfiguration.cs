using Appostolic.Api.Domain.Guardrails;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Appostolic.Api.Infrastructure.Configurations;

/// <summary>
/// Entity configuration for denomination guardrail presets.
/// </summary>
public class GuardrailDenominationPolicyConfiguration : IEntityTypeConfiguration<GuardrailDenominationPolicy>
{
    public void Configure(EntityTypeBuilder<GuardrailDenominationPolicy> b)
    {
        b.ToTable("guardrail_denomination_policies", "app");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id).HasColumnName("id").HasMaxLength(80);
        b.Property(x => x.Name).HasColumnName("name").HasMaxLength(160).IsRequired();
        b.Property(x => x.Notes).HasColumnName("notes").HasColumnType("text");
        b.Property(x => x.Definition).HasColumnName("definition").HasColumnType("jsonb").IsRequired();
        b.Property(x => x.Version).HasColumnName("version").HasDefaultValue(1);
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("timezone('utc', now())");
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at");

        b.HasIndex(x => x.Name).HasDatabaseName("ix_guardrail_denomination_policies_name");
    }
}
