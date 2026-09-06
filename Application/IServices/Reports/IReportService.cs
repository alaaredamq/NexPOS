using Application.IServices.Base;

namespace Application.IServices.Reports;

public interface IReportService : IBaseService
{
    Task<object> GetSalesByDayAsync(int days = 7);
    Task<object> GetSalesByWeekAsync(int weeks = 4);
    Task<object> GetSalesByMonthAsync(int months = 12);
    Task<object> GetSalesByYearAsync(int years = 5);
    Task<object> GetProfitByDayAsync(int days = 7);
    Task<object> GetProfitByWeekAsync(int weeks = 4);
    Task<object> GetProfitByMonthAsync(int months = 12);
    Task<object> GetProfitByYearAsync(int years = 5);
    Task<object> GetIncomeExpensesByDayAsync(int days = 7);
    Task<object> GetIncomeExpensesByWeekAsync(int weeks = 4);
    Task<object> GetIncomeExpensesByMonthAsync(int months = 12);
    Task<object> GetIncomeExpensesByYearAsync(int years = 5);
    Task<object> GetTopSellingProductsAsync(int count = 10);
    Task<object> GetTopCustomersAsync(int count = 10);
    Task<object> GetTopSuppliersAsync(int count = 10);
    Task<object> GetSalesByDiscountAsync();
}
