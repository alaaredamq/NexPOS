using System.ComponentModel.DataAnnotations;
using Core.Auditing;
using Core.Entities.Base;

namespace Core.Entities.Products;

/// <summary>What happened to a product. Stored as a number - never reorder, only append.</summary>
public enum ProductHistoryType
{
    /// <summary>The product was added (<see cref="ProductHistory.StockAfter"/> is the opening stock).</summary>
    Created = 0,
    /// <summary>Units were added by hand from the inventory page.</summary>
    StockAdded = 1,
    /// <summary>Units were removed by hand from the inventory page (damage, loss, count correction).</summary>
    StockRemoved = 2,
    /// <summary>The stock figure was typed in directly - on the product form or in an import file.</summary>
    StockSet = 3,
    /// <summary>Units left the shelf in a sale (<see cref="ProductHistory.OrderId"/> says which).</summary>
    Sold = 4,
    /// <summary>A sale was cancelled or its receipt voided and the units came back.</summary>
    SaleReturned = 5,
    /// <summary>The selling price changed (<see cref="ProductHistory.OldValue"/> to <see cref="ProductHistory.NewValue"/>).</summary>
    SellingPriceChanged = 6,
    /// <summary>The purchase (cost) price changed.</summary>
    CostPriceChanged = 7,
    /// <summary>The minimum-stock level changed.</summary>
    MinimumStockChanged = 8,
    /// <summary>Other details were edited; <see cref="ProductHistory.Details"/> lists which.</summary>
    Updated = 9,
    /// <summary>The product was deleted.</summary>
    Deleted = 10,
    /// <summary>Units arrived from a supplier purchase (<see cref="ProductHistory.PurchaseId"/> says which).</summary>
    Received = 11,
}

/// <summary>
/// One line of a product's history: a single thing that happened to it, when, by whom, and - for
/// anything that moved stock - how many units and what the stock was before and after.
/// The sentence shown to the user is built from these fields at display time (so it follows the
/// language the user is in), not stored.
/// Lives in the business database on purpose: a backup restore brings the products and their history
/// back to the same moment, and the history stays tied to the stock figures it explains.
/// Rows are append-only.
/// </summary>
[AuditIgnoreEntity]
public class ProductHistory : BaseEntity, IEntityBase<int>
{
    public int ProductId { get; set; }

    public ProductHistoryType Type { get; set; }

    /// <summary>Signed units moved: positive added to stock, negative took away. Null for events that don't touch stock.</summary>
    public decimal? QuantityChange { get; set; }

    public decimal? StockBefore { get; set; }

    public decimal? StockAfter { get; set; }

    /// <summary>The order a sale / return belongs to.</summary>
    public int? OrderId { get; set; }

    /// <summary>The supplier purchase a <see cref="ProductHistoryType.Received"/> line belongs to.</summary>
    public int? PurchaseId { get; set; }

    /// <summary>The reason the person gave (stock adjustments, cancellations).</summary>
    [StringLength(500)]
    public string? Reason { get; set; }

    /// <summary>Before / after for price and minimum-stock changes, as invariant-culture numbers.</summary>
    [StringLength(50)]
    public string? OldValue { get; set; }

    [StringLength(50)]
    public string? NewValue { get; set; }

    /// <summary>Comma-separated field keys for <see cref="ProductHistoryType.Updated"/> (name, barcode, category, ...).</summary>
    [StringLength(200)]
    public string? Details { get; set; }

    /// <summary>Who did it. No foreign key: the line must outlive the user, so the name is copied here.</summary>
    public int? UserId { get; set; }

    [StringLength(100)]
    public string? UserName { get; set; }
}
