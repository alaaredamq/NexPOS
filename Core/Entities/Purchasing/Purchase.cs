using Core.Entities.Base;
using Core.Entities.Products;
using Core.Entities.Sales;
using Core.Entities.Users;
using Core.Enum;

namespace Core.Entities.Purchasing;

/// <summary>
/// Goods bought from a supplier. Saving one is "goods received": every line adds its quantity to the product's
/// stock, and whatever was not paid on the spot is added to the supplier's balance (see <see cref="Supplier.Balance"/>).
/// All money on a purchase is in the supplier's currency at the time of the purchase.
/// </summary>
public class Purchase : BaseEntity, IEntityBase<int>
{
    public int SupplierId { get; set; }
    public Supplier Supplier { get; set; } = null!;

    /// <summary>Who entered the purchase.</summary>
    public int UserId { get; set; }
    public User User { get; set; } = null!;

    public int CurrencyId { get; set; }
    public Currency Currency { get; set; } = null!;

    /// <summary>The day the goods arrived (UTC instant of local midnight-or-later; shown as a day).</summary>
    public DateTime PurchaseDate { get; set; }

    /// <summary>The number printed on the supplier's own invoice or delivery note, so the two papers can be matched.</summary>
    public string? SupplierReference { get; set; }
    public string? Notes { get; set; }

    public decimal Subtotal { get; set; }
    public decimal DiscountPercent { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal Total { get; set; }

    /// <summary>Paid to the supplier at the time of the purchase. <c>Total - AmountPaid</c> went on the supplier's account.</summary>
    public decimal AmountPaid { get; set; }

    /// <summary>The cash drop that took <see cref="AmountPaid"/> out of the cash drawer, when it was paid from the till. Cancelling the purchase leaves it: the money did leave the drawer.</summary>
    public int? CashDropId { get; set; }

    public PurchaseStatus Status { get; set; } = PurchaseStatus.Received;
    public DateTime? CancelledAt { get; set; }
    public int? CancelledByUserId { get; set; }
    public string? CancelReason { get; set; }

    public ICollection<PurchaseItem> Items { get; set; } = new List<PurchaseItem>();

    /// <summary>Printed number, derived from the id like order numbers are.</summary>
    public string Number => FormatNumber(Id);

    public static string FormatNumber(int id) => $"PUR-{id:D6}";
}
