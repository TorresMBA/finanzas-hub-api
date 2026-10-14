using Microsoft.EntityFrameworkCore;
using PersonalFinance.BuildingBlocks.Application.Abstractions;
using PersonalFinance.BuildingBlocks.Domain;
using PersonalFinance.Modules.Categories.Contracts;
using PersonalFinance.Modules.Ledger.Application.Abstractions;
using PersonalFinance.Modules.Ledger.Application.Accounts;
using PersonalFinance.Modules.Ledger.Application.ExchangeRates;
using PersonalFinance.Modules.Ledger.Contracts;
using PersonalFinance.Modules.Ledger.Domain;

namespace PersonalFinance.Modules.Ledger.Application.Transactions;

/// <summary>
/// Registers the movements of a file with the rules of the domain, reading accounts, categories, tags and rates
/// once for all of them instead of once per movement. Everything is about the accounts of the current person.
/// </summary>
internal sealed class LedgerImport(
    ILedgerDb db, LedgerScope scope, ExchangeRateBook rates, ICategoryLookup categories, ITagLookup tags, IAuditLog audit, IClock clock) : ILedgerImport
{
    public async Task<Result<Guid>> OpenAccountAsync(string name, LedgerAccountType type, string currency, CancellationToken cancellationToken)
    {
        if (scope.Owner is not (var workspaceId, var userId))
        {
            return LedgerErrors.Unauthenticated;
        }

        if (type is LedgerAccountType.CreditCard or LedgerAccountType.GoalFund)
        {
            return Error.Validation("ledger.import.account_type_invalid", "An import opens bank accounts, cash and wallets. Open a credit card from the accounts.");
        }

        var opened = Account.Open(workspaceId, userId, name, Enum.Parse<AccountType>(type.ToString()), currency, null, clock.UtcNow);
        if (opened.IsFailure)
        {
            return opened.Error;
        }

        var account = opened.Value;
        if (await db.NameIsTakenAsync(account, cancellationToken) || db.Accounts.Local.Any(other => other.NameKey == account.NameKey))
        {
            return LedgerErrors.AccountNameTaken;
        }

        db.Accounts.Add(account);
        audit.Record(new AuditEntry("ledger", "ledger.account.opened", "account", account.Id)
        {
            OwnerUserId = userId,
            After = new { account.Name, Type = account.Type.ToString(), account.Currency, Origin = "import" }
        });
        return account.Id;
    }

    public async Task<IReadOnlyList<LedgerEntryKey>> GetEntryKeysAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        await db.Entries.AsNoTracking()
            .Where(entry => entry.IsEffective && entry.OccurredOn >= from && entry.OccurredOn <= to)
            .Select(entry => new LedgerEntryKey(entry.AccountId, entry.OccurredOn, entry.SignedAmount))
            .ToListAsync(cancellationToken);

    public async Task<LedgerImportOutcome> PostAsync(IReadOnlyList<LedgerImportItem> items, CancellationToken cancellationToken)
    {
        if (scope.Owner is not (_, var userId))
        {
            return new LedgerImportOutcome([], null, LedgerErrors.Unauthenticated);
        }

        // The owner filter leaves out the accounts of anyone else. The ones opened by this same command are not in the database yet.
        var accountIds = items.SelectMany(item => new[] { item.AccountId, item.CounterAccountId ?? item.AccountId }).Distinct().ToList();
        var accounts = (await db.Accounts.Where(account => accountIds.Contains(account.Id)).ToListAsync(cancellationToken))
            .Concat(db.Accounts.Local.Where(account => accountIds.Contains(account.Id)))
            .DistinctBy(account => account.Id).ToDictionary(account => account.Id);
        var known = await categories.GetAsync(items.Where(item => item.CategoryId is not null).Select(item => item.CategoryId!.Value).Distinct().ToList(), cancellationToken);
        var resolved = await tags.ResolveAsync(items.SelectMany(item => item.Tags).Distinct(StringComparer.OrdinalIgnoreCase).ToList(), cancellationToken);
        if (resolved.IsFailure)
        {
            return new LedgerImportOutcome([], items.Index().First(item => item.Item.Tags.Count > 0).Index, resolved.Error);
        }

        var tagIds = resolved.Value.ToDictionary(tag => tag.Name, tag => tag.Id, StringComparer.OrdinalIgnoreCase);
        var table = await rates.LoadAsync(cancellationToken);
        var now = clock.UtcNow;
        var operations = new List<Operation>(items.Count);
        foreach (var (index, item) in items.Index())
        {
            var posted = Build(item, accounts, known, now);
            if (posted.IsSuccess && item.Tags.Count > 0)
            {
                var tagged = posted.Value.SetTags(item.Tags.Where(tagIds.ContainsKey).Select(name => tagIds[name]).Distinct().ToList());
                posted = tagged.IsFailure ? tagged.Error : posted;
            }

            if (posted.IsFailure)
            {
                return new LedgerImportOutcome([], index, posted.Error);
            }

            foreach (var entry in posted.Value.Entries)
            {
                entry.ValueWith(entry.Currency == table.BaseCurrency ? null : table.At(entry.Currency, entry.OccurredOn));
            }

            operations.Add(posted.Value);
        }

        db.Operations.AddRange(operations);
        LedgerMetrics.OperationsPosted.Add(operations.Count, new KeyValuePair<string, object?>("kind", "Import"));
        if (operations.Count > 0)
        {
            // One line for the whole file: the movements are history, and each can still be edited or voided with its own trail.
            audit.Record(new AuditEntry("ledger", "ledger.import.posted", "operation", operations[0].Id)
            {
                OwnerUserId = userId,
                After = new { operations.Count, From = operations.Min(operation => operation.OccurredOn), To = operations.Max(operation => operation.OccurredOn) }
            });
        }

        return new LedgerImportOutcome(operations.Select(operation => operation.Id).ToList(), null, null);
    }

    private static Result<Operation> Build(
        LedgerImportItem item, Dictionary<Guid, Account> accounts, IReadOnlyDictionary<Guid, CategoryInfo> categories, DateTimeOffset now)
    {
        if (!accounts.TryGetValue(item.AccountId, out var account))
        {
            return LedgerErrors.AccountNotFound;
        }

        switch (item.Kind)
        {
            case LedgerImportKind.OpeningBalance:
                return Operation.OpenBalance(account, item.Amount, item.OccurredOn, now);

            case LedgerImportKind.Transfer:
                return item.CounterAccountId is { } counterId && accounts.TryGetValue(counterId, out var counter)
                    ? Operation.PostTransfer(
                        account, counter, item.Amount, account.Currency == counter.Currency ? null : item.CounterAmount, item.OccurredOn, item.Description, item.Note, now)
                    : LedgerErrors.AccountNotFound;

            default:
                var isIncome = item.Kind == LedgerImportKind.Income;
                if (item.CategoryId is not { } categoryId || !categories.TryGetValue(categoryId, out var category))
                {
                    return LedgerErrors.CategoryNotFound;
                }

                if ((category.Use == CategoryUse.Income) != isIncome)
                {
                    return Error.BusinessRule("ledger.category_kind_mismatch", "An income needs an income category and an expense an expense category.");
                }

                return Operation.PostTransaction(
                    isIncome ? OperationKind.Income : OperationKind.Expense, account, item.Amount, categoryId, item.OccurredOn, item.Description, null, item.Note,
                    false, now);
        }
    }
}
