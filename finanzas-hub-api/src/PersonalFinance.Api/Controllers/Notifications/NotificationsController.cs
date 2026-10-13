using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using PersonalFinance.Api.Http;
using PersonalFinance.BuildingBlocks.Application.Messaging;
using PersonalFinance.BuildingBlocks.Domain;
using PersonalFinance.Modules.Notifications.Application;
using PersonalFinance.Modules.Notifications.Contracts;

namespace PersonalFinance.Api.Controllers.Notifications;

public sealed class NotificationPreferenceRequest
{
    [Required]
    public NotificationCategory? Category { get; init; }

    public bool EmailEnabled { get; init; }
}

public sealed class UpdateNotificationSettingsRequest
{
    public bool QuietHoursEnabled { get; init; }

    /// <summary>As 22:00, in the time zone of the workspace.</summary>
    [Required]
    public TimeOnly? QuietFrom { get; init; }

    [Required]
    public TimeOnly? QuietTo { get; init; }

    /// <summary>In the base currency. Null to hear about no movement.</summary>
    public decimal? LargeMovementThreshold { get; init; }

    /// <summary>The categories left out keep what they had.</summary>
    public IReadOnlyList<NotificationPreferenceRequest> Preferences { get; init; } = [];
}

/// <summary>The inbox and the notification settings of the signed-in person. They follow the person across workspaces.</summary>
[Tags("Notifications")]
[Route("api/v1/me")]
[Problem(StatusCodes.Status401Unauthorized)]
public sealed class NotificationsController : ApiControllerBase
{
    /// <summary>The newest notices, newest first, and how many of the whole inbox are unread.</summary>
    [HttpGet("notifications", Name = "ListNotifications")]
    [ProducesResponseType<NotificationListResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] bool unreadOnly, [FromServices] IQueryHandler<ListNotificationsQuery, NotificationListResponse> handler, CancellationToken cancellationToken) =>
        ToActionResult(await handler.HandleAsync(new ListNotificationsQuery(unreadOnly), cancellationToken), Ok);

    /// <summary>What the badge shows. Asked again every minute in case a signal was lost.</summary>
    [HttpGet("notifications/unread-count", Name = "GetUnreadNotificationCount")]
    [ProducesResponseType<UnreadCountResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UnreadCount(
        [FromServices] IQueryHandler<GetUnreadCountQuery, UnreadCountResponse> handler, CancellationToken cancellationToken) =>
        ToActionResult(await handler.HandleAsync(new GetUnreadCountQuery(), cancellationToken), Ok);

    [HttpPost("notifications/{notificationId:guid}/read", Name = "MarkNotificationRead")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [Problem(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> MarkRead(
        Guid notificationId, [FromServices] ICommandHandler<MarkNotificationReadCommand, Unit> handler, CancellationToken cancellationToken) =>
        ToActionResult(await handler.HandleAsync(new MarkNotificationReadCommand(notificationId), cancellationToken), _ => NoContent());

    [HttpPost("notifications/read-all", Name = "MarkAllNotificationsRead")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> MarkAllRead(
        [FromServices] ICommandHandler<MarkAllNotificationsReadCommand, Unit> handler, CancellationToken cancellationToken) =>
        ToActionResult(await handler.HandleAsync(new MarkAllNotificationsReadCommand(), cancellationToken), _ => NoContent());

    /// <summary>Which categories also go to the email, the quiet hours and the amount that makes a movement large.</summary>
    [HttpGet("notification-settings", Name = "GetNotificationSettings")]
    [ProducesResponseType<NotificationSettingsResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSettings(
        [FromServices] IQueryHandler<GetNotificationSettingsQuery, NotificationSettingsResponse> handler, CancellationToken cancellationToken) =>
        ToActionResult(await handler.HandleAsync(new GetNotificationSettingsQuery(), cancellationToken), Ok);

    [HttpPut("notification-settings", Name = "UpdateNotificationSettings")]
    [ProducesResponseType<NotificationSettingsResponse>(StatusCodes.Status200OK)]
    [Problem(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateSettings(
        UpdateNotificationSettingsRequest request, [FromServices] ICommandHandler<UpdateNotificationSettingsCommand, NotificationSettingsResponse> handler,
        CancellationToken cancellationToken) =>
        ToActionResult(
            await handler.HandleAsync(
                new UpdateNotificationSettingsCommand(
                    request.QuietHoursEnabled, request.QuietFrom!.Value, request.QuietTo!.Value, request.LargeMovementThreshold,
                    request.Preferences.Select(preference => new NotificationPreferenceChange(preference.Category!.Value, preference.EmailEnabled)).ToList()),
                cancellationToken),
            Ok);
}
