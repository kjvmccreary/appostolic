using Appostolic.Api.Domain.Guardrails;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Appostolic.Api.Infrastructure.Configurations;

/// <summary>
/// Entity configuration for per-user guardrail preferences.
/// </summary>
public class GuardrailUserPreferenceConfiguration : IEntityTypeConfiguration<GuardrailUserPreference>
{
    public void Configure(EntityTypeBuilder<GuardrailUserPreference> b)
    {
        b.ToTable("guardrail_user_preferences", "app");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        b.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
        b.Property(x => x.Preferences).HasColumnName("preferences").HasColumnType("jsonb").IsRequired();
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("timezone('utc', now())");
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        b.Property(x => x.LastAppliedAt).HasColumnName("last_applied_at");

        b.HasIndex(x => new { x.TenantId, x.UserId }).IsUnique().HasDatabaseName("ux_guardrail_user_preferences_tenant_user");

        b.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
