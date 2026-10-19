using System.Buffers.Text;
using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PersonalFinance.BuildingBlocks.Application.Messaging;
using PersonalFinance.BuildingBlocks.Domain;
using PersonalFinance.Modules.Categories.Contracts;
using PersonalFinance.Modules.Ledger.Application.Abstractions;
using PersonalFinance.Modules.Ledger.Application.ExchangeRates;
using PersonalFinance.Modules.Ledger.Domain;

namespace PersonalFinance.Modules.Ledger.Application.Transactions;

/// <summary>Income and expenses of one currency among what the filter matches. Currencies are never added together.</summary>
/// <param name="BaseNet">
/// The net brought to the base currency, each amount at what it was worth when it was registered. Null without a
/// base currency, or when part of it could not be converted.
/// </param>
public sealed record CurrencyTotalsResponse(string Currency, decimal Income, decimal Expense, decimal Net, int Count, decimal? BaseNet = null);

/// <summary>
/// The same income and expenses, every currency brought to the base currency of the workspace. An amount in
/// another currency counts for what it was worth when it was registered, or at the latest rate when it had none.
/// </summary>
/// <param name="MissingRates">Currencies that could not be converted and are left out of the amounts.</param>
public sealed record BaseTotalsResponse(string Currency, decimal Income, decimal Expense, decimal Net, IReadOnlyList<string> MissingRates);

/// <param name="NextCursor">Send it back to get the following page; null on the last one.</param>
/// <param name="Totals">Over everything the filter matches, not only this page.</param>
/// <param name="BaseTotals">Null while the workspace has no base currency.</param>
public sealed record TransactionPageResponse(
    IReadOnlyList<TransactionResponse> Items, string? NextCursor, IReadOnlyList<CurrencyTotalsResponse> Totals, BaseTotalsResponse? BaseTotals);

/// <param name="Kind">Income, Expense or Transfer; or the movements the application makes on an account.</param>
/// <param name="Status">Voided transactions are left out unless this asks for them.</param>
/// <param name="CategoryId">Matches the category and its subcategories.</param>
/// <param name="Tag">A tag name.</param>
/// <param name="Text">Searched in the description, the merchant and the note.</param>
/// <param name="Currency">Movements on an account of that currency; a transfer matches by either of its sides.</param>
public sealed record ListTransactionsQuery(
    OperationKind? Kind, OperationStatus? Status, Guid? AccountId, Guid? CategoryId, string? Tag, string? Text, DateOnly? From, DateOnly? To, string? Cursor, int? Limit,
    string? Currency = null)
    : IQuery<TransactionPageResponse>;

internal sealed class ListTransactionsHandler(ILedgerDb db, TransactionReader reader, ICategoryLookup categories, ITagLookup tags, ExchangeRateBook rates)
    : IQueryHandler<ListTransactionsQuery, TransactionPageResponse>
{
    private const int DefaultLimit = 50;
    private const int MaxLimit = 100;

    public async Task<Result<TransactionPageResponse>> HandleAsync(ListTransactionsQuery query, CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(query.Limit ?? DefaultLimit, 1, MaxLimit);
        PageCursor? after = null;
        if (!string.IsNullOrEmpty(query.Cursor) && !PageCursor.TryParse(query.Cursor, out after))
        {
            return Error.Validation("ledger.cursor_invalid", "The cursor is not valid. Start again from the first page.");
        }

        // No owner condition here on purpose: the context's global filter already limits operations to the current person.
        var operations = db.Operations.AsNoTracking();
        operations = query.Status is { } status
            ? operations.Where(operation => operation.Status == status)
            : operations.Where(operation => operation.Status != OperationStatus.Voided);

        if (query.Kind is { } kind)
        {
            operations = operations.Where(operation => operation.Kind == kind);
        }

        if (query.AccountId is { } accountId)
        {
            operations = operations.Where(operation => operation.Entries.Any(entry => entry.AccountId == accountId));
        }

        if (!string.IsNullOrWhiteSpace(query.Currency))
        {
            var currency = BuildingBlocks.Domain.Currency.From(query.Currency);
            if (currency.IsFailure)
            {
                return currency.Error;
            }

            var code = currency.Value.Code;
            operations = operations.Where(operation => operation.Entries.Any(entry => entry.Currency == code));
        }

        if (query.CategoryId is { } categoryId)
        {
            var family = await categories.WithSubcategoriesAsync(categoryId, cancellationToken);
            operations = operations.Where(operation => operation.CategoryId != null && family.Contains(operation.CategoryId.Value));
        }

        if (!string.IsNullOrWhiteSpace(query.Tag))
        {
            // A name that is not a tag of the person matches nothing.
            var tagId = (await tags.FindByNameAsync(query.Tag, cancellationToken))?.Id ?? Guid.Empty;
            operations = operations.Where(operation => operation.Tags.Any(tag => tag.TagId == tagId));
        }

        if (!string.IsNullOrWhiteSpace(query.Text))
        {
            var text = query.Text.Trim().ToLowerInvariant();
            operations = operations.Where(operation =>
                operation.Description.ToLower().Contains(text)
                || (operation.Merchant != null && operation.Merchant.ToLower().Contains(text))
                || (operation.Note != null && operation.Note.ToLower().Contains(text)));
        }

        if (query.From is { } from)
        {
            operations = operations.Where(operation => operation.OccurredOn >= from);
        }

        if (query.To is { } to)
        {
            operations = operations.Where(operation => operation.OccurredOn <= to);
        }

        var (totals, baseTotals) = await TotalsAsync(operations, cancellationToken);

        if (after is not null)
        {
            var (date, id) = (after.OccurredOn, after.Id);
            operations = operations.Where(operation => operation.OccurredOn < date || (operation.OccurredOn == date && operation.Id.CompareTo(id) < 0));
        }

        // Newest first. The id breaks ties and is time ordered, so within a day the last one registered comes first.
        var page = await operations
            .Include(operation => operation.Entries)
            .OrderByDescending(operation => operation.OccurredOn)
            .ThenByDescending(operation => operation.Id)
            .Take(limit + 1)
            .ToListAsync(cancellationToken);
        var hasMore = page.Count > limit;
        var items = page.Take(limit).ToList();
        return new TransactionPageResponse(
            await reader.DescribeAsync(items, query.AccountId, cancellationToken),
            hasMore ? new PageCursor(items[^1].OccurredOn, items[^1].Id).ToString() : null,
            totals,
            baseTotals);
    }

    /// <summary>Only income and expenses: a transfer, an opening balance or an adjustment is neither. A refund takes away from the expenses.</summary>
    private async Task<(IReadOnlyList<CurrencyTotalsResponse> ByCurrency, BaseTotalsResponse? InBase)> TotalsAsync(
        IQueryable<Operation> operations, CancellationToken cancellationToken)
    {
        var rows = await operations
            .Where(operation => operation.Kind == OperationKind.Income || operation.Kind == OperationKind.Expense || operation.Kind == OperationKind.Refund)
            .SelectMany(operation => operation.Entries, (operation, entry) => new
            {
                entry.Currency,
                Kind = operation.Kind == OperationKind.Refund ? OperationKind.Expense : operation.Kind,
                Amount = operation.Kind == OperationKind.Refund ? -entry.Amount : entry.Amount,
                BaseAmount = operation.Kind == OperationKind.Refund ? -entry.BaseAmount : entry.BaseAmount
            })
            .GroupBy(row => new { row.Currency, row.Kind })
            .Select(group => new TotalsRow(
                group.Key.Currency,
                group.Key.Kind,
                group.Sum(row => row.Amount),
                group.Count(),
                group.Sum(row => row.BaseAmount ?? 0m),
                group.Sum(row => row.BaseAmount == null ? row.Amount : 0m)))
            .ToListAsync(cancellationToken);

        var table = await rates.LoadAsync(cancellationToken);
        var byCurrency = rows.GroupBy(row => row.Currency)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => CurrencyTotals(group, table))
            .ToList();
        return (byCurrency, BaseTotals(rows, table));
    }

    private static BaseTotalsResponse? BaseTotals(IReadOnlyList<TotalsRow> rows, RateTable table)
    {
        if (table.BaseCurrency is not { } baseCurrency)
        {
            return null;
        }

        var (income, expense) = (new BaseSum(table), new BaseSum(table));
        foreach (var row in rows)
        {
            (row.Kind == OperationKind.Income ? income : expense).Add(row.Currency, row.Unvalued, row.Valued);
        }

        return new BaseTotalsResponse(
            baseCurrency, income.Total, expense.Total, income.Total - expense.Total,
            income.Missing.Union(expense.Missing).Order(StringComparer.Ordinal).ToList());
    }

    private static CurrencyTotalsResponse CurrencyTotals(IGrouping<string, TotalsRow> group, RateTable table)
    {
        var income = group.Where(row => row.Kind == OperationKind.Income).Sum(row => row.Total);
        var expense = group.Where(row => row.Kind == OperationKind.Expense).Sum(row => row.Total);

        // The same net in the base currency; null when part of it cannot be converted.
        var inBase = new BaseSum(table);
        foreach (var row in group)
        {
            var sign = row.Kind == OperationKind.Income ? 1m : -1m;
            inBase.Add(row.Currency, sign * row.Unvalued, sign * row.Valued);
        }

        var baseNet = table.BaseCurrency is null || inBase.Missing.Count > 0 ? (decimal?)null : inBase.Total;
        return new CurrencyTotalsResponse(group.Key, income, expense, income - expense, group.Sum(row => row.Count), baseNet);
    }

    /// <summary>The income or expenses of one currency: what was valued when posted and what still has to be converted.</summary>
    private sealed record TotalsRow(string Currency, OperationKind Kind, decimal Total, int Count, decimal Valued, decimal Unvalued);

    /// <summary>Where a page ended: the date and id of its last row. Opaque to the client.</summary>
    private sealed record PageCursor(DateOnly OccurredOn, Guid Id)
    {
        public static bool TryParse(string text, out PageCursor? cursor)
        {
            cursor = null;
            try
            {
                var parts = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(text)).Split('|');
                if (parts.Length == 2
                    && DateOnly.TryParseExact(parts[0], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                    && Guid.TryParseExact(parts[1], "N", out var id))
                {
                    cursor = new PageCursor(date, id);
                }
            }
            catch (FormatException)
            {
                // Not base64url: the caller reports an invalid cursor.
            }

            return cursor is not null;
        }

        public override string ToString() =>
            Base64Url.EncodeToString(Encoding.UTF8.GetBytes($"{OccurredOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}|{Id:N}"));
    }
}
