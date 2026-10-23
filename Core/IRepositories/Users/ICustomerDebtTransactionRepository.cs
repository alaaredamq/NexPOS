using Core.Entities.Users;
using Core.IRepositories.Base;

namespace Core.IRepositories.Users;

public interface ICustomerDebtTransactionRepository : IBaseRepository<CustomerDebtTransaction, int>
{
    Task<List<CustomerDebtTransaction>> GetByCustomerIdAsync(int customerId);
}
