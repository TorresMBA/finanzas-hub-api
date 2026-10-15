using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PersonalFinance.Modules.Notifications.Domain;

namespace PersonalFinance.Infrastructure.Persistence.Configurations.Notifications;

internal sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("notifications", Schemas.Notifications);
        builder.HasKey(notification => notification.Id);
        builder.Property(notification => notification.Id).ValueGeneratedNever();
        builder.Property(notification => notification.Category).HasConversion<string>().HasMaxLength(32);
        builder.Property(notification => notification.Severity).HasConversion<string>().HasMaxLength(16);
        builder.Property(notification => notification.Title).HasMaxLength(Notification.MaxTitleLength).IsRequired();
        builder.Property(notification => notification.Body).HasMaxLength(Notification.MaxBodyLength).IsRequired();
        builder.Property(notification => notification.Link).HasMaxLength(Notification.MaxLinkLength);
        builder.Property(notification => notification.DedupeKey).HasMaxLength(Notification.MaxDedupeKeyLength);

        // The publisher checks first; this stops two requests racing each other from saying the same thing twice.
        builder.HasIndex(notification => new { notification.UserId, notification.DedupeKey }).IsUnique().HasFilter("dedupe_key IS NOT NULL");
        builder.HasIndex(notification => new { notification.UserId, notification.CreatedAt });
        builder.HasIndex(notification => notification.UserId).HasFilter("read_at IS NULL").HasDatabaseName("ix_notifications_unread");
    }
}

internal sealed class NotificationSettingsConfiguration : IEntityTypeConfiguration<NotificationSettings>
{
    public void Configure(EntityTypeBuilder<NotificationSettings> builder)
    {
        builder.ToTable("notification_settings", Schemas.Notifications, table =>
            table.HasCheckConstraint("ck_notification_settings_threshold", "large_movement_threshold IS NULL OR large_movement_threshold > 0"));
        builder.HasKey(settings => settings.Id);
        builder.Property(settings => settings.Id).HasColumnName("user_id").ValueGeneratedNever();
        builder.Property(settings => settings.LargeMovementThreshold).HasPrecision(18, 2);
        builder.Ignore(settings => settings.UserId);

        builder.OwnsMany(settings => settings.Preferences, preferences =>
        {
            preferences.ToTable("notification_preferences", Schemas.Notifications);
            preferences.WithOwner().HasForeignKey("UserId");
            preferences.HasKey("UserId", nameof(NotificationPreference.Category), nameof(NotificationPreference.Channel));
            preferences.Property(preference => preference.Category).HasConversion<string>().HasMaxLength(32);
            preferences.Property(preference => preference.Channel).HasConversion<string>().HasMaxLength(16);
        });
        builder.Navigation(settings => settings.Preferences).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class ChannelEndpointConfiguration : IEntityTypeConfiguration<ChannelEndpoint>
{
    public void Configure(EntityTypeBuilder<ChannelEndpoint> builder)
    {
        builder.ToTable("channel_endpoints", Schemas.Notifications);
        builder.HasKey(endpoint => endpoint.Id);
        builder.Property(endpoint => endpoint.Id).ValueGeneratedNever();
        builder.Property(endpoint => endpoint.Channel).HasConversion<string>().HasMaxLength(16);
        builder.Property(endpoint => endpoint.Phone).HasMaxLength(PhoneNumber.MaxDigits);
        builder.Property(endpoint => endpoint.PendingPhone).HasMaxLength(PhoneNumber.MaxDigits);
        builder.Property(endpoint => endpoint.CodeHash).HasMaxLength(64);
        builder.Ignore(endpoint => endpoint.IsVerified);

        builder.HasIndex(endpoint => new { endpoint.UserId, endpoint.Channel }).IsUnique();

        // A verified number speaks for one person only.
        builder.HasIndex(endpoint => new { endpoint.Channel, endpoint.Phone }).IsUnique().HasFilter("phone IS NOT NULL");
    }
}

internal sealed class DeliveryConfiguration : IEntityTypeConfiguration<Delivery>
{
    public void Configure(EntityTypeBuilder<Delivery> builder)
    {
        builder.ToTable("deliveries", Schemas.Notifications);
        builder.HasKey(delivery => delivery.Id);
        builder.Property(delivery => delivery.Id).ValueGeneratedNever();
        builder.Property(delivery => delivery.Channel).HasConversion<string>().HasMaxLength(16);
        builder.Property(delivery => delivery.Kind).HasConversion<string>().HasMaxLength(24);
        builder.Property(delivery => delivery.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(delivery => delivery.To).HasMaxLength(PhoneNumber.MaxDigits).IsRequired();
        builder.Property(delivery => delivery.Text).HasMaxLength(Delivery.MaxTextLength);
        builder.Property(delivery => delivery.ExternalId).HasMaxLength(200);
        builder.Property(delivery => delivery.LastError).HasMaxLength(500);

        builder.HasIndex(delivery => delivery.NextAttemptAt).HasFilter("status = 'Pending'").HasDatabaseName("ix_deliveries_pending");
        builder.HasIndex(delivery => delivery.ExternalId).HasFilter("external_id IS NOT NULL");
        builder.HasIndex(delivery => delivery.UserId);
    }
}

internal sealed class ReceivedWebhookEventConfiguration : IEntityTypeConfiguration<ReceivedWebhookEvent>
{
    public void Configure(EntityTypeBuilder<ReceivedWebhookEvent> builder)
    {
        builder.ToTable("received_webhook_events", Schemas.Notifications);
        builder.HasKey(received => received.Id);
        builder.Property(received => received.Id).ValueGeneratedNever();
        builder.Property(received => received.Provider).HasMaxLength(32).IsRequired();
        builder.Property(received => received.ExternalId).HasMaxLength(ReceivedWebhookEvent.MaxExternalIdLength).IsRequired();

        // The same event twice is acted on once, also when two deliveries of it race each other.
        builder.HasIndex(received => new { received.Provider, received.ExternalId }).IsUnique();
    }
}
