using Core.Entities.Users;
using Core.IRepositories.Base;

namespace Core.IRepositories.Users;

public interface IUserSessionRepository : IRepository<UserSession>
{
    Task<UserSession?> GetBySessionTokenAsync(string sessionToken);
    Task<List<UserSession>> GetByUserIdAsync(int userId);
    Task<List<UserSession>> GetActiveSessionsAsync();
    Task CleanupExpiredSessionsAsync();
}
