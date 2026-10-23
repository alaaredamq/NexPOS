using Core.Entities.Products;
using Core.IRepositories.Products;
using Infrastructure.Data;
using Infrastructure.Repositories.Classes.Base;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories.Products;

public class ProductCategoryRepository : Repository<ProductCategory>, IProductCategoryRepository
{
    public ProductCategoryRepository(ErpDbContext dbContext, IServiceProvider serviceProvider)
        : base(dbContext, serviceProvider)
    {
    }

    public async Task<ProductCategory?> GetByIdWithProductsAsync(int id) =>
        await GetAllQueryable()
            .Include(c => c.Products.Where(p => !p.IsDeleted).OrderBy(p => p.Name))
                .ThenInclude(p => p.Currency)
            .FirstOrDefaultAsync(c => c.Id == id);

    public async Task<Dictionary<int, int>> GetProductCountsAsync() =>
        await _Context.Set<Product>()
            .Where(p => !p.IsDeleted)
            .GroupBy(p => p.CategoryId)
            .Select(g => new { CategoryId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CategoryId, x => x.Count);
}


