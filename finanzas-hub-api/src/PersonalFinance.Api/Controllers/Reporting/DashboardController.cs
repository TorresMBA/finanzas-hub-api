using Microsoft.AspNetCore.Mvc;
using PersonalFinance.Api.Http;
using PersonalFinance.BuildingBlocks.Application.Messaging;
using PersonalFinance.Modules.Reporting.Application.Dashboard;

namespace PersonalFinance.Api.Controllers.Reporting;

/// <summary>The home page of the signed-in person in this workspace. It shows only that person's money.</summary>
[Tags("Reporting")]
[Route("api/v1/workspaces/{workspaceId:guid}/dashboard")]
[Problem(StatusCodes.Status401Unauthorized)]
[Problem(StatusCodes.Status404NotFound)]
public sealed class DashboardController : ApiControllerBase
{
    /// <summary>Net worth, income, expenses, cash flow of six months, top categories, latest movements and main accounts of a month.</summary>
    /// <param name="month">As 2026-10. The current month when left out.</param>
    [HttpGet(Name = "GetDashboard")]
    [ProducesResponseType<DashboardResponse>(StatusCodes.Status200OK)]
    [Problem(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Get(
        Guid workspaceId, [FromQuery] string? month, [FromServices] IQueryHandler<GetDashboardQuery, DashboardResponse> handler,
        CancellationToken cancellationToken) =>
        ToActionResult(await handler.HandleAsync(new GetDashboardQuery(month), cancellationToken), Ok);
}
