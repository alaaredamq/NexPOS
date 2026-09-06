using Application.DTOs.Dashboard;

namespace Application.IServices.Dashboard;

public interface IDashboardService
{
    Task<DashboardStatsDTO> GetDashboardStatsAsync();
    Task<SalesAnalyticsDTO> GetSalesAnalyticsAsync(string period = "month");
    Task<CategoryAnalyticsDTO> GetCategoryAnalyticsAsync();
    Task<List<TopProductDTO>> GetTopProductsAsync(int limit = 10);
    Task<List<TopCustomerDTO>> GetTopCustomersAsync(int limit = 10);
    Task<List<RecentOrderDTO>> GetRecentOrdersAsync(int limit = 10);
    Task<RevenueTrendDTO> GetRevenueTrendAsync(string period = "month");
    Task<ProfitAnalysisDTO> GetProfitAnalysisAsync(string period = "month");
    Task<InventoryStatusDTO> GetInventoryStatusAsync();
}
