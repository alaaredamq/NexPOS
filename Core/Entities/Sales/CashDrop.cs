using Core.Entities.Base;
using Core.Entities.Users;

namespace Core.Entities.Sales;

/// <summary>
/// Represents a cash drop (removal of cash from drawer during shift)
/// </summary>
public class CashDrop : BaseEntity, IEntityBase<int>
{
    public int CashDrawerSessionId { get; set; }
    public CashDrawerSession CashDrawerSession { get; set; } = null!;
    
    public int UserId { get; set; }
    public User User { get; set; } = null!;

    // Present in the real database but missing here until A15's reconciliation - added to match.
    public bool IsDeleted { get; set; }

    public decimal Amount { get; set; }
    public DateTime DroppedAt { get; set; }
    public string? Reason { get; set; }
    public string? Notes { get; set; }
    
    public CashDrop(int cashDrawerSessionId, int userId, decimal amount, string? reason = null, string? notes = null)
    {
        CashDrawerSessionId = cashDrawerSessionId;
        UserId = userId;
        Amount = amount;
        DroppedAt = DateTime.UtcNow;
        Reason = reason;
        Notes = notes;
    }
    
    // Parameterless constructor for EF Core
    private CashDrop() { }
}

