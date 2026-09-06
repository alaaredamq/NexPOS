using Application.IServices.Base;
using Core.Entities.Sales;

namespace Application.IServices.Sales;

public interface ITaxService : IBaseService
{
    Task<List<Tax>> GetAllAsync();
    Task<List<Tax>> GetBySupplierIdAsync(int? supplierId);
    Task<Tax?> GetDefaultBySupplierIdAsync(int? supplierId);
    Task<Tax> GetByIdAsync(int id);
    Task<Tax> CreateAsync(Tax tax);
    Task UpdateAsync(int id, Tax tax);
    Task DeleteAsync(int id);
}
