using Microsoft.EntityFrameworkCore;
using PersonalFinance.BuildingBlocks.Application.Abstractions;
using PersonalFinance.BuildingBlocks.Application.Messaging;
using PersonalFinance.BuildingBlocks.Application.Security;
using PersonalFinance.BuildingBlocks.Domain;
using PersonalFinance.Modules.Ledger.Application.Abstractions;
using PersonalFinance.Modules.Ledger.Domain;

namespace PersonalFinance.Modules.Ledger.Application.ExchangeRates;

/// <summary>Where every conversion to the base currency gets its rates from.</summary>
internal sealed class ExchangeRateBook(ILedgerDb db, IPersonalSpace workspace)
{
    public async Task<RateTable> LoadAsync(CancellationToken cancellationToken)
    {
        // A handful of rows per person. No owner condition: the context's global filter already limits them.
        var baseCurrency = workspace.BaseCurrency;
        IReadOnlyList<ExchangeRate> rates = baseCurrency is null
            ? []
            : await db.ExchangeRates.AsNoTracking().Where(rate => rate.BaseCurrency == baseCurrency).ToListAsync(cancellationToken);
        return new RateTable(baseCurrency, rates);
    }

    /// <summary>
    /// Writes on each entry what it is worth in the base currency on its date. Called whenever an operation is
    /// registered or its amounts change, so the reports of that month stay put when the rate moves later.
    /// </summary>
    public async Task ValueAsync(Operation operation, CancellationToken cancellationToken)
    {
        var table = await LoadAsync(cancellationToken);
        foreach (var entry in operation.Entries)
        {
            if (entry.Currency != table.BaseCurrency && operation.ManualFxRate is { } stated && table.BaseCurrency is { } baseCurrency)
            {
                // The rate the person stated for this transaction wins over the one of their table.
                entry.ValueAt(stated, baseCurrency);
                continue;
            }

            entry.ValueWith(entry.Currency == table.BaseCurrency ? null : table.At(entry.Currency, entry.OccurredOn));
        }
    }

    /// <summary>The base currency of the workspace; null until it is chosen.</summary>
    public string? BaseCurrency => workspace.BaseCurrency;
}

public sealed record ExchangeRateResponse(string Currency, string BaseCurrency, decimal Rate, DateOnly AsOf);

/// <summary>The rate in force for each currency the person has given one.</summary>
public sealed record ListExchangeRatesQuery : IQuery<IReadOnlyList<ExchangeRateResponse>>;

internal sealed class ListExchangeRatesHandler(ExchangeRateBook book) : IQueryHandler<ListExchangeRatesQuery, IReadOnlyList<ExchangeRateResponse>>
{
    public async Task<Result<IReadOnlyList<ExchangeRateResponse>>> HandleAsync(ListExchangeRatesQuery query, CancellationToken cancellationToken)
    {
        var table = await book.LoadAsync(cancellationToken);
        return Currency.Supported
            .Select(currency => table.Latest(currency.Code))
            .OfType<ExchangeRate>()
            .OrderBy(rate => rate.Currency, StringComparer.Ordinal)
            .Select(rate => new ExchangeRateResponse(rate.Currency, rate.BaseCurrency, rate.Rate, rate.AsOf))
            .ToList();
    }
}

/// <summary>States how much of the base currency one unit of another currency is worth, from a day on.</summary>
/// <param name="AsOf">Today when left out. A rate already given for that day is replaced.</param>
public sealed record SetExchangeRateCommand(string Currency, decimal Rate, DateOnly? AsOf) : ICommand<ExchangeRateResponse>;

internal sealed class SetExchangeRateHandler(ILedgerDb db, LedgerScope scope, IPersonalSpace workspace, IAuditLog audit, IClock clock)
    : ICommandHandler<SetExchangeRateCommand, ExchangeRateResponse>
{
    public async Task<Result<ExchangeRateResponse>> HandleAsync(SetExchangeRateCommand command, CancellationToken cancellationToken)
    {
        if (scope.Owner is not (var workspaceId, var userId))
        {
            return LedgerErrors.Unauthenticated;
        }

        if (workspace.BaseCurrency is not { } baseCurrency)
        {
            return Error.BusinessRule("ledger.exchange_rate.no_base_currency", "The workspace has no base currency yet. Finish setting it up first.");
        }

        var asOf = command.AsOf ?? scope.Today;
        if (asOf > scope.Today)
        {
            return Error.Validation("ledger.exchange_rate.date_in_future", "An exchange rate cannot start on a day that has not come.");
        }

        var set = ExchangeRate.Set(workspaceId, userId, baseCurrency, command.Currency, command.Rate, asOf, clock.UtcNow);
        if (set.IsFailure)
        {
            return set.Error;
        }

        var rate = set.Value;
        var existing = await db.ExchangeRates.SingleOrDefaultAsync(
            other => other.BaseCurrency == baseCurrency && other.Currency == rate.Currency && other.AsOf == asOf, cancellationToken);
        var before = existing?.Rate;
        if (existing is null)
        {
            db.ExchangeRates.Add(rate);
        }
        else
        {
            existing.Change(command.Rate, clock.UtcNow);
            rate = existing;
        }

        audit.Record(new AuditEntry("ledger", "ledger.exchange_rate.set", "exchange_rate", rate.Id)
        {
            OwnerUserId = userId,
            Before = before is null ? null : new { Rate = before },
            After = new { rate.Currency, rate.BaseCurrency, rate.Rate, rate.AsOf }
        });
        return new ExchangeRateResponse(rate.Currency, rate.BaseCurrency, rate.Rate, rate.AsOf);
    }
}

/// <param name="MinorUnits">How many decimals an amount in it carries.</param>
public sealed record CurrencyResponse(string Code, string Symbol, string Name, string PluralName, int MinorUnits);

/// <summary>The currencies an account can have, with how each is written. The same for everyone.</summary>
public sealed record ListCurrenciesQuery : IQuery<IReadOnlyList<CurrencyResponse>>;

internal sealed class ListCurrenciesHandler : IQueryHandler<ListCurrenciesQuery, IReadOnlyList<CurrencyResponse>>
{
    public Task<Result<IReadOnlyList<CurrencyResponse>>> HandleAsync(ListCurrenciesQuery query, CancellationToken cancellationToken) =>
        Task.FromResult<Result<IReadOnlyList<CurrencyResponse>>>(Currency.Supported
            .Select(currency => new CurrencyResponse(currency.Code, currency.Symbol, currency.Name, currency.PluralName, currency.MinorUnits))
            .ToList());
}
