using Application.IServices.Base;
using Core.Entities.Users;

namespace Application.IServices.Users;

public interface ICustomerService : IBaseService
{
    Task<List<Customer>> GetAllAsync();
    Task<List<Customer>> GetAllWithOrdersAsync();
    Task<(List<Customer> Items, bool HasMore)> SearchPagedAsync(string? search, int skip, int take);
    Task<Customer> GetByIdAsync(int id);
    Task<Customer> CreateAsync(Customer customer);
    Task UpdateAsync(int id, Customer customer);
    Task DeleteAsync(int id);
}


