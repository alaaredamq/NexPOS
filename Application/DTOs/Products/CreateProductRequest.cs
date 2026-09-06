using System.ComponentModel.DataAnnotations;

namespace Application.DTOs.Products;

public class CreateProductRequest
{
    [Required]
    [StringLength(255, MinimumLength = 2)]
    public string Name { get; set; } = string.Empty;

    [StringLength(1000)]
    public string? Description { get; set; }

    [Required(ErrorMessage = "Barcode is required")]
    [StringLength(50, MinimumLength = 1)]
    public string Barcode { get; set; } = string.Empty;

    [Required]
    [Range(1, int.MaxValue, ErrorMessage = "Category is required")]
    public int CategoryId { get; set; }

    [Required]
    [Range(1, int.MaxValue, ErrorMessage = "Supplier is required")]
    public int SupplierId { get; set; }

    public int CurrencyId { get; set; }

    [Required]
    [Range(0.01, double.MaxValue, ErrorMessage = "Purchase price must be greater than 0")]
    public decimal PurchasePrice { get; set; }

    [Required]
    [Range(0.01, double.MaxValue, ErrorMessage = "Selling price must be greater than 0")]
    public decimal SellingPrice { get; set; }

    [Range(0, double.MaxValue)]
    public decimal StockQuantity { get; set; } = 0;

    [Range(0, double.MaxValue)]
    public decimal MinStockLevel { get; set; } = 0;

    public bool IsActive { get; set; } = true;

    [StringLength(255)]
    public string? ImageUrl { get; set; }

    [StringLength(50)]
    public string? Unit { get; set; }

    [StringLength(50)]
    public string? Brand { get; set; }
    
    // Pack properties
    public bool IsPack { get; set; }
    
    [Range(1, int.MaxValue, ErrorMessage = "Pack size must be greater than 0")]
    public int? PackSize { get; set; }
    
    public int? SingleProductId { get; set; }
}
