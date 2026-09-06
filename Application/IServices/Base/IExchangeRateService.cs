using Core.Entities.Sales;

namespace Application.IServices.Base;

public interface IExchangeRateService : IBaseService
{
    Task<List<ExchangeRate>> GetAllAsync(int? supplierId = null);
    Task<ExchangeRate> GetByIdAsync(int id);
    Task<ExchangeRate> CreateAsync(ExchangeRate rate);
    Task UpdateAsync(int id, ExchangeRate rate);
    Task DeleteAsync(int id);
}


