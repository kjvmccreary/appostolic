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
    b.Property(x => x.TokenHash).HasColumnName("token_hash").HasColumnType("varchar(128)");
        b.Property(x => x.TenantId).HasColumnName("tenant_id");
        b.Property(x => x.DedupeKey).HasColumnName("dedupe_key").HasMaxLength(200);
    // Resend columns
    b.Property(x => x.ResendOfNotificationId).HasColumnName("resend_of_notification_id");
    b.Property(x => x.ResendReason).HasColumnName("resend_reason").HasColumnType("text");
    b.Property(x => x.ResendCount).HasColumnName("resend_count").HasDefaultValue(0);
    b.Property(x => x.LastResendAt).HasColumnName("last_resend_at");
    b.Property(x => x.ThrottleUntil).HasColumnName("throttle_until");
        b.Property(x => x.Status).HasColumnName("status").HasConversion<string>();
        b.Property(x => x.AttemptCount).HasColumnName("attempt_count").HasColumnType("smallint");
        b.Property(x => x.NextAttemptAt).HasColumnName("next_attempt_at");
        b.Property(x => x.LastError).HasColumnName("last_error").HasColumnType("text");
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("timezone('utc', now())");
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("timezone('utc', now())");
        b.Property(x => x.SentAt).HasColumnName("sent_at");

        // Relationships
        b.HasOne<Notification>()
            .WithMany()
            .HasForeignKey(x => x.ResendOfNotificationId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("fk_notifications_resend_of");

        // Indexes
        b.HasIndex(x => new { x.Status, x.NextAttemptAt })
            .HasDatabaseName("ix_notifications_status_next_attempt");

        b.HasIndex(x => new { x.TenantId, x.CreatedAt })
            .IsDescending(false, true)
            .HasDatabaseName("ix_notifications_tenant_created");

        b.HasIndex(x => x.CreatedAt)
            .IsDescending(true)
            .HasDatabaseName("ix_notifications_created_desc");

        // Resend indexes
        b.HasIndex(x => x.ResendOfNotificationId)
            .HasDatabaseName("ix_notifications_resend_of");

        b.HasIndex(x => new { x.ToEmail, x.Kind, x.CreatedAt })
            .IsDescending(false, false, true)
            .HasDatabaseName("ix_notifications_to_kind_created");

        // Partial unique index on dedupe_key for in-flight statuses only; Sent is excluded now that TTL table handles dedupe
        b.HasIndex(x => x.DedupeKey)
            .IsUnique()
            .HasFilter("dedupe_key IS NOT NULL AND status IN ('Queued','Sending')")
            .HasDatabaseName("ux_notifications_dedupe_key_active");
    }
}
