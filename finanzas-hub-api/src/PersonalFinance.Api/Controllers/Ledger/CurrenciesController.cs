using Microsoft.AspNetCore.Mvc;
using PersonalFinance.Api.Http;
using PersonalFinance.BuildingBlocks.Application.Messaging;
using PersonalFinance.Modules.Ledger.Application.ExchangeRates;

namespace PersonalFinance.Api.Controllers.Ledger;

/// <summary>The currencies the application works with. Nothing here belongs to a workspace.</summary>
[Tags("Ledger")]
[Route("api/v1/currencies")]
[Problem(StatusCodes.Status401Unauthorized)]
public sealed class CurrenciesController : ApiControllerBase
{
    /// <summary>Each currency with its symbol, its name and how many decimals an amount in it carries.</summary>
    [HttpGet(Name = "ListCurrencies")]
    [ProducesResponseType<IReadOnlyList<CurrencyResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromServices] IQueryHandler<ListCurrenciesQuery, IReadOnlyList<CurrencyResponse>> handler, CancellationToken cancellationToken) =>
        ToActionResult(await handler.HandleAsync(new ListCurrenciesQuery(), cancellationToken), Ok);
}
