using Application.IServices.Licensing;
using Common.Licensing;
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
    private readonly ILicenseService _license;

    public DashboardController(IDashboardApiService dashboard, ILicenseService license)
    {
        _dashboard = dashboard;
        _license = license;
    }

    /// <summary>Everything on the dashboard except the chart. Figures are cached for 2 minutes, like the Razor page.</summary>
    [HttpGet]
    [RequirePermission(PermissionHelper.Permissions.Dashboard_View)]
    [ProducesResponseType<DashboardDto>(StatusCodes.Status200OK)]
    [SwaggerOperation(OperationId = "getDashboard")]
    public async Task<DashboardDto> Get() => await _dashboard.GetAsync();

    /// <summary>
    /// The sales chart. <c>period</c> is <c>week</c>, <c>month</c> (default), <c>quarter</c>, <c>year</c> or <c>all</c> (since the first sale); an unknown value means the month.
    /// The Basic plan has only <c>week</c>: any other period answers 403 <c>plan_upgrade_required</c> (feature <c>Reports</c>).
    /// </summary>
    [HttpGet("sales-trend")]
    [RequirePermission(PermissionHelper.Permissions.Dashboard_View)]
    [ProducesResponseType<SalesTrendDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status403Forbidden, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "getSalesTrend")]
    public async Task<ActionResult<SalesTrendDto>> SalesTrend([FromQuery] string? period)
    {
        if (!string.Equals(period, DashboardPeriod.Week, StringComparison.OrdinalIgnoreCase) && !_license.GetState().Includes(ProFeature.Reports))
            return PlanUpgradeRequired(ProFeature.Reports);
        return await _dashboard.TrendAsync(period);
    }
}
