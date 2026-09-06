using Core.Entities.Inventory;
using Core.IRepositories.Inventory;
using Infrastructure.Data;
using Infrastructure.Repositories.Base;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure.Repositories.Inventory;

public class CartSessionRepository : BaseRepository<CartSession, int>, ICartSessionRepository
{
    public CartSessionRepository(ErpDbContext context, IServiceProvider serviceProvider) 
        : base(context, serviceProvider)
    {
    }

    public async Task<CartSession?> GetBySessionIdAsync(string sessionId)
    {
        return await _Entities
            .Include(cs => cs.CartItems)
            .ThenInclude(ci => ci.Product)
            .Include(cs => cs.Customer)
            .FirstOrDefaultAsync(cs => cs.SessionId == sessionId);
    }

    public async Task<CartSession?> GetActiveBySessionIdAsync(string sessionId)
    {
        return await _Entities
            .Include(cs => cs.CartItems)
            .ThenInclude(ci => ci.Product)
            .Include(cs => cs.Customer)
            .FirstOrDefaultAsync(cs => cs.SessionId == sessionId && cs.IsActive);
    }

    public async Task<List<CartSession>> GetByCustomerIdAsync(int customerId)
    {
        return await _Entities
            .Include(cs => cs.CartItems)
            .ThenInclude(ci => ci.Product)
            .Where(cs => cs.CustomerId == customerId)
            .ToListAsync();
    }

    public async Task<CartSession> CreateOrGetAsync(string sessionId, int? customerId = null)
    {
        var existingCart = await GetActiveBySessionIdAsync(sessionId);
        if (existingCart != null)
        {
            return existingCart;
        }

        var newCart = new CartSession(sessionId, customerId);
        return await SaveAsync(newCart);
    }
}
