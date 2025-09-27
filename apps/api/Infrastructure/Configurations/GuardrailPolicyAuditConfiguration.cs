using Appostolic.Api.Domain.Guardrails;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Appostolic.Api.Infrastructure.Configurations;

/// <summary>
/// Entity configuration for guardrail policy audit entries.
/// </summary>
public class GuardrailPolicyAuditConfiguration : IEntityTypeConfiguration<GuardrailPolicyAudit>
{
    public void Configure(EntityTypeBuilder<GuardrailPolicyAudit> b)
    {
        b.ToTable("guardrail_policy_audits", "app");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.Scope).HasColumnName("scope").HasMaxLength(32).IsRequired();
        b.Property(x => x.TenantId).HasColumnName("tenant_id");
        b.Property(x => x.TenantPolicyId).HasColumnName("tenant_policy_id");
        b.Property(x => x.SystemPolicyId).HasColumnName("system_policy_id");
        b.Property(x => x.PresetId).HasColumnName("preset_id").HasMaxLength(80);
        b.Property(x => x.PolicyKey).HasColumnName("policy_key").HasMaxLength(64);
        b.Property(x => x.Layer).HasColumnName("layer").HasConversion<int?>();
        b.Property(x => x.Version).HasColumnName("version");
        b.Property(x => x.Action).HasColumnName("action").HasMaxLength(64).IsRequired();
        b.Property(x => x.ActorUserId).HasColumnName("actor_user_id");
        b.Property(x => x.SnapshotKey).HasColumnName("snapshot_key").HasMaxLength(256).IsRequired();
        b.Property(x => x.SnapshotUrl).HasColumnName("snapshot_url").HasMaxLength(512).IsRequired();
        b.Property(x => x.SnapshotHash).HasColumnName("snapshot_hash").HasMaxLength(128).IsRequired();
        b.Property(x => x.SnapshotContentType).HasColumnName("snapshot_content_type").HasMaxLength(64).HasDefaultValue("application/json");
        b.Property(x => x.DiffSummary).HasColumnName("diff_summary").HasColumnType("jsonb");
        b.Property(x => x.OccurredAt).HasColumnName("occurred_at").HasDefaultValueSql("timezone('utc', now())");

        b.HasIndex(x => x.TenantId).HasDatabaseName("ix_guardrail_policy_audits_tenant");
        b.HasIndex(x => new { x.Scope, x.OccurredAt }).HasDatabaseName("ix_guardrail_policy_audits_scope");
        b.HasIndex(x => x.TenantPolicyId).HasDatabaseName("ix_guardrail_policy_audits_tenant_policy");

        b.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.SetNull);

        b.HasOne<GuardrailTenantPolicy>()
            .WithMany()
            .HasForeignKey(x => x.TenantPolicyId)
            .OnDelete(DeleteBehavior.SetNull);

        b.HasOne<GuardrailSystemPolicy>()
            .WithMany()
            .HasForeignKey(x => x.SystemPolicyId)
            .OnDelete(DeleteBehavior.SetNull);

        b.HasOne<GuardrailDenominationPolicy>()
            .WithMany()
            .HasForeignKey(x => x.PresetId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
