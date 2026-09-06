using Core.Entities.Sales;

namespace Application.IServices.Sales;

public interface ICashDrawerService
{
    // Session Management
    Task<CashDrawerSession> OpenDrawerAsync(int userId, decimal openingCash, string? notes = null);
    Task<CashDrawerSession> CloseDrawerAsync(int sessionId, decimal actualCash, string? notes = null);
    Task<CashDrawerSession?> GetOpenSessionByUserIdAsync(int userId);

    /// <summary>
    /// Cheap lookup of the user's open session id (no Orders/CashDrops loaded) for the checkout hot path.
    /// </summary>
    Task<int?> GetOpenSessionIdByUserIdAsync(int userId);
    Task<CashDrawerSession?> GetSessionWithDetailsAsync(int sessionId);
    Task<List<CashDrawerSession>> GetSessionHistoryAsync(int userId, DateTime? startDate = null, DateTime? endDate = null);
    Task<List<CashDrawerSession>> GetAllOpenSessionsAsync();
    Task<bool> HasOpenSessionAsync(int userId);
    
    // Cash Drop Management
    Task<CashDrop> AddCashDropAsync(int sessionId, int userId, decimal amount, string? reason = null, string? notes = null);
    Task<List<CashDrop>> GetSessionCashDropsAsync(int sessionId);
    Task<decimal> GetTotalCashDropsAsync(int sessionId);
    
    // Session Calculations
    Task<decimal> CalculateExpectedCashAsync(int sessionId);
    Task<SessionSummary> GetSessionSummaryAsync(int sessionId);
    
    // Z-Report
    Task<ZReport> GenerateZReportAsync(int sessionId);
}

public class SessionSummary
{
    public int SessionId { get; set; }
    public DateTime OpenedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public decimal OpeningCash { get; set; }
    public decimal TotalCashSales { get; set; }
    public decimal TotalCardSales { get; set; }
    public decimal TotalOtherSales { get; set; }
    public decimal TotalCashDrops { get; set; }
    public decimal ExpectedCash { get; set; }
    public decimal? ActualCash { get; set; }
    public decimal? CashDifference { get; set; }
    public int TotalTransactions { get; set; }
    public decimal TotalSales { get; set; }
    public decimal TotalDiscounts { get; set; }
    public decimal TotalAdjustments { get; set; }
}

public class ZReport
{
    public int SessionId { get; set; }
    public string CashierName { get; set; } = string.Empty;
    public DateTime OpenedAt { get; set; }
    public DateTime ClosedAt { get; set; }
    
    // Opening/Closing
    public decimal OpeningCash { get; set; }
    public decimal ClosingCash { get; set; }
    
    // Sales Summary
    public int TotalTransactions { get; set; }
    public decimal TotalSales { get; set; }
    public decimal TotalDiscounts { get; set; }
    public decimal TotalAdjustments { get; set; }
    public decimal NetSales { get; set; }
    
    // Payment Methods
    public decimal CashSales { get; set; }
    public decimal CardSales { get; set; }
    public decimal OtherPayments { get; set; }
    
    // Cash Management
    public decimal TotalCashDrops { get; set; }
    public int CashDropCount { get; set; }
    public decimal ExpectedCash { get; set; }
    public decimal ActualCash { get; set; }
    public decimal CashDifference { get; set; }
    
    // Orders
    public List<ZReportOrder> Orders { get; set; } = new();
    public List<ZReportCashDrop> CashDrops { get; set; } = new();
}

public class ZReportOrder
{
    public int OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public DateTime OrderDate { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public string PaymentMethod { get; set; } = string.Empty;
}

public class ZReportCashDrop
{
    public int Id { get; set; }
    public DateTime DroppedAt { get; set; }
    public decimal Amount { get; set; }
    public string? Reason { get; set; }
    public string? Notes { get; set; }
}

