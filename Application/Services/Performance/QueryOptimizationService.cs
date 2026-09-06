using Application.IServices.Performance;
using Core.Entities.Products;
using Core.Entities.Users;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Application.Services.Performance;

public class QueryOptimizationService : IQueryOptimizationService
{
    private readonly ErpDbContext _context;

    public QueryOptimizationService(ErpDbContext context)
    {
        _context = context;
    }

    public async Task<List<Product>> GetProductsOptimizedAsync(int page = 1, int pageSize = 20, string? searchTerm = null)
    {
        var query = _context.Products
            .AsNoTracking()
            .Include(p => p.Supplier)
            .Include(p => p.Category)
            .Where(p => p.IsActive);

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var term = searchTerm.ToLower();
            query = query.Where(p => 
                p.Name.ToLower().Contains(term) ||
                p.Description.ToLower().Contains(term) ||
                p.Barcode.Contains(term));
        }

        return await query
            .OrderBy(p => p.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
    }

    public async Task<List<Customer>> GetCustomersOptimizedAsync(int page = 1, int pageSize = 20, string? searchTerm = null)
    {
        var query = _context.Customers.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var term = searchTerm.ToLower();
            query = query.Where(c => 
                c.FirstName.ToLower().Contains(term) ||
                c.LastName.ToLower().Contains(term) ||
                c.Email.ToLower().Contains(term));
        }

        return await query
            .OrderBy(c => c.LastName)
            .ThenBy(c => c.FirstName)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
    }

    public async Task<Dictionary<string, object>> GetDashboardStatsAsync()
    {
        var stats = new Dictionary<string, object>();

        // Get basic counts
        stats["TotalProducts"] = await _context.Products.CountAsync();
        stats["ActiveProducts"] = await _context.Products.CountAsync(p => p.IsActive);
        stats["TotalCustomers"] = await _context.Customers.CountAsync();
        stats["TotalSuppliers"] = await _context.Suppliers.CountAsync();
        stats["TotalOrders"] = await _context.Orders.CountAsync();

        // Get recent activity
        var lastWeek = DateTime.UtcNow.AddDays(-7);
        stats["RecentOrders"] = await _context.Orders.CountAsync(o => o.CreatedAt >= lastWeek);
        stats["RecentCustomers"] = await _context.Customers.CountAsync(c => c.CreatedAt >= lastWeek);

        // Get low stock products (using MinimumStock as threshold)
        stats["LowStockProducts"] = await _context.Products.CountAsync(p => p.MinimumStock.HasValue && p.MinimumStock <= 10);

        // Get total sales for current month
        var currentMonth = DateTime.UtcNow.Date.AddDays(1 - DateTime.UtcNow.Day);
        stats["MonthlySales"] = await _context.Orders
            .Where(o => o.OrderDate >= currentMonth)
            .SumAsync(o => o.TotalAmount);

        return stats;
    }

    public async Task<List<Product>> GetMainScreenProductsOptimizedAsync()
    {
        return await _context.Products
            .AsNoTracking()
            .Include(p => p.Supplier)
            .Include(p => p.Category)
            .Where(p => p.IsActive)
            .OrderByDescending(p => p.CreatedAt)
            .Take(20)
            .ToListAsync();
    }

    public async Task<List<Product>> GetLowStockProductsAsync(int threshold = 10)
    {
        return await _context.Products
            .AsNoTracking()
            .Include(p => p.Supplier)
            .Where(p => p.IsActive && p.MinimumStock.HasValue && p.MinimumStock <= threshold)
            .OrderBy(p => p.MinimumStock)
            .ToListAsync();
    }
}
