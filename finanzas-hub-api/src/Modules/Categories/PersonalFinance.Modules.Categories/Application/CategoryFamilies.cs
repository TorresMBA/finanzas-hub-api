using Microsoft.EntityFrameworkCore;
using PersonalFinance.BuildingBlocks.Application.Security;
using PersonalFinance.BuildingBlocks.Domain;
using PersonalFinance.Modules.Categories.Application.Abstractions;
using PersonalFinance.Modules.Categories.Domain;

namespace PersonalFinance.Modules.Categories.Application;

public sealed record SubcategoryResponse(Guid Id, string Name, bool IsArchived);

/// <summary>A category with its subcategories and the names of the tags it suggests.</summary>
public sealed record CategoryResponse(
    Guid Id, CategoryKind Kind, string Name, string Icon, CategoryColor Color, bool IsArchived, IReadOnlyList<string> Tags, IReadOnlyList<SubcategoryResponse> Subcategories);

internal static class CategoryErrors
{
    public static readonly Error Unauthenticated = Error.Unauthorized("auth.unauthenticated", "Authentication is required.");

    public static readonly Error NotFound = Error.NotFound("categories.not_found", "The category was not found.");

    public static readonly Error ParentNotFound = Error.NotFound("categories.parent_not_found", "The parent category was not found.");

    public static readonly Error NameTaken = Error.Conflict("categories.name_taken", "There is already a category with that name in the same place.");

    public static readonly Error TagNotFound = Error.NotFound("categories.tag.not_found", "The tag was not found.");
}

/// <summary>
/// Loads and describes categories as families. Every query goes through the owner filter of the context, so
/// a category of someone else is simply not found.
/// </summary>
internal sealed class CategoryFamilies(ICategoriesDb db, ICurrentUser currentUser, IPersonalSpace workspace)
{
    /// <summary>A root category with its subcategories, and which of them a command asked for.</summary>
    internal sealed record Family(Category Root, List<Category> Subcategories, Category Target);

    private static readonly StringComparer ByName = StringComparer.InvariantCultureIgnoreCase;

    /// <summary>Who the categories of this request belong to, or null when there is no signed-in member.</summary>
    public (Guid WorkspaceId, Guid UserId)? Owner =>
        currentUser.UserId is { } userId && workspace.WorkspaceId is { } workspaceId ? (workspaceId, userId) : null;

    public async Task<Family?> FindAsync(Guid categoryId, CancellationToken cancellationToken)
    {
        if (await db.Categories.SingleOrDefaultAsync(category => category.Id == categoryId, cancellationToken) is not { } target)
        {
            return null;
        }

        var rootId = target.ParentId ?? target.Id;
        var family = await db.Categories.Where(category => category.Id == rootId || category.ParentId == rootId).ToListAsync(cancellationToken);
        return new Family(family.Single(category => category.Id == rootId), family.Where(category => category.Id != rootId).ToList(), target);
    }

    public Task<bool> RootNameIsTakenAsync(Category root, CancellationToken cancellationToken) =>
        db.Categories.AnyAsync(
            category => category.ParentId == null && category.ArchivedAt == null && category.NameKey == root.NameKey && category.Id != root.Id, cancellationToken);

    public static bool SubcategoryNameIsTaken(Family family, Category subcategory) =>
        family.Subcategories.Any(sibling => !sibling.IsArchived && sibling.NameKey == subcategory.NameKey && sibling.Id != subcategory.Id);

    public async Task<CategoryResponse> DescribeAsync(Family family, CancellationToken cancellationToken)
    {
        var tagIds = family.Root.Tags.Select(tag => tag.TagId).ToList();
        var names = await db.Tags.AsNoTracking().Where(tag => tagIds.Contains(tag.Id)).ToDictionaryAsync(tag => tag.Id, tag => tag.Name, cancellationToken);

        // Tags created by this same command are not in the database yet.
        foreach (var added in db.Tags.Local.Where(tag => tagIds.Contains(tag.Id)))
        {
            names[added.Id] = added.Name;
        }

        return Describe(family.Root, family.Subcategories, names, includeArchived: true);
    }

    public static IReadOnlyList<CategoryResponse> Describe(IReadOnlyCollection<Category> categories, IReadOnlyDictionary<Guid, string> tagNames, bool includeArchived)
    {
        var subcategories = categories.Where(category => !category.IsRoot).ToLookup(category => category.ParentId!.Value);
        return categories.Where(category => category.IsRoot)
            .OrderByDescending(category => category.Kind)
            .ThenBy(category => category.Name, ByName)
            .Select(root => Describe(root, subcategories[root.Id], tagNames, includeArchived))
            .ToList();
    }

    private static CategoryResponse Describe(Category root, IEnumerable<Category> subcategories, IReadOnlyDictionary<Guid, string> tagNames, bool includeArchived) => new(
        root.Id, root.Kind, root.Name, root.Icon!, root.Color!.Value, root.IsArchived,
        root.Tags.Where(tag => tagNames.ContainsKey(tag.TagId)).Select(tag => tagNames[tag.TagId]).Order(StringComparer.Ordinal).ToList(),
        subcategories.Where(subcategory => includeArchived || !subcategory.IsArchived)
            .OrderBy(subcategory => subcategory.Name, ByName)
            .Select(subcategory => new SubcategoryResponse(subcategory.Id, subcategory.Name, subcategory.IsArchived))
            .ToList());
}
