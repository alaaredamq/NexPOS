using Core.Entities.Sales;
using Core.IRepositories.Base;

namespace Core.IRepositories.Sales;

public interface ICashDropRepository : IBaseRepository<CashDrop, int>
{
    Task<List<CashDrop>> GetBySessionIdAsync(int sessionId);
    Task<decimal> GetTotalDropsBySessionIdAsync(int sessionId);
}

