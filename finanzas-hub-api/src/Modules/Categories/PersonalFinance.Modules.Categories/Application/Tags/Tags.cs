using Microsoft.EntityFrameworkCore;
using PersonalFinance.BuildingBlocks.Application.Abstractions;
using PersonalFinance.BuildingBlocks.Application.Messaging;
using PersonalFinance.BuildingBlocks.Domain;
using PersonalFinance.Modules.Categories.Application.Abstractions;
using PersonalFinance.Modules.Categories.Domain;

namespace PersonalFinance.Modules.Categories.Application.Tags;

/// <param name="CategoryCount">How many active categories suggest it.</param>
public sealed record TagResponse(Guid Id, string Name, int CategoryCount);

/// <summary>Turns the names a person typed into their tags, creating the ones that do not exist yet.</summary>
internal sealed class TagResolver(ICategoriesDb db, IClock clock)
{
    public async Task<Result<IReadOnlyList<Tag>>> ResolveAsync(Guid workspaceId, Guid userId, IEnumerable<string> names, CancellationToken cancellationToken)
    {
        var normalized = new List<string>();
        foreach (var name in names)
        {
            var valid = Tag.Normalize(name);
            if (valid.IsFailure)
            {
                return valid.Error;
            }

            if (!normalized.Contains(valid.Value))
            {
                normalized.Add(valid.Value);
            }
        }

        var existing = await db.Tags.Where(tag => normalized.Contains(tag.Name)).ToListAsync(cancellationToken);
        var tags = new List<Tag>();
        foreach (var name in normalized)
        {
            var tag = existing.SingleOrDefault(candidate => candidate.Name == name);
            if (tag is null)
            {
                tag = Tag.Create(workspaceId, userId, name, clock.UtcNow).Value;
                db.Tags.Add(tag);
            }

            // Typing the name of an archived tag brings it back instead of failing on its unique name.
            tag.Restore();
            tags.Add(tag);
        }

        return tags;
    }
}

public sealed record ListTagsQuery : IQuery<IReadOnlyList<TagResponse>>;

internal sealed class ListTagsHandler(ICategoriesDb db) : IQueryHandler<ListTagsQuery, IReadOnlyList<TagResponse>>
{
    public async Task<Result<IReadOnlyList<TagResponse>>> HandleAsync(ListTagsQuery query, CancellationToken cancellationToken)
    {
        var tags = await db.Tags.AsNoTracking().Where(tag => tag.ArchivedAt == null).OrderBy(tag => tag.Name).ToListAsync(cancellationToken);
        var uses = (await db.Categories.AsNoTracking().Where(category => category.ArchivedAt == null).ToListAsync(cancellationToken))
            .SelectMany(category => category.Tags)
            .CountBy(tag => tag.TagId)
            .ToDictionary();
        return tags.Select(tag => new TagResponse(tag.Id, tag.Name, uses.GetValueOrDefault(tag.Id))).ToList();
    }
}

public sealed record RenameTagCommand(Guid TagId, string Name) : ICommand<TagResponse>;

internal sealed class RenameTagHandler(ICategoriesDb db, IAuditLog audit) : ICommandHandler<RenameTagCommand, TagResponse>
{
    public async Task<Result<TagResponse>> HandleAsync(RenameTagCommand command, CancellationToken cancellationToken)
    {
        if (await db.Tags.SingleOrDefaultAsync(tag => tag.Id == command.TagId && tag.ArchivedAt == null, cancellationToken) is not { } tag)
        {
            return CategoryErrors.TagNotFound;
        }

        var before = tag.Name;
        var renamed = tag.Rename(command.Name);
        if (renamed.IsFailure)
        {
            return renamed.Error;
        }

        // The name is unique per person, archived tags included: typing an archived name brings that tag back.
        if (tag.Name != before && await db.Tags.AnyAsync(other => other.Name == tag.Name && other.Id != tag.Id, cancellationToken))
        {
            return Error.Conflict("categories.tag.name_taken", "There is already a tag with that name.");
        }

        if (tag.Name != before)
        {
            audit.Record(new AuditEntry("categories", "categories.tag.renamed", "tag", tag.Id)
            {
                OwnerUserId = tag.OwnerUserId,
                Before = new { Name = before },
                After = new { tag.Name }
            });
        }

        var uses = await db.Categories.CountAsync(category => category.ArchivedAt == null && category.Tags.Any(suggested => suggested.TagId == tag.Id), cancellationToken);
        return new TagResponse(tag.Id, tag.Name, uses);
    }
}

public sealed record ArchiveTagCommand(Guid TagId) : ICommand<Unit>;

internal sealed class ArchiveTagHandler(ICategoriesDb db, IAuditLog audit, IClock clock) : ICommandHandler<ArchiveTagCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(ArchiveTagCommand command, CancellationToken cancellationToken)
    {
        if (await db.Tags.SingleOrDefaultAsync(tag => tag.Id == command.TagId, cancellationToken) is not { } tag)
        {
            return CategoryErrors.TagNotFound;
        }

        // An archived tag is no longer suggested by any category.
        foreach (var category in await db.Categories.Where(category => category.Tags.Any(suggested => suggested.TagId == tag.Id)).ToListAsync(cancellationToken))
        {
            category.RemoveTag(tag.Id);
        }

        tag.Archive(clock.UtcNow);
        audit.Record(new AuditEntry("categories", "categories.tag.archived", "tag", tag.Id) { OwnerUserId = tag.OwnerUserId, Before = new { tag.Name } });
        return Unit.Value;
    }
}
