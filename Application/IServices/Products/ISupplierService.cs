using Core.Entities.Products;
using Application.IServices.Base;

namespace Application.IServices.Products;

public interface ISupplierService : IBaseService
{
    Task<List<Supplier>> GetAllAsync();
    Task<Supplier> GetByIdAsync(int id);
    Task<Supplier> CreateAsync(Supplier supplier);
    Task UpdateAsync(int id, Supplier supplier);
    Task DeleteAsync(int id);
}


