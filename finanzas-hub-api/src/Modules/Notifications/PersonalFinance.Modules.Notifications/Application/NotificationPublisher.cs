using System.Diagnostics.Metrics;
using Microsoft.EntityFrameworkCore;
using PersonalFinance.BuildingBlocks.Application.Abstractions;
using PersonalFinance.BuildingBlocks.Application.Security;
using PersonalFinance.BuildingBlocks.Domain;
using PersonalFinance.Modules.Identity.Contracts;
using PersonalFinance.Modules.Notifications.Application.Abstractions;
using PersonalFinance.Modules.Notifications.Contracts;
using PersonalFinance.Modules.Notifications.Domain;

namespace PersonalFinance.Modules.Notifications.Application;

internal static class NotificationErrors
{
    public static readonly Error Unauthenticated = Error.Unauthorized("auth.unauthenticated", "Authentication is required.");

    public static readonly Error NotFound = Error.NotFound("notifications.not_found", "The notification was not found.");
}

/// <summary>The settings of a person; the defaults for someone who never saved any.</summary>
internal sealed class NotificationSettingsStore(INotificationsDb db)
{
    public async Task<NotificationSettings> ReadAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.NotificationSettings.AsNoTracking().SingleOrDefaultAsync(settings => settings.Id == userId, cancellationToken)
        ?? NotificationSettings.DefaultFor(userId);

    /// <summary>Tracked, and added to the context when the person had none.</summary>
    public async Task<NotificationSettings> ForUpdateAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (await db.NotificationSettings.SingleOrDefaultAsync(settings => settings.Id == userId, cancellationToken) is { } existing)
        {
            return existing;
        }

        var created = NotificationSettings.DefaultFor(userId);
        db.NotificationSettings.Add(created);
        return created;
    }
}

/// <summary>
/// Where every notice enters. It writes the inbox row, signals the open browsers of the person and queues the
/// email in the same transaction as the caller: if that is rolled back, none of it happened.
/// </summary>
internal sealed class NotificationPublisher(
    INotificationsDb db, NotificationSettingsStore settingsStore, IUserDirectory users, IEmailSender email, IAppLinks links, IRealtimeNotifier realtime,
    IPersonalSpace workspace, IClock clock) : INotificationPublisher
{
    /// <summary>The name the browsers listen to on the hub.</summary>
    public const string CreatedEvent = "notification.created";

    // Same meter name as the platform's, which is the one the hosts export.
    private static readonly Counter<long> Published =
        new Meter("PersonalFinance").CreateCounter<long>("notifications.published", description: "Notices put in an inbox.");

    public async Task<bool> PublishAsync(NotificationRequest request, CancellationToken cancellationToken)
    {
        if (request.DedupeKey is { } key
            && (db.Notifications.Local.Any(local => local.UserId == request.UserId && local.DedupeKey == key)
                || await db.Notifications.AnyAsync(saved => saved.UserId == request.UserId && saved.DedupeKey == key, cancellationToken)))
        {
            return false;
        }

        var notification = Notification.Create(request, clock.UtcNow);
        db.Notifications.Add(notification);
        realtime.ToUser(request.UserId, CreatedEvent, new { notification.Id, Category = request.Category.ToString(), notification.Title });

        var settings = await settingsStore.ReadAsync(request.UserId, cancellationToken);
        var emailed = settings.EmailEnabled(request.Category) && await users.FindAsync(request.UserId, cancellationToken) is { } user && Queue(user, notification, settings);
        Published.Add(1, new KeyValuePair<string, object?>("category", request.Category.ToString()), new KeyValuePair<string, object?>("email", emailed));
        return true;
    }

    public async Task<decimal?> GetLargeMovementThresholdAsync(Guid userId, CancellationToken cancellationToken) =>
        (await settingsStore.ReadAsync(userId, cancellationToken)).LargeMovementThreshold;

    private bool Queue(UserSummary user, Notification notification, NotificationSettings settings)
    {
        // Something critical does not wait for the morning.
        var holdUntil = notification.Severity == NotificationSeverity.Critical ? null : settings.HoldUntil(LocalNow());
        var body = notification.Link is null ? notification.Body : $"{notification.Body}\n\n{links.To(notification.Link)}";
        email.Queue(new EmailMessage(user.Email, notification.Title, body, holdUntil?.ToUniversalTime()));
        return true;
    }

    /// <summary>The quiet hours are read in the time zone of the workspace the notice came from.</summary>
    private DateTimeOffset LocalNow()
    {
        var zone = workspace.TimeZone is { } id && TimeZoneInfo.TryFindSystemTimeZoneById(id, out var found) ? found : TimeZoneInfo.Utc;
        return TimeZoneInfo.ConvertTime(clock.UtcNow, zone);
    }
}
