using Core.Entities.Sales;
using Core.IRepositories.Sales;
using Infrastructure.Data;
using Infrastructure.Repositories.Classes.Base;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories.Sales;

public class PaymentMethodRepository : Repository<PaymentMethod>, IPaymentMethodRepository
{
    public PaymentMethodRepository(ErpDbContext dbContext, IServiceProvider serviceProvider)
        : base(dbContext, serviceProvider)
    {
    }

    public async Task<List<PaymentMethod>> GetBySupplierIdAsync(int? supplierId)
    {
        return await GetAllQueryable()
            .Where(pm => pm.SupplierId == supplierId)
            .ToListAsync();
    }

    public async Task<PaymentMethod?> GetDefaultBySupplierIdAsync(int? supplierId)
    {
        return await GetAllQueryable()
            .FirstOrDefaultAsync(pm => pm.SupplierId == supplierId && pm.IsDefault);
    }
}
