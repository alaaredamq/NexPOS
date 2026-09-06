using Core.Entities.Sales;
using Core.IRepositories.Base;

namespace Core.IRepositories.Sales;

public interface ITaxRepository : IRepository<Tax>
{
    Task<List<Tax>> GetBySupplierIdAsync(int? supplierId);
    Task<Tax?> GetDefaultBySupplierIdAsync(int? supplierId);
}
