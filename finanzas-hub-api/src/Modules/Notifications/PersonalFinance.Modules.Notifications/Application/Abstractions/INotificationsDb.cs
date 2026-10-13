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
}
