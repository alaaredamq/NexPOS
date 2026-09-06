using Core.Entities.Products;
using Core.IRepositories.Products;
using Infrastructure.Data;
using Infrastructure.Repositories.Base;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories.Products;

public class MainScreenProductRepository : BaseRepository<MainScreenProduct, int>, IMainScreenProductRepository
{
    private readonly ErpDbContext _erpDbContext;

    public MainScreenProductRepository(ErpDbContext db, IServiceProvider serviceProvider) : base(db, serviceProvider)
    {
        _erpDbContext = db;
    }

    public async Task<IEnumerable<MainScreenProduct>> GetPinnedProductsAsync()
    {
        return await Table
            .Include(msp => msp.Product)
            .ThenInclude(p => p.Supplier)
            .Where(msp => msp.IsPinned)
            .OrderBy(msp => msp.DisplayOrder)
            .ToListAsync();
    }

    public async Task<IEnumerable<MainScreenProduct>> GetMainScreenProductsAsync()
    {
        return await Table
            .Include(msp => msp.Product)
            .ThenInclude(p => p.Supplier)
            .OrderBy(msp => msp.DisplayOrder)
            .ToListAsync();
    }

    public async Task<MainScreenProduct?> GetByProductIdAsync(int productId)
    {
        return await Table
            .Include(msp => msp.Product)
            .FirstOrDefaultAsync(msp => msp.ProductId == productId);
    }

    public async Task PinProductAsync(int productId, int displayOrder)
    {
        var existing = await GetByProductIdAsync(productId);
        if (existing != null)
        {
            existing.IsPinned = true;
            existing.DisplayOrder = displayOrder;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            var mainScreenProduct = new MainScreenProduct
            {
                ProductId = productId,
                DisplayOrder = displayOrder,
                IsPinned = true,
                CreatedAt = DateTime.UtcNow
            };
            await SaveAsync(mainScreenProduct);
        }
    }

    public async Task UnpinProductAsync(int productId)
    {
        var existing = await GetByProductIdAsync(productId);
        if (existing != null)
        {
            existing.IsPinned = false;
            existing.UpdatedAt = DateTime.UtcNow;
            await SaveChangesAsync();
        }
    }

    public async Task UpdateDisplayOrderAsync(int productId, int newOrder)
    {
        var existing = await GetByProductIdAsync(productId);
        if (existing != null)
        {
            existing.DisplayOrder = newOrder;
            existing.UpdatedAt = DateTime.UtcNow;
            await SaveChangesAsync();
        }
    }

    public async Task<IEnumerable<Product>> SearchProductsForMainScreenAsync(string searchTerm, int limit = 20)
    {
        var query = _erpDbContext.Products
            .Include(p => p.Supplier)
            .Include(p => p.Category)
            .Where(p => p.IsActive);

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var term = searchTerm.ToLower();
            query = query.Where(p => 
                p.Name.ToLower().Contains(term) ||
                p.Description.ToLower().Contains(term) ||
                p.Barcode.Contains(term) ||
                p.Supplier.Name.ToLower().Contains(term) ||
                (p.Category != null && p.Category.Name.ToLower().Contains(term))
            );
        }

        return await query
            .OrderBy(p => p.Name)
            .Take(limit)
            .ToListAsync();
    }
}
