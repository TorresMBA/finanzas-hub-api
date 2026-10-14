using Microsoft.EntityFrameworkCore;
using PersonalFinance.BuildingBlocks.Application.Abstractions;
using PersonalFinance.BuildingBlocks.Application.Messaging;
using PersonalFinance.BuildingBlocks.Domain;
using PersonalFinance.Modules.Categories.Contracts;
using PersonalFinance.Modules.Integrations.Application.Abstractions;
using PersonalFinance.Modules.Integrations.Domain;

namespace PersonalFinance.Modules.Integrations.Application;

/// <summary>"When the description contains this text, the movement is of this category."</summary>
/// <param name="CategoryName">With its parent for a subcategory, as "Comida / Restaurantes".</param>
public sealed record CategorizationRuleResponse(Guid Id, string Pattern, Guid CategoryId, string CategoryName, bool IsIncome, bool IsActive);

internal static class RuleErrors
{
    public static readonly Error NotFound = Error.NotFound("imports.rule.not_found", "The rule was not found.");

    public static readonly Error Duplicate = Error.Conflict("imports.rule.duplicate", "There is already a rule for that text.");
}

/// <summary>Reads the rules of the current person with the name of their category.</summary>
internal sealed class RuleReader(ICategoryLookup categories)
{
    public async Task<IReadOnlyList<CategorizationRuleResponse>> DescribeAsync(IReadOnlyList<CategorizationRule> rules, CancellationToken cancellationToken)
    {
        var known = await categories.GetAsync(rules.Select(rule => rule.CategoryId).Distinct().ToList(), cancellationToken);
        return rules
            .Select(rule => (Rule: rule, Category: known.GetValueOrDefault(rule.CategoryId)))
            .Select(row => new CategorizationRuleResponse(
                row.Rule.Id, row.Rule.Pattern, row.Rule.CategoryId,
                row.Category is null ? string.Empty : row.Category.ParentName is null ? row.Category.Name : $"{row.Category.ParentName} / {row.Category.Name}",
                row.Category?.Use == CategoryUse.Income, row.Rule.IsActive))
            .ToList();
    }
}

public sealed record ListCategorizationRulesQuery : IQuery<IReadOnlyList<CategorizationRuleResponse>>;

internal sealed class ListCategorizationRulesHandler(IIntegrationsDb db, RuleReader reader)
    : IQueryHandler<ListCategorizationRulesQuery, IReadOnlyList<CategorizationRuleResponse>>
{
    public async Task<Result<IReadOnlyList<CategorizationRuleResponse>>> HandleAsync(ListCategorizationRulesQuery query, CancellationToken cancellationToken) =>
        Result.Success(await reader.DescribeAsync(
            await db.CategorizationRules.AsNoTracking().OrderBy(rule => rule.PatternKey).ToListAsync(cancellationToken), cancellationToken));
}

/// <param name="RuleId">Null creates the rule.</param>
public sealed record SaveCategorizationRuleCommand(Guid? RuleId, string Pattern, Guid CategoryId, bool IsActive) : ICommand<CategorizationRuleResponse>;

internal sealed class SaveCategorizationRuleHandler(
    IIntegrationsDb db, ImportReader imports, RuleReader reader, ICategoryLookup categories, IAuditLog audit, IClock clock)
    : ICommandHandler<SaveCategorizationRuleCommand, CategorizationRuleResponse>
{
    public async Task<Result<CategorizationRuleResponse>> HandleAsync(SaveCategorizationRuleCommand command, CancellationToken cancellationToken)
    {
        if (imports.Owner is not (var workspaceId, var userId))
        {
            return ImportErrors.Unauthenticated;
        }

        // A category of someone else is not found, exactly as one that does not exist.
        if (await categories.FindAsync(command.CategoryId, cancellationToken) is not { IsArchived: false })
        {
            return ImportErrors.CategoryNotFound;
        }

        CategorizationRule rule;
        if (command.RuleId is { } ruleId)
        {
            if (await db.CategorizationRules.SingleOrDefaultAsync(existing => existing.Id == ruleId, cancellationToken) is not { } found)
            {
                return RuleErrors.NotFound;
            }

            var updated = found.Update(command.Pattern, command.CategoryId, command.IsActive);
            if (updated.IsFailure)
            {
                return updated.Error;
            }

            rule = found;
        }
        else
        {
            var created = CategorizationRule.Create(workspaceId, userId, command.Pattern, command.CategoryId, clock.UtcNow);
            if (created.IsFailure)
            {
                return created.Error;
            }

            rule = created.Value;
            db.CategorizationRules.Add(rule);
        }

        if (await db.CategorizationRules.AnyAsync(other => other.PatternKey == rule.PatternKey && other.Id != rule.Id, cancellationToken))
        {
            return RuleErrors.Duplicate;
        }

        audit.Record(new AuditEntry("integrations", command.RuleId is null ? "imports.rule.created" : "imports.rule.updated", "categorization_rule", rule.Id)
        {
            OwnerUserId = userId,
            After = new { rule.Pattern, rule.CategoryId, rule.IsActive }
        });
        return (await reader.DescribeAsync([rule], cancellationToken))[0];
    }
}

public sealed record DeleteCategorizationRuleCommand(Guid RuleId) : ICommand<Unit>;

internal sealed class DeleteCategorizationRuleHandler(IIntegrationsDb db, IAuditLog audit) : ICommandHandler<DeleteCategorizationRuleCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(DeleteCategorizationRuleCommand command, CancellationToken cancellationToken)
    {
        if (await db.CategorizationRules.SingleOrDefaultAsync(rule => rule.Id == command.RuleId, cancellationToken) is not { } rule)
        {
            return RuleErrors.NotFound;
        }

        db.CategorizationRules.Remove(rule);
        audit.Record(new AuditEntry("integrations", "imports.rule.deleted", "categorization_rule", rule.Id) { OwnerUserId = rule.OwnerUserId, Before = new { rule.Pattern } });
        return Unit.Value;
    }
}
