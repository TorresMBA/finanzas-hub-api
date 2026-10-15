using Microsoft.EntityFrameworkCore;
using PersonalFinance.Modules.Notifications.Domain;

namespace PersonalFinance.Modules.Notifications.Application.Abstractions;

/// <summary>
/// The tables this module may read and write. They belong to a person, not to a workspace, so no global filter
/// covers them: every query here names the user.
/// </summary>
public interface INotificationsDb
{
    DbSet<Notification> Notifications { get; }

    DbSet<NotificationSettings> NotificationSettings { get; }

    DbSet<ChannelEndpoint> ChannelEndpoints { get; }

    DbSet<Delivery> Deliveries { get; }

    DbSet<ReceivedWebhookEvent> ReceivedWebhookEvents { get; }
}

/// <summary>How WhatsApp is set up in this installation.</summary>
public interface IWhatsAppSetup
{
    /// <summary>
    /// Whether a verification code is also shown on screen. Only on a development machine without a WhatsApp
    /// account, where messages are written to a folder instead of sent and nobody could receive the code.
    /// </summary>
    bool ShowsCodesOnScreen { get; }
}
