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
