using Microsoft.EntityFrameworkCore;
using PersonalFinance.BuildingBlocks.Application.Abstractions;
using PersonalFinance.Modules.Identity.Application.Abstractions;

namespace PersonalFinance.Modules.Identity.Application;

/// <summary>Nobody opens the application on a workspace that was deleted (ADR-024): it opens on the first one again.</summary>
internal sealed class IdentityUsage(IIdentityDb db) : IWorkspaceContents
{
    public Task<bool> HasDataAsync(Guid workspaceId, CancellationToken cancellationToken) => Task.FromResult(false);

    public Task RemoveLeftoversAsync(Guid workspaceId, CancellationToken cancellationToken) =>
        db.UserPreferences
            .Where(preferences => preferences.DefaultWorkspaceId == workspaceId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(preferences => preferences.DefaultWorkspaceId, (Guid?)null), cancellationToken);
}
