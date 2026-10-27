using System.ComponentModel.DataAnnotations;
using Core.Entities.Base;

namespace Core.Entities.Products;

/// <summary>
/// An extra selling price for a product (Wholesale, Staff, ...) the cashier can pick at the till.
/// The product's own <see cref="Product.SellingPrice"/> stays the default price; these are the others.
/// Expressed in the product's currency, like <see cref="Product.SellingPrice"/>.
/// There is deliberately no back-reference to <see cref="Product"/> so serialising a product can't loop.
/// </summary>
public class ProductPrice : IEntityBase<int>
{
    public int Id { get; set; }

    public int ProductId { get; set; }

    [Required]
    [StringLength(50)]
    public string Name { get; set; } = string.Empty;

    public decimal Price { get; set; }

    public int SortOrder { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }
}
