using Microsoft.EntityFrameworkCore;
using PersonalFinance.BuildingBlocks.Application.Abstractions;
using PersonalFinance.BuildingBlocks.Application.Events;
using PersonalFinance.BuildingBlocks.Application.Messaging;
using PersonalFinance.BuildingBlocks.Domain;
using PersonalFinance.Modules.Categories.Application.Abstractions;
using PersonalFinance.Modules.Categories.Domain;
using PersonalFinance.Modules.Workspaces.Contracts.Events;

namespace PersonalFinance.Modules.Categories.Application.Defaults;

/// <summary>
/// Copies the default catalog to one person in one workspace. Running it again adds only what is missing: an
/// entry the person already received is skipped even if they renamed or archived it, and so is one whose name
/// they are already using.
/// </summary>
internal sealed class DefaultCategoriesSeeder(ICategoriesDb db, IAuditLog audit, IClock clock)
{
    public async Task<int> SeedAsync(Guid workspaceId, Guid userId, CancellationToken cancellationToken)
    {
        // The person receiving the categories is not always the one making the request: someone who registers
        // from an invitation is not signed in yet. So the owner filter is replaced by an explicit condition.
        var roots = await db.Categories.IgnoreQueryFilters().AsNoTracking()
            .Where(category => category.WorkspaceId == workspaceId && category.OwnerUserId == userId && category.ParentId == null)
            .Select(category => new { category.SystemKey, category.NameKey, category.ArchivedAt })
            .ToListAsync(cancellationToken);
        var tags = await db.Tags.IgnoreQueryFilters()
            .Where(tag => tag.WorkspaceId == workspaceId && tag.OwnerUserId == userId)
            .ToDictionaryAsync(tag => tag.Name, cancellationToken);

        var now = clock.UtcNow;
        var created = 0;
        foreach (var template in DefaultCatalog.Categories)
        {
            var root = Category.CreateRoot(workspaceId, userId, template.Kind, template.Name, template.Icon, template.Color, now, template.Key).Value;
            if (roots.Any(existing => existing.SystemKey == template.Key || (existing.ArchivedAt == null && existing.NameKey == root.NameKey)))
            {
                continue;
            }

            var suggested = new List<Guid>();
            foreach (var name in template.Tags)
            {
                if (!tags.TryGetValue(name, out var tag))
                {
                    tag = Tag.Create(workspaceId, userId, name, now).Value;
                    tags.Add(name, tag);
                    db.Tags.Add(tag);
                }

                // A tag the person archived is not brought back by the catalog.
                if (!tag.IsArchived)
                {
                    suggested.Add(tag.Id);
                }
            }

            root.SetTags(suggested);
            db.Categories.Add(root);
            db.Categories.AddRange(template.Subcategories.Select(subcategory => root.AddSubcategory(subcategory.Name, now, subcategory.Key).Value));
            created++;
        }

        if (created > 0)
        {
            audit.Record(new AuditEntry("categories", "categories.defaults.seeded", "workspace", workspaceId)
            {
                WorkspaceId = workspaceId,
                OwnerUserId = userId,
                After = new { Categories = created }
            });
        }

        return created;
    }
}

/// <summary>The owner gets the catalog when the workspace is ready to hold data.</summary>
internal sealed class SeedCategoriesOnOnboardingCompleted(DefaultCategoriesSeeder seeder) : IDomainEventHandler<OnboardingCompleted>
{
    public Task HandleAsync(OnboardingCompleted domainEvent, CancellationToken cancellationToken) =>
        seeder.SeedAsync(domainEvent.WorkspaceId, domainEvent.OwnerUserId, cancellationToken);
}

/// <summary>Each member gets their own copy: categories are private, so nobody inherits the owner's.</summary>
internal sealed class SeedCategoriesOnMemberJoined(DefaultCategoriesSeeder seeder) : IDomainEventHandler<MemberJoined>
{
    public Task HandleAsync(MemberJoined domainEvent, CancellationToken cancellationToken) =>
        seeder.SeedAsync(domainEvent.WorkspaceId, domainEvent.UserId, cancellationToken);
}

public sealed record SeededCategoriesResponse(int Created);

/// <summary>For someone whose categories were never seeded, or who wants back the defaults they are missing.</summary>
public sealed record SeedDefaultCategoriesCommand : ICommand<SeededCategoriesResponse>;

internal sealed class SeedDefaultCategoriesHandler(DefaultCategoriesSeeder seeder, CategoryFamilies families)
    : ICommandHandler<SeedDefaultCategoriesCommand, SeededCategoriesResponse>
{
    public async Task<Result<SeededCategoriesResponse>> HandleAsync(SeedDefaultCategoriesCommand command, CancellationToken cancellationToken) =>
        families.Owner is not (var workspaceId, var userId)
            ? CategoryErrors.Unauthenticated
            : new SeededCategoriesResponse(await seeder.SeedAsync(workspaceId, userId, cancellationToken));
}
