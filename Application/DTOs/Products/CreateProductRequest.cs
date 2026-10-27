using System.ComponentModel.DataAnnotations;

namespace Application.DTOs.Products;

public class CreateProductRequest
{
    [Required]
    [StringLength(255, MinimumLength = 2)]
    [Display(Name = "Product Name")]
    public string Name { get; set; } = string.Empty;

    [StringLength(1000)]
    [Display(Name = "Description")]
    public string? Description { get; set; }

    [Required(ErrorMessage = "Barcode is required")]
    [StringLength(50, MinimumLength = 1)]
    [Display(Name = "Barcode")]
    public string Barcode { get; set; } = string.Empty;

    [Required]
    [Range(1, int.MaxValue, ErrorMessage = "Category is required")]
    [Display(Name = "Category")]
    public int CategoryId { get; set; }

    [Required]
    [Range(1, int.MaxValue, ErrorMessage = "Supplier is required")]
    [Display(Name = "Supplier")]
    public int SupplierId { get; set; }

    [Display(Name = "Currency")]
    public int CurrencyId { get; set; }

    [Required]
    [Range(0.01, double.MaxValue, ErrorMessage = "Purchase price must be greater than 0")]
    [Display(Name = "Purchase Price")]
    public decimal PurchasePrice { get; set; }

    [Required]
    [Range(0.01, double.MaxValue, ErrorMessage = "Selling price must be greater than 0")]
    [Display(Name = "Selling Price")]
    public decimal SellingPrice { get; set; }

    [Range(0, double.MaxValue)]
    [Display(Name = "Initial Stock Quantity")]
    public decimal StockQuantity { get; set; } = 0;

    [Range(0, double.MaxValue)]
    [Display(Name = "Minimum Stock Level")]
    public decimal MinStockLevel { get; set; } = 0;

    public bool IsActive { get; set; } = true;

    [StringLength(255)]
    public string? ImageUrl { get; set; }

    [StringLength(50)]
    [Display(Name = "Unit")]
    public string? Unit { get; set; }

    [StringLength(50)]
    [Display(Name = "Brand")]
    public string? Brand { get; set; }
    
    // Pack properties
    public bool IsPack { get; set; }
    
    [Range(1, int.MaxValue, ErrorMessage = "Pack size must be greater than 0")]
    [Display(Name = "Pack Size")]
    public int? PackSize { get; set; }
    
    [Display(Name = "Single Product")]
    public int? SingleProductId { get; set; }

    /// <summary>Extra prices the cashier can pick at the till (Wholesale, ...). The selling price stays the default.</summary>
    public List<ProductPriceInput> PriceTiers { get; set; } = new();
}
