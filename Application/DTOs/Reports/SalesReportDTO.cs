namespace Application.DTOs.Reports
{
    public class SalesReportDTO
    {
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public decimal TotalSales { get; set; }
        /// <summary>Selling price minus cost price over the lines sold; a product without a cost price is left out (same rule as the dashboard).</summary>
        public decimal TotalProfit { get; set; }
        public int TotalOrders { get; set; }
        public decimal AverageOrderValue { get; set; }
        public decimal TotalItemsSold { get; set; }
        public List<DailySalesDTO> DailySales { get; set; } = new();
        public List<TopProductDTO> TopProducts { get; set; } = new();
        public List<CategorySalesDTO> CategorySales { get; set; } = new();
        public List<PaymentMethodSalesDTO> PaymentMethodSales { get; set; } = new();
    }

    public class DailySalesDTO
    {
        public DateTime Date { get; set; }
        public decimal Sales { get; set; }
        public int Orders { get; set; }
        public decimal ItemsSold { get; set; }
    }

    public class TopProductDTO
    {
        public int ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public decimal QuantitySold { get; set; }
        public decimal TotalRevenue { get; set; }
        public decimal AveragePrice { get; set; }
    }

    public class CategorySalesDTO
    {
        public int CategoryId { get; set; }
        public string CategoryName { get; set; } = string.Empty;
        public decimal TotalSales { get; set; }
        public int TotalOrders { get; set; }
        public decimal Percentage { get; set; }
    }

    public class PaymentMethodSalesDTO
    {
        public int PaymentMethodId { get; set; }
        public string PaymentMethodName { get; set; } = string.Empty;
        public decimal TotalSales { get; set; }
        public int TotalOrders { get; set; }
        public decimal Percentage { get; set; }
    }

    public class SalesReportRequest
    {
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public string ReportType { get; set; } = "daily"; // daily, weekly, monthly, yearly
        public bool IncludeProductDetails { get; set; } = true;
        public bool IncludeCategoryBreakdown { get; set; } = true;
        public bool IncludePaymentMethodBreakdown { get; set; } = true;
    }
}
