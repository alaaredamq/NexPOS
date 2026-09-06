using Core.Entities.Products;
using Core.Entities.Users;

namespace Application.IServices.Performance;

public interface IQueryOptimizationService
{
    Task<List<Product>> GetProductsOptimizedAsync(int page = 1, int pageSize = 20, string? searchTerm = null);
    Task<List<Customer>> GetCustomersOptimizedAsync(int page = 1, int pageSize = 20, string? searchTerm = null);
    Task<Dictionary<string, object>> GetDashboardStatsAsync();
    Task<List<Product>> GetMainScreenProductsOptimizedAsync();
    Task<List<Product>> GetLowStockProductsAsync(int threshold = 10);
}
