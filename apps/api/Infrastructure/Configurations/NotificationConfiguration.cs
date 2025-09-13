using Appostolic.Api.Domain.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Appostolic.Api.Infrastructure.Configurations;

public class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> b)
    {
        b.ToTable("notifications", "app");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.Kind).HasColumnName("kind").HasConversion<string>();
        b.Property(x => x.ToEmail).HasColumnName("to_email").HasColumnType("citext").IsRequired();
        b.Property(x => x.ToName).HasColumnName("to_name").HasColumnType("text");
        b.Property(x => x.Subject).HasColumnName("subject").HasColumnType("text");
        b.Property(x => x.BodyHtml).HasColumnName("body_html").HasColumnType("text");
        b.Property(x => x.BodyText).HasColumnName("body_text").HasColumnType("text");
        b.Property(x => x.DataJson).HasColumnName("data_json").HasColumnType("jsonb").IsRequired();
        b.Property(x => x.TenantId).HasColumnName("tenant_id");
        b.Property(x => x.DedupeKey).HasColumnName("dedupe_key").HasMaxLength(200);
        b.Property(x => x.Status).HasColumnName("status").HasConversion<string>();
        b.Property(x => x.AttemptCount).HasColumnName("attempt_count").HasColumnType("smallint");
        b.Property(x => x.NextAttemptAt).HasColumnName("next_attempt_at");
        b.Property(x => x.LastError).HasColumnName("last_error").HasColumnType("text");
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("timezone('utc', now())");
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("timezone('utc', now())");
        b.Property(x => x.SentAt).HasColumnName("sent_at");

        // Indexes
        b.HasIndex(x => new { x.Status, x.NextAttemptAt })
            .HasDatabaseName("ix_notifications_status_next_attempt");

        b.HasIndex(x => new { x.TenantId, x.CreatedAt })
            .IsDescending(false, true)
            .HasDatabaseName("ix_notifications_tenant_created");

        b.HasIndex(x => x.CreatedAt)
            .IsDescending(true)
            .HasDatabaseName("ix_notifications_created_desc");

        // Partial unique index on dedupe_key where status in ('Queued','Sending','Sent')
        b.HasIndex(x => x.DedupeKey)
            .IsUnique()
            .HasFilter("dedupe_key IS NOT NULL AND status IN ('Queued','Sending','Sent')")
            .HasDatabaseName("ux_notifications_dedupe_key_active");
    }
}
