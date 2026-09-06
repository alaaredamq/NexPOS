using Application.IServices.Sales;
using Core.Entities.Sales;
using Core.ICache;
using Core.IRepositories.Sales;

namespace Application.Services.Sales;

public class TaxService : ITaxService
{
    private readonly ITaxRepository _repo;
    private readonly ICacheService _cache;

    public TaxService(ITaxRepository repo, ICacheService cache)
    {
        _repo = repo;
        _cache = cache;
    }

    public async Task<List<Tax>> GetAllAsync() =>
        await _cache.CreateOrGetAsync(CacheKeys.SalesTaxes, () => _repo.GetAllAsync(), CacheKeys.LookupTtlMinutes)
        ?? new List<Tax>();

    public async Task<List<Tax>> GetBySupplierIdAsync(int? supplierId) => await _repo.GetBySupplierIdAsync(supplierId);
    public async Task<Tax?> GetDefaultBySupplierIdAsync(int? supplierId) => await _repo.GetDefaultBySupplierIdAsync(supplierId);
    public async Task<Tax> GetByIdAsync(int id) => await _repo.GetByIdAsync(id);

    public async Task<Tax> CreateAsync(Tax tax)
    {
        var created = await _repo.SaveAsync(tax);
        _cache.Remove(CacheKeys.SalesTaxes);
        return created;
    }

    public async Task UpdateAsync(int id, Tax tax)
    {
        var existing = await _repo.GetByIdAsync(id);
        existing.Name = tax.Name;
        existing.Rate = tax.Rate;
        existing.IsDefault = tax.IsDefault;
        existing.SupplierId = tax.SupplierId;
        await _repo.SaveAsync(existing);
        _cache.Remove(CacheKeys.SalesTaxes);
    }

    public async Task DeleteAsync(int id)
    {
        var existing = await _repo.GetByIdAsync(id);
        await _repo.DeleteAsync(existing);
        _cache.Remove(CacheKeys.SalesTaxes);
    }
}
