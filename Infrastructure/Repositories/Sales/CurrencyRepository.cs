using Core.Entities.Sales;
using Core.IRepositories.Sales;
using Infrastructure.Data;
using Infrastructure.Repositories.Classes.Base;

namespace Infrastructure.Repositories.Sales;

public class CurrencyRepository : Repository<Currency>, ICurrencyRepository
{
    public CurrencyRepository(ErpDbContext dbContext, IServiceProvider serviceProvider)
        : base(dbContext, serviceProvider)
    {
    }
}


