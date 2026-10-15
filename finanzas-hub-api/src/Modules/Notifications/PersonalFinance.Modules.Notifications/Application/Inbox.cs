using Microsoft.EntityFrameworkCore;
using PersonalFinance.BuildingBlocks.Application.Abstractions;
using PersonalFinance.BuildingBlocks.Application.Messaging;
using PersonalFinance.BuildingBlocks.Application.Security;
using PersonalFinance.BuildingBlocks.Domain;
using PersonalFinance.Modules.Notifications.Application.Abstractions;
using PersonalFinance.Modules.Notifications.Contracts;
using PersonalFinance.Modules.Notifications.Domain;

namespace PersonalFinance.Modules.Notifications.Application;

/// <param name="Link">A path inside the web application.</param>
public sealed record NotificationResponse(
    Guid Id, NotificationCategory Category, NotificationSeverity Severity, string Title, string Body, string? Link, Guid? WorkspaceId, DateTimeOffset CreatedAt,
    bool IsRead);

/// <param name="Items">The newest hundred at most, newest first.</param>
/// <param name="UnreadCount">Of the whole inbox, whatever the filter.</param>
public sealed record NotificationListResponse(IReadOnlyList<NotificationResponse> Items, int UnreadCount);

public sealed record UnreadCountResponse(int Count);

public sealed record ListNotificationsQuery(bool UnreadOnly) : IQuery<NotificationListResponse>;

internal sealed class ListNotificationsHandler(INotificationsDb db, ICurrentUser currentUser) : IQueryHandler<ListNotificationsQuery, NotificationListResponse>
{
    private const int PageSize = 100;

    public async Task<Result<NotificationListResponse>> HandleAsync(ListNotificationsQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return NotificationErrors.Unauthenticated;
        }

        // The inbox of the signed-in person and of nobody else: the user id never comes from the request.
        var mine = db.Notifications.AsNoTracking().Where(notification => notification.UserId == userId);
        var items = await mine
            .Where(notification => !query.UnreadOnly || notification.ReadAt == null)
            .OrderByDescending(notification => notification.CreatedAt)
            .ThenByDescending(notification => notification.Id)
            .Take(PageSize)
            .Select(notification => new NotificationResponse(
                notification.Id, notification.Category, notification.Severity, notification.Title, notification.Body, notification.Link, notification.WorkspaceId,
                notification.CreatedAt, notification.ReadAt != null))
            .ToListAsync(cancellationToken);
        return new NotificationListResponse(items, await mine.CountAsync(notification => notification.ReadAt == null, cancellationToken));
    }
}

public sealed record GetUnreadCountQuery : IQuery<UnreadCountResponse>;

internal sealed class GetUnreadCountHandler(INotificationsDb db, ICurrentUser currentUser) : IQueryHandler<GetUnreadCountQuery, UnreadCountResponse>
{
    public async Task<Result<UnreadCountResponse>> HandleAsync(GetUnreadCountQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return NotificationErrors.Unauthenticated;
        }

        return new UnreadCountResponse(
            await db.Notifications.CountAsync(notification => notification.UserId == userId && notification.ReadAt == null, cancellationToken));
    }
}

public sealed record MarkNotificationReadCommand(Guid NotificationId) : ICommand<Unit>;

internal sealed class MarkNotificationReadHandler(INotificationsDb db, ICurrentUser currentUser, IClock clock) : ICommandHandler<MarkNotificationReadCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(MarkNotificationReadCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return NotificationErrors.Unauthenticated;
        }

        // The notice of someone else is not found, exactly as if it did not exist.
        var notification = await db.Notifications.SingleOrDefaultAsync(
            candidate => candidate.Id == command.NotificationId && candidate.UserId == userId, cancellationToken);
        if (notification is null)
        {
            return NotificationErrors.NotFound;
        }

        notification.MarkRead(clock.UtcNow);
        return Unit.Value;
    }
}

public sealed record MarkAllNotificationsReadCommand : ICommand<Unit>;

internal sealed class MarkAllNotificationsReadHandler(INotificationsDb db, ICurrentUser currentUser, IClock clock)
    : ICommandHandler<MarkAllNotificationsReadCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(MarkAllNotificationsReadCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return NotificationErrors.Unauthenticated;
        }

        var unread = await db.Notifications.Where(notification => notification.UserId == userId && notification.ReadAt == null).ToListAsync(cancellationToken);
        unread.ForEach(notification => notification.MarkRead(clock.UtcNow));
        return Unit.Value;
    }
}

/// <param name="EmailEnabled">Whether notices of this category also go to the email of the person. The inbox always gets them.</param>
/// <param name="WhatsAppEnabled">Whether they also go to their verified WhatsApp number.</param>
public sealed record NotificationPreferenceResponse(NotificationCategory Category, bool EmailEnabled, bool WhatsAppEnabled);

/// <param name="QuietFrom">While the quiet hours last, an email that is not critical waits. In the time zone of the workspace.</param>
/// <param name="LargeMovementThreshold">From which amount, in the base currency, a movement is worth a notice. Null: never.</param>
public sealed record NotificationSettingsResponse(
    bool QuietHoursEnabled, TimeOnly QuietFrom, TimeOnly QuietTo, decimal? LargeMovementThreshold, IReadOnlyList<NotificationPreferenceResponse> Preferences)
{
    public static NotificationSettingsResponse Of(NotificationSettings settings) => new(
        settings.QuietHoursEnabled, settings.QuietFrom, settings.QuietTo, settings.LargeMovementThreshold,
        Enum.GetValues<NotificationCategory>().Select(category => new NotificationPreferenceResponse(category, settings.EmailEnabled(category), settings.WhatsAppEnabled(category))).ToList());
}

public sealed record GetNotificationSettingsQuery : IQuery<NotificationSettingsResponse>;

internal sealed class GetNotificationSettingsHandler(NotificationSettingsStore store, ICurrentUser currentUser)
    : IQueryHandler<GetNotificationSettingsQuery, NotificationSettingsResponse>
{
    public async Task<Result<NotificationSettingsResponse>> HandleAsync(GetNotificationSettingsQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return NotificationErrors.Unauthenticated;
        }

        return NotificationSettingsResponse.Of(await store.ReadAsync(userId, cancellationToken));
    }
}

/// <param name="WhatsAppEnabled">Null keeps what it had.</param>
public sealed record NotificationPreferenceChange(NotificationCategory Category, bool EmailEnabled, bool? WhatsAppEnabled = null);

/// <param name="Preferences">The categories left out keep what they had.</param>
public sealed record UpdateNotificationSettingsCommand(
    bool QuietHoursEnabled, TimeOnly QuietFrom, TimeOnly QuietTo, decimal? LargeMovementThreshold, IReadOnlyList<NotificationPreferenceChange> Preferences)
    : ICommand<NotificationSettingsResponse>;

internal sealed class UpdateNotificationSettingsHandler(NotificationSettingsStore store, WhatsAppBook whatsApp, ICurrentUser currentUser, IAuditLog audit)
    : ICommandHandler<UpdateNotificationSettingsCommand, NotificationSettingsResponse>
{
    public async Task<Result<NotificationSettingsResponse>> HandleAsync(UpdateNotificationSettingsCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return NotificationErrors.Unauthenticated;
        }

        var settings = await store.ForUpdateAsync(userId, cancellationToken);
        var updated = settings.Update(command.QuietHoursEnabled, command.QuietFrom, command.QuietTo, command.LargeMovementThreshold);
        if (updated.IsFailure)
        {
            return updated.Error;
        }

        if (command.Preferences.Any(preference => preference.WhatsAppEnabled == true && !settings.WhatsAppEnabled(preference.Category))
            && await whatsApp.VerifiedPhoneAsync(userId, cancellationToken) is null)
        {
            return WhatsAppErrors.NotVerified;
        }

        foreach (var preference in command.Preferences)
        {
            settings.SetEmail(preference.Category, preference.EmailEnabled);
            if (preference.WhatsAppEnabled is { } whatsAppEnabled)
            {
                settings.SetWhatsApp(preference.Category, whatsAppEnabled);
            }
        }

        var response = NotificationSettingsResponse.Of(settings);
        audit.Record(new AuditEntry("notifications", "notifications.settings.updated", "notification_settings", userId) { OwnerUserId = userId, After = response });
        return response;
    }
}
