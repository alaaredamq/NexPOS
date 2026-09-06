using Core.Entities.Sales;
using Core.IRepositories.Base;

namespace Core.IRepositories.Sales;

public interface ICashDrawerSessionRepository : IBaseRepository<CashDrawerSession, int>
{
    Task<CashDrawerSession?> GetOpenSessionByUserIdAsync(int userId);

    /// <summary>
    /// Returns just the id of the user's open session (or null). Unlike
    /// <see cref="GetOpenSessionByUserIdAsync"/> this does not load the session's Orders/CashDrops,
    /// so it stays cheap even late in a busy shift - used on the checkout hot path.
    /// </summary>
    Task<int?> GetOpenSessionIdByUserIdAsync(int userId);

    Task<List<CashDrawerSession>> GetSessionsByUserIdAsync(int userId, DateTime? startDate = null, DateTime? endDate = null);
    Task<CashDrawerSession?> GetSessionWithDetailsAsync(int sessionId);
    Task<List<CashDrawerSession>> GetAllOpenSessionsAsync();
    Task<bool> HasOpenSessionAsync(int userId);
}

