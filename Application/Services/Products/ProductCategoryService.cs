using Application.IServices.Products;
using Core.Entities.Products;
using Core.ICache;
using Core.IRepositories.Products;
using Microsoft.EntityFrameworkCore;

namespace Application.Services.Products;

public class ProductCategoryService : IProductCategoryService
{
    private readonly IProductCategoryRepository _repo;
    private readonly ICacheService _cache;

    public ProductCategoryService(IProductCategoryRepository repo, ICacheService cache)
    {
        _repo = repo;
        _cache = cache;
    }

    public async Task<List<ProductCategory>> GetAllAsync()
    {
        return await _cache.CreateOrGetAsync(
            CacheKeys.Categories,
            () => _repo.GetAllAsync(),
            CacheKeys.LookupTtlMinutes) ?? new List<ProductCategory>();
    }

    public async Task<ProductCategory> GetByIdAsync(int id)
    {
        return await _repo.GetByIdAsync(id);
    }

    public async Task<ProductCategory?> GetByIdWithProductsAsync(int id)
    {
        return await _repo.GetByIdWithProductsAsync(id);
    }

    // Not cached: product counts change whenever a product is added/removed, and the category cache is only cleared on category edits.
    public async Task<Dictionary<int, int>> GetProductCountsAsync()
    {
        return await _repo.GetProductCountsAsync();
    }

    public async Task<ProductCategory> CreateAsync(ProductCategory category)
    {
        var created = await _repo.SaveAsync(category);
        _cache.Remove(CacheKeys.Categories);
        return created;
    }

    public async Task UpdateAsync(int id, ProductCategory category)
    {
        var existing = await _repo.GetByIdAsync(id);
        existing.Name = category.Name;
        existing.Description = category.Description;
        existing.IsActive = category.IsActive;
        await _repo.SaveAsync(existing);
        _cache.Remove(CacheKeys.Categories);
    }

    public async Task DeleteAsync(int id)
    {
        var existing = await _repo.GetByIdAsync(id);
        try
        {
            await _repo.DeleteAsync(existing);
        }
        catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException)
        {
            // Products (including soft-deleted ones kept for sales history) still point at this category.
            throw new InvalidOperationException(
                "This category is still used by products, including deleted ones kept for sales history, so it can't be removed. Deactivate it instead.", ex);
        }
        _cache.Remove(CacheKeys.Categories);
    }
}
