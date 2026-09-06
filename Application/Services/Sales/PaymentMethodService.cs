using Application.IServices.Sales;
using Core.Entities.Sales;
using Core.ICache;
using Core.IRepositories.Sales;

namespace Application.Services.Sales;

public class PaymentMethodService : IPaymentMethodService
{
    private readonly IPaymentMethodRepository _repo;
    private readonly ICacheService _cache;

    public PaymentMethodService(IPaymentMethodRepository repo, ICacheService cache)
    {
        _repo = repo;
        _cache = cache;
    }

    public async Task<List<PaymentMethod>> GetAllAsync() =>
        await _cache.CreateOrGetAsync(CacheKeys.PaymentMethods, () => _repo.GetAllAsync(), CacheKeys.LookupTtlMinutes)
        ?? new List<PaymentMethod>();

    public async Task<List<PaymentMethod>> GetBySupplierIdAsync(int? supplierId) => await _repo.GetBySupplierIdAsync(supplierId);
    public async Task<PaymentMethod?> GetDefaultBySupplierIdAsync(int? supplierId) => await _repo.GetDefaultBySupplierIdAsync(supplierId);
    public async Task<PaymentMethod> GetByIdAsync(int id) => await _repo.GetByIdAsync(id);

    public async Task<PaymentMethod> CreateAsync(PaymentMethod paymentMethod)
    {
        var created = await _repo.SaveAsync(paymentMethod);
        _cache.Remove(CacheKeys.PaymentMethods);
        return created;
    }

    public async Task UpdateAsync(int id, PaymentMethod paymentMethod)
    {
        var existing = await _repo.GetByIdAsync(id);
        existing.Name = paymentMethod.Name;
        existing.IsDefault = paymentMethod.IsDefault;
        existing.SupplierId = paymentMethod.SupplierId;
        await _repo.SaveAsync(existing);
        _cache.Remove(CacheKeys.PaymentMethods);
    }

    public async Task DeleteAsync(int id)
    {
        var existing = await _repo.GetByIdAsync(id);
        await _repo.DeleteAsync(existing);
        _cache.Remove(CacheKeys.PaymentMethods);
    }
}
