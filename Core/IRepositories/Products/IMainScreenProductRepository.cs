using Core.Entities.Base;
using Core.Entities.Products;
using Core.IRepositories.Base;

namespace Core.IRepositories.Products;

public interface IMainScreenProductRepository : IBaseRepository<MainScreenProduct, int>
{
    Task<IEnumerable<MainScreenProduct>> GetPinnedProductsAsync();
    Task<IEnumerable<MainScreenProduct>> GetMainScreenProductsAsync();
    Task<MainScreenProduct?> GetByProductIdAsync(int productId);
    Task PinProductAsync(int productId, int displayOrder);
    Task UnpinProductAsync(int productId);
    Task UpdateDisplayOrderAsync(int productId, int newOrder);
    Task<IEnumerable<Product>> SearchProductsForMainScreenAsync(string searchTerm, int limit = 20);
}
