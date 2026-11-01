namespace Application.DTOs.Dashboard;

public class DashboardStatsDTO
{
    public decimal TotalSales { get; set; }
    public string TotalSalesFormatted { get; set; } = string.Empty;
    public decimal TotalSalesChange { get; set; }
    public int TotalOrders { get; set; }
    public int TotalOrdersChange { get; set; }
    public int TotalCustomers { get; set; }
    public int TotalCustomersChange { get; set; }
    public int TotalProducts { get; set; }
    public int TotalProductsChange { get; set; }
    public decimal TodaySales { get; set; }
    public string TodaySalesFormatted { get; set; } = string.Empty;
    public int TodayOrders { get; set; }
    public decimal Profit { get; set; }
    public string ProfitFormatted { get; set; } = string.Empty;
    public decimal ProfitChange { get; set; }
    public string CurrencyNotation { get; set; } = "$";
}

public class SalesAnalyticsDTO
{
    public List<string> Labels { get; set; } = new();
    public List<decimal> SalesData { get; set; } = new();
    public List<decimal> ProfitData { get; set; } = new();
    /// <summary>First local day of each bucket, parallel to <see cref="Labels"/>.</summary>
    public List<DateTime> Dates { get; set; } = new();
    public string Period { get; set; } = string.Empty;
}

public class CategoryAnalyticsDTO
{
    public List<CategorySalesDTO> Categories { get; set; } = new();
    public List<string> Labels { get; set; } = new();
    public List<decimal> Values { get; set; } = new();
    public List<string> Colors { get; set; } = new();
}

public class CategorySalesDTO
{
    public string Name { get; set; } = string.Empty;
    public decimal Sales { get; set; }
    public decimal Percentage { get; set; }
    public int ProductCount { get; set; }
    public string Color { get; set; } = string.Empty;
}

public class TopProductDTO
{
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string Barcode { get; set; } = string.Empty;
    public decimal TotalSales { get; set; }
    public int TotalQuantity { get; set; }
    public decimal UnitPrice { get; set; }
    public string Category { get; set; } = string.Empty;
}

public class TopCustomerDTO
{
    public int CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public decimal TotalSpent { get; set; }
    public int TotalOrders { get; set; }
    public DateTime LastOrderDate { get; set; }
}

public class RecentOrderDTO
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string AmountFormatted { get; set; } = string.Empty;
    public DateTime OrderDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public string PaymentMethod { get; set; } = string.Empty;
    public string CurrencyNotation { get; set; } = "$";
}

public class RevenueTrendDTO
{
    public List<string> Labels { get; set; } = new();
    public List<decimal> RevenueData { get; set; } = new();
    public List<decimal> ExpenseData { get; set; } = new();
    public decimal TotalRevenue { get; set; }
    public decimal TotalExpenses { get; set; }
    public decimal NetProfit { get; set; }
}

public class ProfitAnalysisDTO
{
    public List<string> Labels { get; set; } = new();
    public List<decimal> ProfitData { get; set; } = new();
    public List<decimal> RevenueData { get; set; } = new();
    public List<decimal> CostData { get; set; } = new();
    public decimal AverageProfitMargin { get; set; }
    public string Period { get; set; } = string.Empty;
}

public class InventoryStatusDTO
{
    public int TotalProducts { get; set; }
    public int LowStockProducts { get; set; }
    public int OutOfStockProducts { get; set; }
    public int ActiveProducts { get; set; }
    public decimal TotalInventoryValue { get; set; }
    public List<LowStockProductDTO> LowStockItems { get; set; } = new();
}

public class LowStockProductDTO
{
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal CurrentStock { get; set; }
    public decimal MinimumStock { get; set; }
    public decimal UnitPrice { get; set; }
    public string Category { get; set; } = string.Empty;
}
