using Core.Entities.Inventory;
using Core.IRepositories.Base;

namespace Core.IRepositories.Inventory;

public interface ICartSessionRepository : IBaseRepository<CartSession, int>
{
    Task<CartSession?> GetBySessionIdAsync(string sessionId);
    Task<CartSession?> GetActiveBySessionIdAsync(string sessionId);
    Task<List<CartSession>> GetByCustomerIdAsync(int customerId);
    Task<CartSession> CreateOrGetAsync(string sessionId, int? customerId = null);
}
