using Core.Entities.Products;

namespace Core.IRepositories.Products;

/// <summary>
/// A product's editable fields as they were at one moment, so a later save can be compared against them.
/// A tracked <see cref="Product"/> can't be used for that: callers edit the very same instance the service
/// then loads, so "before" and "after" would be one object.
/// </summary>
public sealed record ProductSnapshot(
    string Name,
    string? Description,
    string Barcode,
    decimal? CostPrice,
    decimal SellingPrice,
    decimal StockQuantity,
    decimal? MinimumStock,
    int CategoryId,
    int SupplierId,
    bool IsActive,
    string? Unit)
{
    public static ProductSnapshot Of(Product p) => new(
        p.Name, p.Description, p.Barcode, p.CostPrice, p.SellingPrice, p.StockQuantity,
        p.MinimumStock, p.CategoryId, p.SupplierId, p.IsActive, p.Unit);
}
