using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using PersonalFinance.Api.Http;
using PersonalFinance.BuildingBlocks.Application.Messaging;
using PersonalFinance.Modules.Ledger.Application.Accounts;
using PersonalFinance.Modules.Ledger.Domain;

namespace PersonalFinance.Api.Controllers.Ledger;

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

    /// <summary>What the account holds today. Leave it out to open it empty.</summary>
    public decimal OpeningBalance { get; init; }
}

public sealed class UpdateAccountRequest
{
    [Required, StringLength(Account.MaxNameLength, MinimumLength = 1)]
    public string Name { get; init; } = string.Empty;

    [StringLength(Account.MaxInstitutionLength)]
    public string? Institution { get; init; }
}

public sealed class AdjustAccountBalanceRequest
{
    /// <summary>What the account really holds. The ledger records the difference.</summary>
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

        var command = new OpenAccountCommand(request.Name, request.Type!.Value, request.Currency, request.Institution, request.OpeningBalance, key);
        return ToActionResult(await handler.HandleAsync(command, cancellationToken), account => Created($"/api/v1/workspaces/{workspaceId}/accounts/{account.Id}", account));
    }

    /// <summary>Renames the account. Its type and its currency never change.</summary>
    [HttpPatch("{accountId:guid}", Name = "UpdateAccount")]
    [ProducesResponseType<AccountResponse>(StatusCodes.Status200OK)]
    [Problem(StatusCodes.Status400BadRequest)]
    [Problem(StatusCodes.Status409Conflict)]
    [Problem(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Update(
        Guid workspaceId, Guid accountId, UpdateAccountRequest request, [FromServices] ICommandHandler<UpdateAccountCommand, AccountResponse> handler,
        CancellationToken cancellationToken) =>
        ToActionResult(await handler.HandleAsync(new UpdateAccountCommand(accountId, request.Name, request.Institution), cancellationToken), Ok);

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
}
