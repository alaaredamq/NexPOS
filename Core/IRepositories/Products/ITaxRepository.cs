using Core.Entities.Products;
using Core.IRepositories.Base;
using Core.Enum;

namespace Core.IRepositories.Products;

public interface ITaxRepository : IRepository<TaxRate>
{
    Task<TaxRate?> GetDefaultTaxAsync();
    Task<TaxRate?> GetActiveTaxAsync(TaxType type);
    Task<List<TaxRate>> GetActiveTaxesAsync();
    Task<List<TaxRate>> GetTaxesByTypeAsync(TaxType type);
    Task<List<TaxRate>> GetTaxesByCategoryAsync(int categoryId);
    Task<List<TaxRate>> GetEffectiveTaxesAsync(DateTime date);
    Task<List<TaxRate>> GetTaxesByEffectiveDateRangeAsync(DateTime startDate, DateTime endDate);
    Task<bool> SetDefaultTaxAsync(int taxId);
    Task<bool> DeactivateTaxAsync(int taxId);
    Task<List<TaxRate>> SearchTaxesAsync(string searchTerm);
}
