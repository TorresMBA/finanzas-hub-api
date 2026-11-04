using Microsoft.EntityFrameworkCore;
using PersonalFinance.BuildingBlocks.Application.Abstractions;
using PersonalFinance.Modules.Notifications.Application.Abstractions;

namespace PersonalFinance.Modules.Notifications.Application;

/// <summary>The notices of a deleted workspace go with it: they would open a page that no longer exists (ADR-024).</summary>
internal sealed class NotificationsUsage(INotificationsDb db) : IWorkspaceContents
{
    public Task<bool> HasDataAsync(Guid workspaceId, CancellationToken cancellationToken) => Task.FromResult(false);

    public Task RemoveLeftoversAsync(Guid workspaceId, CancellationToken cancellationToken) =>
        db.Notifications.IgnoreQueryFilters().Where(notification => notification.WorkspaceId == workspaceId).ExecuteDeleteAsync(cancellationToken);
}
