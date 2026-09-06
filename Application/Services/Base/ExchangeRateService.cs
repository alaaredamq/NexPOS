using Application.IServices.Base;
using Core.Entities.Sales;
using Core.IRepositories.Sales;

namespace Application.Services.Base;

public class ExchangeRateService : IExchangeRateService
{
    private readonly IExchangeRateRepository _repo;

    public ExchangeRateService(IExchangeRateRepository repo)
    {
        _repo = repo;
    }

    public async Task<List<ExchangeRate>> GetAllAsync(int? supplierId = null)
    {
        if (supplierId.HasValue)
            return await _repo.GetAllAsync(x => x.SupplierId == supplierId);
        return await _repo.GetAllAsync();
    }

    public async Task<ExchangeRate> GetByIdAsync(int id)
    {
        return await _repo.GetByIdAsync(id);
    }

    public async Task<ExchangeRate> CreateAsync(ExchangeRate rate)
    {
        // Optional: enforce uniqueness handled by DB unique index
        return await _repo.SaveAsync(rate);
    }

    public async Task UpdateAsync(int id, ExchangeRate rate)
    {
        var existing = await _repo.GetByIdAsync(id);
        existing.FromCurrencyId = rate.FromCurrencyId;
        existing.ToCurrencyId = rate.ToCurrencyId;
        existing.Rate = rate.Rate;
        existing.SupplierId = rate.SupplierId;
        await _repo.SaveAsync(existing);
    }

    public async Task DeleteAsync(int id)
    {
        var existing = await _repo.GetByIdAsync(id);
        await _repo.DeleteAsync(existing);
    }
}


