using Core.Time;
using Application.IServices.Dashboard;
using Application.DTOs.Dashboard;
using Application.IServices.Products;
using Application.IServices.Users;
using Application.IServices.Sales;
using Application.IServices.Reports;
using Application.IServices.Base;
using Core.Entities.Inventory;
using Core.Entities.Sales;
using Core.Enum;
using Microsoft.Extensions.Logging;

namespace Application.Services.Dashboard;

public class DashboardService : IDashboardService
{
    private readonly IProductService _productService;
    private readonly ICustomerService _customerService;
    private readonly IOrderService _orderService;
    private readonly ISalesReportService _salesReportService;
    private readonly ICurrencyService _currencyService;
    private readonly IReceiptService _receiptService;
    private readonly ILogger<DashboardService> _logger;

    public DashboardService(
        IProductService productService,
        ICustomerService customerService,
        IOrderService orderService,
        ISalesReportService salesReportService,
        ICurrencyService currencyService,
        IReceiptService receiptService,
        ILogger<DashboardService> logger)
    {
        _productService = productService;
        _customerService = customerService;
        _orderService = orderService;
        _salesReportService = salesReportService;
        _currencyService = currencyService;
        _receiptService = receiptService;
        _logger = logger;
    }
    
    private string FormatCurrency(decimal amount, string? notation = null)
    {
        notation = notation ?? "$";
        return $"{notation}{amount:N2}";
    }

    public async Task<DashboardStatsDTO> GetDashboardStatsAsync()
    {
        try
        {
            // "Today" and "this month" are local calendar periods, but order dates are stored in UTC,
            // so build the windows in local time and convert the boundaries (otherwise between local
            // midnight and the UTC offset, today's sales land in yesterday's window).
            var todayLocal = DateTime.Today;
            var thisMonthStartLocal = new DateTime(todayLocal.Year, todayLocal.Month, 1);
            var lastMonthStartLocal = thisMonthStartLocal.AddMonths(-1);

            var today = LocalToUtc(todayLocal);
            var todayEnd = LocalToUtc(todayLocal.AddDays(1)).AddTicks(-1);
            var thisMonthStart = LocalToUtc(thisMonthStartLocal);
            // Prior-month window covering the same number of days, so "change" compares like-for-like
            var lastMonthStart = LocalToUtc(lastMonthStartLocal);
            var lastMonthComparableEnd = LocalToUtc(lastMonthStartLocal.AddDays(todayLocal.Day)).AddTicks(-1);

            // Get basic counts
            var products = await _productService.GetAllAsync();
            var customers = await _customerService.GetAllAsync();

            // Get default currency
            var defaultCurrency = await _currencyService.GetDefaultAsync();
            var currencyNotation = defaultCurrency?.Notation ?? "$";

            var totalSales = await _salesReportService.GetTotalSalesAsync(thisMonthStart, todayEnd);
            var totalOrders = await _salesReportService.GetTotalOrdersAsync(thisMonthStart, todayEnd);
            var todaySales = await _salesReportService.GetTotalSalesAsync(today, todayEnd);
            var todayOrders = await _salesReportService.GetTotalOrdersAsync(today, todayEnd);

            var lastMonthSales = await _salesReportService.GetTotalSalesAsync(lastMonthStart, lastMonthComparableEnd);
            var lastMonthOrders = await _salesReportService.GetTotalOrdersAsync(lastMonthStart, lastMonthComparableEnd);
            var totalSalesChange = lastMonthSales > 0 ? Math.Round((totalSales - lastMonthSales) / lastMonthSales * 100, 1) : 0m;
            var totalOrdersChange = totalOrders - lastMonthOrders;

            // Profit = sum of (sale price - cost price) over items actually sold this month.
            // Only counts items whose Product has a CostPrice set; items without one contribute 0
            // rather than a guessed margin.
            var ordersThisMonth = await _orderService.GetByDateRangeAsync(thisMonthStart, todayEnd);
            var ordersLastMonth = await _orderService.GetByDateRangeAsync(lastMonthStart, lastMonthComparableEnd);
            var profit = CalculateProfit(ordersThisMonth);
            var lastMonthProfit = CalculateProfit(ordersLastMonth);
            var profitChange = lastMonthProfit > 0 ? Math.Round((profit - lastMonthProfit) / lastMonthProfit * 100, 1) : 0m;

            return new DashboardStatsDTO
            {
                TotalSales = totalSales,
                TotalSalesFormatted = FormatCurrency(totalSales, currencyNotation),
                TotalSalesChange = totalSalesChange,
                TotalOrders = totalOrders,
                TotalOrdersChange = totalOrdersChange,
                TotalCustomers = customers.Count,
                // No CreatedAt on Customer/Product to derive a real period-over-period count from -
                // 0 rather than a fabricated trend. Would need a schema change to do for real.
                TotalCustomersChange = 0,
                TotalProducts = products.Count,
                TotalProductsChange = 0,
                TodaySales = todaySales,
                TodaySalesFormatted = FormatCurrency(todaySales, currencyNotation),
                TodayOrders = todayOrders,
                Profit = profit,
                ProfitFormatted = FormatCurrency(profit, currencyNotation),
                ProfitChange = profitChange,
                CurrencyNotation = currencyNotation
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting dashboard stats");
            return new DashboardStatsDTO(); // Return empty stats on error
        }
    }

    private static DateTime LocalToUtc(DateTime local) =>
        DateTime.SpecifyKind(local, DateTimeKind.Local).ToUniversalTime();

    private static decimal CalculateProfit(IEnumerable<Core.Entities.Sales.Order> orders)
    {
        return orders
            .SelectMany(o => o.Cart.CartItems)
            .Where(ci => ci.Product.CostPrice.HasValue)
            .Sum(ci => (ci.UnitPrice - ci.Product.CostPrice!.Value) * EffectiveQuantity(ci));
    }

    // For weight/volume products the sold amount lives in WeightOrVolume; for piece products it's Quantity.
    private static decimal EffectiveQuantity(CartItem ci) =>
        ci.WeightOrVolume is { } wv && wv > 0 ? wv : ci.Quantity;

    // Cost of goods for a line: 0 when the product has no CostPrice set (don't guess a margin).
    private static decimal LineCost(CartItem ci) =>
        (ci.Product?.CostPrice ?? 0m) * EffectiveQuantity(ci);

    // A completed sale for reporting purposes — POS checkout writes orders as Delivered; exclude
    // anything cancelled or returned so it doesn't inflate the dashboard figures.
    private static bool IsCompletedSale(Core.Entities.Sales.Order o) =>
        o.Status != (int)OrderStatus.Cancelled && o.Status != (int)OrderStatus.Returned;

    // Resolve a dashboard "period" string to a [start, now] window, matching the buckets the
    // charts expect: week/month → daily points, quarter/year → monthly points.
    private static (DateTime Start, DateTime End) ResolvePeriodWindow(string period)
    {
        // Month / quarter / year start on the LOCAL calendar; orders are stored in UTC, so convert the edges.
        var now = DateTime.UtcNow;
        var local = DateTime.Now;
        DateTime start = (period ?? "month").ToLowerInvariant() switch
        {
            "week" => now.AddDays(-7),
            "quarter" => new DateTime(local.Year, ((local.Month - 1) / 3) * 3 + 1, 1).LocalToUtc(),
            "year" => new DateTime(local.Year, 1, 1).LocalToUtc(),
            _ => new DateTime(local.Year, local.Month, 1).LocalToUtc(),
        };
        return (start, now);
    }

    private static bool UsesMonthlyBuckets(string period) =>
        (period ?? "month").ToLowerInvariant() is "quarter" or "year";

    // Groups completed orders into ordered chart buckets, returning (label, revenue, cost) per bucket.
    // Revenue is Order.TotalAmount (what the customer actually paid, after discounts); cost is COGS.
    private static List<(string Label, decimal Revenue, decimal Cost)> BuildTrendBuckets(
        IEnumerable<Core.Entities.Sales.Order> orders, string period)
    {
        var completed = orders.Where(IsCompletedSale);

        if (UsesMonthlyBuckets(period))
        {
            return completed
                .GroupBy(o => { var d = o.OrderDate.UtcToLocal(); return new DateTime(d.Year, d.Month, 1); }) // local month
                .OrderBy(g => g.Key)
                .Select(g => (
                    g.Key.ToString("MMM"),
                    g.Sum(o => o.TotalAmount),
                    g.SelectMany(o => o.Cart.CartItems).Sum(LineCost)))
                .ToList();
        }

        return completed
            .GroupBy(o => o.OrderDate.UtcToLocal().Date) // local day, not UTC day
            .OrderBy(g => g.Key)
            .Select(g => (
                g.Key.ToString("MMM dd"),
                g.Sum(o => o.TotalAmount),
                g.SelectMany(o => o.Cart.CartItems).Sum(LineCost)))
            .ToList();
    }

    public async Task<SalesAnalyticsDTO> GetSalesAnalyticsAsync(string period = "month")
    {
        try
        {
            var (startDate, endDate) = ResolvePeriodWindow(period);
            var orders = await _orderService.GetByDateRangeAsync(startDate, endDate);
            var buckets = BuildTrendBuckets(orders, period);

            return new SalesAnalyticsDTO
            {
                Labels = buckets.Select(b => b.Label).ToList(),
                SalesData = buckets.Select(b => b.Revenue).ToList(),
                // Real gross profit per bucket = revenue − COGS (from Product.CostPrice), no longer a flat 30% guess.
                ProfitData = buckets.Select(b => b.Revenue - b.Cost).ToList(),
                Period = period
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting sales analytics");
            return new SalesAnalyticsDTO();
        }
    }

    public async Task<CategoryAnalyticsDTO> GetCategoryAnalyticsAsync()
    {
        try
        {
            var localNow = DateTime.Now;
            var startDate = new DateTime(localNow.Year, localNow.Month, 1).LocalToUtc(); // start of the LOCAL month
            var endDate = DateTime.UtcNow;

            // Get real category sales data from SalesReportService
            var categorySalesData = await _salesReportService.GetCategorySalesAsync(startDate, endDate);

            // Convert Reports.CategorySalesDTO to Dashboard.CategorySalesDTO
            var categoryGroups = categorySalesData
                .Select(c => new CategorySalesDTO
                {
                    Name = c.CategoryName,
                    Sales = c.TotalSales,
                    ProductCount = 0, // Not available in Reports DTO
                    Percentage = c.Percentage,
                    Color = GetCategoryColor(c.CategoryName)
                })
                .OrderByDescending(c => c.Sales)
                .Take(10) // Limit to top 10 categories
                .ToList();

            // If no sales data, fall back to product categories
            if (!categoryGroups.Any())
            {
                var categories = await _productService.GetAllAsync();
                categoryGroups = categories
                    .Where(p => p.Category != null)
                    .GroupBy(p => p.Category!.Name)
                    .Select(g => new CategorySalesDTO
                    {
                        Name = g.Key,
                        Sales = 0,
                        ProductCount = g.Count(),
                        Percentage = 0,
                        Color = GetCategoryColor(g.Key)
                    })
                    .OrderByDescending(c => c.ProductCount)
                    .Take(10)
                    .ToList();
            }

            return new CategoryAnalyticsDTO
            {
                Categories = categoryGroups,
                Labels = categoryGroups.Select(c => c.Name).ToList(),
                Values = categoryGroups.Select(c => c.Percentage).ToList(),
                Colors = categoryGroups.Select(c => c.Color).ToList()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting category analytics");
            return new CategoryAnalyticsDTO();
        }
    }

    public async Task<List<TopProductDTO>> GetTopProductsAsync(int limit = 10)
    {
        try
        {
            // Trailing 90 days of real sales, ranked by revenue.
            var end = DateTime.UtcNow;
            var start = end.AddDays(-90);
            var orders = await _orderService.GetByDateRangeAsync(start, end);

            return orders
                .Where(IsCompletedSale)
                .SelectMany(o => o.Cart.CartItems)
                .Where(ci => ci.Product != null)
                .GroupBy(ci => ci.ProductId)
                .Select(g =>
                {
                    var product = g.First().Product;
                    return new TopProductDTO
                    {
                        ProductId = g.Key,
                        ProductName = product.Name,
                        Barcode = product.Barcode ?? "",
                        TotalSales = g.Sum(ci => ci.UnitPrice * EffectiveQuantity(ci)),
                        TotalQuantity = (int)Math.Round(g.Sum(EffectiveQuantity)),
                        UnitPrice = product.SellingPrice,
                        Category = product.Category?.Name ?? "Uncategorized"
                    };
                })
                .OrderByDescending(p => p.TotalSales)
                .Take(limit)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting top products");
            return new List<TopProductDTO>();
        }
    }

    public async Task<List<TopCustomerDTO>> GetTopCustomersAsync(int limit = 10)
    {
        try
        {
            // Trailing 12 months of real orders, grouped by customer. Walk-in sales (no CustomerId) are excluded.
            var end = DateTime.UtcNow;
            var start = end.AddYears(-1);
            var orders = await _orderService.GetByDateRangeAsync(start, end);

            return orders
                .Where(o => o.CustomerId.HasValue && o.Customer != null && IsCompletedSale(o))
                .GroupBy(o => o.CustomerId!.Value)
                .Select(g =>
                {
                    var customer = g.First().Customer!;
                    return new TopCustomerDTO
                    {
                        CustomerId = g.Key,
                        CustomerName = $"{customer.FirstName} {customer.LastName}".Trim(),
                        Email = customer.Email ?? "",
                        TotalSpent = g.Sum(o => o.TotalAmount),
                        TotalOrders = g.Count(),
                        LastOrderDate = g.Max(o => o.OrderDate)
                    };
                })
                .OrderByDescending(c => c.TotalSpent)
                .Take(limit)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting top customers");
            return new List<TopCustomerDTO>();
        }
    }

    public async Task<List<RecentOrderDTO>> GetRecentOrdersAsync(int limit = 10)
    {
        try
        {
            var defaultCurrency = await _currencyService.GetDefaultAsync();
            var currencyNotation = defaultCurrency?.Notation ?? "$";

            // Look back far enough to reliably fill `limit` rows without loading the whole table.
            var end = DateTime.UtcNow;
            var start = end.AddDays(-90);

            var orders = await _orderService.GetByDateRangeAsync(start, end);

            // Payment method isn't on Order — it's recorded on the Receipt. Map by OrderId.
            var receipts = await _receiptService.GetReceiptsByDateRangeAsync(start, end);
            var paymentByOrderId = receipts
                .GroupBy(r => r.OrderId)
                .ToDictionary(g => g.Key, g => g.First().PaymentMethod?.Name ?? "—");

            return orders
                .OrderByDescending(o => o.OrderDate)
                .Take(limit)
                .Select(o => new RecentOrderDTO
                {
                    Id = o.Id,
                    OrderId = o.Id,
                    CustomerName = o.Customer != null
                        ? $"{o.Customer.FirstName} {o.Customer.LastName}".Trim()
                        : "Walk-in",
                    Amount = o.TotalAmount,
                    AmountFormatted = FormatCurrency(o.TotalAmount, currencyNotation),
                    OrderDate = o.OrderDate,
                    Status = ((OrderStatus)o.Status).ToString(),
                    PaymentMethod = paymentByOrderId.TryGetValue(o.Id, out var pm) ? pm : "—",
                    CurrencyNotation = currencyNotation
                })
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting recent orders");
            return new List<RecentOrderDTO>();
        }
    }

    public async Task<RevenueTrendDTO> GetRevenueTrendAsync(string period = "month")
    {
        try
        {
            var (start, end) = ResolvePeriodWindow(period);
            var orders = await _orderService.GetByDateRangeAsync(start, end);
            var buckets = BuildTrendBuckets(orders, period);

            // No operating-expense data exists in this system; "expense" here is cost of goods sold
            // (Product.CostPrice), so NetProfit is gross profit.
            var revenueData = buckets.Select(b => b.Revenue).ToList();
            var expenseData = buckets.Select(b => b.Cost).ToList();

            return new RevenueTrendDTO
            {
                Labels = buckets.Select(b => b.Label).ToList(),
                RevenueData = revenueData,
                ExpenseData = expenseData,
                TotalRevenue = revenueData.Sum(),
                TotalExpenses = expenseData.Sum(),
                NetProfit = revenueData.Sum() - expenseData.Sum()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting revenue trend");
            return new RevenueTrendDTO();
        }
    }

    public async Task<ProfitAnalysisDTO> GetProfitAnalysisAsync(string period = "month")
    {
        try
        {
            var (start, end) = ResolvePeriodWindow(period);
            var orders = await _orderService.GetByDateRangeAsync(start, end);
            var buckets = BuildTrendBuckets(orders, period);

            var revenueData = buckets.Select(b => b.Revenue).ToList();
            var costData = buckets.Select(b => b.Cost).ToList();
            var profitData = revenueData.Zip(costData, (r, c) => r - c).ToList();

            var totalRevenue = revenueData.Sum();
            var totalProfit = profitData.Sum();

            return new ProfitAnalysisDTO
            {
                Labels = buckets.Select(b => b.Label).ToList(),
                RevenueData = revenueData,
                CostData = costData,
                ProfitData = profitData,
                AverageProfitMargin = totalRevenue > 0 ? totalProfit / totalRevenue * 100 : 0,
                Period = period
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting profit analysis");
            return new ProfitAnalysisDTO();
        }
    }

    public async Task<InventoryStatusDTO> GetInventoryStatusAsync()
    {
        try
        {
            var products = await _productService.GetAllAsync();
            var totalProducts = products.Count;
            var activeProducts = products.Count(p => p.IsActive);
            var lowStockProducts = products.Count(p => p.StockQuantity <= (p.MinimumStock ?? 0));
            var outOfStockProducts = products.Count(p => p.StockQuantity == 0);
            var totalInventoryValue = products.Sum(p => p.StockQuantity * p.SellingPrice);

            var lowStockItems = products
                .Where(p => p.StockQuantity <= (p.MinimumStock ?? 0))
                .Select(p => new LowStockProductDTO
                {
                    ProductId = p.Id,
                    ProductName = p.Name,
                    CurrentStock = p.StockQuantity,
                    MinimumStock = p.MinimumStock ?? 0,
                    UnitPrice = p.SellingPrice,
                    Category = p.Category?.Name ?? "Uncategorized"
                })
                .ToList();

            return new InventoryStatusDTO
            {
                TotalProducts = totalProducts,
                LowStockProducts = lowStockProducts,
                OutOfStockProducts = outOfStockProducts,
                ActiveProducts = activeProducts,
                TotalInventoryValue = totalInventoryValue,
                LowStockItems = lowStockItems
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting inventory status");
            return new InventoryStatusDTO();
        }
    }

    private string GetCategoryColor(string categoryName)
    {
        return categoryName.ToLower() switch
        {
            "electronics" => "#008080",
            "clothing" => "#20c997",
            "books" => "#17a2b8",
            "home" => "#ffc107",
            "sports" => "#dc3545",
            "food" => "#28a745",
            "beverages" => "#6f42c1",
            "health" => "#fd7e14",
            _ => "#6c757d"
        };
    }
}
