using Core.Entities.Sales;

namespace Application.IServices.Base;

public interface ICurrencyService : IBaseService
{
    Task<List<Currency>> GetAllAsync();
    Task<Currency> GetByIdAsync(int id);
    Task<Currency> CreateAsync(Currency currency, bool setAsDefault);
    Task UpdateAsync(int id, Currency currency, bool setAsDefault);
    Task DeleteAsync(int id);
    Task<Currency?> GetDefaultAsync();
    Task SetDefaultAsync(int id);
}


