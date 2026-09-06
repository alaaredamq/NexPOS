using Core.Entities.Users;
using Core.IRepositories.Users;
using Infrastructure.Data;
using Infrastructure.Repositories.Classes.Base;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories.Users;

public class UserSessionRepository : Repository<UserSession>, IUserSessionRepository
{
    public UserSessionRepository(ErpDbContext dbContext, IServiceProvider serviceProvider)
        : base(dbContext, serviceProvider)
    {
    }

    public async Task<UserSession?> GetBySessionTokenAsync(string sessionToken)
    {
        return await GetAllQueryable()
            .Include(s => s.User)
            .ThenInclude(u => u.Role)
            .FirstOrDefaultAsync(s => s.SessionToken == sessionToken && s.IsActive);
    }

    public async Task<List<UserSession>> GetByUserIdAsync(int userId)
    {
        return await GetAllQueryable()
            .Where(s => s.UserId == userId && s.IsActive)
            .ToListAsync();
    }

    public async Task<List<UserSession>> GetActiveSessionsAsync()
    {
        // Note: IsExpired() cannot be translated to SQL, so the expiry check is expressed
        // directly against ExpiresAt here.
        var now = DateTime.UtcNow;
        return await GetAllQueryable()
            .Include(s => s.User)
            .Where(s => s.IsActive && s.ExpiresAt > now)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync();
    }

    public async Task CleanupExpiredSessionsAsync()
    {
        var now = DateTime.UtcNow;
        var expiredSessions = await GetAllQueryable()
            .Where(s => s.ExpiresAt <= now)
            .ToListAsync();

        foreach (var session in expiredSessions)
        {
            session.Deactivate();
            await SaveAsync(session);
        }
    }
}
