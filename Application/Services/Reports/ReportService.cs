using Application.IServices.Reports;
using Core.Entities.Sales;
using Core.Enum;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Application.Services.Reports;

public class ReportService : IReportService
{
    private readonly ErpDbContext _db;

    public ReportService(ErpDbContext db)
    {
        _db = db;
    }

    // Every report here is about sales: cancelled/returned orders (e.g. from a voided receipt) are not.
    private IQueryable<Order> SoldOrders => _db.Orders
        .Where(o => o.Status != (int)OrderStatus.Cancelled && o.Status != (int)OrderStatus.Returned);

    public async Task<object> GetSalesByDayAsync(int days = 7)
    {
        var since = DateTime.UtcNow.Date.AddDays(-days + 1);
        var data = await SoldOrders
            .Where(o => o.OrderDate >= since)
            .GroupBy(o => o.OrderDate.Date)
            .Select(g => new { date = g.Key, total = g.Sum(x => x.TotalAmount) })
            .OrderBy(x => x.date)
            .ToListAsync();

        var labels = data.Select(d => d.date.ToString("MMM dd")).ToList();
        var totals = data.Select(d => (double)d.total).ToList();

        return new { labels, datasets = new[] { new { label = "Sales", data = totals } } };
    }

    public async Task<object> GetSalesByWeekAsync(int weeks = 4)
    {
        var since = DateTime.UtcNow.Date.AddDays(-weeks * 7 + 1);
        var orders = await SoldOrders
            .Where(o => o.OrderDate >= since)
            .Select(o => new { o.OrderDate, o.TotalAmount })
            .ToListAsync();

        var data = orders
            .GroupBy(o => GetWeekBucketStart(o.OrderDate))
            .Select(g => new { week = g.Key, total = g.Sum(x => x.TotalAmount) })
            .OrderBy(x => x.week)
            .ToList();

        var labels = data.Select(d => $"Week {d.week:MMM dd}").ToList();
        var totals = data.Select(d => (double)d.total).ToList();

        return new { labels, datasets = new[] { new { label = "Sales", data = totals } } };
    }

    public async Task<object> GetSalesByMonthAsync(int months = 12)
    {
        var since = DateTime.UtcNow.Date.AddMonths(-months + 1);
        var data = await SoldOrders
            .Where(o => o.OrderDate >= since)
            .GroupBy(o => new { o.OrderDate.Year, o.OrderDate.Month })
            .Select(g => new { year = g.Key.Year, month = g.Key.Month, total = g.Sum(x => x.TotalAmount) })
            .OrderBy(x => x.year).ThenBy(x => x.month)
            .ToListAsync();

        var labels = data.Select(d => $"{d.month:D2}/{d.year}").ToList();
        var totals = data.Select(d => (double)d.total).ToList();

        return new { labels, datasets = new[] { new { label = "Sales", data = totals } } };
    }

    public async Task<object> GetSalesByYearAsync(int years = 5)
    {
        var since = DateTime.UtcNow.Date.AddYears(-years + 1);
        var data = await SoldOrders
            .Where(o => o.OrderDate >= since)
            .GroupBy(o => o.OrderDate.Year)
            .Select(g => new { year = g.Key, total = g.Sum(x => x.TotalAmount) })
            .OrderBy(x => x.year)
            .ToListAsync();

        var labels = data.Select(d => d.year.ToString()).ToList();
        var totals = data.Select(d => (double)d.total).ToList();

        return new { labels, datasets = new[] { new { label = "Sales", data = totals } } };
    }

    public async Task<object> GetProfitByDayAsync(int days = 7)
    {
        var since = DateTime.UtcNow.Date.AddDays(-days + 1);
        var data = await SoldOrders
            .Where(o => o.OrderDate >= since)
            .Include(o => o.Cart)
            .ThenInclude(c => c.CartItems)
            .ThenInclude(ci => ci.Product)
            .GroupBy(o => o.OrderDate.Date)
            .Select(g => new { 
                date = g.Key, 
                totalSales = g.Sum(x => x.TotalAmount),
                totalCost = g.SelectMany(x => x.Cart.CartItems).Sum(ci => ci.Quantity * (ci.Product.CostPrice ?? 0))
            })
            .OrderBy(x => x.date)
            .ToListAsync();

        var labels = data.Select(d => d.date.ToString("MMM dd")).ToList();
        var profits = data.Select(d => (double)(d.totalSales - d.totalCost)).ToList();

        return new { labels, datasets = new[] { new { label = "Profit", data = profits } } };
    }

    public async Task<object> GetProfitByWeekAsync(int weeks = 4)
    {
        var since = DateTime.UtcNow.Date.AddDays(-weeks * 7 + 1);
        var orders = await SoldOrders
            .Where(o => o.OrderDate >= since)
            .Select(o => new
            {
                o.OrderDate,
                o.TotalAmount,
                Cost = o.Cart.CartItems.Sum(ci => ci.Quantity * (ci.Product.CostPrice ?? 0))
            })
            .ToListAsync();

        var data = orders
            .GroupBy(o => GetWeekBucketStart(o.OrderDate))
            .Select(g => new {
                week = g.Key,
                totalSales = g.Sum(x => x.TotalAmount),
                totalCost = g.Sum(x => x.Cost)
            })
            .OrderBy(x => x.week)
            .ToList();

        var labels = data.Select(d => $"Week {d.week:MMM dd}").ToList();
        var profits = data.Select(d => (double)(d.totalSales - d.totalCost)).ToList();

        return new { labels, datasets = new[] { new { label = "Profit", data = profits } } };
    }

    public async Task<object> GetProfitByMonthAsync(int months = 12)
    {
        var since = DateTime.UtcNow.Date.AddMonths(-months + 1);
        var data = await SoldOrders
            .Where(o => o.OrderDate >= since)
            .Include(o => o.Cart)
            .ThenInclude(c => c.CartItems)
            .ThenInclude(ci => ci.Product)
            .GroupBy(o => new { o.OrderDate.Year, o.OrderDate.Month })
            .Select(g => new { 
                year = g.Key.Year, 
                month = g.Key.Month, 
                totalSales = g.Sum(x => x.TotalAmount),
                totalCost = g.SelectMany(x => x.Cart.CartItems).Sum(ci => ci.Quantity * (ci.Product.CostPrice ?? 0))
            })
            .OrderBy(x => x.year).ThenBy(x => x.month)
            .ToListAsync();

        var labels = data.Select(d => $"{d.month:D2}/{d.year}").ToList();
        var profits = data.Select(d => (double)(d.totalSales - d.totalCost)).ToList();

        return new { labels, datasets = new[] { new { label = "Profit", data = profits } } };
    }

    public async Task<object> GetProfitByYearAsync(int years = 5)
    {
        var since = DateTime.UtcNow.Date.AddYears(-years + 1);
        var data = await SoldOrders
            .Where(o => o.OrderDate >= since)
            .Include(o => o.Cart)
            .ThenInclude(c => c.CartItems)
            .ThenInclude(ci => ci.Product)
            .GroupBy(o => o.OrderDate.Year)
            .Select(g => new { 
                year = g.Key, 
                totalSales = g.Sum(x => x.TotalAmount),
                totalCost = g.SelectMany(x => x.Cart.CartItems).Sum(ci => ci.Quantity * (ci.Product.CostPrice ?? 0))
            })
            .OrderBy(x => x.year)
            .ToListAsync();

        var labels = data.Select(d => d.year.ToString()).ToList();
        var profits = data.Select(d => (double)(d.totalSales - d.totalCost)).ToList();

        return new { labels, datasets = new[] { new { label = "Profit", data = profits } } };
    }

    public async Task<object> GetIncomeExpensesByDayAsync(int days = 7)
    {
        var since = DateTime.UtcNow.Date.AddDays(-days + 1);
        var data = await SoldOrders
            .Where(o => o.OrderDate >= since)
            .Include(o => o.Cart)
            .ThenInclude(c => c.CartItems)
            .ThenInclude(ci => ci.Product)
            .GroupBy(o => o.OrderDate.Date)
            .Select(g => new { 
                date = g.Key, 
                income = g.Sum(x => x.TotalAmount),
                expenses = g.SelectMany(x => x.Cart.CartItems).Sum(ci => ci.Quantity * (ci.Product.CostPrice ?? 0))
            })
            .OrderBy(x => x.date)
            .ToListAsync();

        var labels = data.Select(d => d.date.ToString("MMM dd")).ToList();
        var income = data.Select(d => (double)d.income).ToList();
        var expenses = data.Select(d => (double)d.expenses).ToList();

        return new { 
            labels, 
            datasets = new[] { 
                new { label = "Income", data = income },
                new { label = "Expenses", data = expenses }
            } 
        };
    }

    public async Task<object> GetIncomeExpensesByWeekAsync(int weeks = 4)
    {
        var since = DateTime.UtcNow.Date.AddDays(-weeks * 7 + 1);
        var orders = await SoldOrders
            .Where(o => o.OrderDate >= since)
            .Select(o => new
            {
                o.OrderDate,
                o.TotalAmount,
                Expense = o.Cart.CartItems.Sum(ci => ci.Quantity * (ci.Product.CostPrice ?? 0))
            })
            .ToListAsync();

        var data = orders
            .GroupBy(o => GetWeekBucketStart(o.OrderDate))
            .Select(g => new {
                week = g.Key,
                income = g.Sum(x => x.TotalAmount),
                expenses = g.Sum(x => x.Expense)
            })
            .OrderBy(x => x.week)
            .ToList();

        var labels = data.Select(d => $"Week {d.week:MMM dd}").ToList();
        var income = data.Select(d => (double)d.income).ToList();
        var expenses = data.Select(d => (double)d.expenses).ToList();

        return new { 
            labels, 
            datasets = new[] { 
                new { label = "Income", data = income },
                new { label = "Expenses", data = expenses }
            } 
        };
    }

    public async Task<object> GetIncomeExpensesByMonthAsync(int months = 12)
    {
        var since = DateTime.UtcNow.Date.AddMonths(-months + 1);
        var data = await SoldOrders
            .Where(o => o.OrderDate >= since)
            .Include(o => o.Cart)
            .ThenInclude(c => c.CartItems)
            .ThenInclude(ci => ci.Product)
            .GroupBy(o => new { o.OrderDate.Year, o.OrderDate.Month })
            .Select(g => new { 
                year = g.Key.Year, 
                month = g.Key.Month, 
                income = g.Sum(x => x.TotalAmount),
                expenses = g.SelectMany(x => x.Cart.CartItems).Sum(ci => ci.Quantity * (ci.Product.CostPrice ?? 0))
            })
            .OrderBy(x => x.year).ThenBy(x => x.month)
            .ToListAsync();

        var labels = data.Select(d => $"{d.month:D2}/{d.year}").ToList();
        var income = data.Select(d => (double)d.income).ToList();
        var expenses = data.Select(d => (double)d.expenses).ToList();

        return new { 
            labels, 
            datasets = new[] { 
                new { label = "Income", data = income },
                new { label = "Expenses", data = expenses }
            } 
        };
    }

    public async Task<object> GetIncomeExpensesByYearAsync(int years = 5)
    {
        var since = DateTime.UtcNow.Date.AddYears(-years + 1);
        var data = await SoldOrders
            .Where(o => o.OrderDate >= since)
            .Include(o => o.Cart)
            .ThenInclude(c => c.CartItems)
            .ThenInclude(ci => ci.Product)
            .GroupBy(o => o.OrderDate.Year)
            .Select(g => new { 
                year = g.Key, 
                income = g.Sum(x => x.TotalAmount),
                expenses = g.SelectMany(x => x.Cart.CartItems).Sum(ci => ci.Quantity * (ci.Product.CostPrice ?? 0))
            })
            .OrderBy(x => x.year)
            .ToListAsync();

        var labels = data.Select(d => d.year.ToString()).ToList();
        var income = data.Select(d => (double)d.income).ToList();
        var expenses = data.Select(d => (double)d.expenses).ToList();

        return new { 
            labels, 
            datasets = new[] { 
                new { label = "Income", data = income },
                new { label = "Expenses", data = expenses }
            } 
        };
    }

    public async Task<object> GetTopSellingProductsAsync(int count = 10)
    {
        var data = await SoldOrders
            .Include(o => o.Cart)
            .ThenInclude(c => c.CartItems)
            .ThenInclude(ci => ci.Product)
            .SelectMany(o => o.Cart.CartItems)
            .GroupBy(ci => new { ci.Product.Id, ci.Product.Name })
            .Select(g => new { 
                productId = g.Key.Id, 
                productName = g.Key.Name, 
                quantity = g.Sum(ci => ci.Quantity),
                revenue = g.Sum(ci => ci.TotalPrice)
            })
            .OrderByDescending(x => x.quantity)
            .Take(count)
            .ToListAsync();

        var labels = data.Select(d => d.productName).ToList();
        var quantities = data.Select(d => d.quantity).ToList();

        return new { labels, datasets = new[] { new { label = "Quantity Sold", data = quantities } } };
    }

    public async Task<object> GetTopCustomersAsync(int count = 10)
    {
        var data = await SoldOrders
            .Include(o => o.Customer)
            .GroupBy(o => new { o.Customer.Id, o.Customer.FirstName, o.Customer.LastName })
            .Select(g => new { 
                customerId = g.Key.Id, 
                customerName = $"{g.Key.FirstName} {g.Key.LastName}".Trim(), 
                orderCount = g.Count(),
                totalSpent = g.Sum(x => x.TotalAmount)
            })
            .OrderByDescending(x => x.totalSpent)
            .Take(count)
            .ToListAsync();

        var labels = data.Select(d => d.customerName).ToList();
        var totals = data.Select(d => (double)d.totalSpent).ToList();

        return new { labels, datasets = new[] { new { label = "Total Spent", data = totals } } };
    }

    public async Task<object> GetTopSuppliersAsync(int count = 10)
    {
        var data = await SoldOrders
            .Include(o => o.Cart)
            .ThenInclude(c => c.CartItems)
            .ThenInclude(ci => ci.Product)
            .ThenInclude(p => p.Supplier)
            .SelectMany(o => o.Cart.CartItems)
            .GroupBy(ci => new { ci.Product.Supplier.Id, ci.Product.Supplier.Name })
            .Select(g => new { 
                supplierId = g.Key.Id, 
                supplierName = g.Key.Name, 
                productCount = g.Count(),
                revenue = g.Sum(ci => ci.TotalPrice)
            })
            .OrderByDescending(x => x.revenue)
            .Take(count)
            .ToListAsync();

        var labels = data.Select(d => d.supplierName).ToList();
        var revenues = data.Select(d => (double)d.revenue).ToList();

        return new { labels, datasets = new[] { new { label = "Revenue", data = revenues } } };
    }

    public async Task<object> GetSalesByDiscountAsync()
    {
        // This would need discount tracking in orders - simplified for now
        var data = await SoldOrders
            .GroupBy(o => "No Discount") // Placeholder - would need actual discount tracking
            .Select(g => new { discount = g.Key, sales = g.Sum(x => x.TotalAmount) })
            .ToListAsync();

        var labels = data.Select(d => d.discount).ToList();
        var sales = data.Select(d => (double)d.sales).ToList();

        return new { labels, datasets = new[] { new { label = "Sales", data = sales } } };
    }

    // EF.Functions.DateFromParts is SQL-Server-only, with no SQLite translation - week-bucketing
    // is done in-memory on already-materialized rows instead (see the *ByWeekAsync methods above).
    private static DateTime GetWeekBucketStart(DateTime orderDate) =>
        new DateTime(orderDate.Year, 1, 1).AddDays((orderDate.DayOfYear / 7) * 7);
}
