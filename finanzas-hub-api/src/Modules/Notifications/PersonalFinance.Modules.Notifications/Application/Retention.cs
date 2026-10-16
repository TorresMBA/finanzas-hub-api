using Microsoft.EntityFrameworkCore;
using PersonalFinance.BuildingBlocks.Application.Abstractions;
using PersonalFinance.BuildingBlocks.Application.Messaging;
using PersonalFinance.BuildingBlocks.Domain;
using PersonalFinance.Modules.Notifications.Application.Abstractions;
using PersonalFinance.Modules.Notifications.Domain;

namespace PersonalFinance.Modules.Notifications.Application;

public sealed record PurgeNotificationHistoryCommand : ICommand<PurgeNotificationHistoryResponse>;

public sealed record PurgeNotificationHistoryResponse(int Notifications, int Deliveries, int WebhookEvents);

/// <summary>
/// Deletes what only matters for a while: notices older than <see cref="NoticeDays"/>, WhatsApp messages that already went
/// out, and the ids of webhook events once Meta no longer retries them. None of it is financial or part of the audit.
/// It runs for everybody at once; these tables carry no ownership filter.
/// </summary>
internal sealed class PurgeNotificationHistoryHandler(INotificationsDb db, IClock clock) : ICommandHandler<PurgeNotificationHistoryCommand, PurgeNotificationHistoryResponse>
{
    public const int NoticeDays = 90;
    public const int DeliveryDays = 90;

    /// <summary>Meta retries an event for up to seven days; a month leaves room to spare.</summary>
    public const int WebhookEventDays = 30;

    public async Task<Result<PurgeNotificationHistoryResponse>> HandleAsync(PurgeNotificationHistoryCommand command, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var notices = now.AddDays(-NoticeDays);
        var deliveries = now.AddDays(-DeliveryDays);
        var webhookEvents = now.AddDays(-WebhookEventDays);

        var removedNotices = await db.Notifications
            .Where(notification => notification.CreatedAt < notices)
            .ExecuteDeleteAsync(cancellationToken);

        // A message still waiting to go out is kept, however old: the job that sends it decides when it gives up.
        var removedDeliveries = await db.Deliveries
            .Where(delivery => delivery.CreatedAt < deliveries && delivery.Status != DeliveryStatus.Pending)
            .ExecuteDeleteAsync(cancellationToken);

        var removedEvents = await db.ReceivedWebhookEvents
            .Where(received => received.ReceivedAt < webhookEvents)
            .ExecuteDeleteAsync(cancellationToken);

        return new PurgeNotificationHistoryResponse(removedNotices, removedDeliveries, removedEvents);
    }
}
