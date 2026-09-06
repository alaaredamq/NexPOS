using Core.Entities.Products;

namespace Core.DTOs;

public class ProductDTO : Product
{
    public ProductDTO(){}
    
    /// <summary>
    /// The supplier's discount rate (0-100 percentage)
    /// </summary>
    public double SupplierDiscountRate { get; set; }
    
    /// <summary>
    /// The calculated discounted price after applying the supplier's discount
    /// </summary>
    public decimal DiscountedPrice => SupplierDiscountRate > 0 
        ? SellingPrice * (1 - (decimal)(SupplierDiscountRate / 100)) 
        : SellingPrice;
    
    /// <summary>
    /// Whether the product has a supplier discount applied
    /// </summary>
    public bool HasSupplierDiscount => SupplierDiscountRate > 0;
    
    /// <summary>
    /// The effective price to use (discounted price if discount exists, otherwise selling price)
    /// </summary>
    public decimal EffectivePrice => DiscountedPrice;
}