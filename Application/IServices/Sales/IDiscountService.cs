using Application.IServices.Base;
using Core.Entities.Sales;

namespace Application.IServices.Sales;

public interface IDiscountService : IBaseService
{
    Task<List<Discount>> GetAllAsync();
    Task<List<Discount>> GetBySupplierIdAsync(int? supplierId);
    Task<Discount?> GetDefaultBySupplierIdAsync(int? supplierId);
    Task<Discount> GetByIdAsync(int id);
    Task<Discount> CreateAsync(Discount discount);
    Task UpdateAsync(int id, Discount discount);
    Task DeleteAsync(int id);
}
