namespace Application.DTOs.Products;

public class ProductResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Barcode { get; set; }
    public int CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public decimal PurchasePrice { get; set; }
    public decimal SellingPrice { get; set; }
    public decimal StockQuantity { get; set; }
    public decimal MinStockLevel { get; set; }
    public bool IsActive { get; set; }
    public string? ImageUrl { get; set; }
    public string? Unit { get; set; }
    public string? Brand { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    
    // Computed properties
    public decimal ProfitMargin => SellingPrice - PurchasePrice;
    public bool IsLowStock => StockQuantity <= MinStockLevel;
    public bool IsOutOfStock => StockQuantity == 0;
}
