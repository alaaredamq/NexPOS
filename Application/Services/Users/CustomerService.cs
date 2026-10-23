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
        await _repo.DeleteAsync(existing);
    }
}


