using Application.IServices.Notifications;
using Core.Entities.Notifications;
using Core.Entities.Products;
using Core.IRepositories.Notifications;
using Core.IRepositories.Products;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Services.Notifications;

public class StockAlertService : IStockAlertService
{
    private readonly IProductRepository _productRepo;
    private readonly IAlertRepository _alertRepo;
    private readonly ILogger<StockAlertService> _logger;

    public StockAlertService(
        IProductRepository productRepo,
        IAlertRepository alertRepo,
        ILogger<StockAlertService> logger)
    {
        _productRepo = productRepo;
        _alertRepo = alertRepo;
        _logger = logger;
    }

    public async Task CheckAndCreateStockAlertsAsync()
    {
        try
        {
            _logger.LogInformation("Checking for low stock and out of stock products");
            
            var allProducts = await _productRepo.GetAllAsync();
            var productsToCheck = allProducts.Where(p => p.IsActive && !p.IsDeleted && p.MinimumStock.HasValue).ToList();
            
            int lowStockCount = 0;
            int outOfStockCount = 0;
            
            foreach (var product in productsToCheck)
            {
                // Out of stock
                if (product.StockQuantity <= 0)
                {
                    await CreateOrUpdateAlertAsync(
                        $"Product Out of Stock: {product.Name}",
                        $"Product '{product.Name}' (Barcode: {product.Barcode}) is out of stock. Current quantity: {product.StockQuantity}",
                        AlertType.OutOfStock,
                        AlertStatus.Active,
                        new { ProductId = product.Id, ProductName = product.Name, StockQuantity = product.StockQuantity }
                    );
                    outOfStockCount++;
                }
                // Low stock
                else if (product.MinimumStock.HasValue && product.StockQuantity <= product.MinimumStock.Value && product.MinimumStock.Value > 0)
                {
                    await CreateOrUpdateAlertAsync(
                        $"Low Stock Alert: {product.Name}",
                        $"Product '{product.Name}' (Barcode: {product.Barcode}) is running low. Current quantity: {product.StockQuantity}, Minimum level: {product.MinimumStock.Value}",
                        AlertType.LowStock,
                        AlertStatus.Active,
                        new { ProductId = product.Id, ProductName = product.Name, StockQuantity = product.StockQuantity, MinimumStock = product.MinimumStock.Value }
                    );
                    lowStockCount++;
                }
            }
            
            _logger.LogInformation("Stock alerts check completed. Out of stock: {OutOfStock}, Low stock: {LowStock}", 
                outOfStockCount, lowStockCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking stock alerts");
        }
    }

    public async Task CheckProductStockAfterSaleAsync(int productId)
    {
        try
        {
            // Lightweight, no-tracking read: we only need scalar stock fields, and AsNoTracking
            // guarantees we see the real post-deduction quantity rather than a stale tracked copy
            // left behind by the ExecuteUpdate stock decrement.
            var product = await _productRepo.Table
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == productId);
            if (product == null || !product.IsActive)
            {
                return;
            }
            
            // Out of stock
            if (product.StockQuantity <= 0)
            {
                await CreateOrUpdateAlertAsync(
                    $"Product Out of Stock: {product.Name}",
                    $"Product '{product.Name}' (Barcode: {product.Barcode}) is now out of stock after recent sale. Current quantity: {product.StockQuantity}",
                    AlertType.OutOfStock,
                    AlertStatus.Active,
                    new { ProductId = product.Id, ProductName = product.Name, StockQuantity = product.StockQuantity }
                );
            }
            // Low stock
            else if (product.MinimumStock.HasValue && product.StockQuantity <= product.MinimumStock.Value && product.MinimumStock.Value > 0)
            {
                await CreateOrUpdateAlertAsync(
                    $"Low Stock Alert: {product.Name}",
                    $"Product '{product.Name}' (Barcode: {product.Barcode}) is now running low after recent sale. Current quantity: {product.StockQuantity}, Minimum level: {product.MinimumStock.Value}",
                    AlertType.LowStock,
                    AlertStatus.Active,
                    new { ProductId = product.Id, ProductName = product.Name, StockQuantity = product.StockQuantity, MinimumStock = product.MinimumStock.Value }
                );
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking product stock after sale for product {ProductId}", productId);
        }
    }

    private async Task CreateOrUpdateAlertAsync(string title, string message, AlertType type, AlertStatus status, object metadata)
    {
        try
        {
            // Check if similar alert already exists and is active
            var metadataJson = System.Text.Json.JsonSerializer.Serialize(metadata);
            var existingAlert = await _alertRepo.GetActiveByTypeAndMetadataAsync(type, metadataJson);

            if (existingAlert != null)
            {
                // Update existing alert
                existingAlert.Message = message;
                existingAlert.UpdatedAt = DateTime.UtcNow;
                await _alertRepo.SaveAsync(existingAlert);
                _logger.LogDebug("Updated existing alert: {Title}", title);
            }
            else
            {
                // Create new alert
                var alert = new Alert
                {
                    Title = title,
                    Message = message,
                    Type = type,
                    Status = status,
                    Metadata = metadataJson,
                    CreatedAt = DateTime.UtcNow
                };
                
                await _alertRepo.SaveAsync(alert);
                _logger.LogInformation("Created new alert: {Title}", title);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating/updating alert: {Title}", title);
        }
    }
}

