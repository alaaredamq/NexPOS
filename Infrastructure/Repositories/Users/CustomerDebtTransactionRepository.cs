using Core.Entities.Users;
using Core.IRepositories.Users;
using Infrastructure.Data;
using Infrastructure.Repositories.Base;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories.Users;

public class CustomerDebtTransactionRepository : BaseRepository<CustomerDebtTransaction, int>, ICustomerDebtTransactionRepository
{
    public CustomerDebtTransactionRepository(ErpDbContext context, IServiceProvider serviceProvider)
        : base(context, serviceProvider)
    {
    }

    public async Task<List<CustomerDebtTransaction>> GetByCustomerIdAsync(int customerId)
    {
        return await _Entities
            .Include(t => t.User)
            .Where(t => t.CustomerId == customerId)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync();
    }

    public async Task<List<CustomerDebtTransaction>> GetByOrderIdAsync(int orderId)
    {
        return await _Entities
            .Where(t => t.OrderId == orderId)
            .ToListAsync();
    }
}
