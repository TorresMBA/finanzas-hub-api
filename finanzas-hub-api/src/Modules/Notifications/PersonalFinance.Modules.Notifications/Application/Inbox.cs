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

/// <param name="Items">A page, newest first. The categories the person took out of the inbox never show.</param>
/// <param name="UnreadCount">Of the whole inbox, whatever the filter.</param>
/// <param name="NextCursor">Pass it as <c>before</c> to get the next page; null on the last one.</param>
public sealed record NotificationListResponse(IReadOnlyList<NotificationResponse> Items, int UnreadCount, Guid? NextCursor = null);

public sealed record UnreadCountResponse(int Count);

/// <param name="Category">Only notices of this category.</param>
/// <param name="Before">The <c>NextCursor</c> of the page before: the notices older than that one.</param>
public sealed record ListNotificationsQuery(bool UnreadOnly, NotificationCategory? Category = null, Guid? Before = null) : IQuery<NotificationListResponse>;

/// <summary>The notices of the signed-in person that the inbox shows: those of the categories they kept in it.</summary>
internal sealed class InboxScope(INotificationsDb db, NotificationSettingsStore settings)
{
    public async Task<IQueryable<Notification>> MineAsync(Guid userId, CancellationToken cancellationToken)
    {
        var read = await settings.ReadAsync(userId, cancellationToken);
        var hidden = Enum.GetValues<NotificationCategory>().Where(category => !read.InboxEnabled(category)).ToList();

        // The user id never comes from the request.
        return db.Notifications.AsNoTracking().Where(notification => notification.UserId == userId && !hidden.Contains(notification.Category));
    }
}

internal sealed class ListNotificationsHandler(InboxScope inbox, ICurrentUser currentUser) : IQueryHandler<ListNotificationsQuery, NotificationListResponse>
{
    public const int PageSize = 50;

    public async Task<Result<NotificationListResponse>> HandleAsync(ListNotificationsQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return NotificationErrors.Unauthenticated;
        }

        var mine = await inbox.MineAsync(userId, cancellationToken);
        var page = mine
            .Where(notification => !query.UnreadOnly || notification.ReadAt == null)
            .Where(notification => query.Category == null || notification.Category == query.Category);
        if (query.Before is { } before)
        {
            // A cursor that is not one of the person's own notices gives an empty page, never someone else's.
            if (await mine.Where(notification => notification.Id == before).Select(notification => (DateTimeOffset?)notification.CreatedAt).SingleOrDefaultAsync(cancellationToken)
                is not { } createdAt)
            {
                return new NotificationListResponse([], await mine.CountAsync(notification => notification.ReadAt == null, cancellationToken));
            }

            page = page.Where(notification => notification.CreatedAt < createdAt || (notification.CreatedAt == createdAt && notification.Id < before));
        }

        var items = await page
            .OrderByDescending(notification => notification.CreatedAt)
            .ThenByDescending(notification => notification.Id)
            .Take(PageSize + 1)
            .Select(notification => new NotificationResponse(
                notification.Id, notification.Category, notification.Severity, notification.Title, notification.Body, notification.Link, notification.WorkspaceId,
                notification.CreatedAt, notification.ReadAt != null))
            .ToListAsync(cancellationToken);
        var next = items.Count > PageSize ? items[PageSize - 1].Id : (Guid?)null;
        return new NotificationListResponse(
            items.Take(PageSize).ToList(), await mine.CountAsync(notification => notification.ReadAt == null, cancellationToken), next);
    }
}

public sealed record GetUnreadCountQuery : IQuery<UnreadCountResponse>;

internal sealed class GetUnreadCountHandler(InboxScope inbox, ICurrentUser currentUser) : IQueryHandler<GetUnreadCountQuery, UnreadCountResponse>
{
    public async Task<Result<UnreadCountResponse>> HandleAsync(GetUnreadCountQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return NotificationErrors.Unauthenticated;
        }

        return new UnreadCountResponse(await (await inbox.MineAsync(userId, cancellationToken)).CountAsync(notification => notification.ReadAt == null, cancellationToken));
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
/// <param name="InboxEnabled">Whether they show in the inbox. Turned off, they are kept as read and no toast appears.</param>
public sealed record NotificationPreferenceResponse(NotificationCategory Category, bool EmailEnabled, bool WhatsAppEnabled, bool InboxEnabled);

/// <param name="QuietFrom">While the quiet hours last, an email that is not critical waits. In the time zone of the workspace.</param>
/// <param name="LargeMovementThreshold">From which amount, in the base currency, a movement is worth a notice. Null: never.</param>
public sealed record NotificationSettingsResponse(
    bool QuietHoursEnabled, TimeOnly QuietFrom, TimeOnly QuietTo, decimal? LargeMovementThreshold, IReadOnlyList<NotificationPreferenceResponse> Preferences)
{
    public static NotificationSettingsResponse Of(NotificationSettings settings) => new(
        settings.QuietHoursEnabled, settings.QuietFrom, settings.QuietTo, settings.LargeMovementThreshold,
        Enum.GetValues<NotificationCategory>().Select(category => new NotificationPreferenceResponse(category, settings.EmailEnabled(category), settings.WhatsAppEnabled(category), settings.InboxEnabled(category))).ToList());
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
/// <param name="InboxEnabled">Null keeps what it had.</param>
public sealed record NotificationPreferenceChange(NotificationCategory Category, bool EmailEnabled, bool? WhatsAppEnabled = null, bool? InboxEnabled = null);

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

            if (preference.InboxEnabled is { } inboxEnabled)
            {
                settings.SetInbox(preference.Category, inboxEnabled);
            }
        }

        var response = NotificationSettingsResponse.Of(settings);
        audit.Record(new AuditEntry("notifications", "notifications.settings.updated", "notification_settings", userId) { OwnerUserId = userId, After = response });
        return response;
    }
}
