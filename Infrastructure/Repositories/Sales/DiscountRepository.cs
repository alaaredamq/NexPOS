using Core.Entities.Sales;
using Core.IRepositories.Sales;
using Infrastructure.Data;
using Infrastructure.Repositories.Classes.Base;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories.Sales;

public class DiscountRepository : Repository<Discount>, IDiscountRepository
{
    public DiscountRepository(ErpDbContext dbContext, IServiceProvider serviceProvider)
        : base(dbContext, serviceProvider)
    {
    }

    public async Task<List<Discount>> GetBySupplierIdAsync(int? supplierId)
    {
        return await GetAllQueryable()
            .Where(d => d.SupplierId == supplierId)
            .ToListAsync();
    }

    public async Task<Discount?> GetDefaultBySupplierIdAsync(int? supplierId)
    {
        return await GetAllQueryable()
            .FirstOrDefaultAsync(d => d.SupplierId == supplierId && d.IsDefault);
    }
}
