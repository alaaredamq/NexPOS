using Core.Entities.Products;
using Core.IRepositories.Base;

namespace Core.IRepositories.Products;

public interface IProductCategoryRepository : IRepository<ProductCategory>
{
    /// <summary>Loads a category with its non-deleted products (and their currency), or null if it doesn't exist.</summary>
    Task<ProductCategory?> GetByIdWithProductsAsync(int id);

    /// <summary>Number of non-deleted products per category id. Categories without products are absent.</summary>
    Task<Dictionary<int, int>> GetProductCountsAsync();
}


