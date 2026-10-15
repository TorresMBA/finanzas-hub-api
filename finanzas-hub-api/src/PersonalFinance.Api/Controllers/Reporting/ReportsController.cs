using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using PersonalFinance.Api.Http;
using PersonalFinance.BuildingBlocks.Application.Messaging;
using PersonalFinance.Modules.Reporting.Application.Reports;

namespace PersonalFinance.Api.Controllers.Reporting;

/// <summary>Reports of the signed-in person by month, quarter or year, and their download. Only that person's money.</summary>
[Tags("Reporting")]
[Route("api/v1/workspaces/{workspaceId:guid}")]
[Problem(StatusCodes.Status401Unauthorized)]
[Problem(StatusCodes.Status404NotFound)]
public sealed class ReportsController : ApiControllerBase
{
    /// <summary>Income, expenses, savings and daily expense against the period before; expenses by category, shops and the savings rate by month.</summary>
    /// <param name="period">Month, quarter or year.</param>
    /// <param name="anchor">A month inside the period, as 2026-10.</param>
    [HttpGet("reports/summary", Name = "GetReportSummary")]
    [ProducesResponseType<ReportSummaryResponse>(StatusCodes.Status200OK)]
    [Problem(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Summary(
        Guid workspaceId, [FromQuery, Required] ReportPeriodKind? period, [FromQuery, Required] string? anchor,
        [FromServices] IQueryHandler<GetReportSummaryQuery, ReportSummaryResponse> handler, CancellationToken cancellationToken) =>
        ToActionResult(await handler.HandleAsync(new GetReportSummaryQuery(period!.Value, anchor!), cancellationToken), Ok);

    /// <summary>The report as an Excel workbook (summary and movements) or the movements of the period as CSV.</summary>
    [HttpGet("reports/export", Name = "ExportReport")]
    [ProducesResponseType<FileContentResult>(StatusCodes.Status200OK, ReportFileResponse.XlsxType, ReportFileResponse.CsvType)]
    [Problem(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Export(
        Guid workspaceId, [FromQuery, Required] ReportPeriodKind? period, [FromQuery, Required] string? anchor, [FromQuery, Required] ReportFormat? format,
        [FromServices] IQueryHandler<ExportReportQuery, ReportFileResponse> handler, CancellationToken cancellationToken) =>
        ToActionResult(
            await handler.HandleAsync(new ExportReportQuery(period!.Value, anchor!, format!.Value), cancellationToken),
            file => File(file.Content, file.ContentType, file.FileName));

    /// <summary>The person's own movements, accounts and goals whose text contains <paramref name="q"/>.</summary>
    /// <param name="q">Between 2 and 80 characters.</param>
    [HttpGet("search", Name = "Search")]
    [ProducesResponseType<SearchResponse>(StatusCodes.Status200OK)]
    [Problem(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Search(
        Guid workspaceId, [FromQuery, Required] string? q, [FromServices] IQueryHandler<SearchQuery, SearchResponse> handler, CancellationToken cancellationToken) =>
        ToActionResult(await handler.HandleAsync(new SearchQuery(q!), cancellationToken), Ok);
}
