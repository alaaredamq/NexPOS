using Core.Entities.Users;
using Core.IRepositories.Users;
using Infrastructure.Data;
using Infrastructure.Repositories.Classes.Base;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories.Users;

public class CustomerRepository : Repository<Customer>, ICustomerRepository
{
    public CustomerRepository(ErpDbContext dbContext, IServiceProvider serviceProvider)
        : base(dbContext, serviceProvider)
    {
    }

    public new async Task<Customer> GetByIdAsync(int id)
    {
        var customer = await GetAllQueryable()
            .Include(c => c.Orders)
                .ThenInclude(o => o.Currency)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (customer == null)
            throw new ArgumentNullException($"Customer with id {id} not found");

        return customer;
    }
}


