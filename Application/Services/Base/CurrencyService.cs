using Application.IServices.Base;
using Core.Entities.Sales;
using Core.IRepositories.Sales;

namespace Application.Services.Base;

public class CurrencyService : ICurrencyService
{
    private readonly ICurrencyRepository _repo;

    public CurrencyService(ICurrencyRepository repo)
    {
        _repo = repo;
    }

    public async Task<List<Currency>> GetAllAsync()
    {
        return await _repo.GetAllAsync();
    }

    public async Task<Currency> GetByIdAsync(int id)
    {
        return await _repo.GetByIdAsync(id);
    }

    public async Task<Currency> CreateAsync(Currency currency, bool setAsDefault)
    {
        if (setAsDefault)
        {
            var all = await _repo.GetAllAsync();
            var currentDefault = all.FirstOrDefault(c => c.IsDefault);
            if (currentDefault != null)
            {
                currentDefault.IsDefault = false;
                await _repo.SaveAsync(currentDefault);
            }
            currency.IsDefault = true;
        }
        return await _repo.SaveAsync(currency);
    }

    public async Task UpdateAsync(int id, Currency currency, bool setAsDefault)
    {
        var existing = await _repo.GetByIdAsync(id);
        existing.Name = currency.Name;
        existing.RateToUSD = currency.RateToUSD;
        existing.IsActive = currency.IsActive;
        existing.Code = currency.Code;
        existing.Notation = currency.Notation;

        if (setAsDefault && !existing.IsDefault)
        {
            var all = await _repo.GetAllAsync();
            var currentDefault = all.FirstOrDefault(c => c.IsDefault && c.Id != id);
            if (currentDefault != null)
            {
                currentDefault.IsDefault = false;
                await _repo.SaveAsync(currentDefault);
            }
            existing.IsDefault = true;
        }
        else if (!setAsDefault && existing.IsDefault)
        {
            throw new InvalidOperationException("Cannot unset default currency without setting another default.");
        }

        await _repo.SaveAsync(existing);
    }

    public async Task DeleteAsync(int id)
    {
        var existing = await _repo.GetByIdAsync(id);
        if (existing.IsDefault) throw new InvalidOperationException("Cannot delete the default currency.");
        await _repo.DeleteAsync(existing);
    }

    public async Task<Currency?> GetDefaultAsync()
    {
        var all = await _repo.GetAllAsync();
        return all.FirstOrDefault(c => c.IsDefault) ?? all.OrderBy(c => c.Id).FirstOrDefault();
    }

    public async Task SetDefaultAsync(int id)
    {
        var all = await _repo.GetAllAsync();
        var toSet = all.FirstOrDefault(c => c.Id == id) ?? throw new ArgumentNullException("currency not found");
        foreach (var c in all)
        {
            c.IsDefault = c.Id == id;
            await _repo.SaveAsync(c);
        }
    }
}


