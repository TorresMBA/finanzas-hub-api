using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PersonalFinance.BuildingBlocks.Application.Abstractions;
using PersonalFinance.BuildingBlocks.Application.Messaging;
using PersonalFinance.BuildingBlocks.Domain;
using PersonalFinance.Modules.Categories.Contracts;
using PersonalFinance.Modules.Integrations.Application.Abstractions;
using PersonalFinance.Modules.Integrations.Domain;
using PersonalFinance.Modules.Ledger.Contracts;

namespace PersonalFinance.Modules.Integrations.Application;

/// <summary>The latest imports of the person, newest first.</summary>
public sealed record ListImportsQuery : IQuery<IReadOnlyList<ImportSummaryResponse>>;

internal sealed class ListImportsHandler(IIntegrationsDb db) : IQueryHandler<ListImportsQuery, IReadOnlyList<ImportSummaryResponse>>
{
    public async Task<Result<IReadOnlyList<ImportSummaryResponse>>> HandleAsync(ListImportsQuery query, CancellationToken cancellationToken) =>
        (await db.ImportSessions.AsNoTracking().OrderByDescending(session => session.CreatedAt).Take(20).ToListAsync(cancellationToken))
            .Select(ImportReader.Summarize).ToList();
}

/// <param name="Filter">pending, ready, duplicate, excluded, invalid or imported. Every line when left out.</param>
public sealed record GetImportQuery(Guid ImportId, string? Filter, int Offset) : IQuery<ImportDetailResponse>;

internal sealed class GetImportHandler(ImportReader reader) : IQueryHandler<GetImportQuery, ImportDetailResponse>
{
    public async Task<Result<ImportDetailResponse>> HandleAsync(GetImportQuery query, CancellationToken cancellationToken) =>
        await reader.FindAsync(query.ImportId, cancellationToken) is { } session
            ? await reader.DescribeAsync(session, await reader.RecordsAsync(session.Id, cancellationToken), query.Filter, query.Offset, cancellationToken)
            : ImportErrors.NotFound;
}

/// <summary>
/// Reads a file and leaves its lines waiting for review. Nothing enters the ledger here. The content of the
/// file is never logged nor kept: only its lines, and a hash to recognise it.
/// </summary>
/// <param name="AccountId">Where a bank statement goes. A Bluecoins export brings its own accounts.</param>
public sealed record UploadImportCommand(string? FileName, byte[] Content, Guid? AccountId) : ICommand<ImportDetailResponse>;

internal sealed class UploadImportHandler(IIntegrationsDb db, ImportReader reader, ICategoryDirectory categories, ISpreadsheets spreadsheets, IAuditLog audit, IClock clock)
    : ICommandHandler<UploadImportCommand, ImportDetailResponse>
{
    public async Task<Result<ImportDetailResponse>> HandleAsync(UploadImportCommand command, CancellationToken cancellationToken)
    {
        if (reader.Owner is not (var workspaceId, var userId))
        {
            return ImportErrors.Unauthenticated;
        }

        if (command.Content.Length is 0 or > ImportSession.MaxFileBytes)
        {
            return Error.Validation("imports.file_size_invalid", $"The file must have something in it and at most {ImportSession.MaxFileBytes / (1024 * 1024)} MB.");
        }

        var workbook = ISpreadsheets.IsWorkbook(command.Content);
        var text = workbook ? AsTable(spreadsheets.ReadFirstSheet(command.Content)) : Decode(command.Content);
        var parsed = text is null ? (Result<ParsedFile>)ImportFileReader.NotRecognized : ImportFileReader.Read(text);
        if (parsed.IsFailure)
        {
            return parsed.Error;
        }

        // A bank statement saved as an Excel workbook is read as the same table.
        var (format, rows) = (workbook && parsed.Value.Format == ImportFormat.Csv ? ImportFormat.Xlsx : parsed.Value.Format, parsed.Value.Rows);
        if (rows.Count is 0 or > ImportSession.MaxRows)
        {
            return Error.Validation("imports.row_count_invalid", $"The file must have between 1 and {ImportSession.MaxRows} movements. Split a longer one by year.");
        }

        // The same file sent again while it waits for review is the same import, not a second copy of its lines.
        var hash = Convert.ToHexString(SHA256.HashData(command.Content));
        if (await db.ImportSessions.SingleOrDefaultAsync(open => open.FileHash == hash && open.Status == ImportStatus.Review, cancellationToken) is { } waiting)
        {
            return await reader.DescribeAsync(waiting, await reader.RecordsAsync(waiting.Id, cancellationToken), null, 0, cancellationToken);
        }

        var accounts = await reader.AccountsAsync(cancellationToken);
        var session = ImportSession.Start(workspaceId, userId, format, command.FileName, hash, rows.Count, clock.UtcNow);
        if (format == ImportFormat.Bluecoins)
        {
            var fallback = reader.BaseCurrency ?? "PEN";
            foreach (var name in rows.SelectMany(row => new[] { row.Account, row.CounterAccount ?? string.Empty }).Where(name => name.Length > 0)
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var currency = rows.Where(row => row.Currency is not null && Same(row.Account, name)).GroupBy(row => row.Currency!)
                    .OrderByDescending(group => group.Count()).Select(group => group.Key).FirstOrDefault();
                var match = accounts.FirstOrDefault(account => TextKey.Of(account.Name) == TextKey.Of(name));
                session.AddAccount(name, match?.Currency ?? currency ?? fallback, match?.Id);
            }
        }
        else
        {
            if (command.AccountId is not { } accountId)
            {
                return Error.Validation("imports.account_required", "Choose the account this statement is of.");
            }

            if (accounts.FirstOrDefault(account => account.Id == accountId) is not { } account)
            {
                return ImportErrors.AccountNotFound;
            }

            session.AddAccount(string.Empty, account.Currency, account.Id);
        }

        var known = await categories.ListAsync(cancellationToken);
        foreach (var key in rows.Where(row => row.Kind is ImportKind.Income or ImportKind.Expense)
                     .Select(row => (IsIncome: row.Kind == ImportKind.Income, Group: TextKey.Of(row.CategoryGroup), Name: TextKey.Of(row.Category))).Distinct())
        {
            var sample = rows.First(row => row.Kind is ImportKind.Income or ImportKind.Expense && (row.Kind == ImportKind.Income) == key.IsIncome
                && TextKey.Of(row.CategoryGroup) == key.Group && TextKey.Of(row.Category) == key.Name);
            var match = Match(known, key.IsIncome, key.Group, key.Name);
            session.AddCategory(key.IsIncome, sample.CategoryGroup, sample.Category, match, match is null && key.Name.Length > 0);
        }

        var records = rows.Select(row => ImportRecord.From(session, row)).ToList();
        await ApplyRulesAsync(records, known, cancellationToken);
        await reader.MarkDuplicatesAsync(session, records, cancellationToken);

        db.ImportSessions.Add(session);
        db.ImportRecords.AddRange(records);
        audit.Record(new AuditEntry("integrations", "imports.uploaded", "import", session.Id)
        {
            OwnerUserId = userId,
            After = new { session.FileName, Format = format.ToString(), Lines = rows.Count, Invalid = rows.Count(row => row.Error is not null) }
        });
        return await reader.DescribeAsync(session, records, null, 0, cancellationToken);
    }

    /// <summary>A line of a file that names no category takes the one of the most specific rule its description matches.</summary>
    private async Task ApplyRulesAsync(List<ImportRecord> records, IReadOnlyList<CategoryInfo> known, CancellationToken cancellationToken)
    {
        var uses = known.ToDictionary(category => category.Id, category => category.Use);
        var rules = (await db.CategorizationRules.AsNoTracking().Where(rule => rule.IsActive).ToListAsync(cancellationToken))
            .Where(rule => uses.ContainsKey(rule.CategoryId)).OrderByDescending(rule => rule.PatternKey.Length).ThenBy(rule => rule.CreatedAt).ToList();
        if (rules.Count == 0)
        {
            return;
        }

        foreach (var record in records.Where(record => record is { Error: null, SourceCategory.Length: 0, Kind: ImportKind.Income or ImportKind.Expense }))
        {
            var use = record.Kind == ImportKind.Income ? CategoryUse.Income : CategoryUse.Expense;
            record.Classify(rules.FirstOrDefault(rule => uses[rule.CategoryId] == use && rule.Matches(record.Description))?.CategoryId);
        }
    }

    /// <summary>The category of the person a category of the file is: the same name under the same group, or the only one with that name.</summary>
    private static Guid? Match(IReadOnlyList<CategoryInfo> known, bool isIncome, string group, string name)
    {
        if (name.Length == 0)
        {
            return null;
        }

        var named = known.Where(category => (category.Use == CategoryUse.Income) == isIncome && TextKey.Of(category.Name) == name).ToList();
        var exact = named.FirstOrDefault(category => group.Length > 0 && TextKey.Of(category.ParentName ?? category.Name) == group);
        return exact?.Id ?? (named.Count == 1 ? named[0].Id : null);
    }

    private static bool Same(string? one, string? other) => string.Equals(one, other, StringComparison.OrdinalIgnoreCase);

    /// <summary>The first sheet of a workbook as the text of a table, so it is read by the same rules as a CSV. Null when it is not a workbook.</summary>
    private static string? AsTable(IReadOnlyList<IReadOnlyList<string>>? sheet) =>
        sheet is null
            ? null
            : string.Join('\n', sheet.Select(row => string.Join(',', row.Select(cell => $"\"{cell.Replace("\"", "\"\"")}\""))));

    /// <summary>Apps export in UTF-8 or UTF-16; banks, often in the code page of Windows. A file that is not valid UTF-8 is read as the latter.</summary>
    private static string Decode(byte[] content)
    {
        if (content is [0xFF, 0xFE, ..])
        {
            return Encoding.Unicode.GetString(content);
        }

        try
        {
            return new UTF8Encoding(false, true).GetString(content);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(content);
        }
    }
}

/// <param name="AccountId">One of the person's accounts.</param>
/// <param name="CreateAs">Instead of an account: open a new one of this kind, named as in the file, on confirming.</param>
public sealed record MapImportAccountCommand(Guid ImportId, Guid MappingId, Guid? AccountId, NewAccountType? CreateAs) : ICommand<Unit>;

internal sealed class MapImportAccountHandler(ImportReader reader) : ICommandHandler<MapImportAccountCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(MapImportAccountCommand command, CancellationToken cancellationToken)
    {
        if (await reader.FindAsync(command.ImportId, cancellationToken) is not { } session)
        {
            return ImportErrors.NotFound;
        }

        if (command.AccountId is { } accountId && (await reader.AccountsAsync(cancellationToken)).All(account => account.Id != accountId))
        {
            return ImportErrors.AccountNotFound;
        }

        var mapped = session.MapAccount(command.MappingId, command.AccountId, command.CreateAs);
        if (mapped.IsFailure)
        {
            return mapped.Error;
        }

        // Which lines the ledger already has depends on the account they go to.
        await reader.MarkDuplicatesAsync(session, await reader.RecordsAsync(session.Id, cancellationToken), cancellationToken);
        return Unit.Value;
    }
}

/// <param name="Create">Instead of a category: create one named as in the file on confirming.</param>
public sealed record MapImportCategoryCommand(Guid ImportId, Guid MappingId, Guid? CategoryId, bool Create) : ICommand<Unit>;

internal sealed class MapImportCategoryHandler(ImportReader reader, ICategoryLookup categories) : ICommandHandler<MapImportCategoryCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(MapImportCategoryCommand command, CancellationToken cancellationToken)
    {
        if (await reader.FindAsync(command.ImportId, cancellationToken) is not { } session)
        {
            return ImportErrors.NotFound;
        }

        if (command.CategoryId is { } categoryId)
        {
            if (await categories.FindAsync(categoryId, cancellationToken) is not { IsArchived: false } category)
            {
                return ImportErrors.CategoryNotFound;
            }

            if (session.Categories.FirstOrDefault(mapping => mapping.Id == command.MappingId) is { } target && target.IsIncome != (category.Use == CategoryUse.Income))
            {
                return ImportErrors.CategoryKindMismatch;
            }
        }

        var mapped = session.MapCategory(command.MappingId, command.CategoryId, command.Create);
        return mapped.IsFailure ? mapped.Error : Unit.Value;
    }
}

/// <summary>What the person decides about one line: its category, and whether it goes in.</summary>
/// <param name="CategoryId">Left out, the category stays as it is.</param>
/// <param name="Excluded">Left out, it stays as it is. False on a duplicate says it is another movement and brings it in.</param>
public sealed record ReviewImportRecordCommand(Guid ImportId, Guid RecordId, Guid? CategoryId, bool? Excluded) : ICommand<Unit>;

internal sealed class ReviewImportRecordHandler(IIntegrationsDb db, ImportReader reader, ICategoryLookup categories) : ICommandHandler<ReviewImportRecordCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(ReviewImportRecordCommand command, CancellationToken cancellationToken)
    {
        if (await reader.FindAsync(command.ImportId, cancellationToken) is not { } session)
        {
            return ImportErrors.NotFound;
        }

        if (session.Status == ImportStatus.Committed)
        {
            return ImportSession.AlreadyCommitted;
        }

        if (await db.ImportRecords.SingleOrDefaultAsync(line => line.Id == command.RecordId && line.SessionId == session.Id, cancellationToken) is not { } record)
        {
            return ImportErrors.RecordNotFound;
        }

        if (command.CategoryId is { } categoryId)
        {
            if (await categories.FindAsync(categoryId, cancellationToken) is not { IsArchived: false } category)
            {
                return ImportErrors.CategoryNotFound;
            }

            if (record.Kind is not (ImportKind.Income or ImportKind.Expense) || (record.Kind == ImportKind.Income) != (category.Use == CategoryUse.Income))
            {
                return ImportErrors.CategoryKindMismatch;
            }

            record.Classify(categoryId);
        }

        if (command.Excluded is { } excluded)
        {
            record.Exclude(excluded);
        }

        return Unit.Value;
    }
}

/// <summary>
/// Registers in the ledger every line that is ready, opening the accounts and creating the categories the
/// person asked for. All of it or nothing. An import is confirmed once.
/// </summary>
public sealed record CommitImportCommand(Guid ImportId, string IdempotencyKey) : ICommand<ImportDetailResponse>, IIdempotentCommand;

internal sealed class CommitImportHandler(ImportReader reader, ILedgerImport ledger, ICategoryDirectory categories, IAuditLog audit, IClock clock)
    : ICommandHandler<CommitImportCommand, ImportDetailResponse>
{
    public async Task<Result<ImportDetailResponse>> HandleAsync(CommitImportCommand command, CancellationToken cancellationToken)
    {
        if (await reader.FindAsync(command.ImportId, cancellationToken) is not { } session)
        {
            return ImportErrors.NotFound;
        }

        if (session.Status == ImportStatus.Committed)
        {
            return ImportSession.AlreadyCommitted;
        }

        // The ledger may have changed since the file was read: another import, or the same movements typed by hand.
        var records = await reader.RecordsAsync(session.Id, cancellationToken);
        await reader.MarkDuplicatesAsync(session, records, cancellationToken);

        var currencies = (await reader.AccountsAsync(cancellationToken)).ToDictionary(account => account.Id, account => account.Currency);
        var states = records.Select(record => (Record: record, State: session.StateOf(record, currencies))).ToList();
        var pending = states.Count(row => row.State is ImportRecordState.NeedsAccount or ImportRecordState.NeedsCategory or ImportRecordState.CurrencyMismatch);
        if (pending > 0)
        {
            return Error.BusinessRule("imports.unresolved", $"{pending} lines still need an account or a category. Resolve them or leave them out.");
        }

        var ready = states.Where(row => row.State == ImportRecordState.Ready).Select(row => row.Record).ToList();
        if (ready.Count == 0)
        {
            return Error.BusinessRule("imports.nothing_to_import", "There is nothing new to import in this file.");
        }

        foreach (var mapping in session.Accounts.Where(mapping => mapping.CreateAs is not null
                     && ready.Any(record => Same(record.SourceAccount, mapping.SourceName) || Same(record.SourceCounterAccount, mapping.SourceName))))
        {
            var opened = await ledger.OpenAccountAsync(mapping.SourceName, Enum.Parse<LedgerAccountType>(mapping.CreateAs!.Value.ToString()), mapping.Currency, cancellationToken);
            if (opened.IsFailure)
            {
                return opened.Error;
            }

            mapping.Opened(opened.Value);
        }

        var items = new List<LedgerImportItem>(ready.Count);
        foreach (var record in ready)
        {
            Guid? categoryId = null;
            if (record.Kind is ImportKind.Income or ImportKind.Expense)
            {
                var isIncome = record.Kind == ImportKind.Income;
                categoryId = record.CategoryId;
                if (categoryId is null && session.CategoryOf(isIncome, record.SourceCategoryGroup, record.SourceCategory) is { } mapping)
                {
                    if (mapping.Create)
                    {
                        var created = await categories.EnsureAsync(
                            isIncome ? CategoryUse.Income : CategoryUse.Expense, mapping.SourceName, mapping.SourceGroup.Length > 0 ? mapping.SourceGroup : null, cancellationToken);
                        if (created.IsFailure)
                        {
                            return created.Error;
                        }

                        mapping.Created(created.Value);
                    }

                    categoryId = mapping.CategoryId;
                }
            }

            items.Add(new LedgerImportItem(
                Enum.Parse<LedgerImportKind>(record.Kind.ToString()), session.AccountOf(record.SourceAccount)!.AccountId!.Value, record.Amount, record.OccurredOn!.Value,
                record.Description, record.Note, categoryId, record.Kind == ImportKind.Transfer ? session.AccountOf(record.SourceCounterAccount)!.AccountId : null,
                record.CounterAmount, record.Labels.Split(',', StringSplitOptions.RemoveEmptyEntries)));
        }

        var outcome = await ledger.PostAsync(items, cancellationToken);
        if (outcome.IsFailure)
        {
            var line = outcome.FailedIndex is { } index ? ready[index].Line : 0;
            return Error.BusinessRule("imports.record_rejected", $"Line {line}: {outcome.Error!.Message}");
        }

        foreach (var (record, operationId) in ready.Zip(outcome.OperationIds))
        {
            record.Imported(operationId);
        }

        var committed = session.Commit(ready.Count, clock.UtcNow);
        if (committed.IsFailure)
        {
            return committed.Error;
        }

        ImportMetrics.Records.Add(ready.Count, new KeyValuePair<string, object?>("outcome", "imported"));
        ImportMetrics.Records.Add(states.Count(row => row.State == ImportRecordState.Duplicate), new KeyValuePair<string, object?>("outcome", "duplicate"));
        ImportMetrics.Records.Add(states.Count(row => row.State == ImportRecordState.Invalid), new KeyValuePair<string, object?>("outcome", "invalid"));
        audit.Record(new AuditEntry("integrations", "imports.committed", "import", session.Id)
        {
            OwnerUserId = session.OwnerUserId,
            After = new { session.FileName, Imported = ready.Count, Total = records.Count }
        });
        return await reader.DescribeAsync(session, records, null, 0, cancellationToken);
    }

    private static bool Same(string? one, string? other) => string.Equals(one, other, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Throws away an import that was not confirmed. Its lines were never in the ledger.</summary>
public sealed record DiscardImportCommand(Guid ImportId) : ICommand<Unit>;

internal sealed class DiscardImportHandler(IIntegrationsDb db, ImportReader reader, IAuditLog audit) : ICommandHandler<DiscardImportCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(DiscardImportCommand command, CancellationToken cancellationToken)
    {
        if (await reader.FindAsync(command.ImportId, cancellationToken) is not { } session)
        {
            return ImportErrors.NotFound;
        }

        if (session.Status == ImportStatus.Committed)
        {
            return ImportSession.AlreadyCommitted;
        }

        // Its lines go with it: the database deletes them in cascade.
        db.ImportSessions.Remove(session);
        audit.Record(new AuditEntry("integrations", "imports.discarded", "import", session.Id) { OwnerUserId = session.OwnerUserId, Before = new { session.FileName } });
        return Unit.Value;
    }
}

internal static class ImportMetrics
{
    // Same meter name as the platform's, which is the one the hosts export.
    private static readonly System.Diagnostics.Metrics.Meter Meter = new("PersonalFinance");

    public static readonly System.Diagnostics.Metrics.Counter<long> Records =
        Meter.CreateCounter<long>("imports.records", description: "Lines of confirmed imports, by what became of them.");
}
