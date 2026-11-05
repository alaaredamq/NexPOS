using Application.DTOs.Reports;

namespace Application.IServices.Reports
{
    public interface ISalesReportService
    {
        Task<SalesReportDTO> GetSalesReportAsync(SalesReportRequest request);
        Task<SalesReportDTO> GetDailySalesReportAsync(DateTime date);
        Task<SalesReportDTO> GetMonthlySalesReportAsync(int year, int month);
        Task<SalesReportDTO> GetYearlySalesReportAsync(int year);
        Task<List<DailySalesDTO>> GetSalesTrendAsync(DateTime startDate, DateTime endDate);
        Task<List<TopProductDTO>> GetTopProductsAsync(DateTime startDate, DateTime endDate, int limit = 10);
        Task<List<CategorySalesDTO>> GetCategorySalesAsync(DateTime startDate, DateTime endDate);
        Task<decimal> GetTotalSalesAsync(DateTime startDate, DateTime endDate);
        Task<decimal> GetTotalProfitAsync(DateTime startDate, DateTime endDate);
        Task<int> GetTotalOrdersAsync(DateTime startDate, DateTime endDate);
        Task<decimal> GetAverageOrderValueAsync(DateTime startDate, DateTime endDate);
    }
}
