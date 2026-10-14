using Microsoft.EntityFrameworkCore;
using PersonalFinance.BuildingBlocks.Application.Security;
using PersonalFinance.BuildingBlocks.Domain;
using PersonalFinance.Modules.Integrations.Application.Abstractions;
using PersonalFinance.Modules.Integrations.Domain;
using PersonalFinance.Modules.Ledger.Contracts;

namespace PersonalFinance.Modules.Integrations.Application;

internal static class ImportErrors
{
    public static readonly Error Unauthenticated = Error.Unauthorized("auth.unauthenticated", "Authentication is required.");

    public static readonly Error NotFound = Error.NotFound("imports.not_found", "The import was not found.");

    public static readonly Error RecordNotFound = Error.NotFound("imports.record_not_found", "That line of the import was not found.");

    public static readonly Error AccountNotFound = Error.NotFound("imports.account_not_found", "The account was not found.");

    public static readonly Error CategoryNotFound = Error.NotFound("imports.category_not_found", "The category was not found.");

    public static readonly Error CategoryKindMismatch = Error.BusinessRule(
        "imports.category_kind_mismatch", "An income needs an income category and an expense an expense category.");
}

public sealed record ImportSummaryResponse(
    Guid Id, string FileName, ImportFormat Format, ImportStatus Status, int RecordCount, int ImportedCount, DateTimeOffset CreatedAt, DateTimeOffset? CommittedAt);

/// <summary>An account as the file names it and which of the person's accounts it is.</summary>
/// <param name="SourceName">Empty for the single account of a bank statement.</param>
/// <param name="CreateAs">Set when a new account will be opened for it on confirming.</param>
/// <param name="RecordCount">Lines of the file on that account.</param>
public sealed record ImportAccountResponse(Guid Id, string SourceName, string Currency, Guid? AccountId, NewAccountType? CreateAs, int RecordCount);

/// <summary>A category as the file names it and which of the person's categories it is.</summary>
/// <param name="SourceName">Empty for the lines of a file that names no category.</param>
/// <param name="Create">Set when a category with that name will be created on confirming.</param>
public sealed record ImportCategoryResponse(Guid Id, bool IsIncome, string SourceGroup, string SourceName, Guid? CategoryId, bool Create, int RecordCount);

/// <param name="Amount">Positive. For an opening balance it carries its sign.</param>
/// <param name="CategoryId">The category it will get: the one chosen for the line, or the one of its category in the file.</param>
/// <param name="IsDuplicate">The ledger already has a movement on that account, that day, for that amount.</param>
/// <param name="Error">Why the line cannot be imported.</param>
public sealed record ImportRecordResponse(
    Guid Id, int Line, ImportKind Kind, DateOnly? OccurredOn, decimal Amount, string? Currency, string Description, string SourceAccount, string? SourceCounterAccount,
    string SourceCategory, Guid? CategoryId, ImportRecordState State, bool IsDuplicate, string? Error);

/// <param name="Ready">Lines that will enter the ledger on confirming.</param>
/// <param name="Pending">Lines that still need an account, a category or the right currency.</param>
/// <param name="Duplicates">Lines left out because the ledger already has a movement like them.</param>
public sealed record ImportCountsResponse(int Ready, int Pending, int Duplicates, int Excluded, int Invalid, int Imported);

/// <summary>An import as the person reviews it.</summary>
/// <param name="Records">One page of the lines of the filter, in the order of the file.</param>
/// <param name="MatchingRecords">How many lines the filter has in all.</param>
public sealed record ImportDetailResponse(
    ImportSummaryResponse Import, ImportCountsResponse Counts, IReadOnlyList<ImportAccountResponse> Accounts, IReadOnlyList<ImportCategoryResponse> Categories,
    IReadOnlyList<ImportRecordResponse> Records, int MatchingRecords);

/// <summary>
/// Reads imports and decides where each line stands. Everything read is already limited to the current person,
/// whose imports, like their ledger, live in their personal workspace.
/// </summary>
internal sealed class ImportReader(IIntegrationsDb db, ILedgerQueries ledgerQueries, ILedgerImport ledger, ICurrentUser currentUser, IPersonalSpace workspace)
{
    public const int PageSize = 100;

    public (Guid WorkspaceId, Guid UserId)? Owner =>
        currentUser.UserId is { } userId && workspace.WorkspaceId is { } workspaceId ? (workspaceId, userId) : null;

    public string? BaseCurrency => workspace.BaseCurrency;

    /// <summary>The owner filter makes an import of someone else indistinguishable from one that does not exist.</summary>
    public Task<ImportSession?> FindAsync(Guid importId, CancellationToken cancellationToken) =>
        db.ImportSessions.SingleOrDefaultAsync(session => session.Id == importId, cancellationToken);

    public Task<List<ImportRecord>> RecordsAsync(Guid importId, CancellationToken cancellationToken) =>
        db.ImportRecords.Where(record => record.SessionId == importId).OrderBy(record => record.Line).ToListAsync(cancellationToken);

    /// <summary>The accounts of the person a file can be imported into. The fund of a goal is moved only by its goal.</summary>
    public async Task<IReadOnlyList<LedgerAccountBalance>> AccountsAsync(CancellationToken cancellationToken) =>
        (await ledgerQueries.GetAccountBalancesAsync(cancellationToken)).Where(account => account.Type != LedgerAccountType.GoalFund).ToList();

    /// <summary>
    /// Marks the lines the ledger already has: same account, same day, same amount. Each movement of the ledger
    /// accounts for one line only, so two coffees of the same price on the same day are told apart from one.
    /// </summary>
    public async Task MarkDuplicatesAsync(ImportSession session, IReadOnlyList<ImportRecord> records, CancellationToken cancellationToken)
    {
        var dated = records.Where(record => record is { Error: null, OperationId: null, OccurredOn: not null }).ToList();
        if (dated.Count == 0)
        {
            return;
        }

        var existing = await ledger.GetEntryKeysAsync(dated.Min(record => record.OccurredOn!.Value), dated.Max(record => record.OccurredOn!.Value), cancellationToken);
        var left = existing.GroupBy(key => key).ToDictionary(group => group.Key, group => group.Count());
        foreach (var record in dated)
        {
            var isDuplicate = false;
            if (session.AccountOf(record.SourceAccount)?.AccountId is { } accountId)
            {
                var key = new LedgerEntryKey(accountId, record.OccurredOn!.Value, record.SignedAmount);
                if (left.GetValueOrDefault(key) is > 0 and var count)
                {
                    left[key] = count - 1;
                    isDuplicate = true;
                }
            }

            record.MarkDuplicate(isDuplicate);
        }
    }

    public static ImportSummaryResponse Summarize(ImportSession session) => new(
        session.Id, session.FileName, session.Format, session.Status, session.RecordCount, session.ImportedCount, session.CreatedAt, session.CommittedAt);

    public async Task<ImportDetailResponse> DescribeAsync(
        ImportSession session, IReadOnlyList<ImportRecord> records, string? filter, int offset, CancellationToken cancellationToken)
    {
        var currencies = (await AccountsAsync(cancellationToken)).ToDictionary(account => account.Id, account => account.Currency);
        var states = records.Select(record => (Record: record, State: session.StateOf(record, currencies))).ToList();
        int Count(params ImportRecordState[] wanted) => states.Count(row => wanted.Contains(row.State));

        ImportRecordState[] pending = [ImportRecordState.NeedsAccount, ImportRecordState.NeedsCategory, ImportRecordState.CurrencyMismatch];
        ImportRecordState[]? shown = filter?.Trim().ToLowerInvariant() switch
        {
            "pending" => pending,
            "ready" => [ImportRecordState.Ready],
            "duplicate" => [ImportRecordState.Duplicate],
            "excluded" => [ImportRecordState.Excluded],
            "invalid" => [ImportRecordState.Invalid],
            "imported" => [ImportRecordState.Imported],
            _ => null
        };
        var matching = states.Where(row => shown is null || shown.Contains(row.State)).ToList();
        return new ImportDetailResponse(
            Summarize(session),
            new ImportCountsResponse(
                Count(ImportRecordState.Ready), Count(pending), Count(ImportRecordState.Duplicate), Count(ImportRecordState.Excluded), Count(ImportRecordState.Invalid),
                Count(ImportRecordState.Imported)),
            session.Accounts
                .Select(account => new ImportAccountResponse(
                    account.Id, account.SourceName, account.Currency, account.AccountId, account.CreateAs,
                    records.Count(record => Same(record.SourceAccount, account.SourceName) || Same(record.SourceCounterAccount, account.SourceName))))
                .OrderBy(account => account.SourceName, StringComparer.InvariantCultureIgnoreCase).ToList(),
            session.Categories
                .Select(category => new ImportCategoryResponse(
                    category.Id, category.IsIncome, category.SourceGroup, category.SourceName, category.CategoryId, category.Create,
                    records.Count(record => record.Kind == (category.IsIncome ? ImportKind.Income : ImportKind.Expense)
                        && Same(record.SourceCategoryGroup ?? string.Empty, category.SourceGroup) && Same(record.SourceCategory, category.SourceName))))
                .OrderBy(category => category.IsIncome).ThenBy(category => category.SourceGroup, StringComparer.InvariantCultureIgnoreCase)
                .ThenBy(category => category.SourceName, StringComparer.InvariantCultureIgnoreCase).ToList(),
            matching.Skip(Math.Max(0, offset)).Take(PageSize).Select(row => Describe(session, row.Record, row.State)).ToList(),
            matching.Count);
    }

    private static ImportRecordResponse Describe(ImportSession session, ImportRecord record, ImportRecordState state) => new(
        record.Id, record.Line, record.Kind, record.OccurredOn, record.Amount, record.Currency, record.Description, record.SourceAccount, record.SourceCounterAccount,
        string.IsNullOrEmpty(record.SourceCategoryGroup) || Same(record.SourceCategoryGroup, record.SourceCategory)
            ? record.SourceCategory
            : $"{record.SourceCategoryGroup} / {record.SourceCategory}",
        record.Kind is ImportKind.Income or ImportKind.Expense
            ? record.CategoryId ?? session.CategoryOf(record.Kind == ImportKind.Income, record.SourceCategoryGroup, record.SourceCategory)?.CategoryId
            : null,
        state, record.IsDuplicate, record.Error);

    private static bool Same(string? one, string? other) => string.Equals(one, other, StringComparison.OrdinalIgnoreCase);
}
