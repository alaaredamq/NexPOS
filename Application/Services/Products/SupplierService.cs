using Application.IServices.Products;
using Core.Entities.Products;
using Core.IRepositories.Products;

namespace Application.Services.Products;

public class SupplierService : ISupplierService
{
    private readonly ISupplierRepository _repo;

    public SupplierService(ISupplierRepository repo)
    {
        _repo = repo;
    }

    public async Task<List<Supplier>> GetAllAsync()
    {
        return (await _repo.GetAllAsync()).OrderBy(x => x.OrderNumber).ToList();
    }

    public async Task<Supplier> GetByIdAsync(int id)
    {
        return await _repo.GetByIdAsync(id);
    }

    public async Task<Supplier> CreateAsync(Supplier supplier)
    {
        return await _repo.SaveAsync(supplier);
    }

    public async Task UpdateAsync(int id, Supplier supplier)
    {
        var existing = await _repo.GetByIdAsync(id);
        existing.Name = supplier.Name;
        existing.Email = supplier.Email;
        existing.Phone = supplier.Phone;
        existing.Address = supplier.Address;
        existing.City = supplier.City;
        existing.Country = supplier.Country;
        existing.PostalCode = supplier.PostalCode;
        existing.IsActive = supplier.IsActive;
        existing.CurrencyId = supplier.CurrencyId;
        existing.DiscountRate = supplier.DiscountRate;
        await _repo.SaveAsync(existing);
    }

    public async Task DeleteAsync(int id)
    {
        var existing = await _repo.GetByIdAsync(id);
        await _repo.DeleteAsync(existing);
    }
}


