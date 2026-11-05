namespace Core.Enum;

public enum PurchaseStatus
{
    /// <summary>Goods arrived: stock was increased and the supplier's balance moved.</summary>
    Received = 0,
    /// <summary>Undone: the stock it added was taken back and the amount it put on the supplier's balance was removed.</summary>
    Cancelled = 1
}
