using System.Text;

namespace Application.IServices.Reports;

public interface IExportService
{
    Task<byte[]> ExportToCsvAsync<T>(IEnumerable<T> data, string fileName = "export") where T : class;
    Task<byte[]> ExportToExcelAsync<T>(IEnumerable<T> data, string fileName = "export") where T : class;
    Task<string> GenerateCsvContentAsync<T>(IEnumerable<T> data) where T : class;
    Task<byte[]> ExportProductsToCsvAsync();
    Task<byte[]> ExportCustomersToCsvAsync();
    Task<byte[]> ExportCategoriesToCsvAsync();
    Task<byte[]> ExportSuppliersToCsvAsync();
    Task<byte[]> ExportOrdersToCsvAsync();
}
