using Core.Entities.Products;
using Core.IRepositories;
using Infrastructure.Data;
using Infrastructure.Repositories.Classes.Base;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories
{
    public class ProductDisplayOrderRepository : Repository<ProductDisplayOrder>, IProductDisplayOrderRepository
    {
        private readonly ErpDbContext _context;
        
        public ProductDisplayOrderRepository(ErpDbContext context, IServiceProvider serviceProvider) : base(context, serviceProvider)
        {
            _context = context;
        }

        public async Task<IEnumerable<ProductDisplayOrder>> GetActiveDisplayOrderAsync()
        {
            return await _Entities
                .Where(pdo => pdo.IsActive)
                .Include(pdo => pdo.Product)
                    .ThenInclude(p => p.Category)
                .Include(pdo => pdo.Product)
                    .ThenInclude(p => p.Currency)
                .Include(pdo => pdo.Product)
                    .ThenInclude(p => p.Supplier)
                .OrderBy(pdo => pdo.DisplayOrder)
                .ToListAsync();
        }

        public async Task UpdateDisplayOrderAsync(IEnumerable<ProductDisplayOrder> displayOrders)
        {
            // Clear change tracker to prevent circular dependencies
            _context.ChangeTracker.Clear();

            // Get IDs to update
            var displayOrdersList = displayOrders.ToList();
            var ids = displayOrdersList.Select(d => d.Id).ToList();
            
            // Fetch existing entities without navigation properties (AsNoTracking to avoid tracking issues)
            var existingOrders = await _Entities
                .Where(pdo => ids.Contains(pdo.Id))
                .AsNoTracking()
                .ToListAsync();
            
            // Create a dictionary for quick lookup
            var orderLookup = displayOrdersList.ToDictionary(d => d.Id);
            
            // Update each entity
            foreach (var existing in existingOrders)
            {
                if (orderLookup.TryGetValue(existing.Id, out var updated))
                {
                    existing.DisplayOrder = updated.DisplayOrder;
                    existing.IsActive = updated.IsActive;
                    existing.UpdatedAt = DateTime.UtcNow;
                    
                    // Attach and mark as modified
                    _Entities.Attach(existing);
                    _context.Entry(existing).State = EntityState.Modified;
                }
            }
            
            await SaveChangesAsync();
        }
    }
}
