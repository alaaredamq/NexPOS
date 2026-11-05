using Core.Entities.Base;
using Core.Entities.Products;

namespace Core.Entities.Purchasing;

/// <summary>One product on a purchase. Name, barcode and unit are copied so the paper still reads right after the product is renamed or deleted.</summary>
public class PurchaseItem : BaseEntity, IEntityBase<int>
{
    public int PurchaseId { get; set; }
    public Purchase Purchase { get; set; } = null!;

    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public string ProductName { get; set; } = null!;
    public string? Barcode { get; set; }
    public string? Unit { get; set; }

    /// <summary>In the product's unit (pieces, kg, litres).</summary>
    public decimal Quantity { get; set; }

    /// <summary>Per unit, in the purchase's (supplier's) currency.</summary>
    public decimal UnitCost { get; set; }
    public decimal LineTotal { get; set; }

    /// <summary>True when this line also set the product's cost price (converted to the product's currency).</summary>
    public bool UpdatedCostPrice { get; set; }
}
