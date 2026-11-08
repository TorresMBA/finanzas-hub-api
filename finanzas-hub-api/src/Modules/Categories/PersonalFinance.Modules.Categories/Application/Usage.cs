using Microsoft.EntityFrameworkCore;
using PersonalFinance.BuildingBlocks.Application.Abstractions;
using PersonalFinance.Modules.Categories.Application.Abstractions;

namespace PersonalFinance.Modules.Categories.Application;

/// <summary>
/// Categories and tags are how a workspace is set up, not what was registered in it: they never keep it from being
/// deleted, and they go with it (ADR-024). Whatever used them already stopped it.
/// </summary>
internal sealed class CategoriesUsage(ICategoriesDb db) : IWorkspaceContents
{
    public Task<bool> HasDataAsync(Guid workspaceId, CancellationToken cancellationToken) => Task.FromResult(false);

    public async Task RemoveLeftoversAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        // Subcategories first: a category cannot go while one points at it. Their suggested tags go with each row.
        var categories = db.Categories.IgnoreQueryFilters().Where(category => category.WorkspaceId == workspaceId);
        await categories.Where(category => category.ParentId != null).ExecuteDeleteAsync(cancellationToken);
        await categories.ExecuteDeleteAsync(cancellationToken);
        await db.Tags.IgnoreQueryFilters().Where(tag => tag.WorkspaceId == workspaceId).ExecuteDeleteAsync(cancellationToken);
    }
}
