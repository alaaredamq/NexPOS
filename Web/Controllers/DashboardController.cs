using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Localization;
using Web.Attributes;
using Web.Helpers;
using Application.DTOs.Dashboard;
using Application.IServices.Dashboard;
using Core.ICache;

namespace Web.Controllers;

[Authorize]
[RequirePermission(PermissionHelper.Permissions.Dashboard_View)]
public class DashboardController : BaseController
{
    private readonly IViewLocalizer _localizer;
    private readonly IDashboardService _dashboardService;
    private readonly ICacheService _cache;
    private readonly ILogger<DashboardController> _logger;

    public DashboardController(IViewLocalizer localizer, IDashboardService dashboardService, ICacheService cache, ILogger<DashboardController> logger)
    {
        _localizer = localizer;
        _dashboardService = dashboardService;
        _cache = cache;
        _logger = logger;
    }

    public async Task<IActionResult> Index()
    {
        try
        {
            // Dashboard aggregates are expensive (many DB round trips) and only need to be
            // near-real-time. Cache each for a couple of minutes.
            int ttl = CacheKeys.DashboardTtlMinutes;
            ViewBag.Stats = await _cache.CreateOrGetAsync(CacheKeys.Dashboard("stats"), () => _dashboardService.GetDashboardStatsAsync(), ttl);
            ViewBag.RecentOrders = await _cache.CreateOrGetAsync(CacheKeys.Dashboard("recent-orders", "10"), () => _dashboardService.GetRecentOrdersAsync(10), ttl);
            ViewBag.TopProducts = await _cache.CreateOrGetAsync(CacheKeys.Dashboard("top-products", "5"), () => _dashboardService.GetTopProductsAsync(5), ttl);
            ViewBag.TopCustomers = await _cache.CreateOrGetAsync(CacheKeys.Dashboard("top-customers", "5"), () => _dashboardService.GetTopCustomersAsync(5), ttl);
            ViewBag.CategoryAnalytics = await _cache.CreateOrGetAsync(CacheKeys.Dashboard("category-analytics"), () => _dashboardService.GetCategoryAnalyticsAsync(), ttl);
            ViewBag.SalesAnalytics = await _cache.CreateOrGetAsync(CacheKeys.Dashboard("sales-analytics", "month"), () => _dashboardService.GetSalesAnalyticsAsync("month"), ttl);
            ViewBag.InventoryStatus = await _cache.CreateOrGetAsync(CacheKeys.Dashboard("inventory-status"), () => _dashboardService.GetInventoryStatusAsync(), ttl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading the dashboard");

            // Empty figures, never sample ones: zeros are obviously "no data", invented sales look real.
            ViewBag.Stats = new DashboardStatsDTO();
            ViewBag.RecentOrders = new List<RecentOrderDTO>();
            ViewBag.TopProducts = new List<TopProductDTO>();
            ViewBag.TopCustomers = new List<TopCustomerDTO>();
            ViewBag.CategoryAnalytics = new CategoryAnalyticsDTO();
            ViewBag.SalesAnalytics = new SalesAnalyticsDTO();
            ViewBag.InventoryStatus = new InventoryStatusDTO();
        }

        return View();
    }

    [HttpGet]
    public async Task<IActionResult> GetSalesAnalytics(string period = "month")
    {
        try
        {
            var analytics = await _cache.CreateOrGetAsync(
                CacheKeys.Dashboard("sales-analytics", period ?? "month"),
                () => _dashboardService.GetSalesAnalyticsAsync(period),
                CacheKeys.DashboardTtlMinutes);
            return Json(analytics);
        }
        catch (Exception ex)
        {
            return Json(new { error = "Failed to load analytics data" });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetCategoryAnalytics()
    {
        try
        {
            var analytics = await _cache.CreateOrGetAsync(
                CacheKeys.Dashboard("category-analytics"),
                () => _dashboardService.GetCategoryAnalyticsAsync(),
                CacheKeys.DashboardTtlMinutes);
            return Json(analytics);
        }
        catch (Exception ex)
        {
            return Json(new { error = "Failed to load category analytics" });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetTopProducts(int limit = 10)
    {
        try
        {
            var products = await _dashboardService.GetTopProductsAsync(limit);
            return Json(products);
        }
        catch (Exception ex)
        {
            return Json(new { error = "Failed to load top products" });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetTopCustomers(int limit = 10)
    {
        try
        {
            var customers = await _dashboardService.GetTopCustomersAsync(limit);
            return Json(customers);
        }
        catch (Exception ex)
        {
            return Json(new { error = "Failed to load top customers" });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetInventoryStatus()
    {
        try
        {
            var status = await _dashboardService.GetInventoryStatusAsync();
            return Json(status);
        }
        catch (Exception ex)
        {
            return Json(new { error = "Failed to load inventory status" });
        }
    }
}