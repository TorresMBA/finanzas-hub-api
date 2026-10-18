using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using PersonalFinance.Api.Http;
using PersonalFinance.BuildingBlocks.Application.Messaging;
using PersonalFinance.Modules.Ledger.Application.Accounts;
using PersonalFinance.Modules.Ledger.Application.Transactions;
using PersonalFinance.Modules.Ledger.Application.Transfers;
using PersonalFinance.Modules.Ledger.Domain;

namespace PersonalFinance.Api.Controllers.Ledger;

/// <summary>What makes an account a credit card.</summary>
public sealed class CreditCardRequest
{
    /// <summary>The credit line, in the currency of the account.</summary>
    [Required]
    public decimal? CreditLimit { get; init; }

    /// <summary>Day of the month the payment is due.</summary>
    [Required, Range(1, 31)]
    public int? PaymentDay { get; init; }

    /// <summary>Day of the month the statement closes, when known.</summary>
    [Range(1, 31)]
    public int? ClosingDay { get; init; }

    internal CreditCardTerms ToTerms() => new(CreditLimit!.Value, PaymentDay!.Value, ClosingDay);
}

public sealed class OpenAccountRequest
{
    [Required, StringLength(Account.MaxNameLength, MinimumLength = 1)]
    public string Name { get; init; } = string.Empty;

    [Required]
    public AccountType? Type { get; init; }

    [Required, StringLength(3, MinimumLength = 3)]
    public string Currency { get; init; } = string.Empty;

    [StringLength(Account.MaxInstitutionLength)]
    public string? Institution { get; init; }

    /// <summary>What the account holds today. Leave it out to open it empty. Negative for money owed, as the debt a credit card already carries.</summary>
    public decimal OpeningBalance { get; init; }

    /// <summary>Whether the account counts in the net worth. It does unless this says otherwise.</summary>
    public bool IncludeInNetWorth { get; init; } = true;

    /// <summary>Required for a credit card, and only allowed for one.</summary>
    public CreditCardRequest? CreditCard { get; init; }
}

public sealed class UpdateAccountRequest
{
    [Required, StringLength(Account.MaxNameLength, MinimumLength = 1)]
    public string Name { get; init; } = string.Empty;

    [StringLength(Account.MaxInstitutionLength)]
    public string? Institution { get; init; }

    /// <summary>Left out, the account keeps what it has.</summary>
    public bool? IncludeInNetWorth { get; init; }

    /// <summary>The line and the days of a credit card. Left out, the card keeps the ones it has.</summary>
    public CreditCardRequest? CreditCard { get; init; }
}

public sealed class SetAccountFavoriteRequest
{
    public bool IsFavorite { get; init; }
}

public sealed class PayCreditCardRequest
{
    /// <summary>The account the money comes from. Not another credit card.</summary>
    [Required]
    public Guid? FromAccountId { get; init; }

    /// <summary>What the card is paid, in the currency of the card.</summary>
    [Required]
    public decimal? Amount { get; init; }

    /// <summary>What leaves the paying account, in its currency. Required when that currency is not the one of the card.</summary>
    public decimal? SourceAmount { get; init; }

    [Required]
    public DateOnly? OccurredOn { get; init; }

    [StringLength(Operation.MaxNoteLength)]
    public string? Note { get; init; }
}

public sealed class AdjustAccountBalanceRequest
{
    /// <summary>What the account really holds; negative for money owed. The ledger records the difference.</summary>
    [Required]
    public decimal? ActualBalance { get; init; }

    /// <summary>The day the balance was checked. Today when left out.</summary>
    public DateOnly? OccurredOn { get; init; }
}

/// <summary>The accounts of the signed-in person in this workspace. Balances are derived from the ledger on every answer.</summary>
[Tags("Ledger")]
[Route("api/v1/workspaces/{workspaceId:guid}/accounts")]
[Problem(StatusCodes.Status401Unauthorized)]
[Problem(StatusCodes.Status404NotFound)]
public sealed class AccountsController : ApiControllerBase
{
    [HttpGet(Name = "ListAccounts")]
    [ProducesResponseType<IReadOnlyList<AccountResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        Guid workspaceId, [FromQuery] bool includeArchived, [FromServices] IQueryHandler<ListAccountsQuery, IReadOnlyList<AccountResponse>> handler,
        CancellationToken cancellationToken) =>
        ToActionResult(await handler.HandleAsync(new ListAccountsQuery(includeArchived), cancellationToken), Ok);

    /// <summary>Net worth: what the accounts that count hold and owe, in the base currency and currency by currency.</summary>
    [HttpGet("summary", Name = "GetAccountSummary")]
    [ProducesResponseType<AccountSummaryResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Summary(
        Guid workspaceId, [FromServices] IQueryHandler<GetAccountSummaryQuery, AccountSummaryResponse> handler, CancellationToken cancellationToken) =>
        ToActionResult(await handler.HandleAsync(new GetAccountSummaryQuery(), cancellationToken), Ok);

    [HttpGet("{accountId:guid}", Name = "GetAccount")]
    [ProducesResponseType<AccountResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(
        Guid workspaceId, Guid accountId, [FromServices] IQueryHandler<GetAccountQuery, AccountResponse> handler, CancellationToken cancellationToken) =>
        ToActionResult(await handler.HandleAsync(new GetAccountQuery(accountId), cancellationToken), Ok);

    /// <summary>Opens an account. An opening balance is registered as an entry, so the balance is always a sum.</summary>
    [HttpPost(Name = "OpenAccount")]
    [ProducesResponseType<AccountResponse>(StatusCodes.Status201Created)]
    [Problem(StatusCodes.Status400BadRequest)]
    [Problem(StatusCodes.Status409Conflict)]
    [Problem(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Open(
        Guid workspaceId, OpenAccountRequest request, [FromHeader(Name = IdempotencyKeyHeader)] string? idempotencyKey,
        [FromServices] ICommandHandler<OpenAccountCommand, AccountResponse> handler, CancellationToken cancellationToken)
    {
        if (!TryReadIdempotencyKey(idempotencyKey, out var key, out var problem))
        {
            return problem;
        }

        var command = new OpenAccountCommand(
            request.Name, request.Type!.Value, request.Currency, request.Institution, request.OpeningBalance, request.IncludeInNetWorth,
            request.CreditCard?.ToTerms(), key);
        return ToActionResult(await handler.HandleAsync(command, cancellationToken), account => Created($"/api/v1/workspaces/{workspaceId}/accounts/{account.Id}", account));
    }

    /// <summary>Changes the name, whether it counts in the net worth and, for a card, its line and days. Type and currency never change.</summary>
    [HttpPatch("{accountId:guid}", Name = "UpdateAccount")]
    [ProducesResponseType<AccountResponse>(StatusCodes.Status200OK)]
    [Problem(StatusCodes.Status400BadRequest)]
    [Problem(StatusCodes.Status409Conflict)]
    [Problem(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Update(
        Guid workspaceId, Guid accountId, UpdateAccountRequest request, [FromServices] ICommandHandler<UpdateAccountCommand, AccountResponse> handler,
        CancellationToken cancellationToken) =>
        ToActionResult(
            await handler.HandleAsync(
                new UpdateAccountCommand(accountId, request.Name, request.Institution, request.IncludeInNetWorth, request.CreditCard?.ToTerms()), cancellationToken),
            Ok);

    /// <summary>Marks the account as one of the main ones of the home screen, or unmarks it.</summary>
    [HttpPut("{accountId:guid}/favorite", Name = "SetAccountFavorite")]
    [ProducesResponseType<AccountResponse>(StatusCodes.Status200OK)]
    [Problem(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> SetFavorite(
        Guid workspaceId, Guid accountId, SetAccountFavoriteRequest request, [FromServices] ICommandHandler<SetAccountFavoriteCommand, AccountResponse> handler,
        CancellationToken cancellationToken) =>
        ToActionResult(await handler.HandleAsync(new SetAccountFavoriteCommand(accountId, request.IsFavorite), cancellationToken), Ok);

    /// <summary>The account stops being offered. Its movements and its balance stay.</summary>
    [HttpPost("{accountId:guid}/archive", Name = "ArchiveAccount")]
    [ProducesResponseType<AccountResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Archive(
        Guid workspaceId, Guid accountId, [FromServices] ICommandHandler<ArchiveAccountCommand, AccountResponse> handler, CancellationToken cancellationToken) =>
        ToActionResult(await handler.HandleAsync(new ArchiveAccountCommand(accountId), cancellationToken), Ok);

    [HttpPost("{accountId:guid}/restore", Name = "RestoreAccount")]
    [ProducesResponseType<AccountResponse>(StatusCodes.Status200OK)]
    [Problem(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Restore(
        Guid workspaceId, Guid accountId, [FromServices] ICommandHandler<RestoreAccountCommand, AccountResponse> handler, CancellationToken cancellationToken) =>
        ToActionResult(await handler.HandleAsync(new RestoreAccountCommand(accountId), cancellationToken), Ok);

    /// <summary>States the real balance of the account. The ledger registers an adjustment for the difference.</summary>
    [HttpPost("{accountId:guid}/adjust-balance", Name = "AdjustAccountBalance")]
    [ProducesResponseType<AccountResponse>(StatusCodes.Status200OK)]
    [Problem(StatusCodes.Status400BadRequest)]
    [Problem(StatusCodes.Status409Conflict)]
    [Problem(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> AdjustBalance(
        Guid workspaceId, Guid accountId, AdjustAccountBalanceRequest request, [FromHeader(Name = IdempotencyKeyHeader)] string? idempotencyKey,
        [FromServices] ICommandHandler<AdjustAccountBalanceCommand, AccountResponse> handler, CancellationToken cancellationToken)
    {
        if (!TryReadIdempotencyKey(idempotencyKey, out var key, out var problem))
        {
            return problem;
        }

        var command = new AdjustAccountBalanceCommand(accountId, request.ActualBalance!.Value, request.OccurredOn, key);
        return ToActionResult(await handler.HandleAsync(command, cancellationToken), Ok);
    }

    /// <summary>Pays a credit card from another account. It is a transfer: neither an income nor an expense.</summary>
    [HttpPost("{accountId:guid}/payments", Name = "PayCreditCard")]
    [ProducesResponseType<TransactionResponse>(StatusCodes.Status201Created)]
    [Problem(StatusCodes.Status400BadRequest)]
    [Problem(StatusCodes.Status409Conflict)]
    [Problem(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Pay(
        Guid workspaceId, Guid accountId, PayCreditCardRequest request, [FromHeader(Name = IdempotencyKeyHeader)] string? idempotencyKey,
        [FromServices] ICommandHandler<PayCreditCardCommand, TransactionResponse> handler, CancellationToken cancellationToken)
    {
        if (!TryReadIdempotencyKey(idempotencyKey, out var key, out var problem))
        {
            return problem;
        }

        var command = new PayCreditCardCommand(
            accountId, request.FromAccountId!.Value, request.Amount!.Value, request.SourceAmount, request.OccurredOn!.Value, request.Note, key);
        return ToActionResult(
            await handler.HandleAsync(command, cancellationToken),
            payment => Created($"/api/v1/workspaces/{workspaceId}/transactions/{payment.Id}", payment));
    }
}
