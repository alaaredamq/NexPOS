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
    /// Atomically checks and decrements stock in a single database UPDATE (WHERE StockQuantity >= quantity),
    /// so two concurrent sales of the same product can't both pass a stale in-memory stock check.
    /// Returns false if the product doesn't exist or has insufficient stock.
    /// </summary>
    Task<bool> TryDeductStockAsync(int productId, decimal quantity);
}