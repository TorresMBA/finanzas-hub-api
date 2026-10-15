namespace PersonalFinance.Modules.Notifications.Contracts;

/// <summary>What a notice is about. A person chooses per category whether it also goes to their email or WhatsApp.</summary>
public enum NotificationCategory
{
    Budget,
    LargeMovement,

    /// <summary>Payments coming up: reminders of scheduled movements and credit cards about to fall due.</summary>
    Bills,

    /// <summary>What happens in a goal the person takes part in: someone contributed, it reached its target.</summary>
    Goals,

    /// <summary>What happens in a group the person splits expenses with: a new expense with their part, a payment, a reminder.</summary>
    Shared
}

public enum NotificationSeverity
{
    Info,
    Warning,

    /// <summary>Not held back by the quiet hours.</summary>
    Critical
}

/// <summary>
/// A notice for one person. The module that publishes decides what to say; Notifications decides where it goes
/// and makes sure it is said once.
/// </summary>
/// <param name="WorkspaceId">The workspace it is about, if any.</param>
/// <param name="Link">A path inside the web application, starting with a slash.</param>
/// <param name="DedupeKey">The same key for the same person is published only once. Null to always publish.</param>
public sealed record NotificationRequest(
    Guid UserId, Guid? WorkspaceId, NotificationCategory Category, NotificationSeverity Severity, string Title, string Body, string? Link, string? DedupeKey);

public interface INotificationPublisher
{
    /// <summary>Puts the notice in the inbox of the person and, if they asked for it, queues an email. Runs in the transaction of the caller.</summary>
    /// <returns>False when a notice with the same key already existed.</returns>
    Task<bool> PublishAsync(NotificationRequest request, CancellationToken cancellationToken);

    /// <summary>From which amount, in the base currency, a person wants to hear about a movement. Null when they do not.</summary>
    Task<decimal?> GetLargeMovementThresholdAsync(Guid userId, CancellationToken cancellationToken);
}

/// <summary>Writes to a person directly, outside the inbox: the answer to something they asked by WhatsApp.</summary>
public interface IDirectMessenger
{
    /// <summary>Queues the text for the verified WhatsApp number of the person, in the transaction of the caller.</summary>
    /// <returns>False when the person has no verified number.</returns>
    Task<bool> ReplyByWhatsAppAsync(Guid userId, string text, CancellationToken cancellationToken);
}
