using System.Diagnostics.Metrics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PersonalFinance.BuildingBlocks.Application.Abstractions;
using PersonalFinance.BuildingBlocks.Application.Messaging;
using PersonalFinance.BuildingBlocks.Application.Security;
using PersonalFinance.BuildingBlocks.Domain;
using PersonalFinance.Modules.Notifications.Application.Abstractions;
using PersonalFinance.Modules.Notifications.Contracts;
using PersonalFinance.Modules.Notifications.Domain;

namespace PersonalFinance.Modules.Notifications.Application;

/// <param name="Phone">The verified number, with its country code ("+51987654321"). Null until one is verified.</param>
/// <param name="PendingPhone">The number a code was sent to and not yet confirmed.</param>
public sealed record WhatsAppChannelResponse(string? Phone, string? PendingPhone, DateTimeOffset? CodeExpiresAt)
{
    public bool IsVerified => Phone is not null;

    internal static WhatsAppChannelResponse Of(ChannelEndpoint? endpoint)
    {
        if (endpoint is null)
        {
            return new WhatsAppChannelResponse(null, null, null);
        }

        var phone = endpoint.Phone is { } verified ? $"+{verified}" : null;
        var pending = endpoint.PendingPhone is { } waiting ? $"+{waiting}" : null;
        return new WhatsAppChannelResponse(phone, pending, endpoint.PendingPhone is null ? null : endpoint.CodeExpiresAt);
    }
}

/// <param name="DevelopmentCode">The code itself, only on a development machine without a WhatsApp account. Null everywhere else.</param>
public sealed record WhatsAppVerificationResponse(WhatsAppChannelResponse Channel, string? DevelopmentCode);

internal static class WhatsAppErrors
{
    public static readonly Error PhoneInvalid =
        Error.Validation("notifications.phone_invalid", "Write the number with its country code, as +51 987 654 321.");

    public static readonly Error PhoneTaken = Error.Conflict("notifications.phone_taken", "That number is already verified by someone else.");

    public static readonly Error NotVerified =
        Error.Validation("notifications.whatsapp_not_verified", "Verify your WhatsApp number before turning on notices there.");

    public static readonly Error NoPendingCode = Error.Validation("notifications.code_invalid", "The code is not correct.");
}

/// <summary>The WhatsApp numbers of people, and the messages queued for them.</summary>
internal sealed class WhatsAppBook(INotificationsDb db, IClock clock)
{
    public Task<ChannelEndpoint?> EndpointAsync(Guid userId, CancellationToken cancellationToken) =>
        db.ChannelEndpoints.SingleOrDefaultAsync(endpoint => endpoint.UserId == userId && endpoint.Channel == NotificationChannel.WhatsApp, cancellationToken);

    public async Task<string?> VerifiedPhoneAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.ChannelEndpoints.AsNoTracking()
            .Where(endpoint => endpoint.UserId == userId && endpoint.Channel == NotificationChannel.WhatsApp && endpoint.Phone != null)
            .Select(endpoint => endpoint.Phone)
            .SingleOrDefaultAsync(cancellationToken);

    /// <summary>Who a number belongs to: only a verified number speaks for a person.</summary>
    public async Task<Guid?> OwnerOfAsync(string phone, CancellationToken cancellationToken) =>
        await db.ChannelEndpoints.AsNoTracking()
            .Where(endpoint => endpoint.Channel == NotificationChannel.WhatsApp && endpoint.Phone == phone)
            .Select(endpoint => (Guid?)endpoint.UserId)
            .SingleOrDefaultAsync(cancellationToken);

    public void Queue(Guid userId, string to, DeliveryKind kind, string text, IReadOnlyList<string> parameters, DateTimeOffset? notBefore = null, Guid? notificationId = null) =>
        db.Deliveries.Add(Delivery.WhatsApp(userId, to, kind, text, parameters, clock.UtcNow, notBefore, notificationId));
}

internal sealed class DirectMessenger(WhatsAppBook book) : IDirectMessenger
{
    public async Task<bool> ReplyByWhatsAppAsync(Guid userId, string text, CancellationToken cancellationToken)
    {
        if (await book.VerifiedPhoneAsync(userId, cancellationToken) is not { } phone)
        {
            return false;
        }

        book.Queue(userId, phone, DeliveryKind.Reply, text, [text]);
        return true;
    }
}

public sealed record GetWhatsAppChannelQuery : IQuery<WhatsAppChannelResponse>;

internal sealed class GetWhatsAppChannelHandler(WhatsAppBook book, ICurrentUser currentUser) : IQueryHandler<GetWhatsAppChannelQuery, WhatsAppChannelResponse>
{
    public async Task<Result<WhatsAppChannelResponse>> HandleAsync(GetWhatsAppChannelQuery query, CancellationToken cancellationToken) =>
        currentUser.UserId is { } userId
            ? WhatsAppChannelResponse.Of(await book.EndpointAsync(userId, cancellationToken))
            : NotificationErrors.Unauthenticated;
}

/// <summary>One message sent, or to be sent, by WhatsApp. What it said is not kept once sent, so only its notice names it.</summary>
/// <param name="Title">The title of the notice it carried; null for a reply or a code.</param>
/// <param name="LastError">Why the last attempt failed, while it failed.</param>
public sealed record WhatsAppDeliveryResponse(
    Guid Id, DeliveryKind Kind, DeliveryStatus Status, string? Title, DateTimeOffset CreatedAt, DateTimeOffset? SentAt, int Attempts, string? LastError);

/// <summary>The last messages to the signed-in person by WhatsApp, newest first.</summary>
public sealed record ListWhatsAppDeliveriesQuery : IQuery<IReadOnlyList<WhatsAppDeliveryResponse>>;

internal sealed class ListWhatsAppDeliveriesHandler(INotificationsDb db, ICurrentUser currentUser)
    : IQueryHandler<ListWhatsAppDeliveriesQuery, IReadOnlyList<WhatsAppDeliveryResponse>>
{
    public const int Limit = 20;

    public async Task<Result<IReadOnlyList<WhatsAppDeliveryResponse>>> HandleAsync(ListWhatsAppDeliveriesQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return NotificationErrors.Unauthenticated;
        }

        // No global filter covers these tables: the user is named in both queries.
        var deliveries = await db.Deliveries.AsNoTracking()
            .Where(delivery => delivery.UserId == userId && delivery.Channel == NotificationChannel.WhatsApp)
            .OrderByDescending(delivery => delivery.CreatedAt)
            .Take(Limit)
            .ToListAsync(cancellationToken);
        var noticeIds = deliveries.Where(delivery => delivery.NotificationId != null).Select(delivery => delivery.NotificationId!.Value).ToList();
        var titles = await db.Notifications.AsNoTracking()
            .Where(notification => notification.UserId == userId && noticeIds.Contains(notification.Id))
            .ToDictionaryAsync(notification => notification.Id, notification => notification.Title, cancellationToken);
        return deliveries.Select(delivery => new WhatsAppDeliveryResponse(
                delivery.Id, delivery.Kind, delivery.Status, delivery.NotificationId is { } id ? titles.GetValueOrDefault(id) : null, delivery.CreatedAt,
                delivery.SentAt, delivery.Attempts, delivery.Status == DeliveryStatus.Failed || delivery.Status == DeliveryStatus.Pending ? delivery.LastError : null))
            .ToList();
    }
}

/// <param name="Phone">With its country code.</param>
public sealed record StartWhatsAppVerificationCommand(string Phone) : ICommand<WhatsAppVerificationResponse>;

/// <summary>Sends a code by WhatsApp to the number the person says is theirs. Until they type it back, nothing goes there.</summary>
internal sealed class StartWhatsAppVerificationHandler(INotificationsDb db, WhatsAppBook book, IWhatsAppSetup setup, ICurrentUser currentUser, IClock clock, IAuditLog audit)
    : ICommandHandler<StartWhatsAppVerificationCommand, WhatsAppVerificationResponse>
{
    public async Task<Result<WhatsAppVerificationResponse>> HandleAsync(StartWhatsAppVerificationCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return NotificationErrors.Unauthenticated;
        }

        if (PhoneNumber.Normalize(command.Phone) is not { } phone)
        {
            return WhatsAppErrors.PhoneInvalid;
        }

        if (await book.OwnerOfAsync(phone, cancellationToken) is { } owner && owner != userId)
        {
            return WhatsAppErrors.PhoneTaken;
        }

        var endpoint = await book.EndpointAsync(userId, cancellationToken);
        if (endpoint is null)
        {
            endpoint = ChannelEndpoint.WhatsAppOf(userId);
            db.ChannelEndpoints.Add(endpoint);
        }

        var started = endpoint.StartVerification(phone, clock.UtcNow);
        if (started.IsFailure)
        {
            return started.Error;
        }

        var code = started.Value;
        book.Queue(userId, phone, DeliveryKind.VerificationCode, $"Tu código de PersonalHub es {code}. Vence en 10 minutos. No lo compartas.", [code]);
        audit.Record(new AuditEntry("notifications", "notifications.whatsapp.verification_started", "channel_endpoint", endpoint.Id)
        {
            OwnerUserId = userId,
            After = new { Phone = PhoneNumber.Mask(phone) }
        });
        return new WhatsAppVerificationResponse(WhatsAppChannelResponse.Of(endpoint), setup.ShowsCodesOnScreen ? code : null);
    }
}

/// <param name="Rejection">Why the code was not accepted. It comes as a value, not as a failed result, so that the wrong
/// attempt is saved: a failed result would roll it back and leave the code open to guessing.</param>
public sealed record WhatsAppConfirmation(WhatsAppChannelResponse Channel, Error? Rejection);

public sealed record ConfirmWhatsAppVerificationCommand(string Code) : ICommand<WhatsAppConfirmation>;

internal sealed class ConfirmWhatsAppVerificationHandler(WhatsAppBook book, ICurrentUser currentUser, IClock clock, IAuditLog audit)
    : ICommandHandler<ConfirmWhatsAppVerificationCommand, WhatsAppConfirmation>
{
    public async Task<Result<WhatsAppConfirmation>> HandleAsync(ConfirmWhatsAppVerificationCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return NotificationErrors.Unauthenticated;
        }

        if (await book.EndpointAsync(userId, cancellationToken) is not { PendingPhone: { } pending } endpoint)
        {
            return WhatsAppErrors.NoPendingCode;
        }

        // Someone else may have proven the same number since the code went out.
        if (await book.OwnerOfAsync(pending, cancellationToken) is { } owner && owner != userId)
        {
            return WhatsAppErrors.PhoneTaken;
        }

        var confirmed = endpoint.Confirm(command.Code, clock.UtcNow);
        if (confirmed.IsFailure)
        {
            return new WhatsAppConfirmation(WhatsAppChannelResponse.Of(endpoint), confirmed.Error);
        }

        audit.Record(new AuditEntry("notifications", "notifications.whatsapp.verified", "channel_endpoint", endpoint.Id)
        {
            OwnerUserId = userId,
            After = new { Phone = PhoneNumber.Mask(endpoint.Phone!) }
        });
        return new WhatsAppConfirmation(WhatsAppChannelResponse.Of(endpoint), null);
    }
}

public sealed record RemoveWhatsAppCommand : ICommand<WhatsAppChannelResponse>;

/// <summary>Forgets the number: no more notices go there and messages from it no longer speak for the person.</summary>
internal sealed class RemoveWhatsAppHandler(INotificationsDb db, WhatsAppBook book, NotificationSettingsStore settings, ICurrentUser currentUser, IAuditLog audit)
    : ICommandHandler<RemoveWhatsAppCommand, WhatsAppChannelResponse>
{
    public async Task<Result<WhatsAppChannelResponse>> HandleAsync(RemoveWhatsAppCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return NotificationErrors.Unauthenticated;
        }

        if (await book.EndpointAsync(userId, cancellationToken) is { } endpoint)
        {
            db.ChannelEndpoints.Remove(endpoint);
            (await settings.ForUpdateAsync(userId, cancellationToken)).TurnOffWhatsApp();
            audit.Record(new AuditEntry("notifications", "notifications.whatsapp.removed", "channel_endpoint", endpoint.Id) { OwnerUserId = userId });
        }

        return WhatsAppChannelResponse.Of(null);
    }
}

/// <summary>A text message from a verified number, not yet acted on.</summary>
public sealed record WhatsAppInboundMessage(string ExternalId, Guid UserId, string Text);

/// <param name="Body">The body exactly as WhatsApp sent it, after its signature was checked.</param>
public sealed record ReceiveWhatsAppWebhookCommand(string Body) : ICommand<IReadOnlyList<WhatsAppInboundMessage>>;

/// <summary>
/// Reads what WhatsApp sent. Delivery reports update their messages here, once each. Messages are only sorted out:
/// those from verified numbers go back to the caller, which hands each to <see cref="HandleWhatsAppMessageCommand"/>
/// acting as the person who wrote it. The rest are dropped.
/// </summary>
internal sealed class ReceiveWhatsAppWebhookHandler(INotificationsDb db, WhatsAppBook book, IClock clock, ILogger<ReceiveWhatsAppWebhookHandler> logger)
    : ICommandHandler<ReceiveWhatsAppWebhookCommand, IReadOnlyList<WhatsAppInboundMessage>>
{
    private static readonly Counter<long> Received =
        new Meter("PersonalFinance").CreateCounter<long>("whatsapp.webhook.events", description: "Events received from WhatsApp, by kind and outcome.");

    public async Task<Result<IReadOnlyList<WhatsAppInboundMessage>>> HandleAsync(ReceiveWhatsAppWebhookCommand command, CancellationToken cancellationToken)
    {
        WhatsAppPayload payload;
        try
        {
            payload = WhatsAppPayload.Parse(command.Body);
        }
        catch (JsonException)
        {
            return Error.Validation("notifications.webhook_invalid", "The body is not a WhatsApp event.");
        }

        foreach (var report in payload.Statuses)
        {
            var externalId = $"{report.MessageId}:{report.Status}";
            if (db.ReceivedWebhookEvents.Local.Any(seen => seen.ExternalId == externalId)
                || await db.ReceivedWebhookEvents.AnyAsync(seen => seen.Provider == ReceivedWebhookEvent.WhatsAppProvider && seen.ExternalId == externalId, cancellationToken))
            {
                Received.Add(1, Tag("status"), Outcome("repeated"));
                continue;
            }

            db.ReceivedWebhookEvents.Add(ReceivedWebhookEvent.Status(externalId, clock.UtcNow));
            var delivery = await db.Deliveries.SingleOrDefaultAsync(candidate => candidate.ExternalId == report.MessageId, cancellationToken);
            delivery?.Report(report.Status, report.Error);
            Received.Add(1, Tag("status"), Outcome(delivery is null ? "unknown" : report.Status.ToString().ToLowerInvariant()));
        }

        var inbound = new List<WhatsAppInboundMessage>();
        foreach (var message in payload.Messages)
        {
            if (await book.OwnerOfAsync(message.From, cancellationToken) is not { } userId)
            {
                // Nobody proved this number: whatever it says, it speaks for no one.
                if (logger.IsEnabled(LogLevel.Information))
                {
                    logger.LogInformation("Ignored WhatsApp message {MessageId} from a number nobody verified", message.Id);
                }

                Received.Add(1, Tag("message"), Outcome("unverified"));
                continue;
            }

            inbound.Add(new WhatsAppInboundMessage(message.Id, userId, message.Text));
        }

        return inbound;
    }

    private static KeyValuePair<string, object?> Tag(string kind) => new("kind", kind);

    private static KeyValuePair<string, object?> Outcome(string outcome) => new("outcome", outcome);
}

public sealed record HandleWhatsAppMessageCommand(string ExternalId, string Text) : ICommand<bool>;

/// <summary>
/// Acts on a message of the signed-in person, once: it is recorded with its id, and the module that answers reacts
/// to <see cref="Contracts.Events.InboundMessageReceived"/> in this same transaction.
/// </summary>
/// <returns>False when the message had already been acted on.</returns>
internal sealed class HandleWhatsAppMessageHandler(INotificationsDb db, ICurrentUser currentUser, IClock clock) : ICommandHandler<HandleWhatsAppMessageCommand, bool>
{
    private static readonly Counter<long> Handled =
        new Meter("PersonalFinance").CreateCounter<long>("whatsapp.webhook.events", description: "Events received from WhatsApp, by kind and outcome.");

    public async Task<Result<bool>> HandleAsync(HandleWhatsAppMessageCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return NotificationErrors.Unauthenticated;
        }

        if (await db.ReceivedWebhookEvents.AnyAsync(
                seen => seen.Provider == ReceivedWebhookEvent.WhatsAppProvider && seen.ExternalId == command.ExternalId, cancellationToken))
        {
            Handled.Add(1, new KeyValuePair<string, object?>("kind", "message"), new KeyValuePair<string, object?>("outcome", "repeated"));
            return false;
        }

        db.ReceivedWebhookEvents.Add(ReceivedWebhookEvent.Message(command.ExternalId, userId, command.Text, clock.UtcNow));
        Handled.Add(1, new KeyValuePair<string, object?>("kind", "message"), new KeyValuePair<string, object?>("outcome", "handled"));
        return true;
    }
}
