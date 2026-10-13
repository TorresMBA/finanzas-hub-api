using System.Buffers.Text;
using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PersonalFinance.BuildingBlocks.Application.Messaging;
using PersonalFinance.BuildingBlocks.Domain;
using PersonalFinance.Modules.Categories.Contracts;
using PersonalFinance.Modules.Ledger.Application.Abstractions;
using PersonalFinance.Modules.Ledger.Domain;

namespace PersonalFinance.Modules.Ledger.Application.Transactions;

/// <summary>Income and expenses of one currency among what the filter matches. Currencies are never added together.</summary>
public sealed record CurrencyTotalsResponse(string Currency, decimal Income, decimal Expense, decimal Net, int Count);

/// <param name="NextCursor">Send it back to get the following page; null on the last one.</param>
/// <param name="Totals">Over everything the filter matches, not only this page.</param>
public sealed record TransactionPageResponse(IReadOnlyList<TransactionResponse> Items, string? NextCursor, IReadOnlyList<CurrencyTotalsResponse> Totals);

/// <param name="Status">Voided transactions are left out unless this asks for them.</param>
/// <param name="CategoryId">Matches the category and its subcategories.</param>
/// <param name="Tag">A tag name.</param>
/// <param name="Text">Searched in the description, the merchant and the note.</param>
public sealed record ListTransactionsQuery(
    TransactionType? Type, OperationStatus? Status, Guid? AccountId, Guid? CategoryId, string? Tag, string? Text, DateOnly? From, DateOnly? To, string? Cursor, int? Limit)
    : IQuery<TransactionPageResponse>;

internal sealed class ListTransactionsHandler(ILedgerDb db, TransactionReader reader, ICategoryLookup categories, ITagLookup tags)
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

        if (query.Type is { } type)
        {
            var kind = type.ToKind();
            operations = operations.Where(operation => operation.Kind == kind);
        }

        if (query.AccountId is { } accountId)
        {
            operations = operations.Where(operation => operation.Entries.Any(entry => entry.AccountId == accountId));
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

        var totals = await TotalsAsync(operations, cancellationToken);

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
            await reader.DescribeAsync(items, cancellationToken),
            hasMore ? new PageCursor(items[^1].OccurredOn, items[^1].Id).ToString() : null,
            totals);
    }

    /// <summary>Only income and expenses: an opening balance or an adjustment is neither.</summary>
    private static async Task<IReadOnlyList<CurrencyTotalsResponse>> TotalsAsync(IQueryable<Operation> operations, CancellationToken cancellationToken)
    {
        var rows = await operations
            .Where(operation => operation.Kind == OperationKind.Income || operation.Kind == OperationKind.Expense)
            .SelectMany(operation => operation.Entries, (operation, entry) => new { entry.Currency, operation.Kind, entry.Amount })
            .GroupBy(row => new { row.Currency, row.Kind })
            .Select(group => new { group.Key.Currency, group.Key.Kind, Total = group.Sum(row => row.Amount), Count = group.Count() })
            .ToListAsync(cancellationToken);
        return rows.GroupBy(row => row.Currency)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group =>
            {
                var income = group.Where(row => row.Kind == OperationKind.Income).Sum(row => row.Total);
                var expense = group.Where(row => row.Kind == OperationKind.Expense).Sum(row => row.Total);
                return new CurrencyTotalsResponse(group.Key, income, expense, income - expense, group.Sum(row => row.Count));
            })
            .ToList();
    }

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
