using Core.Time;
using Application.DTOs.Reports;
using Application.IServices.Reports;
using Core.Enum;
using Core.IRepositories.Sales;
using Core.IRepositories.Inventory;
using Microsoft.EntityFrameworkCore;

namespace Application.Services.Reports
{
    public class SalesReportService : ISalesReportService
    {
        private readonly IOrderRepository _orderRepository;
        private readonly ICartRepository _cartRepository;

        public SalesReportService(IOrderRepository orderRepository, ICartRepository cartRepository)
        {
            _orderRepository = orderRepository;
            _cartRepository = cartRepository;
        }

        public async Task<SalesReportDTO> GetSalesReportAsync(SalesReportRequest request)
        {
            // The request dates are local calendar days (as picked on screen); orders are stored in UTC.
            var (fromUtc, toUtc) = AppTime.LocalDayRangeToUtc(request.StartDate, request.EndDate);
            var orders = await _orderRepository.GetAllAsync(o => 
                o.OrderDate >= fromUtc && 
                o.OrderDate <= toUtc &&
                !o.IsDeleted &&
                o.Status != (int)OrderStatus.Cancelled && o.Status != (int)OrderStatus.Returned);

            var totalSales = orders.Sum(o => o.TotalAmount);
            var totalOrders = orders.Count();
            var averageOrderValue = totalOrders > 0 ? totalSales / totalOrders : 0;

            // Get cart items for orders to calculate items sold
            var orderCartIds = orders.Select(o => o.CartId).ToList();
            var cartItems = await _cartRepository.GetAllAsync(c => orderCartIds.Contains(c.Id));
            var allCartItems = cartItems.SelectMany(c => c.CartItems).ToList();

            var report = new SalesReportDTO
            {
                // Echo the picked LOCAL days back marked as Local, so they serialize with an offset and the
                // page shows the same day (an Unspecified end-of-day would be read as UTC and roll into tomorrow).
                StartDate = DateTime.SpecifyKind(request.StartDate, DateTimeKind.Local),
                EndDate = DateTime.SpecifyKind(request.EndDate, DateTimeKind.Local),
                TotalSales = totalSales,
                TotalOrders = totalOrders,
                AverageOrderValue = averageOrderValue,
                TotalItemsSold = allCartItems.Sum(ci => ci.Quantity),
                DailySales = request.ReportType == "daily" ? await GetDailySalesDataAsync(orders) : new List<DailySalesDTO>(),
                TopProducts = request.IncludeProductDetails ? await GetTopProductsDataAsync(orders, allCartItems) : new List<TopProductDTO>(),
                CategorySales = request.IncludeCategoryBreakdown ? await GetCategorySalesDataAsync(orders, allCartItems) : new List<CategorySalesDTO>(),
                PaymentMethodSales = request.IncludePaymentMethodBreakdown ? await GetPaymentMethodSalesDataAsync(orders) : new List<PaymentMethodSalesDTO>()
            };

            return report;
        }

        public async Task<SalesReportDTO> GetDailySalesReportAsync(DateTime date)
        {
            var startDate = date.Date;
            var endDate = startDate.AddDays(1).AddTicks(-1);

            var request = new SalesReportRequest
            {
                StartDate = startDate,
                EndDate = endDate,
                ReportType = "daily"
            };

            return await GetSalesReportAsync(request);
        }

        public async Task<SalesReportDTO> GetMonthlySalesReportAsync(int year, int month)
        {
            var startDate = new DateTime(year, month, 1);
            var endDate = startDate.AddMonths(1).AddTicks(-1);

            var request = new SalesReportRequest
            {
                StartDate = startDate,
                EndDate = endDate,
                ReportType = "monthly"
            };

            return await GetSalesReportAsync(request);
        }

        public async Task<SalesReportDTO> GetYearlySalesReportAsync(int year)
        {
            var startDate = new DateTime(year, 1, 1);
            var endDate = new DateTime(year + 1, 1, 1).AddTicks(-1);

            var request = new SalesReportRequest
            {
                StartDate = startDate,
                EndDate = endDate,
                ReportType = "yearly"
            };

            return await GetSalesReportAsync(request);
        }

        public async Task<List<DailySalesDTO>> GetSalesTrendAsync(DateTime startDate, DateTime endDate)
        {
            var orders = await _orderRepository.GetAllAsync(o => 
                o.OrderDate >= startDate && 
                o.OrderDate <= endDate &&
                !o.IsDeleted &&
                o.Status != (int)OrderStatus.Cancelled && o.Status != (int)OrderStatus.Returned);

            return await GetDailySalesDataAsync(orders);
        }

        public async Task<List<TopProductDTO>> GetTopProductsAsync(DateTime startDate, DateTime endDate, int limit = 10)
        {
            var orders = await _orderRepository.GetAllAsync(o => 
                o.OrderDate >= startDate && 
                o.OrderDate <= endDate &&
                !o.IsDeleted &&
                o.Status != (int)OrderStatus.Cancelled && o.Status != (int)OrderStatus.Returned);

            var orderCartIds = orders.Select(o => o.CartId).ToList();
            var cartItems = await _cartRepository.GetAllAsync(c => orderCartIds.Contains(c.Id));
            var allCartItems = cartItems.SelectMany(c => c.CartItems).ToList();

            return await GetTopProductsDataAsync(orders, allCartItems, limit);
        }

        public async Task<List<CategorySalesDTO>> GetCategorySalesAsync(DateTime startDate, DateTime endDate)
        {
            var orders = await _orderRepository.GetAllAsync(o => 
                o.OrderDate >= startDate && 
                o.OrderDate <= endDate &&
                !o.IsDeleted &&
                o.Status != (int)OrderStatus.Cancelled && o.Status != (int)OrderStatus.Returned);

            var orderCartIds = orders.Select(o => o.CartId).ToList();
            var cartItems = await _cartRepository.GetAllAsync(c => orderCartIds.Contains(c.Id));
            var allCartItems = cartItems.SelectMany(c => c.CartItems).ToList();

            return await GetCategorySalesDataAsync(orders, allCartItems);
        }

        public async Task<decimal> GetTotalSalesAsync(DateTime startDate, DateTime endDate)
        {
            var orders = await _orderRepository.GetAllAsync(o => 
                o.OrderDate >= startDate && 
                o.OrderDate <= endDate &&
                !o.IsDeleted &&
                o.Status != (int)OrderStatus.Cancelled && o.Status != (int)OrderStatus.Returned);

            return orders.Sum(o => o.TotalAmount);
        }

        public async Task<int> GetTotalOrdersAsync(DateTime startDate, DateTime endDate)
        {
            var orders = await _orderRepository.GetAllAsync(o => 
                o.OrderDate >= startDate && 
                o.OrderDate <= endDate &&
                !o.IsDeleted &&
                o.Status != (int)OrderStatus.Cancelled && o.Status != (int)OrderStatus.Returned);

            return orders.Count();
        }

        public async Task<decimal> GetAverageOrderValueAsync(DateTime startDate, DateTime endDate)
        {
            var orders = await _orderRepository.GetAllAsync(o => 
                o.OrderDate >= startDate && 
                o.OrderDate <= endDate &&
                !o.IsDeleted &&
                o.Status != (int)OrderStatus.Cancelled && o.Status != (int)OrderStatus.Returned);

            var totalOrders = orders.Count();
            return totalOrders > 0 ? orders.Sum(o => o.TotalAmount) / totalOrders : 0;
        }

        private async Task<List<DailySalesDTO>> GetDailySalesDataAsync(IEnumerable<Core.Entities.Sales.Order> orders)
        {
            var dailySales = orders
                .GroupBy(o => o.OrderDate.UtcToLocal().Date) // local day, not UTC day
                .Select(g => new DailySalesDTO
                {
                    Date = g.Key,
                    Sales = g.Sum(o => o.TotalAmount),
                    Orders = g.Count(),
                    ItemsSold = 0 // We'll calculate this separately if needed
                })
                .OrderBy(d => d.Date)
                .ToList();

            return await Task.FromResult(dailySales);
        }

        private async Task<List<TopProductDTO>> GetTopProductsDataAsync(IEnumerable<Core.Entities.Sales.Order> orders, IEnumerable<Core.Entities.Inventory.CartItem> cartItems, int limit = 10)
        {
            var productSales = cartItems
                .GroupBy(ci => new { ci.ProductId, ci.Product.Name })
                .Select(g => new TopProductDTO
                {
                    ProductId = g.Key.ProductId,
                    ProductName = g.Key.Name ?? "Unknown Product",
                    QuantitySold = g.Sum(ci => ci.Quantity),
                    TotalRevenue = g.Sum(ci => ci.UnitPrice * ci.Quantity),
                    AveragePrice = g.Average(ci => ci.UnitPrice)
                })
                .OrderByDescending(p => p.QuantitySold)
                .Take(limit)
                .ToList();

            return await Task.FromResult(productSales);
        }

        private async Task<List<CategorySalesDTO>> GetCategorySalesDataAsync(IEnumerable<Core.Entities.Sales.Order> orders, IEnumerable<Core.Entities.Inventory.CartItem> cartItems)
        {
            var categorySales = cartItems
                .Where(ci => ci.Product.Category != null)
                .GroupBy(ci => new { ci.Product.Category.Id, ci.Product.Category.Name })
                .Select(g => new CategorySalesDTO
                {
                    CategoryId = g.Key.Id,
                    CategoryName = g.Key.Name ?? "Unknown Category",
                    TotalSales = g.Sum(ci => ci.UnitPrice * ci.Quantity),
                    TotalOrders = orders.Count()
                })
                .OrderByDescending(c => c.TotalSales)
                .ToList();

            var totalSales = categorySales.Sum(c => c.TotalSales);
            
            foreach (var category in categorySales)
            {
                category.Percentage = totalSales > 0 ? (category.TotalSales / totalSales) * 100 : 0;
            }

            return await Task.FromResult(categorySales);
        }

        private async Task<List<PaymentMethodSalesDTO>> GetPaymentMethodSalesDataAsync(IEnumerable<Core.Entities.Sales.Order> orders)
        {
            // Since the Order entity doesn't have payment method info, we'll create a default entry
            // In a real implementation, you would need to add payment method tracking to orders
            var paymentMethodSales = new List<PaymentMethodSalesDTO>
            {
                new PaymentMethodSalesDTO
                {
                    PaymentMethodId = 1,
                    PaymentMethodName = "Cash",
                    TotalSales = orders.Sum(o => o.TotalAmount),
                    TotalOrders = orders.Count(),
                    Percentage = 100
                }
            };

            return await Task.FromResult(paymentMethodSales);
        }
    }
}
