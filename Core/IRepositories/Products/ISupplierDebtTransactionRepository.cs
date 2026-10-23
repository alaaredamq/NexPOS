using Core.Entities.Products;
using Core.IRepositories.Base;

namespace Core.IRepositories.Products;

public interface ISupplierDebtTransactionRepository : IBaseRepository<SupplierDebtTransaction, int>
{
    Task<List<SupplierDebtTransaction>> GetBySupplierIdAsync(int supplierId);
}
