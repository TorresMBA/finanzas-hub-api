using Microsoft.AspNetCore.Mvc;
using PersonalFinance.Api.Http;
using PersonalFinance.BuildingBlocks.Application.Messaging;
using PersonalFinance.Modules.Reporting.Application.NetWorth;

namespace PersonalFinance.Api.Controllers.Reporting;

/// <summary>The net worth of the signed-in person. It shows only that person's money.</summary>
[Tags("Reporting")]
[Route("api/v1/workspaces/{workspaceId:guid}/net-worth")]
[Problem(StatusCodes.Status401Unauthorized)]
[Problem(StatusCodes.Status404NotFound)]
public sealed class NetWorthController : ApiControllerBase
{
    /// <summary>Net worth today, with the investments at their market value: the same figure as the home page of the current month.</summary>
    [HttpGet(Name = "GetNetWorth")]
    [ProducesResponseType<NetWorthResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(
        Guid workspaceId, [FromServices] IQueryHandler<GetNetWorthQuery, NetWorthResponse> handler, CancellationToken cancellationToken) =>
        ToActionResult(await handler.HandleAsync(new GetNetWorthQuery(), cancellationToken), Ok);
}
