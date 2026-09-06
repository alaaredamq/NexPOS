using Application.IServices.Reports;
using Application.IServices.Products;
using Application.IServices.Users;
using Application.IServices.Sales;
using System.Text;
using System.Reflection;

namespace Application.Services.Reports;

public class ExportService : IExportService
{
    private readonly IProductService _productService;
    private readonly ICustomerService _customerService;
    private readonly IProductCategoryService _categoryService;
    private readonly ISupplierService _supplierService;
    private readonly IOrderService _orderService;

    public ExportService(
        IProductService productService,
        ICustomerService customerService,
        IProductCategoryService categoryService,
        ISupplierService supplierService,
        IOrderService orderService)
    {
        _productService = productService;
        _customerService = customerService;
        _categoryService = categoryService;
        _supplierService = supplierService;
        _orderService = orderService;
    }

    public async Task<byte[]> ExportToCsvAsync<T>(IEnumerable<T> data, string fileName = "export") where T : class
    {
        var csvContent = await GenerateCsvContentAsync(data);
        return Encoding.UTF8.GetBytes(csvContent);
    }

    public async Task<string> GenerateCsvContentAsync<T>(IEnumerable<T> data) where T : class
    {
        var csv = new StringBuilder();
        var properties = typeof(T).GetProperties();

        // Add header row
        var headers = properties.Select(p => GetDisplayName(p.Name));
        csv.AppendLine(string.Join(",", headers.Select(h => $"\"{h}\"")));

        // Add data rows
        foreach (var item in data)
        {
            var values = properties.Select(p =>
            {
                var value = p.GetValue(item);
                if (value == null) return "";
                
                // Handle different data types
                return value switch
                {
                    DateTime dt => dt.ToString("yyyy-MM-dd HH:mm:ss"),
                    decimal dec => dec.ToString("F2"),
                    bool b => b ? "Yes" : "No",
                    _ => value.ToString()?.Replace("\"", "\"\"") ?? ""
                };
            });
            csv.AppendLine(string.Join(",", values.Select(v => $"\"{v}\"")));
        }

        return csv.ToString();
    }

    public async Task<byte[]> ExportToExcelAsync<T>(IEnumerable<T> data, string fileName = "export") where T : class
    {
        // For now, return CSV format. In a real implementation, you would use a library like EPPlus or ClosedXML
        return await ExportToCsvAsync(data, fileName);
    }

    public async Task<byte[]> ExportProductsToCsvAsync()
    {
        var products = await _productService.GetAllAsync();
        return await ExportToCsvAsync(products, "products");
    }

    public async Task<byte[]> ExportCustomersToCsvAsync()
    {
        var customers = await _customerService.GetAllAsync();
        return await ExportToCsvAsync(customers, "customers");
    }

    public async Task<byte[]> ExportCategoriesToCsvAsync()
    {
        var categories = await _categoryService.GetAllAsync();
        return await ExportToCsvAsync(categories, "categories");
    }

    public async Task<byte[]> ExportSuppliersToCsvAsync()
    {
        var suppliers = await _supplierService.GetAllAsync();
        return await ExportToCsvAsync(suppliers, "suppliers");
    }

    public async Task<byte[]> ExportOrdersToCsvAsync()
    {
        // Get orders from the last year
        var orders = await _orderService.GetByDateRangeAsync(DateTime.Now.AddYears(-1), DateTime.Now);
        return await ExportToCsvAsync(orders, "orders");
    }

    private string GetDisplayName(string propertyName)
    {
        return propertyName switch
        {
            "Id" => "ID",
            "Name" => "Name",
            "FirstName" => "First Name",
            "LastName" => "Last Name",
            "Email" => "Email",
            "PhoneNumber" => "Phone Number",
            "Address" => "Address",
            "Barcode" => "Barcode",
            "PurchasePrice" => "Purchase Price",
            "SellingPrice" => "Selling Price",
            "StockQuantity" => "Stock Quantity",
            "IsActive" => "Status",
            "Description" => "Description",
            "ContactPerson" => "Contact Person",
            "CreatedAt" => "Created Date",
            "UpdatedAt" => "Updated Date",
            "CustomerId" => "Customer ID",
            "TotalAmount" => "Total Amount",
            "Status" => "Status",
            _ => propertyName
        };
    }
}
