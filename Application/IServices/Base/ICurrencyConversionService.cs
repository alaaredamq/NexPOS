namespace Application.IServices.Base;

public interface ICurrencyConversionService
{
    decimal ConvertAmount(decimal amount, int fromCurrencyId, int toCurrencyId, int? supplierId = null);
    Task<decimal> ConvertAmountAsync(decimal amount, int fromCurrencyId, int toCurrencyId, int? supplierId = null);
    int GetSystemDefaultCurrencyId();
    Task<int> GetSystemDefaultCurrencyIdAsync();
    int GetSupplierDefaultCurrencyId(int supplierId);
    Task<int> GetSupplierDefaultCurrencyIdAsync(int supplierId);
    Task<decimal> ConvertProductPriceAsync(decimal price, int productId, int targetCurrencyId);
    Task<decimal> ConvertCartTotalAsync(decimal total, int cartId, int targetCurrencyId);
}


