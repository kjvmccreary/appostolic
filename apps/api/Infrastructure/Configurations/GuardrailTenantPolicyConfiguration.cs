using Appostolic.Api.Domain.Guardrails;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Appostolic.Api.Infrastructure.Configurations;

/// <summary>
/// Entity configuration for tenant-scoped guardrail policies.
/// </summary>
public class GuardrailTenantPolicyConfiguration : IEntityTypeConfiguration<GuardrailTenantPolicy>
{
    public void Configure(EntityTypeBuilder<GuardrailTenantPolicy> b)
    {
        b.ToTable("guardrail_tenant_policies", "app");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        b.Property(x => x.Layer).HasColumnName("layer").HasConversion<int>();
    b.Property(x => x.Key).HasColumnName("policy_key").HasMaxLength(64).IsRequired();
        b.Property(x => x.Definition).HasColumnName("definition").HasColumnType("jsonb").IsRequired();
        b.Property(x => x.DerivedFromPresetId).HasColumnName("derived_from_preset_id").HasMaxLength(80);
        b.Property(x => x.Metadata).HasColumnName("metadata").HasColumnType("jsonb");
        b.Property(x => x.Version).HasColumnName("version").HasDefaultValue(1);
        b.Property(x => x.IsActive).HasColumnName("is_active").HasDefaultValue(true);
        b.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id");
        b.Property(x => x.UpdatedByUserId).HasColumnName("updated_by_user_id");
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("timezone('utc', now())");
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        b.Property(x => x.PublishedAt).HasColumnName("published_at");

        b.HasIndex(x => new { x.TenantId, x.Key })
            .IsUnique()
            .HasFilter("is_active = true")
            .HasDatabaseName("ux_guardrail_tenant_policies_active_key");

        b.HasIndex(x => new { x.TenantId, x.Layer }).HasDatabaseName("ix_guardrail_tenant_policies_layer");

        b.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
