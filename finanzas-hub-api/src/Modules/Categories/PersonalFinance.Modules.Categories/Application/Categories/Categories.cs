using Microsoft.EntityFrameworkCore;
using PersonalFinance.BuildingBlocks.Application.Abstractions;
using PersonalFinance.BuildingBlocks.Application.Messaging;
using PersonalFinance.BuildingBlocks.Domain;
using PersonalFinance.Modules.Categories.Application.Abstractions;
using PersonalFinance.Modules.Categories.Application.Tags;
using PersonalFinance.Modules.Categories.Domain;

namespace PersonalFinance.Modules.Categories.Application.Categories;

public sealed record ListCategoriesQuery(CategoryKind? Kind, bool IncludeArchived) : IQuery<IReadOnlyList<CategoryResponse>>;

internal sealed class ListCategoriesHandler(ICategoriesDb db) : IQueryHandler<ListCategoriesQuery, IReadOnlyList<CategoryResponse>>
{
    public async Task<Result<IReadOnlyList<CategoryResponse>>> HandleAsync(ListCategoriesQuery query, CancellationToken cancellationToken)
    {
        // No owner condition here on purpose: the context's global filter already limits both tables to the current person.
        var categories = await db.Categories.AsNoTracking()
            .Where(category => query.Kind == null || category.Kind == query.Kind)
            .Where(category => query.IncludeArchived || category.ArchivedAt == null)
            .ToListAsync(cancellationToken);
        var tagNames = await db.Tags.AsNoTracking().Where(tag => tag.ArchivedAt == null).ToDictionaryAsync(tag => tag.Id, tag => tag.Name, cancellationToken);
        return Result.Success(CategoryFamilies.Describe(categories, tagNames, query.IncludeArchived));
    }
}

/// <param name="Kind">Required for a category; a subcategory takes its parent's.</param>
/// <param name="Tags">Names of the tags to suggest. Only for a category, not for a subcategory.</param>
public sealed record CreateCategoryCommand(CategoryKind? Kind, string Name, Guid? ParentId, string? Icon, CategoryColor? Color, IReadOnlyList<string>? Tags)
    : ICommand<CategoryResponse>;

internal sealed class CreateCategoryHandler(ICategoriesDb db, CategoryFamilies families, TagResolver tagResolver, IAuditLog audit, IClock clock)
    : ICommandHandler<CreateCategoryCommand, CategoryResponse>
{
    public async Task<Result<CategoryResponse>> HandleAsync(CreateCategoryCommand command, CancellationToken cancellationToken)
    {
        if (families.Owner is not (var workspaceId, var userId))
        {
            return CategoryErrors.Unauthenticated;
        }

        var created = command.ParentId is { } parentId
            ? await CreateSubcategoryAsync(command, parentId, cancellationToken)
            : await CreateRootAsync(command, workspaceId, userId, cancellationToken);
        if (created.IsFailure)
        {
            return created.Error;
        }

        var family = created.Value;
        db.Categories.Add(family.Target);
        audit.Record(new AuditEntry("categories", "categories.category.created", "category", family.Target.Id)
        {
            OwnerUserId = userId,
            After = new { family.Target.Name, Kind = family.Target.Kind.ToString(), family.Target.ParentId }
        });
        return await families.DescribeAsync(family, cancellationToken);
    }

    private async Task<Result<CategoryFamilies.Family>> CreateRootAsync(CreateCategoryCommand command, Guid workspaceId, Guid userId, CancellationToken cancellationToken)
    {
        if (command.Kind is not { } kind)
        {
            return Error.Validation("categories.kind_required", "Choose whether the category is for income or for expenses.");
        }

        var created = Category.CreateRoot(workspaceId, userId, kind, command.Name, command.Icon, command.Color, clock.UtcNow);
        if (created.IsFailure)
        {
            return created.Error;
        }

        var root = created.Value;
        if (await families.RootNameIsTakenAsync(root, cancellationToken))
        {
            return CategoryErrors.NameTaken;
        }

        var tags = await tagResolver.ResolveAsync(workspaceId, userId, command.Tags ?? [], cancellationToken);
        if (tags.IsFailure)
        {
            return tags.Error;
        }

        var tagged = root.SetTags(tags.Value.Select(tag => tag.Id).ToList());
        return tagged.IsFailure ? tagged.Error : new CategoryFamilies.Family(root, [], root);
    }

    private async Task<Result<CategoryFamilies.Family>> CreateSubcategoryAsync(CreateCategoryCommand command, Guid parentId, CancellationToken cancellationToken)
    {
        if (await families.FindAsync(parentId, cancellationToken) is not { } family)
        {
            return CategoryErrors.ParentNotFound;
        }

        if (command.Tags is { Count: > 0 })
        {
            return Error.BusinessRule("categories.tags_on_subcategory", "A subcategory uses the tags of its category.");
        }

        var created = family.Target.AddSubcategory(command.Name, clock.UtcNow);
        if (created.IsFailure)
        {
            return created.Error;
        }

        if (CategoryFamilies.SubcategoryNameIsTaken(family, created.Value))
        {
            return CategoryErrors.NameTaken;
        }

        family.Subcategories.Add(created.Value);
        return family with { Target = created.Value };
    }
}

/// <param name="Icon">Only for a category. Null keeps the current one.</param>
/// <param name="Color">Only for a category. Null keeps the current one.</param>
public sealed record UpdateCategoryCommand(Guid CategoryId, string Name, string? Icon, CategoryColor? Color) : ICommand<CategoryResponse>;

internal sealed class UpdateCategoryHandler(CategoryFamilies families, IAuditLog audit) : ICommandHandler<UpdateCategoryCommand, CategoryResponse>
{
    public async Task<Result<CategoryResponse>> HandleAsync(UpdateCategoryCommand command, CancellationToken cancellationToken)
    {
        if (await families.FindAsync(command.CategoryId, cancellationToken) is not { } family)
        {
            return CategoryErrors.NotFound;
        }

        var category = family.Target;
        var before = new { category.Name, category.Icon, Color = category.Color?.ToString() };
        var updated = category.Update(command.Name, command.Icon, command.Color);
        if (updated.IsFailure)
        {
            return updated.Error;
        }

        if (await NameIsTakenAsync(families, family, cancellationToken))
        {
            return CategoryErrors.NameTaken;
        }

        audit.Record(new AuditEntry("categories", "categories.category.updated", "category", category.Id)
        {
            OwnerUserId = category.OwnerUserId,
            Before = before,
            After = new { category.Name, category.Icon, Color = category.Color?.ToString() }
        });
        return await families.DescribeAsync(family, cancellationToken);
    }

    internal static async Task<bool> NameIsTakenAsync(CategoryFamilies families, CategoryFamilies.Family family, CancellationToken cancellationToken) =>
        family.Target.IsRoot
            ? await families.RootNameIsTakenAsync(family.Target, cancellationToken)
            : CategoryFamilies.SubcategoryNameIsTaken(family, family.Target);
}

/// <summary>Moves a subcategory under another category of the same kind. Answers with the category it moved to.</summary>
public sealed record MoveCategoryCommand(Guid CategoryId, Guid ParentId) : ICommand<CategoryResponse>;

internal sealed class MoveCategoryHandler(CategoryFamilies families, IAuditLog audit) : ICommandHandler<MoveCategoryCommand, CategoryResponse>
{
    public async Task<Result<CategoryResponse>> HandleAsync(MoveCategoryCommand command, CancellationToken cancellationToken)
    {
        if (await families.FindAsync(command.CategoryId, cancellationToken) is not { } from)
        {
            return CategoryErrors.NotFound;
        }

        if (await families.FindAsync(command.ParentId, cancellationToken) is not { } to)
        {
            return CategoryErrors.ParentNotFound;
        }

        var category = from.Target;
        if (category.ParentId == command.ParentId)
        {
            return await families.DescribeAsync(to, cancellationToken);
        }

        var moved = category.MoveTo(to.Target);
        if (moved.IsFailure)
        {
            return moved.Error;
        }

        var arrived = to with { Subcategories = [.. to.Subcategories, category], Target = category };
        if (CategoryFamilies.SubcategoryNameIsTaken(arrived, category))
        {
            return CategoryErrors.NameTaken;
        }

        audit.Record(new AuditEntry("categories", "categories.category.moved", "category", category.Id)
        {
            OwnerUserId = category.OwnerUserId,
            Before = new { ParentId = from.Root.Id },
            After = new { category.ParentId }
        });
        return await families.DescribeAsync(arrived, cancellationToken);
    }
}

public sealed record ArchiveCategoryCommand(Guid CategoryId) : ICommand<CategoryResponse>;

internal sealed class ArchiveCategoryHandler(CategoryFamilies families, IAuditLog audit, IClock clock) : ICommandHandler<ArchiveCategoryCommand, CategoryResponse>
{
    public async Task<Result<CategoryResponse>> HandleAsync(ArchiveCategoryCommand command, CancellationToken cancellationToken)
    {
        if (await families.FindAsync(command.CategoryId, cancellationToken) is not { } family)
        {
            return CategoryErrors.NotFound;
        }

        var category = family.Target;
        if (!category.IsArchived)
        {
            // The subcategories get the same instant as their category, which is how restoring it finds them again.
            var now = clock.UtcNow;
            category.Archive(now);
            if (category.IsRoot)
            {
                family.Subcategories.ForEach(subcategory => subcategory.Archive(now));
            }

            audit.Record(new AuditEntry("categories", "categories.category.archived", "category", category.Id) { OwnerUserId = category.OwnerUserId });
        }

        return await families.DescribeAsync(family, cancellationToken);
    }
}

public sealed record RestoreCategoryCommand(Guid CategoryId) : ICommand<CategoryResponse>;

internal sealed class RestoreCategoryHandler(CategoryFamilies families, IAuditLog audit) : ICommandHandler<RestoreCategoryCommand, CategoryResponse>
{
    public async Task<Result<CategoryResponse>> HandleAsync(RestoreCategoryCommand command, CancellationToken cancellationToken)
    {
        if (await families.FindAsync(command.CategoryId, cancellationToken) is not { } family)
        {
            return CategoryErrors.NotFound;
        }

        var category = family.Target;
        if (category.IsArchived)
        {
            if (!category.IsRoot && family.Root.IsArchived)
            {
                return Error.BusinessRule("categories.parent_archived", "Restore the category before its subcategory.");
            }

            if (await UpdateCategoryHandler.NameIsTakenAsync(families, family, cancellationToken))
            {
                return CategoryErrors.NameTaken;
            }

            // Only the subcategories that were archived together with it; one archived earlier stays archived.
            var archivedAt = category.ArchivedAt;
            category.Restore();
            if (category.IsRoot)
            {
                family.Subcategories.Where(subcategory => subcategory.ArchivedAt == archivedAt).ToList().ForEach(subcategory => subcategory.Restore());
            }

            audit.Record(new AuditEntry("categories", "categories.category.restored", "category", category.Id) { OwnerUserId = category.OwnerUserId });
        }

        return await families.DescribeAsync(family, cancellationToken);
    }
}

/// <summary>Replaces the tags a category suggests. A name that is not a tag yet becomes one.</summary>
public sealed record SetCategoryTagsCommand(Guid CategoryId, IReadOnlyList<string> Tags) : ICommand<CategoryResponse>;

internal sealed class SetCategoryTagsHandler(CategoryFamilies families, TagResolver tagResolver, IAuditLog audit) : ICommandHandler<SetCategoryTagsCommand, CategoryResponse>
{
    public async Task<Result<CategoryResponse>> HandleAsync(SetCategoryTagsCommand command, CancellationToken cancellationToken)
    {
        if (await families.FindAsync(command.CategoryId, cancellationToken) is not { } family)
        {
            return CategoryErrors.NotFound;
        }

        var category = family.Target;
        var tags = await tagResolver.ResolveAsync(category.WorkspaceId, category.OwnerUserId, command.Tags, cancellationToken);
        if (tags.IsFailure)
        {
            return tags.Error;
        }

        var tagged = category.SetTags(tags.Value.Select(tag => tag.Id).ToList());
        if (tagged.IsFailure)
        {
            return tagged.Error;
        }

        audit.Record(new AuditEntry("categories", "categories.category.tags_set", "category", category.Id)
        {
            OwnerUserId = category.OwnerUserId,
            After = new { Tags = tags.Value.Select(tag => tag.Name).ToList() }
        });
        return await families.DescribeAsync(family, cancellationToken);
    }
}
