using Core.Entities.Sales;
using Core.IRepositories.Base;

namespace Core.IRepositories.Sales;

public interface IDiscountRepository : IRepository<Discount>
{
    Task<List<Discount>> GetBySupplierIdAsync(int? supplierId);
    Task<Discount?> GetDefaultBySupplierIdAsync(int? supplierId);
}
