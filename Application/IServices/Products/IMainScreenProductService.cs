using Application.IServices.Base;
using Core.Entities.Products;

namespace Application.IServices.Products;

public interface IMainScreenProductService : IBaseService
{
    Task<IEnumerable<MainScreenProduct>> GetAllAsync();
    Task<MainScreenProduct?> GetByIdAsync(int id);
    Task<MainScreenProduct> CreateAsync(MainScreenProduct mainScreenProduct);
    Task<MainScreenProduct> UpdateAsync(MainScreenProduct mainScreenProduct);
    Task DeleteAsync(int id);
    
    Task<IEnumerable<MainScreenProduct>> GetPinnedProductsAsync();
    Task<IEnumerable<MainScreenProduct>> GetMainScreenProductsAsync();
    Task<MainScreenProduct?> GetByProductIdAsync(int productId);
    Task PinProductAsync(int productId, int displayOrder);
    Task UnpinProductAsync(int productId);
    Task UpdateDisplayOrderAsync(int productId, int newOrder);
    Task<IEnumerable<Product>> SearchProductsForMainScreenAsync(string searchTerm, int limit = 20);
    Task ReorderProductsAsync(List<int> productIds);
}
