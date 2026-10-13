using System.Globalization;
using PersonalFinance.BuildingBlocks.Application.Events;
using PersonalFinance.Modules.Ledger.Application.ExchangeRates;
using PersonalFinance.Modules.Ledger.Contracts.Events;
using PersonalFinance.Modules.Notifications.Contracts;

namespace PersonalFinance.Modules.Ledger.Application.Transactions;

/// <summary>
/// Tells the person about an income or an expense at or above the amount they chose. Only when it is
/// registered: editing or voiding it says nothing more.
/// </summary>
internal sealed class NotifyLargeMovement(INotificationPublisher notifications, ExchangeRateBook rates) : IDomainEventHandler<LedgerOperationChanged>
{
    public async Task HandleAsync(LedgerOperationChanged domainEvent, CancellationToken cancellationToken)
    {
        if (domainEvent.Change != LedgerOperationChange.Posted
            || await notifications.GetLargeMovementThresholdAsync(domainEvent.OwnerUserId, cancellationToken) is not { } threshold)
        {
            return;
        }

        // The threshold is in the base currency. A movement in a currency without a rate cannot be compared and says nothing.
        var inBase = (await rates.LoadAsync(cancellationToken)).ToBase(domainEvent.Currency, domainEvent.Amount);
        if (inBase is null || inBase < threshold)
        {
            return;
        }

        var amount = $"{domainEvent.Currency} {domainEvent.Amount.ToString("N2", CultureInfo.InvariantCulture)}";
        await notifications.PublishAsync(
            new NotificationRequest(
                domainEvent.OwnerUserId, domainEvent.WorkspaceId, NotificationCategory.LargeMovement, NotificationSeverity.Info,
                $"{(domainEvent.IsExpense ? "Gasto" : "Ingreso")} de {amount}",
                $"{domainEvent.Description} · {domainEvent.OccurredOn.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}",
                "/transactions", $"movement:{domainEvent.OperationId}"),
            cancellationToken);
    }
}
