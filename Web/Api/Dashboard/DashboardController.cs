using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using Web.Api.Dashboard.Contracts;
using Web.Api.Infrastructure;
using Web.Attributes;
using Web.Helpers;

namespace Web.Api.Dashboard;

/// <summary>The home dashboard: month tiles, latest orders, sales by category and the sales chart.</summary>
/// <remarks>
/// Port of the old <c>DashboardController</c>, which needed <c>Dashboard.View</c> for every action. Its top-products, top-customers and
/// inventory-status endpoints are not here: no page ever displayed what they returned.
/// </remarks>
[Route("api/v1/dashboard")]
[SwaggerTag("Dashboard")]
public sealed class DashboardController : ApiControllerBase
{
    private readonly IDashboardApiService _dashboard;

    public DashboardController(IDashboardApiService dashboard) => _dashboard = dashboard;

    /// <summary>Everything on the dashboard except the chart. Figures are cached for 2 minutes, like the Razor page.</summary>
    [HttpGet]
    [RequirePermission(PermissionHelper.Permissions.Dashboard_View)]
    [ProducesResponseType<DashboardDto>(StatusCodes.Status200OK)]
    [SwaggerOperation(OperationId = "getDashboard")]
    public async Task<DashboardDto> Get() => await _dashboard.GetAsync();

    /// <summary>The sales chart. <c>period</c> is <c>week</c>, <c>month</c> (default), <c>quarter</c>, <c>year</c> or <c>all</c> (since the first sale); an unknown value means the month.</summary>
    [HttpGet("sales-trend")]
    [RequirePermission(PermissionHelper.Permissions.Dashboard_View)]
    [ProducesResponseType<SalesTrendDto>(StatusCodes.Status200OK)]
    [SwaggerOperation(OperationId = "getSalesTrend")]
    public async Task<SalesTrendDto> SalesTrend([FromQuery] string? period) => await _dashboard.TrendAsync(period);
}
