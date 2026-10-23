using Application.IServices.Base;
using Application.IServices.Products;
using Core.DTOs;
using Core.Entities.Products;
using Core.ICache;
using Core.IRepositories.Products;

namespace Application.Services.Products;

public class ProductService : IProductService
{
    private readonly IProductRepository _repo;
    private readonly ICurrencyConversionService _currencyService;
    private readonly ICacheService _cache;

    public ProductService(IProductRepository repo, ICurrencyConversionService currencyService, ICacheService cache)
    {
        _repo = repo;
        _currencyService = currencyService;
        _cache = cache;
    }

    public async Task<List<Product>> GetAllAsync()
    {
        return await _repo.GetAllAsync();
    }

    public async Task<Product> GetByIdAsync(int id)
    {
        return await _repo.GetByIdAsync(id);
    }

    public async Task<Product?> GetByBarcodeAsync(string barcode)
    {
        return await _repo.GetByBarcodeAsync(barcode);
    }

    public async Task<List<Product>> SearchAsync(string? term, int? categoryId, int? supplierId, bool onlyActive = true)
    {
        return await _repo.SearchAsync(term, categoryId, supplierId, onlyActive);
    }

    public async Task<Product> CreateAsync(Product product)
    {
        var created = await _repo.SaveAsync(product);
        await _cache.ClearPOSCachesAsync();
        return created;
    }

    public async Task UpdateAsync(int id, Product product)
    {
        var existing = await _repo.GetByIdAsync(id);
        existing.Name = product.Name;
        existing.Description = product.Description;
        existing.Barcode = product.Barcode;
        existing.CostPrice = product.CostPrice;
        existing.SellingPrice = product.SellingPrice;
        existing.StockQuantity = product.StockQuantity;
        existing.MinimumStock = product.MinimumStock;
        existing.BrandId = product.BrandId;
        existing.CategoryId = product.CategoryId;
        existing.SupplierId = product.SupplierId;
        existing.IsActive = product.IsActive;
        existing.Unit = product.Unit;
        existing.UpdatedAt = DateTime.UtcNow;
        await _repo.SaveAsync(existing);
        await _cache.ClearPOSCachesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        var product = await _repo.GetByIdAsync(id);
        if (product != null)
        {
            // Use soft delete instead of hard delete
            await _repo.SoftDeleteAsync(product);
            await _cache.ClearPOSCachesAsync();
        }
    }

    public List<ProductDTO>? GetCachedProducts()
    {
        // Implementation for cached products
        return null;
    }

    public async Task<decimal> GetProductPriceInCurrencyAsync(int productId, int targetCurrencyId)
    {
        var product = await _repo.GetByIdAsync(productId);
        return await _currencyService.ConvertProductPriceAsync(product.SellingPrice, productId, targetCurrencyId);
    }

    public async Task<List<Product>> GetProductsWithCurrencyConversionAsync(int targetCurrencyId)
    {
        var products = await _repo.GetAllAsync();
        var convertedProducts = new List<Product>();
        
        foreach (var product in products)
        {
            var convertedPrice = await _currencyService.ConvertProductPriceAsync(product.SellingPrice, product.Id, targetCurrencyId);
            var convertedCostPrice = await _currencyService.ConvertProductPriceAsync(product.CostPrice ?? 0, product.Id, targetCurrencyId);
            
            var convertedProduct = new Product
            {
                Id = product.Id,
                Name = product.Name,
                Description = product.Description,
                Barcode = product.Barcode,
                CostPrice = convertedCostPrice,
                SellingPrice = convertedPrice,
                SupplierId = product.SupplierId,
                CategoryId = product.CategoryId,
                BrandId = product.BrandId,
                IsActive = product.IsActive,
                Unit = product.Unit,
                CreatedAt = product.CreatedAt,
                UpdatedAt = product.UpdatedAt
            };
            
            convertedProducts.Add(convertedProduct);
        }
        
        return convertedProducts;
    }

    public async Task<Product> CreateProductWithCurrencyConversionAsync(Product product, int targetCurrencyId)
    {
        // Convert prices to system default currency before saving
        var systemDefaultCurrencyId = await _currencyService.GetSystemDefaultCurrencyIdAsync();
        var supplierCurrencyId = await _currencyService.GetSupplierDefaultCurrencyIdAsync(product.SupplierId);
        
        var convertedSellingPrice = await _currencyService.ConvertAmountAsync(product.SellingPrice, targetCurrencyId, supplierCurrencyId, product.SupplierId);
        decimal? convertedCostPrice = product.CostPrice.HasValue 
            ? await _currencyService.ConvertAmountAsync(product.CostPrice.Value, targetCurrencyId, supplierCurrencyId, product.SupplierId)
            : null;
        
        var productToSave = new Product
        {
            Name = product.Name,
            Description = product.Description,
            Barcode = product.Barcode,
            CostPrice = convertedCostPrice,
            SellingPrice = convertedSellingPrice,
            SupplierId = product.SupplierId,
            CategoryId = product.CategoryId,
            BrandId = product.BrandId,
            IsActive = product.IsActive,
            Unit = product.Unit
        };
        
        var created = await _repo.SaveAsync(productToSave);
        await _cache.ClearPOSCachesAsync();
        return created;
    }
}