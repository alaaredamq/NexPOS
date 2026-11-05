using Core.DTOs;
using Core.Entities.Products;
using Application.IServices.Base;

namespace Application.IServices.Products;

public interface IProductService : IBaseService
{
    Task<List<Product>> GetAllAsync();
    Task<Product> GetByIdAsync(int id);
    Task<Product?> GetByBarcodeAsync(string barcode);
    Task<List<Product>> SearchAsync(string? term, int? categoryId, int? supplierId, bool onlyActive = true);
    Task<List<Product>> SearchAvailableForDisplayOrderAsync(string? term, int skip, int take);
    Task<Product> CreateAsync(Product product);
    /// <summary>Saves the product. The change is recorded in its history; <paramref name="context"/> says how a changed stock figure should read there.</summary>
    Task UpdateAsync(int id, Product product, ProductChangeContext? context = null);
    Task DeleteAsync(int id);
    List<ProductDTO>? GetCachedProducts();
    Task<decimal> GetProductPriceInCurrencyAsync(int productId, int targetCurrencyId);
    Task<List<Product>> GetProductsWithCurrencyConversionAsync(int targetCurrencyId);
    Task<Product> CreateProductWithCurrencyConversionAsync(Product product, int targetCurrencyId);
}