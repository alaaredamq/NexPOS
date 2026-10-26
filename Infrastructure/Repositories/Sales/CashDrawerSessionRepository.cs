using Core.Entities.Sales;
using Core.IRepositories.Sales;
using Infrastructure.Data;
using Infrastructure.Repositories.Base;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories.Sales;

public class CashDrawerSessionRepository : BaseRepository<CashDrawerSession, int>, ICashDrawerSessionRepository
{
    private readonly ErpDbContext _context;

    public CashDrawerSessionRepository(ErpDbContext context, IServiceProvider serviceProvider) 
        : base(context, serviceProvider)
    {
        _context = context;
    }

    public async Task<CashDrawerSession?> GetOpenSessionByUserIdAsync(int userId)
    {
        return await _Entities
            .Include(s => s.User)
            .Include(s => s.CashDrops)
            .Include(s => s.Orders)
            .AsSplitQuery()
            .FirstOrDefaultAsync(s => s.UserId == userId && s.IsOpen);
    }

    public async Task<int?> GetOpenSessionIdByUserIdAsync(int userId)
    {
        return await _Entities
            .Where(s => s.UserId == userId && s.IsOpen)
            .Select(s => (int?)s.Id)
            .FirstOrDefaultAsync();
    }

    public async Task<List<CashDrawerSession>> GetSessionsByUserIdAsync(int userId, DateTime? startDate = null, DateTime? endDate = null)
    {
        var query = _Entities
            .Include(s => s.User)
            .Include(s => s.CashDrops)
            .Where(s => s.UserId == userId);

        if (startDate.HasValue)
        {
            query = query.Where(s => s.OpenedAt >= startDate.Value);
        }

        if (endDate.HasValue)
        {
            query = query.Where(s => s.OpenedAt <= endDate.Value);
        }

        return await query
            .OrderByDescending(s => s.OpenedAt)
            .ToListAsync();
    }

    public async Task<CashDrawerSession?> GetSessionWithDetailsAsync(int sessionId)
    {
        return await _Entities
            .Include(s => s.User)
            .Include(s => s.CashDrops)
                .ThenInclude(cd => cd.User)
            .Include(s => s.Orders)
                .ThenInclude(o => o.Cart)
                    .ThenInclude(c => c.CartItems)
            .Include(s => s.Orders)
                .ThenInclude(o => o.Customer)
            .AsSplitQuery()
            .FirstOrDefaultAsync(s => s.Id == sessionId);
    }

    public async Task<List<CashDrawerSession>> GetAllOpenSessionsAsync()
    {
        return await _Entities
            .Include(s => s.User)
            .Where(s => s.IsOpen)
            .OrderBy(s => s.OpenedAt)
            .ToListAsync();
    }

    public async Task<bool> HasOpenSessionAsync(int userId)
    {
        return await _Entities.AnyAsync(s => s.UserId == userId && s.IsOpen);
    }
}

