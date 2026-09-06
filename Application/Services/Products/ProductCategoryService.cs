using Application.IServices.Products;
using Core.Entities.Products;
using Core.ICache;
using Core.IRepositories.Products;

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
        await _repo.DeleteAsync(existing);
        _cache.Remove(CacheKeys.Categories);
    }
}
