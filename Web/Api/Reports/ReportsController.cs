using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Swashbuckle.AspNetCore.Annotations;
using Web.Api.Infrastructure;
using Web.Api.Reports.Contracts;
using Web.Attributes;
using Web.Helpers;

namespace Web.Api.Reports;

/// <summary>Sales reports: the period tiles and the sales report of a date range, with CSV and PDF export.</summary>
/// <remarks>
/// Port of the old <c>ReportsController</c>, which needed <c>Reports.View</c> for every action. Its daily / monthly / yearly reports and the
/// top-products / category / trend pop-ups are all this one report with a different range. The old "export" only answered "will be implemented".
/// </remarks>
[Route("api/v1/reports")]
[SwaggerTag("Reports")]
public sealed class ReportsController : ApiControllerBase
{
    /// <summary>Longest range a report is built for; the orders of the range are held in memory.</summary>
    public const int MaxRangeDays = 3660;

    private readonly IReportsApiService _reports;
    private readonly IStringLocalizer<SharedResource> _text;

    public ReportsController(IReportsApiService reports, IStringLocalizer<SharedResource> text)
    {
        _reports = reports;
        _text = text;
    }

    /// <summary>Sales and orders of today, this month and this year, and the month's average order.</summary>
    [HttpGet("summary")]
    [RequirePermission(PermissionHelper.Permissions.Reports_View)]
    [ProducesResponseType<ReportsSummaryDto>(StatusCodes.Status200OK)]
    [SwaggerOperation(OperationId = "getReportsSummary")]
    public async Task<ReportsSummaryDto> Summary() => await _reports.SummaryAsync();

    /// <summary>The sales report for the local days <c>from</c> to <c>to</c> (both inclusive, <c>yyyy-MM-dd</c>). 400 when <c>to</c> is before <c>from</c> or the range is over 3660 days.</summary>
    [HttpGet("sales")]
    [RequirePermission(PermissionHelper.Permissions.Reports_View)]
    [ProducesResponseType<SalesReportDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "getSalesReport")]
    public async Task<ActionResult<SalesReportDto>> Sales([FromQuery, Required] DateOnly from, [FromQuery, Required] DateOnly to)
        => Range(from, to) is { } bad ? bad : await _reports.SalesAsync(from, to);

    /// <summary>The same report as a CSV file.</summary>
    [HttpGet("sales.csv")]
    [RequirePermission(PermissionHelper.Permissions.Reports_View)]
    [Produces("text/csv", "application/json")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK, "text/csv")]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "downloadSalesReportCsv")]
    public async Task<IActionResult> SalesCsv([FromQuery, Required] DateOnly from, [FromQuery, Required] DateOnly to)
        => Range(from, to) is { } bad ? bad : File(await _reports.CsvAsync(from, to), "text/csv", $"sales-report_{from:yyyyMMdd}_{to:yyyyMMdd}.csv");

    /// <summary>The same report as an A4 PDF.</summary>
    [HttpGet("sales.pdf")]
    [RequirePermission(PermissionHelper.Permissions.Reports_View)]
    [Produces("application/pdf", "application/json")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK, "application/pdf")]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "downloadSalesReportPdf")]
    public async Task<IActionResult> SalesPdf([FromQuery, Required] DateOnly from, [FromQuery, Required] DateOnly to)
    {
        if (Range(from, to) is { } bad) return bad;
        var (pdf, name) = await _reports.PdfAsync(from, to);
        return File(pdf, "application/pdf", name);
    }

    private ObjectResult? Range(DateOnly from, DateOnly to)
    {
        if (to < from) return ValidationFailure("to", _text["Rep_RangeBackwards"].Value);
        if (to.DayNumber - from.DayNumber >= MaxRangeDays) return ValidationFailure("to", _text["Rep_RangeTooLong", MaxRangeDays].Value);
        return null;
    }
}
