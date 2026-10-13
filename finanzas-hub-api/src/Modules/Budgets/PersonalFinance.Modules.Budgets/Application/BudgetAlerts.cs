using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PersonalFinance.BuildingBlocks.Application.Events;
using PersonalFinance.Modules.Budgets.Application.Abstractions;
using PersonalFinance.Modules.Budgets.Domain;
using PersonalFinance.Modules.Categories.Contracts;
using PersonalFinance.Modules.Ledger.Contracts.Events;
using PersonalFinance.Modules.Notifications.Contracts;

namespace PersonalFinance.Modules.Budgets.Application;

/// <summary>
/// Looks at the budget of a category every time one of its expenses changes, and tells the person when it
/// reaches the share they asked to be warned at and when it passes the limit. Each is said once per budget and
/// month: the key of the notice carries both, and Notifications does not repeat a key.
/// </summary>
internal sealed class EvaluateBudgetThresholds(IBudgetsDb db, BudgetReader reader, ICategoryLookup categories, INotificationPublisher notifications)
    : IDomainEventHandler<LedgerOperationChanged>
{
    public async Task HandleAsync(LedgerOperationChanged domainEvent, CancellationToken cancellationToken)
    {
        if (!domainEvent.IsExpense || domainEvent.CategoryId is not { } categoryId
            || await categories.FindAsync(categoryId, cancellationToken) is not { } category)
        {
            return;
        }

        var month = Budget.FirstDay(domainEvent.OccurredOn);
        var budget = await db.Budgets.AsNoTracking().SingleOrDefaultAsync(
            candidate => candidate.CategoryId == category.RootId && candidate.EffectiveFrom <= month && (candidate.EndsOn == null || candidate.EndsOn >= month),
            cancellationToken);
        if (budget is null)
        {
            return;
        }

        var row = await reader.DescribeAsync(budget, month, cancellationToken);
        var key = $"budget:{budget.CategoryId}:{month:yyyy-MM}";
        var figures = $"Llevas {Money(row.Spent, row.Currency)} de {Money(row.Limit, row.Currency)} en {month.ToString("MM/yyyy", CultureInfo.InvariantCulture)}.";
        if (row.Spent > row.Limit)
        {
            await notifications.PublishAsync(
                new NotificationRequest(
                    domainEvent.OwnerUserId, domainEvent.WorkspaceId, NotificationCategory.Budget, NotificationSeverity.Critical,
                    $"{row.CategoryName} excedido", $"Superaste tu límite por {Money(-row.Remaining, row.Currency)}. {figures}", "/budgets", $"{key}:over"),
                cancellationToken);
        }
        else if (!budget.IsFixed && row.UsedPercent >= budget.AlertThresholdPercent)
        {
            // A fixed payment reaches its limit by being paid: that is not news.
            await notifications.PublishAsync(
                new NotificationRequest(
                    domainEvent.OwnerUserId, domainEvent.WorkspaceId, NotificationCategory.Budget, NotificationSeverity.Warning,
                    $"{row.CategoryName} al {decimal.Truncate(row.UsedPercent)} %", $"Pasaste el {budget.AlertThresholdPercent} % de tu presupuesto. {figures}", "/budgets",
                    $"{key}:{budget.AlertThresholdPercent}"),
                cancellationToken);
        }
    }

    private static string Money(decimal amount, string currency) => $"{currency} {amount.ToString("N2", CultureInfo.InvariantCulture)}";
}
