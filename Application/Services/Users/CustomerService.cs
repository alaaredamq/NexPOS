using Application.IServices.Users;
using Core.Entities.Users;
using Core.IRepositories.Users;

namespace Application.Services.Users;

public class CustomerService : ICustomerService
{
    private readonly ICustomerRepository _repo;

    public CustomerService(ICustomerRepository repo)
    {
        _repo = repo;
    }

    public async Task<List<Customer>> GetAllAsync() => await _repo.GetAllAsync();
    public async Task<List<Customer>> GetAllWithOrdersAsync() => await _repo.GetAllWithOrdersAsync();
    public async Task<Customer> GetByIdAsync(int id) => await _repo.GetByIdAsync(id);
    public async Task<Customer> CreateAsync(Customer customer) => await _repo.SaveAsync(customer);
    public async Task UpdateAsync(int id, Customer customer)
    {
        var existing = await _repo.GetByIdAsync(id);
        existing.FirstName = customer.FirstName;
        existing.LastName = customer.LastName;
        existing.Email = customer.Email;
        existing.PhoneNumber = customer.PhoneNumber;
        existing.Address = customer.Address;
        existing.City = customer.City;
        existing.Country = customer.Country;
        existing.PostalCode = customer.PostalCode;
        existing.IsActive = customer.IsActive;
        await _repo.SaveAsync(existing);
    }
    public async Task DeleteAsync(int id)
    {
        var existing = await _repo.GetByIdAsync(id);

        // Deleted customers vanish from every list, so a debt (either direction) would vanish with them.
        if (existing.Balance != 0)
        {
            throw new InvalidOperationException(
                $"This customer has an outstanding balance of {Math.Abs(existing.Balance):N2}. Settle it before deleting, or deactivate the customer instead.");
        }

        // Soft delete: receipts, debt transactions and carts reference the customer with
        // Restrict FKs, so a hard delete fails as soon as the customer has any history.
        await _repo.SoftDeleteAsync(existing);
    }
}


