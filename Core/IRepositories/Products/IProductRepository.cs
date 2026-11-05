using Core.DTOs;
using Core.Entities.Products;
using Core.ICache;
using Core.IRepositories.Base;

namespace Core.IRepositories.Products;

public interface IProductRepository : IRepository<Product>
{
    public List<ProductDTO>? GetProducts();
    Task<Product?> GetByBarcodeAsync(string barcode);
    Task<List<Product>> SearchAsync(string? term, int? categoryId, int? supplierId, bool onlyActive = true);
    Task<List<Product>> GetActiveProductsAsync();

    /// <summary>
    /// Active products that are not yet on the POS display order, filtered by name/barcode and paged,
    /// so the "add product" picker never has to load the whole catalog.
    /// </summary>
    Task<List<Product>> SearchAvailableForDisplayOrderAsync(string? term, int skip, int take);

    /// <summary>
    /// Atomically checks and decrements stock in a single database UPDATE (WHERE StockQuantity >= quantity),
    /// so two concurrent sales of the same product can't both pass a stale in-memory stock check.
    /// Returns false if the product doesn't exist or has insufficient stock.
    /// </summary>
    Task<bool> TryDeductStockAsync(int productId, decimal quantity);

    /// <summary>
    /// Decrements stock without requiring enough on hand, flooring at zero. Used when the cashier
    /// explicitly overrides an insufficient-stock warning (the stock count was wrong or the item is
    /// physically there anyway).
    /// </summary>
    Task<bool> ForceDeductStockAsync(int productId, decimal quantity);

    /// <summary>
    /// Atomically adds stock back (single UPDATE) - the reverse of a sale's deduction, used when a
    /// sale is voided or its order cancelled. Applies to soft-deleted products too, so the count is
    /// right if the product is ever brought back.
    /// </summary>
    Task<bool> RestoreStockAsync(int productId, decimal quantity);

    /// <summary>
    /// Inserts <paramref name="added"/> and saves every other change already made to tracked products,
    /// all in one SaveChanges (one transaction), so a bulk import is all-or-nothing.
    /// </summary>
    Task SaveRangeAsync(IEnumerable<Product> added);

    /// <summary>The stock figure as the database holds it right now (null when there is no such product).</summary>
    Task<decimal?> GetStockQuantityAsync(int productId);

    /// <summary>The fields of a product as the database holds them right now (null when there is no such product). Reads the row, not a tracked copy.</summary>
    Task<ProductSnapshot?> GetSnapshotAsync(int productId);
}