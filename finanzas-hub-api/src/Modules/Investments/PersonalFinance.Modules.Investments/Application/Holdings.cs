using System.Diagnostics.Metrics;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PersonalFinance.BuildingBlocks.Application.Abstractions;
using PersonalFinance.BuildingBlocks.Application.Messaging;
using PersonalFinance.BuildingBlocks.Application.Security;
using PersonalFinance.BuildingBlocks.Domain;
using PersonalFinance.Modules.Categories.Contracts;
using PersonalFinance.Modules.Investments.Application.Abstractions;
using PersonalFinance.Modules.Investments.Contracts;
using PersonalFinance.Modules.Investments.Domain;
using PersonalFinance.Modules.Ledger.Contracts;
using PersonalFinance.Modules.Notifications.Contracts;
using PersonalFinance.Modules.Scheduling.Contracts;

namespace PersonalFinance.Modules.Investments.Application;

/// <param name="Share">Of the value of the portfolio, from 0 to 100.</param>
public sealed record InvestmentTypeShareResponse(InvestmentType Type, decimal Value, decimal Share);

/// <summary>One investment as the portfolio shows it. Amounts in its own currency.</summary>
/// <param name="Invested">The capital still in it.</param>
/// <param name="GainPercent">Of the capital; null without capital.</param>
/// <param name="Weight">Its part of the portfolio, from 0 to 100, in the base currency.</param>
/// <param name="ProjectedValue">What a term deposit will be worth at maturity.</param>
/// <param name="Trend">What it was worth at the end of each of the last twelve months, the oldest first; the last one is today.</param>
/// <param name="Realized">The gain (or loss, negative) its withdrawals realized so far.</param>
public sealed record HoldingSummaryResponse(
    Guid Id, InvestmentType Type, string Name, string? Ticker, string? Institution, string Currency, decimal Invested, decimal Value, decimal Gain,
    decimal? GainPercent, decimal Weight, decimal? Quantity, DateOnly OpenedOn, DateOnly? ClosedOn, DateOnly? MaturesOn, decimal? AnnualRate, int? TermDays,
    decimal? ProjectedValue, DateOnly? LastValuedOn, IReadOnlyList<decimal> Trend, decimal Realized);

/// <summary>The portfolio of the current person: what it is worth in the base currency, by type and investment by investment.</summary>
/// <param name="BaseCurrency">Null while the workspace has not chosen one.</param>
/// <param name="Gain">Not realized yet: what it is worth minus the capital in it.</param>
/// <param name="Holdings">The open investments, the one worth most first.</param>
/// <param name="Closed">The investments withdrawn in full, the last one closed first. They count in no total.</param>
/// <param name="MissingRates">Currencies without an exchange rate, left out of the totals.</param>
public sealed record InvestmentSummaryResponse(
    string? BaseCurrency, decimal Value, decimal Invested, decimal Gain, decimal? GainPercent, IReadOnlyList<InvestmentTypeShareResponse> Distribution,
    IReadOnlyList<HoldingSummaryResponse> Holdings, IReadOnlyList<string> MissingRates, IReadOnlyList<HoldingSummaryResponse> Closed);

/// <param name="Realized">The gain (or loss, negative) a withdrawal realized.</param>
public sealed record HoldingMovementResponse(Guid Id, HoldingMovementKind Kind, DateOnly OccurredOn, decimal Amount, decimal Capital, decimal Realized, decimal? Quantity, string? AccountName);

public sealed record HoldingValuationResponse(Guid Id, DateOnly OnDate, decimal Value, decimal? UnitPrice);

/// <summary>One investment with everything that happened to it, newest first.</summary>
/// <param name="AccountId">The account of the ledger that holds its capital.</param>
public sealed record HoldingDetailResponse(HoldingSummaryResponse Holding, Guid AccountId, IReadOnlyList<HoldingMovementResponse> Movements, IReadOnlyList<HoldingValuationResponse> Valuations);

internal static class InvestmentErrors
{
    public static readonly Error Unauthenticated = Error.Unauthorized("auth.unauthenticated", "Authentication is required.");

    public static readonly Error NotFound = Error.NotFound("investments.not_found", "The investment was not found.");

    public static readonly Error AccountNotFound = Error.NotFound("investments.account_not_found", "The account was not found.");

    public static readonly Error CurrencyMismatch =
        Error.BusinessRule("investments.currency_mismatch", "The account must have the same currency as the investment.");
}

internal static class InvestmentMetrics
{
    // Same meter name as the platform's, which is the one the hosts export.
    public static readonly Counter<long> Operations =
        new Meter("PersonalFinance").CreateCounter<long>("investments.operations", description: "Investments opened, contributions, redemptions and valuations.");

    public static void Count(string kind) => Operations.Add(1, new KeyValuePair<string, object?>("kind", kind));
}

/// <summary>Reads the portfolio of the current person and turns it into responses.</summary>
internal sealed class PortfolioReader(IInvestmentsDb db, ILedgerQueries ledger, ICurrentUser currentUser, IPersonalSpace workspace, IClock clock)
{
    public const int TrendMonths = 12;

    public (Guid WorkspaceId, Guid UserId)? Owner =>
        currentUser.UserId is { } userId && workspace.WorkspaceId is { } workspaceId ? (workspaceId, userId) : null;

    public string? BaseCurrency => workspace.BaseCurrency;

    /// <summary>Today in the workspace's time zone.</summary>
    public DateOnly Today()
    {
        var zone = workspace.TimeZone is { } id && TimeZoneInfo.TryFindSystemTimeZoneById(id, out var found) ? found : TimeZoneInfo.Utc;
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.UtcNow, zone).DateTime);
    }

    public Task<Holding?> FindAsync(Guid holdingId, CancellationToken cancellationToken) =>
        db.Holdings.SingleOrDefaultAsync(holding => holding.Id == holdingId, cancellationToken);

    public async Task<IReadOnlyList<Holding>> OpenAsync(CancellationToken cancellationToken) =>
        await db.Holdings.AsNoTracking().Where(holding => holding.ClosedOn == null).ToListAsync(cancellationToken);

    /// <summary>The accounts money can come from or go to: in use, holding money, with their names.</summary>
    public async Task<IReadOnlyList<LedgerAccountBalance>> AccountsAsync(CancellationToken cancellationToken) =>
        await ledger.GetAccountBalancesAsync(cancellationToken);

    public async Task<InvestmentSummaryResponse> SummaryAsync(InvestmentType? type, CancellationToken cancellationToken)
    {
        var today = Today();
        var holdings = (await OpenAsync(cancellationToken)).Where(holding => type is null || holding.Type == type).ToList();
        var missing = new SortedSet<string>(StringComparer.Ordinal);
        var valued = new List<(Holding Holding, decimal Value, decimal Capital, decimal? BaseValue)>();
        foreach (var holding in holdings)
        {
            var value = holding.ValueOn(today);
            var baseValue = await ledger.ToBaseAsync(holding.Currency, value, cancellationToken);
            if (baseValue is null)
            {
                missing.Add(holding.Currency);
            }

            valued.Add((holding, value, holding.CapitalOn(today), baseValue));
        }

        var total = valued.Sum(item => item.BaseValue ?? 0);
        var invested = 0m;
        foreach (var item in valued.Where(item => item.BaseValue is not null))
        {
            invested += await ledger.ToBaseAsync(item.Holding.Currency, item.Capital, cancellationToken) ?? 0;
        }

        var rows = valued
            .OrderByDescending(item => item.BaseValue ?? 0)
            .Select(item => Describe(item.Holding, today, total > 0 && item.BaseValue is { } share ? decimal.Round(share / total * 100, 1) : 0))
            .ToList();
        var distribution = valued.Where(item => item.BaseValue is not null)
            .GroupBy(item => item.Holding.Type)
            .Select(group => new InvestmentTypeShareResponse(
                group.Key, group.Sum(item => item.BaseValue!.Value), total > 0 ? decimal.Round(group.Sum(item => item.BaseValue!.Value) / total * 100, 1) : 0))
            .OrderByDescending(share => share.Value)
            .ToList();
        return new InvestmentSummaryResponse(
            BaseCurrency, total, invested, total - invested, invested > 0 ? decimal.Round((total - invested) / invested * 100, 1) : null, distribution, rows, [.. missing],
            (await db.Holdings.AsNoTracking().Where(holding => holding.ClosedOn != null && (type == null || holding.Type == type)).ToListAsync(cancellationToken))
                .OrderByDescending(holding => holding.ClosedOn)
                .Select(holding => Describe(holding, today, null))
                .ToList());
    }

    public async Task<HoldingDetailResponse> DetailAsync(Holding holding, CancellationToken cancellationToken)
    {
        var names = (await AccountsAsync(cancellationToken)).ToDictionary(account => account.Id, account => account.Name);
        return new HoldingDetailResponse(
            Describe(holding, Today(), null),
            holding.AccountId,
            holding.Movements.OrderByDescending(movement => movement.OccurredOn).ThenByDescending(movement => movement.CreatedAt)
                .Select(movement => new HoldingMovementResponse(
                    movement.Id, movement.Kind, movement.OccurredOn, movement.Amount, movement.Capital, movement.Realized, movement.Quantity,
                    names.GetValueOrDefault(movement.CashAccountId)))
                .ToList(),
            holding.Valuations.OrderByDescending(valuation => valuation.OnDate).ThenByDescending(valuation => valuation.CreatedAt)
                .Select(valuation => new HoldingValuationResponse(valuation.Id, valuation.OnDate, valuation.Value, valuation.UnitPrice))
                .ToList());
    }

    private static HoldingSummaryResponse Describe(Holding holding, DateOnly today, decimal? weight)
    {
        var on = holding.ClosedOn is { } closed ? closed.AddDays(-1) : today;
        var value = holding.ValueOn(on);
        var capital = holding.CapitalOn(on);
        var month = new DateOnly(today.Year, today.Month, 1);
        var trend = Enumerable.Range(0, TrendMonths)
            .Select(back => back == 0 ? today : month.AddMonths(1 - back).AddDays(-1))
            .Reverse()
            .Select(holding.ValueOn)
            .ToList();
        return new HoldingSummaryResponse(
            holding.Id, holding.Type, holding.Name, holding.Ticker, holding.Institution, holding.Currency, capital, value, value - capital,
            capital > 0 ? decimal.Round((value - capital) / capital * 100, 1) : null, weight ?? 0, holding.Quantity, holding.OpenedOn, holding.ClosedOn,
            holding.TermDeposit?.MaturesOn, holding.TermDeposit?.AnnualRate, holding.TermDeposit?.Days, holding.ProjectedValue,
            holding.Valuations.Count == 0 ? null : holding.Valuations.Max(valuation => valuation.OnDate), trend, holding.Movements.Sum(movement => movement.Realized));
    }
}

public sealed record GetInvestmentSummaryQuery(InvestmentType? Type) : IQuery<InvestmentSummaryResponse>;

internal sealed class GetInvestmentSummaryHandler(PortfolioReader reader) : IQueryHandler<GetInvestmentSummaryQuery, InvestmentSummaryResponse>
{
    public async Task<Result<InvestmentSummaryResponse>> HandleAsync(GetInvestmentSummaryQuery query, CancellationToken cancellationToken) =>
        reader.Owner is null ? InvestmentErrors.Unauthenticated : await reader.SummaryAsync(query.Type, cancellationToken);
}

public sealed record GetHoldingQuery(Guid HoldingId) : IQuery<HoldingDetailResponse>;

internal sealed class GetHoldingHandler(PortfolioReader reader) : IQueryHandler<GetHoldingQuery, HoldingDetailResponse>
{
    public async Task<Result<HoldingDetailResponse>> HandleAsync(GetHoldingQuery query, CancellationToken cancellationToken) =>
        await reader.FindAsync(query.HoldingId, cancellationToken) is { } holding ? await reader.DetailAsync(holding, cancellationToken) : InvestmentErrors.NotFound;
}

/// <param name="Amount">What goes in, from <paramref name="FromAccountId"/>, in its currency, which becomes the currency of the investment.</param>
/// <param name="Quantity">Units bought, for shares, ETFs and crypto.</param>
/// <param name="AnnualRate">The TEA of a term deposit, in percent.</param>
/// <param name="TermDays">The term of a term deposit.</param>
public sealed record OpenHoldingCommand(
    InvestmentType Type, string Name, string? Ticker, string? Institution, decimal Amount, decimal? Quantity, DateOnly OpenedOn, Guid FromAccountId,
    decimal? AnnualRate, int? TermDays, string IdempotencyKey) : ICommand<HoldingDetailResponse>, IIdempotentCommand;

/// <summary>
/// Opens an investment with its first money: the ledger moves it from the person's account to the account of the
/// investment as a transfer, so their balance goes down and their net worth and their expenses do not move.
/// </summary>
internal sealed class OpenHoldingHandler(IInvestmentsDb db, PortfolioReader reader, ILedgerPosting ledger, IAuditLog audit, IClock clock)
    : ICommandHandler<OpenHoldingCommand, HoldingDetailResponse>
{
    public async Task<Result<HoldingDetailResponse>> HandleAsync(OpenHoldingCommand command, CancellationToken cancellationToken)
    {
        if (reader.Owner is not (var workspaceId, var userId))
        {
            return InvestmentErrors.Unauthenticated;
        }

        if ((await reader.AccountsAsync(cancellationToken)).FirstOrDefault(account => account.Id == command.FromAccountId) is not { } source)
        {
            return InvestmentErrors.AccountNotFound;
        }

        var opened = Holding.Open(
            workspaceId, userId, command.Type, command.Name, command.Ticker, command.Institution, source.Currency, command.OpenedOn, command.AnnualRate,
            command.TermDays, clock.UtcNow);
        if (opened.IsFailure)
        {
            return opened.Error;
        }

        var holding = opened.Value;
        var movementId = Guid.CreateVersion7();
        var contributed = holding.Contribute(movementId, command.Amount, command.Quantity, command.OpenedOn, source.Id, clock.UtcNow);
        if (contributed.IsFailure)
        {
            return contributed.Error;
        }

        var account = await ledger.OpenInvestmentAccountAsync(holding.Name, holding.Currency, cancellationToken);
        if (account.IsFailure)
        {
            return account.Error;
        }

        holding.HoldIn(account.Value);
        var moved = await ledger.MoveInvestmentMoneyAsync(source.Id, account.Value, command.Amount, command.OpenedOn, $"Inversión en {holding.Name}", movementId, cancellationToken);
        if (moved.IsFailure)
        {
            return moved.Error;
        }

        db.Holdings.Add(holding);
        InvestmentMetrics.Count("open");
        audit.Record(new AuditEntry("investments", "investments.holding.opened", "holding", holding.Id)
        {
            OwnerUserId = userId,
            After = new { holding.Type, holding.Name, holding.Currency, command.Amount, command.OpenedOn }
        });
        return await reader.DetailAsync(holding, cancellationToken);
    }
}

public sealed record ContributeToHoldingCommand(Guid HoldingId, decimal Amount, decimal? Quantity, DateOnly OccurredOn, Guid FromAccountId, string IdempotencyKey)
    : ICommand<HoldingDetailResponse>, IIdempotentCommand;

/// <summary>Puts more money into an investment: a transfer from the person's account, never an expense.</summary>
internal sealed class ContributeToHoldingHandler(PortfolioReader reader, ILedgerPosting ledger, IAuditLog audit, IClock clock)
    : ICommandHandler<ContributeToHoldingCommand, HoldingDetailResponse>
{
    public async Task<Result<HoldingDetailResponse>> HandleAsync(ContributeToHoldingCommand command, CancellationToken cancellationToken)
    {
        if (await reader.FindAsync(command.HoldingId, cancellationToken) is not { } holding)
        {
            return InvestmentErrors.NotFound;
        }

        if ((await reader.AccountsAsync(cancellationToken)).FirstOrDefault(account => account.Id == command.FromAccountId) is not { } source)
        {
            return InvestmentErrors.AccountNotFound;
        }

        if (source.Currency != holding.Currency)
        {
            return InvestmentErrors.CurrencyMismatch;
        }

        var movementId = Guid.CreateVersion7();
        var contributed = holding.Contribute(movementId, command.Amount, command.Quantity, command.OccurredOn, source.Id, clock.UtcNow);
        if (contributed.IsFailure)
        {
            return contributed.Error;
        }

        var moved = await ledger.MoveInvestmentMoneyAsync(source.Id, holding.AccountId, command.Amount, command.OccurredOn, $"Aporte a {holding.Name}", movementId, cancellationToken);
        if (moved.IsFailure)
        {
            return moved.Error;
        }

        InvestmentMetrics.Count("contribution");
        audit.Record(new AuditEntry("investments", "investments.holding.contributed", "holding", holding.Id)
        {
            OwnerUserId = holding.OwnerUserId,
            After = new { command.Amount, command.Quantity, command.OccurredOn }
        });
        return await reader.DetailAsync(holding, cancellationToken);
    }
}

public sealed record WithdrawFromHoldingCommand(Guid HoldingId, decimal Amount, decimal? Quantity, DateOnly OccurredOn, Guid ToAccountId, string IdempotencyKey)
    : ICommand<HoldingDetailResponse>, IIdempotentCommand;

/// <summary>
/// Redeems money of an investment into one of the person's accounts. The capital comes back as a transfer; what it
/// earned on top enters as an income of "Inversiones", and what it lost leaves as an expense of it. That is the
/// only moment an investment touches income or expenses.
/// </summary>
internal sealed class WithdrawFromHoldingHandler(PortfolioReader reader, ILedgerPosting ledger, ICategoryDirectory categories, IAuditLog audit, IClock clock)
    : ICommandHandler<WithdrawFromHoldingCommand, HoldingDetailResponse>
{
    public const string Category = "Inversiones";

    public async Task<Result<HoldingDetailResponse>> HandleAsync(WithdrawFromHoldingCommand command, CancellationToken cancellationToken)
    {
        if (await reader.FindAsync(command.HoldingId, cancellationToken) is not { } holding)
        {
            return InvestmentErrors.NotFound;
        }

        if ((await reader.AccountsAsync(cancellationToken)).FirstOrDefault(account => account.Id == command.ToAccountId) is not { } target)
        {
            return InvestmentErrors.AccountNotFound;
        }

        if (target.Currency != holding.Currency)
        {
            return InvestmentErrors.CurrencyMismatch;
        }

        var withdrawn = holding.Withdraw(Guid.CreateVersion7(), command.Amount, command.Quantity, command.OccurredOn, target.Id, clock.UtcNow);
        if (withdrawn.IsFailure)
        {
            return withdrawn.Error;
        }

        var movement = withdrawn.Value;
        if (movement.Capital > 0)
        {
            var moved = await ledger.MoveInvestmentMoneyAsync(holding.AccountId, target.Id, movement.Capital, command.OccurredOn, $"Rescate de {holding.Name}", movement.Id, cancellationToken);
            if (moved.IsFailure)
            {
                return moved.Error;
            }
        }

        var realized = await PostRealizedAsync(holding, movement, target.Id, command.OccurredOn, cancellationToken);
        if (realized.IsFailure)
        {
            return realized.Error;
        }

        InvestmentMetrics.Count(holding.IsClosed ? "redemption_full" : "redemption");
        audit.Record(new AuditEntry("investments", "investments.holding.withdrawn", "holding", holding.Id)
        {
            OwnerUserId = holding.OwnerUserId,
            After = new { movement.Amount, movement.Capital, movement.Realized, command.OccurredOn, holding.IsClosed }
        });
        return await reader.DetailAsync(holding, cancellationToken);
    }

    /// <summary>What the redemption earned or lost on top of the capital, as an income or an expense of "Inversiones".</summary>
    private async Task<Result> PostRealizedAsync(Holding holding, HoldingMovement movement, Guid accountId, DateOnly occurredOn, CancellationToken cancellationToken)
    {
        if (movement.Realized == 0)
        {
            return Result.Success();
        }

        var isLoss = movement.Realized < 0;
        var category = await categories.EnsureAsync(isLoss ? CategoryUse.Expense : CategoryUse.Income, Category, null, cancellationToken);
        if (category.IsFailure)
        {
            return category.Error;
        }

        var posted = await ledger.PostTransactionAsync(
            new LedgerTransactionRequest(
                isLoss, accountId, Math.Abs(movement.Realized), category.Value, occurredOn,
                $"{(isLoss ? "Pérdida" : "Ganancia")} de {holding.Name}", $"investments:realized:{movement.Id:N}"),
            cancellationToken);
        return posted.IsFailure ? posted.Error : Result.Success();
    }
}

/// <param name="Value">What it is worth. Leave it out and give <paramref name="UnitPrice"/> for shares, ETFs and crypto.</param>
public sealed record RecordValuationCommand(Guid HoldingId, DateOnly OnDate, decimal? Value, decimal? UnitPrice) : ICommand<HoldingDetailResponse>;

/// <summary>Writes down what an investment is worth, as read from the bank or broker. No money moves.</summary>
internal sealed class RecordValuationHandler(PortfolioReader reader, IAuditLog audit, IClock clock) : ICommandHandler<RecordValuationCommand, HoldingDetailResponse>
{
    public async Task<Result<HoldingDetailResponse>> HandleAsync(RecordValuationCommand command, CancellationToken cancellationToken)
    {
        if (await reader.FindAsync(command.HoldingId, cancellationToken) is not { } holding)
        {
            return InvestmentErrors.NotFound;
        }

        if (command.OnDate > reader.Today())
        {
            return Error.Validation("investments.date_in_future", "A value is recorded for today or a day before.");
        }

        var recorded = holding.RecordValuation(Guid.CreateVersion7(), command.OnDate, command.Value, command.UnitPrice, clock.UtcNow);
        if (recorded.IsFailure)
        {
            return recorded.Error;
        }

        InvestmentMetrics.Count("valuation");
        audit.Record(new AuditEntry("investments", "investments.holding.valued", "holding", holding.Id)
        {
            OwnerUserId = holding.OwnerUserId,
            After = new { command.OnDate, command.Value, command.UnitPrice }
        });
        return await reader.DetailAsync(holding, cancellationToken);
    }
}

/// <summary>
/// The market value for the net worth: the investments whose account counts in it, valued on the day asked, in the
/// base currency.
/// </summary>
internal sealed class PortfolioValuation(IInvestmentsDb db, ILedgerQueries ledger) : IPortfolioValuation
{
    public async Task<PortfolioValue> GetAsync(DateOnly asOf, CancellationToken cancellationToken)
    {
        var counted = (await ledger.GetAccountBalancesAsync(cancellationToken))
            .Where(account => account.Type == LedgerAccountType.Investment && account.IncludeInNetWorth)
            .Select(account => account.Id)
            .ToHashSet();
        var holdings = await db.Holdings.AsNoTracking().Where(holding => holding.OpenedOn <= asOf).ToListAsync(cancellationToken);
        var (value, invested) = (0m, 0m);
        var missing = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var holding in holdings.Where(holding => counted.Contains(holding.AccountId)))
        {
            var worth = await ledger.ToBaseAsync(holding.Currency, holding.ValueOn(asOf), cancellationToken);
            var capital = await ledger.ToBaseAsync(holding.Currency, holding.CapitalOn(asOf), cancellationToken);
            if (worth is null || capital is null)
            {
                missing.Add(holding.Currency);
                continue;
            }

            value += worth.Value;
            invested += capital.Value;
        }

        return new PortfolioValue(value, invested, [.. missing]);
    }
}

/// <summary>
/// Part of the daily cycle of each person: a term deposit that matures within a week is told once, so the person
/// can decide whether to renew it or take the money.
/// </summary>
internal sealed class DepositMaturityStep(PortfolioReader reader, INotificationPublisher notifications) : IScheduleCycleStep
{
    public const int NoticeDays = 7;

    public async Task<int> RunAsync(DateOnly today, CancellationToken cancellationToken)
    {
        var notices = 0;
        foreach (var holding in (await reader.OpenAsync(cancellationToken)).Where(holding =>
                     holding.TermDeposit is { } terms && terms.MaturesOn >= today && terms.MaturesOn <= today.AddDays(NoticeDays)))
        {
            var maturesOn = holding.TermDeposit!.MaturesOn;
            var when = DueWhen(maturesOn, today);
            var value = holding.ProjectedValue ?? holding.Capital;
            if (await notifications.PublishAsync(
                    new NotificationRequest(
                        holding.OwnerUserId, holding.WorkspaceId, NotificationCategory.Bills, NotificationSeverity.Info, $"Tu depósito «{holding.Name}» vence {when}",
                        $"Al vencer valdrá {holding.Currency} {value.ToString("N2", CultureInfo.InvariantCulture)}. Decide si lo renuevas o rescatas el dinero.",
                        "/investments", $"deposit-maturing:{holding.Id}:{maturesOn:yyyy-MM-dd}"),
                    cancellationToken))
            {
                notices++;
            }
        }

        return notices;
    }

    private static string DueWhen(DateOnly maturesOn, DateOnly today)
    {
        if (maturesOn == today)
        {
            return "hoy";
        }

        return maturesOn == today.AddDays(1) ? "mañana" : $"el {maturesOn.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}";
    }
}
