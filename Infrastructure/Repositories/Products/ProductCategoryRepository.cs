using Core.Entities.Products;
using Core.IRepositories.Products;
using Infrastructure.Data;
using Infrastructure.Repositories.Classes.Base;

namespace Infrastructure.Repositories.Products;

public class ProductCategoryRepository : Repository<ProductCategory>, IProductCategoryRepository
{
    public ProductCategoryRepository(ErpDbContext dbContext, IServiceProvider serviceProvider)
        : base(dbContext, serviceProvider)
    {
    }
}


