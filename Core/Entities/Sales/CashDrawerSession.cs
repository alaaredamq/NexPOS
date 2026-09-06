using Core.Entities.Base;
using Core.Entities.Users;

namespace Core.Entities.Sales;

/// <summary>
/// Represents a cash drawer session (shift) with opening and closing balances
/// </summary>
public class CashDrawerSession : BaseEntity, IEntityBase<int>
{
    public int UserId { get; set; }
    public User User { get; set; } = null!;

    // Present in the real database but missing here until A15's reconciliation - added to match.
    // (CashDifference also exists as a real column but is left unmapped - it's the computed
    // property below, intentionally never persisted.)
    public bool IsDeleted { get; set; }

    // Session details
    public DateTime OpenedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public bool IsOpen { get; set; }
    
    // Opening balance
    public decimal OpeningCash { get; set; }
    
    // Closing balance (when session is closed)
    public decimal? ExpectedCash { get; set; }
    public decimal? ActualCash { get; set; }
    public decimal? CashDifference => ActualCash.HasValue && ExpectedCash.HasValue 
        ? ActualCash.Value - ExpectedCash.Value 
        : null;
    
    // Sales summary
    public decimal? TotalSales { get; set; }
    public decimal? TotalCash { get; set; }
    public decimal? TotalCard { get; set; }
    public decimal? TotalOtherPayments { get; set; }
    public int? TotalTransactions { get; set; }
    
    // Cash management
    public decimal? TotalCashDrops { get; set; }
    public decimal? TotalCashAdded { get; set; }
    
    // Discounts and adjustments
    public decimal? TotalDiscounts { get; set; }
    public decimal? TotalAdjustments { get; set; }
    
    // Notes
    public string? OpeningNotes { get; set; }
    public string? ClosingNotes { get; set; }
    
    // Navigation properties
    public ICollection<CashDrop> CashDrops { get; set; } = new List<CashDrop>();
    public ICollection<Order> Orders { get; set; } = new List<Order>();
    
    public CashDrawerSession(int userId, decimal openingCash, string? openingNotes = null)
    {
        UserId = userId;
        OpenedAt = DateTime.UtcNow;
        OpeningCash = openingCash;
        IsOpen = true;
        OpeningNotes = openingNotes;
    }
    
    // Parameterless constructor for EF Core
    private CashDrawerSession() { }
}

