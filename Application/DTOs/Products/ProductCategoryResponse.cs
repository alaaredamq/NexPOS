namespace Application.DTOs.Products;

public class ProductCategoryResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; }
    public string? ImageUrl { get; set; }
    public string? Color { get; set; }
    public int DisplayOrder { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    
    // Navigation properties
    public ICollection<ProductResponse>? Products { get; set; }
    
    // Computed properties
    public int ProductCount => Products?.Count ?? 0;
    public bool HasProducts => ProductCount > 0;
}
