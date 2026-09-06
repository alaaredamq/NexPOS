using System.ComponentModel.DataAnnotations;

namespace Application.DTOs.Products;

public class UpdateProductRequest
{
    [Required]
    public int Id { get; set; }

    [Required]
    [StringLength(255, MinimumLength = 2)]
    public string Name { get; set; } = string.Empty;

    [StringLength(1000)]
    public string? Description { get; set; }

    [StringLength(50)]
    public string? Barcode { get; set; }

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
}
