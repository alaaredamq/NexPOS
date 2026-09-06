namespace Application.IServices.Notifications;

public interface IStockAlertService
{
    Task CheckAndCreateStockAlertsAsync();
    Task CheckProductStockAfterSaleAsync(int productId);
}

