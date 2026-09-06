using System.ComponentModel.DataAnnotations;
using Core.Entities.Base;
using Core.Entities.Sales;

namespace Core.Entities.Products;

public class Product : ISSoftDeletable, IEntityBase<int>
{   
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public string Barcode { get; set; } = null!;
    public decimal? CostPrice { get; set; }
    public decimal SellingPrice { get; set; }
    public int BrandId { get; set; }
    public int SupplierId { get; set; }
    /// <summary>
    /// Stock quantity in the product's unit (pieces for piece products, kg/L for weight/volume products)
    /// </summary>
    public decimal StockQuantity { get; set; }
    public decimal? MinimumStock { get; set; }
    public decimal? MaximumStock { get; set; }
    public int CurrencyId { get; set; }
    public int CategoryId { get; set; }
    public bool IsActive { get; set; }
    
    /// <summary>
    /// Unit of measurement for the product (piece, kg, liter, etc.)
    /// </summary>
    [StringLength(50)]
    public string? Unit { get; set; }
    
    // Pack properties
    public bool IsPack { get; set; }
    public int? PackSize { get; set; } // Number of single products in this pack
    public int? SingleProductId { get; set; } // Reference to the single product this pack contains

    #region Navigation Properties

    // public Brand Brand { get; set; } = null!;
    public Supplier Supplier { get; set; } = null!;
    public Currency Currency { get; set; } = null!;
    public ProductCategory Category { get; set; } = null!;
    public string? Image { get; set; }
    public Product? SingleProduct { get; set; } // Navigation property to the single product

    #endregion

    #region Constructors

    public Product()
    {
    }
    
    #endregion
    
}

