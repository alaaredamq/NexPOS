using Core.Entities.Sales;
using Core.IRepositories.Sales;
using Infrastructure.Data;
using Infrastructure.Repositories.Classes.Base;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories.Sales;

public class TaxRepository : Repository<Tax>, ITaxRepository
{
    public TaxRepository(ErpDbContext dbContext, IServiceProvider serviceProvider)
        : base(dbContext, serviceProvider)
    {
    }

    public async Task<List<Tax>> GetBySupplierIdAsync(int? supplierId)
    {
        return await GetAllQueryable()
            .Where(t => t.SupplierId == supplierId)
            .ToListAsync();
    }

    public async Task<Tax?> GetDefaultBySupplierIdAsync(int? supplierId)
    {
        return await GetAllQueryable()
            .FirstOrDefaultAsync(t => t.SupplierId == supplierId && t.IsDefault);
    }
}
