using System.ComponentModel.DataAnnotations;

namespace Application.DTOs.Products;

public class CreateProductCategoryRequest
{
    [Required]
    [StringLength(100, MinimumLength = 2)]
    public string Name { get; set; } = string.Empty;

    [StringLength(500)]
    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;

    [StringLength(255)]
    public string? ImageUrl { get; set; }

    [StringLength(50)]
    public string? Color { get; set; }

    public int DisplayOrder { get; set; } = 0;
}
