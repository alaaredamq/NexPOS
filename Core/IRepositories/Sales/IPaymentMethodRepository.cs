using Core.Entities.Sales;
using Core.IRepositories.Base;

namespace Core.IRepositories.Sales;

public interface IPaymentMethodRepository : IRepository<PaymentMethod>
{
    Task<List<PaymentMethod>> GetBySupplierIdAsync(int? supplierId);
    Task<PaymentMethod?> GetDefaultBySupplierIdAsync(int? supplierId);
}
