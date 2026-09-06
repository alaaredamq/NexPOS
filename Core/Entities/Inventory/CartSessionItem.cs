using Core.Entities.Base;
using Core.Entities.Products;

namespace Core.Entities.Inventory;

public class CartSessionItem : BaseEntity, IEntityBase<int>
{
    public int CartSessionId { get; set; }
    public int ProductId { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    
    /// <summary>
    /// Weight or volume amount for products sold by weight/volume (e.g., 0.5 for 500g or 0.5L).
    /// Null for products sold by piece. The unit is determined by the Product.Unit field.
    /// </summary>
    public decimal? WeightOrVolume { get; set; }
    
    /// <summary>
    /// Total price for this cart item.
    /// For weight/volume products, uses WeightOrVolume * UnitPrice.
    /// For piece products, uses Quantity * UnitPrice.
    /// </summary>
    public decimal TotalPrice => WeightOrVolume.HasValue && WeightOrVolume.Value > 0 
        ? UnitPrice * WeightOrVolume.Value 
        : UnitPrice * Quantity;

    #region Navigation Properties

    public CartSession CartSession { get; set; } = null!;
    public Product Product { get; set; } = null!;

    #endregion

    #region Constructors

    public CartSessionItem(int cartSessionId, int productId, int quantity, decimal unitPrice, decimal? weightOrVolume = null)
    {
        CartSessionId = cartSessionId;
        ProductId = productId;
        Quantity = quantity;
        UnitPrice = unitPrice;
        WeightOrVolume = weightOrVolume;
        CreatedAt = DateTime.UtcNow;
    }

    protected CartSessionItem() { }

    #endregion
}
