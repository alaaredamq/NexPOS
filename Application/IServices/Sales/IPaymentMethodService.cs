using Application.IServices.Base;
using Core.Entities.Sales;

namespace Application.IServices.Sales;

public interface IPaymentMethodService : IBaseService
{
    Task<List<PaymentMethod>> GetAllAsync();
    Task<List<PaymentMethod>> GetBySupplierIdAsync(int? supplierId);
    Task<PaymentMethod?> GetDefaultBySupplierIdAsync(int? supplierId);
    Task<PaymentMethod> GetByIdAsync(int id);
    Task<PaymentMethod> CreateAsync(PaymentMethod paymentMethod);
    Task UpdateAsync(int id, PaymentMethod paymentMethod);
    Task DeleteAsync(int id);
}
