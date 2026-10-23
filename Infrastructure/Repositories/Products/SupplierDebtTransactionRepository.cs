using Core.Entities.Products;
using Core.IRepositories.Products;
using Infrastructure.Data;
using Infrastructure.Repositories.Base;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories.Products;

public class SupplierDebtTransactionRepository : BaseRepository<SupplierDebtTransaction, int>, ISupplierDebtTransactionRepository
{
    public SupplierDebtTransactionRepository(ErpDbContext context, IServiceProvider serviceProvider)
        : base(context, serviceProvider)
    {
    }

    public async Task<List<SupplierDebtTransaction>> GetBySupplierIdAsync(int supplierId)
    {
        return await _Entities
            .Include(t => t.User)
            .Where(t => t.SupplierId == supplierId)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync();
    }
}
