using Appostolic.Api.Domain.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Appostolic.Api.Infrastructure.Configurations;

public class NotificationDedupeConfiguration : IEntityTypeConfiguration<NotificationDedupe>
{
    public void Configure(EntityTypeBuilder<NotificationDedupe> b)
    {
        b.ToTable("notification_dedupes", "app");
        b.HasKey(x => x.DedupeKey);

        b.Property(x => x.DedupeKey).HasColumnName("dedupe_key").HasMaxLength(200);
        b.Property(x => x.ExpiresAt).HasColumnName("expires_at");
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("timezone('utc', now())");
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("timezone('utc', now())");

        b.HasIndex(x => x.ExpiresAt).HasDatabaseName("ix_notification_dedupes_expires");
    }
}
