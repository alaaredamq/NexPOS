using Application.IServices.Sales;
using Core.Entities.Sales;
using Core.ICache;
using Core.IRepositories.Sales;

namespace Application.Services.Sales;

public class DiscountService : IDiscountService
{
    private readonly IDiscountRepository _repo;
    private readonly ICacheService _cache;

    public DiscountService(IDiscountRepository repo, ICacheService cache)
    {
        _repo = repo;
        _cache = cache;
    }

    public async Task<List<Discount>> GetAllAsync() =>
        await _cache.CreateOrGetAsync(CacheKeys.Discounts, () => _repo.GetAllAsync(), CacheKeys.LookupTtlMinutes)
        ?? new List<Discount>();

    public async Task<List<Discount>> GetBySupplierIdAsync(int? supplierId) => await _repo.GetBySupplierIdAsync(supplierId);
    public async Task<Discount?> GetDefaultBySupplierIdAsync(int? supplierId) => await _repo.GetDefaultBySupplierIdAsync(supplierId);
    public async Task<Discount> GetByIdAsync(int id) => await _repo.GetByIdAsync(id);

    public async Task<Discount> CreateAsync(Discount discount)
    {
        var created = await _repo.SaveAsync(discount);
        _cache.Remove(CacheKeys.Discounts);
        return created;
    }

    public async Task UpdateAsync(int id, Discount discount)
    {
        var existing = await _repo.GetByIdAsync(id);
        existing.Name = discount.Name;
        existing.Rate = discount.Rate;
        existing.IsDefault = discount.IsDefault;
        existing.SupplierId = discount.SupplierId;
        await _repo.SaveAsync(existing);
        _cache.Remove(CacheKeys.Discounts);
    }

    public async Task DeleteAsync(int id)
    {
        var existing = await _repo.GetByIdAsync(id);
        await _repo.DeleteAsync(existing);
        _cache.Remove(CacheKeys.Discounts);
    }
}
