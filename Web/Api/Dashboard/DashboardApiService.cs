using Application.DTOs.Dashboard;
using Application.IServices.Dashboard;
using Core.Enum;
using Core.ICache;
using Web.Api.Dashboard.Contracts;

namespace Web.Api.Dashboard;

public interface IDashboardApiService
{
    Task<DashboardDto> GetAsync();

    /// <summary>The sales chart for <paramref name="period"/> (one of <see cref="DashboardPeriod.All"/>; anything else is the month, as the Razor page did).</summary>
    Task<SalesTrendDto> TrendAsync(string? period);
}

/// <summary>
/// The figures of the old Dashboard page, read through the same <see cref="IDashboardService"/> and cached under the same keys and
/// 2-minute lifetime as the Razor controller (<see cref="CacheKeys.Dashboard"/>), so both screens agree and a figure costs one query set.
/// </summary>
public sealed class DashboardApiService : IDashboardApiService
{
    private readonly IDashboardService _dashboard;
    private readonly ICacheService _cache;

    public DashboardApiService(IDashboardService dashboard, ICacheService cache)
    {
        _dashboard = dashboard;
        _cache = cache;
    }

    public async Task<DashboardDto> GetAsync()
    {
        const int ttl = CacheKeys.DashboardTtlMinutes;
        var stats = await _cache.CreateOrGetAsync(CacheKeys.Dashboard("stats"), () => _dashboard.GetDashboardStatsAsync(), ttl) ?? new DashboardStatsDTO();
        var recent = await _cache.CreateOrGetAsync(CacheKeys.Dashboard("recent-orders", "10"), () => _dashboard.GetRecentOrdersAsync(10), ttl) ?? new List<RecentOrderDTO>();
        var categories = await _cache.CreateOrGetAsync(CacheKeys.Dashboard("category-analytics"), () => _dashboard.GetCategoryAnalyticsAsync(), ttl) ?? new CategoryAnalyticsDTO();

        // With no sales the old service lists every category at 0% (by product count). A donut of zeros says nothing; send none.
        var slices = categories.Categories.Where(c => c.Sales > 0).ToList();

        return new DashboardDto
        {
            CurrencyNotation = stats.CurrencyNotation,
            MonthSales = stats.TotalSales,
            MonthSalesChangePercent = stats.TotalSalesChange,
            MonthOrders = stats.TotalOrders,
            MonthOrdersChange = stats.TotalOrdersChange,
            TodaySales = stats.TodaySales,
            TodayOrders = stats.TodayOrders,
            MonthProfit = stats.Profit,
            MonthProfitChangePercent = stats.ProfitChange,
            CustomerCount = stats.TotalCustomers,
            ProductCount = stats.TotalProducts,
            RecentOrders = recent.Select(o => new DashboardOrderDto
            {
                Id = o.Id,
                CustomerName = string.IsNullOrWhiteSpace(o.CustomerName) || o.CustomerName == "Walk-in" ? null : o.CustomerName,
                Amount = o.Amount,
                CurrencyNotation = o.CurrencyNotation,
                OrderDate = DateTime.SpecifyKind(o.OrderDate, DateTimeKind.Utc),
                Status = Enum.TryParse<OrderStatus>(o.Status, out var status) ? (int)status : (int)OrderStatus.Pending,
                PaymentMethod = string.IsNullOrWhiteSpace(o.PaymentMethod) || o.PaymentMethod == "—" ? null : o.PaymentMethod,
            }).ToList(),
            Categories = slices.Select(c => new DashboardCategoryDto { Name = c.Name, Sales = c.Sales, Percentage = c.Percentage }).ToList(),
        };
    }

    public async Task<SalesTrendDto> TrendAsync(string? period)
    {
        var key = DashboardPeriod.All.FirstOrDefault(p => string.Equals(p, period, StringComparison.OrdinalIgnoreCase)) ?? DashboardPeriod.Month;
        var analytics = await _cache.CreateOrGetAsync(CacheKeys.Dashboard("sales-analytics", key), () => _dashboard.GetSalesAnalyticsAsync(key), CacheKeys.DashboardTtlMinutes) ?? new SalesAnalyticsDTO();
        var notation = (await _cache.CreateOrGetAsync(CacheKeys.Dashboard("stats"), () => _dashboard.GetDashboardStatsAsync(), CacheKeys.DashboardTtlMinutes))?.CurrencyNotation ?? "$";

        var count = new[] { analytics.Dates.Count, analytics.SalesData.Count, analytics.ProfitData.Count }.Min();
        return new SalesTrendDto
        {
            Period = key,
            CurrencyNotation = notation,
            Points = Enumerable.Range(0, count)
                .Select(i => new SalesTrendPointDto { Date = analytics.Dates[i].ToString("yyyy-MM-dd"), Sales = analytics.SalesData[i], Profit = analytics.ProfitData[i] })
                .ToList(),
        };
    }
}
