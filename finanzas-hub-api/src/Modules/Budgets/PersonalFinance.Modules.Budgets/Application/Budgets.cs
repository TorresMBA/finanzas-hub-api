using Microsoft.EntityFrameworkCore;
using PersonalFinance.BuildingBlocks.Application.Abstractions;
using PersonalFinance.BuildingBlocks.Application.Messaging;
using PersonalFinance.BuildingBlocks.Domain;
using PersonalFinance.Modules.Budgets.Application.Abstractions;
using PersonalFinance.Modules.Budgets.Domain;
using PersonalFinance.Modules.Categories.Contracts;
using PersonalFinance.Modules.Ledger.Contracts;

namespace PersonalFinance.Modules.Budgets.Application;

/// <param name="Month">As <c>2026-10</c>. The current month of the workspace when left out.</param>
public sealed record GetBudgetsQuery(string? Month) : IQuery<BudgetsResponse>;

internal sealed class GetBudgetsHandler(BudgetReader reader) : IQueryHandler<GetBudgetsQuery, BudgetsResponse>
{
    public async Task<Result<BudgetsResponse>> HandleAsync(GetBudgetsQuery query, CancellationToken cancellationToken)
    {
        var month = reader.Month(query.Month);
        return month.IsFailure ? month.Error : await reader.ReadAsync(month.Value, cancellationToken);
    }
}

/// <param name="SubcategoryName">The subcategory it went on; null when it went on the category of the budget itself.</param>
/// <param name="Amount">Positive, in the currency of the account it left.</param>
public sealed record BudgetMovementResponse(
    Guid Id, DateOnly OccurredOn, string Description, string? SubcategoryName, string AccountName, decimal Amount, string Currency);

/// <param name="Amount">Spent that day, in the base currency.</param>
public sealed record BudgetDayResponse(DateOnly Day, decimal Amount);

/// <param name="Movements">The expenses of the month that count towards the limit, newest first.</param>
/// <param name="Daily">Every day of the month, also the ones with nothing spent.</param>
public sealed record BudgetDetailResponse(BudgetResponse Budget, IReadOnlyList<BudgetMovementResponse> Movements, IReadOnlyList<BudgetDayResponse> Daily);

/// <param name="Month">As <c>2026-10</c>. The current month of the workspace when left out.</param>
public sealed record GetBudgetDetailQuery(Guid BudgetId, string? Month) : IQuery<BudgetDetailResponse>;

internal sealed class GetBudgetDetailHandler(IBudgetsDb db, BudgetReader reader, ILedgerQueries ledger, ICategoryLookup categories)
    : IQueryHandler<GetBudgetDetailQuery, BudgetDetailResponse>
{
    public async Task<Result<BudgetDetailResponse>> HandleAsync(GetBudgetDetailQuery query, CancellationToken cancellationToken)
    {
        var month = reader.Month(query.Month);
        if (month.IsFailure)
        {
            return month.Error;
        }

        // A budget of someone else is not found: the context's global filter leaves it out.
        var budget = await db.Budgets.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == query.BudgetId, cancellationToken);
        if (budget is null || !budget.Covers(month.Value))
        {
            return BudgetErrors.NotFound;
        }

        var (first, last) = (month.Value, month.Value.AddMonths(1).AddDays(-1));
        var expenses = await ledger.GetExpensesAsync(await categories.WithSubcategoriesAsync(budget.CategoryId, cancellationToken), first, last, cancellationToken);
        var names = await categories.GetAsync(expenses.Select(expense => expense.CategoryId).Where(id => id != budget.CategoryId).Distinct().ToList(), cancellationToken);
        var byDay = expenses.GroupBy(expense => expense.OccurredOn).ToDictionary(group => group.Key, group => group.Sum(expense => expense.BaseAmount ?? 0m));
        return new BudgetDetailResponse(
            await reader.DescribeAsync(budget, first, cancellationToken),
            expenses.Select(expense => new BudgetMovementResponse(
                expense.Id, expense.OccurredOn, expense.Description, names.GetValueOrDefault(expense.CategoryId)?.Name, expense.AccountName, expense.Amount,
                expense.Currency)).ToList(),
            Enumerable.Range(0, last.Day).Select(offset => first.AddDays(offset)).Select(day => new BudgetDayResponse(day, byDay.GetValueOrDefault(day))).ToList());
    }
}

/// <summary>A monthly limit for a root expense category, from the current month on.</summary>
public sealed record CreateBudgetCommand(Guid CategoryId, decimal Amount, int AlertThresholdPercent, bool IsFixed) : ICommand<BudgetResponse>;

internal sealed class CreateBudgetHandler(IBudgetsDb db, BudgetReader reader, ICategoryLookup categories, IAuditLog audit, IClock clock)
    : ICommandHandler<CreateBudgetCommand, BudgetResponse>
{
    public async Task<Result<BudgetResponse>> HandleAsync(CreateBudgetCommand command, CancellationToken cancellationToken)
    {
        if (reader.Owner is not (var workspaceId, var userId))
        {
            return BudgetErrors.Unauthenticated;
        }

        if (reader.BaseCurrency is not { } currency)
        {
            return Error.BusinessRule("budgets.base_currency_required", "Choose the currency of the workspace before creating a budget.");
        }

        if (await categories.FindAsync(command.CategoryId, cancellationToken) is not { } category)
        {
            return Error.NotFound("budgets.category_not_found", "The category was not found.");
        }

        if (category.Use != CategoryUse.Expense || category.RootId != category.Id || category.IsArchived)
        {
            return Error.BusinessRule("budgets.category_invalid", "A budget goes on an expense category of the first level that is in use.");
        }

        var month = reader.CurrentMonth();
        if (await db.Budgets.AnyAsync(budget => budget.CategoryId == command.CategoryId && (budget.EndsOn == null || budget.EndsOn >= month), cancellationToken))
        {
            return Error.Conflict("budgets.already_exists", "That category already has a budget.");
        }

        var created = Budget.Create(
            workspaceId, userId, command.CategoryId, command.Amount, currency, command.AlertThresholdPercent, command.IsFixed, month, clock.UtcNow);
        if (created.IsFailure)
        {
            return created.Error;
        }

        var budget = created.Value;
        db.Budgets.Add(budget);
        audit.Record(new AuditEntry("budgets", "budgets.budget.created", "budget", budget.Id)
        {
            OwnerUserId = userId,
            After = new { budget.CategoryId, budget.Amount, budget.Currency, budget.AlertThresholdPercent, budget.IsFixed, budget.EffectiveFrom }
        });
        return await reader.DescribeAsync(budget, month, cancellationToken);
    }
}

/// <param name="CategoryId">An expense category of the first level.</param>
/// <param name="SuggestedLimit">The average of the three months, rounded up to a whole amount. Zero when nothing was spent.</param>
/// <param name="Months">What was spent in each of the three closed months before the current one, oldest first.</param>
public sealed record BudgetSuggestionResponse(Guid CategoryId, string? Currency, decimal SuggestedLimit, IReadOnlyList<BudgetSuggestionMonth> Months);

/// <param name="Month">As <c>2026-10</c>.</param>
public sealed record BudgetSuggestionMonth(string Month, decimal Spent);

/// <summary>What a limit could be, from what was spent in the category in the last three closed months.</summary>
public sealed record SuggestBudgetQuery(Guid CategoryId) : IQuery<BudgetSuggestionResponse>;

internal sealed class SuggestBudgetHandler(BudgetReader reader, ILedgerQueries ledger, ICategoryLookup categories) : IQueryHandler<SuggestBudgetQuery, BudgetSuggestionResponse>
{
    public const int MonthsAveraged = 3;

    public async Task<Result<BudgetSuggestionResponse>> HandleAsync(SuggestBudgetQuery query, CancellationToken cancellationToken)
    {
        // Someone else's category is not found by the lookup, so it answers 404 like any other.
        if (await categories.FindAsync(query.CategoryId, cancellationToken) is not { Use: CategoryUse.Expense } category || category.RootId != category.Id)
        {
            return Error.NotFound("budgets.category_not_found", "The category was not found.");
        }

        var family = await categories.WithSubcategoriesAsync(category.Id, cancellationToken);
        var months = new List<BudgetSuggestionMonth>();
        for (var back = MonthsAveraged; back >= 1; back--)
        {
            var first = reader.CurrentMonth().AddMonths(-back);
            var totals = await ledger.GetCategoryTotalsAsync(first, first.AddMonths(1).AddDays(-1), cancellationToken);
            var spent = totals.Categories.Where(total => !total.IsIncome && family.Contains(total.CategoryId)).Sum(total => total.Amount);
            months.Add(new BudgetSuggestionMonth($"{first.Year:D4}-{first.Month:D2}", spent));
        }

        var suggested = Math.Max(0m, Math.Ceiling(months.Sum(month => month.Spent) / MonthsAveraged));
        return new BudgetSuggestionResponse(category.Id, reader.BaseCurrency, suggested, months);
    }
}

/// <summary>
/// Changes the limit, the warning or the fixed mark from a month on: the current one when left out. The months
/// before it keep the limit they had.
/// </summary>
/// <param name="FromMonth">As <c>2026-11</c>. It cannot be before the month the budget started.</param>
public sealed record AdjustBudgetCommand(Guid BudgetId, decimal Amount, int AlertThresholdPercent, bool IsFixed, string? FromMonth = null) : ICommand<BudgetResponse>;

internal sealed class AdjustBudgetHandler(IBudgetsDb db, BudgetReader reader, IAuditLog audit, IClock clock) : ICommandHandler<AdjustBudgetCommand, BudgetResponse>
{
    public async Task<Result<BudgetResponse>> HandleAsync(AdjustBudgetCommand command, CancellationToken cancellationToken)
    {
        if (await db.Budgets.SingleOrDefaultAsync(candidate => candidate.Id == command.BudgetId, cancellationToken) is not { } budget)
        {
            return BudgetErrors.NotFound;
        }

        var from = reader.Month(command.FromMonth);
        if (from.IsFailure)
        {
            return from.Error;
        }

        var month = from.Value;
        var before = new { budget.Amount, budget.AlertThresholdPercent, budget.IsFixed };
        var adjusted = budget.Adjust(command.Amount, command.AlertThresholdPercent, command.IsFixed, month, clock.UtcNow);
        if (adjusted.IsFailure)
        {
            return adjusted.Error;
        }

        var current = adjusted.Value;
        if (!ReferenceEquals(current, budget))
        {
            db.Budgets.Add(current);
        }

        audit.Record(new AuditEntry("budgets", "budgets.budget.adjusted", "budget", current.Id)
        {
            OwnerUserId = budget.OwnerUserId,
            Before = before,
            After = new { current.Amount, current.AlertThresholdPercent, current.IsFixed, current.EffectiveFrom, ReplacedBudgetId = ReferenceEquals(current, budget) ? (Guid?)null : budget.Id }
        });
        return await reader.DescribeAsync(current, month, cancellationToken);
    }
}

/// <summary>
/// Stops a budget from the current month on. The months it already governed keep it; one that started this
/// month governed none, so its row goes away and the audit log keeps the trace.
/// </summary>
public sealed record EndBudgetCommand(Guid BudgetId) : ICommand<Unit>;

internal sealed class EndBudgetHandler(IBudgetsDb db, BudgetReader reader, IAuditLog audit) : ICommandHandler<EndBudgetCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(EndBudgetCommand command, CancellationToken cancellationToken)
    {
        if (await db.Budgets.SingleOrDefaultAsync(candidate => candidate.Id == command.BudgetId, cancellationToken) is not { } budget)
        {
            return BudgetErrors.NotFound;
        }

        var month = reader.CurrentMonth();
        if (budget.IsInForce && budget.EffectiveFrom >= month)
        {
            db.Budgets.Remove(budget);
        }
        else if (budget.EndBefore(month) is { IsFailure: true } ended)
        {
            return ended.Error;
        }

        audit.Record(new AuditEntry("budgets", "budgets.budget.ended", "budget", budget.Id)
        {
            OwnerUserId = budget.OwnerUserId,
            Before = new { budget.CategoryId, budget.Amount, budget.EffectiveFrom },
            After = new { budget.EndsOn }
        });
        return Unit.Value;
    }
}
