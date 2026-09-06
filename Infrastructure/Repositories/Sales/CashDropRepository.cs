using Core.Entities.Sales;
using Core.IRepositories.Sales;
using Infrastructure.Data;
using Infrastructure.Repositories.Base;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories.Sales;

public class CashDropRepository : BaseRepository<CashDrop, int>, ICashDropRepository
{
    private readonly ErpDbContext _context;

    public CashDropRepository(ErpDbContext context, IServiceProvider serviceProvider) 
        : base(context, serviceProvider)
    {
        _context = context;
    }

    public async Task<List<CashDrop>> GetBySessionIdAsync(int sessionId)
    {
        return await _Entities
            .Include(cd => cd.User)
            .Where(cd => cd.CashDrawerSessionId == sessionId)
            .OrderByDescending(cd => cd.DroppedAt)
            .ToListAsync();
    }

    public async Task<decimal> GetTotalDropsBySessionIdAsync(int sessionId)
    {
        return await _Entities
            .Where(cd => cd.CashDrawerSessionId == sessionId)
            .SumAsync(cd => cd.Amount);
    }
}

