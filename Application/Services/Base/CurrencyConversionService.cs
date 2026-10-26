using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Application.Services.Base;

using Application.IServices.Base;

public class CurrencyConversionService : ICurrencyConversionService
{
    private readonly ErpDbContext _dbContext;

    public CurrencyConversionService(ErpDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public decimal ConvertAmount(decimal amount, int fromCurrencyId, int toCurrencyId, int? supplierId = null)
    {
        if (fromCurrencyId == toCurrencyId) return amount;

        var rate = GetRate(fromCurrencyId, toCurrencyId, supplierId);
        return amount * rate;
    }

    public async Task<decimal> ConvertAmountAsync(decimal amount, int fromCurrencyId, int toCurrencyId, int? supplierId = null)
    {
        if (fromCurrencyId == toCurrencyId) return amount;

        var rate = await GetRateAsync(fromCurrencyId, toCurrencyId, supplierId);
        return amount * rate;
    }

    public int GetSystemDefaultCurrencyId()
    {
        var defaultCurrency = _dbContext.Currencies.AsNoTracking().FirstOrDefault(c => c.IsDefault);
        return defaultCurrency?.Id ?? _dbContext.Currencies.AsNoTracking().First().Id;
    }

    public async Task<int> GetSystemDefaultCurrencyIdAsync()
    {
        var defaultCurrency = await _dbContext.Currencies.AsNoTracking().FirstOrDefaultAsync(c => c.IsDefault);
        return defaultCurrency?.Id ?? (await _dbContext.Currencies.AsNoTracking().OrderBy(c => c.Id).FirstAsync()).Id;
    }

    public int GetSupplierDefaultCurrencyId(int supplierId)
    {
        var supplier = _dbContext.Suppliers.AsNoTracking().First(s => s.Id == supplierId);
        return supplier.CurrencyId;
    }

    public async Task<int> GetSupplierDefaultCurrencyIdAsync(int supplierId)
    {
        var supplier = await _dbContext.Suppliers.AsNoTracking().FirstAsync(s => s.Id == supplierId);
        return supplier.CurrencyId;
    }

    public async Task<decimal> ConvertProductPriceAsync(decimal price, int productId, int targetCurrencyId)
    {
        var product = await _dbContext.Products
            .Include(p => p.Supplier)
            .AsNoTracking()
            .FirstAsync(p => p.Id == productId);

        var supplierCurrencyId = product.Supplier.CurrencyId;
        return await ConvertAmountAsync(price, supplierCurrencyId, targetCurrencyId, product.SupplierId);
    }

    public async Task<decimal> ConvertCartTotalAsync(decimal total, int cartId, int targetCurrencyId)
    {
        var cart = await _dbContext.Carts
            .Include(c => c.CartItems)
            .ThenInclude(ci => ci.Product)
            .ThenInclude(p => p.Supplier)
            .AsNoTracking()
            .FirstAsync(c => c.Id == cartId);

        var systemDefaultCurrencyId = await GetSystemDefaultCurrencyIdAsync();
        return await ConvertAmountAsync(total, systemDefaultCurrencyId, targetCurrencyId);
    }

    private decimal GetRate(int fromCurrencyId, int toCurrencyId, int? supplierId)
    {
        // Try supplier-specific
        var supplierRate = _dbContext.ExchangeRates
            .AsNoTracking()
            .FirstOrDefault(er => er.FromCurrencyId == fromCurrencyId && er.ToCurrencyId == toCurrencyId && er.SupplierId == supplierId);
        if (supplierRate != null) return supplierRate.Rate;

        // Try generic
        var generalRate = _dbContext.ExchangeRates
            .AsNoTracking()
            .FirstOrDefault(er => er.FromCurrencyId == fromCurrencyId && er.ToCurrencyId == toCurrencyId && er.SupplierId == null);
        if (generalRate != null) return generalRate.Rate;

        // Fallback via USD bridge
        var from = _dbContext.Currencies.AsNoTracking().First(c => c.Id == fromCurrencyId);
        var to = _dbContext.Currencies.AsNoTracking().First(c => c.Id == toCurrencyId);

        // Convert to USD then to target based on RateToUSD
        // amount_in_usd = amount * (1 / from.RateToUSD)
        // amount_in_target = amount_in_usd * to.RateToUSD
        var rateViaUsd = (1m / (decimal)from.RateToUSD) * (decimal)to.RateToUSD;
        return rateViaUsd;
    }

    private async Task<decimal> GetRateAsync(int fromCurrencyId, int toCurrencyId, int? supplierId)
    {
        // Try supplier-specific
        var supplierRate = await _dbContext.ExchangeRates
            .AsNoTracking()
            .FirstOrDefaultAsync(er => er.FromCurrencyId == fromCurrencyId && er.ToCurrencyId == toCurrencyId && er.SupplierId == supplierId);
        if (supplierRate != null) return supplierRate.Rate;

        // Try generic
        var generalRate = await _dbContext.ExchangeRates
            .AsNoTracking()
            .FirstOrDefaultAsync(er => er.FromCurrencyId == fromCurrencyId && er.ToCurrencyId == toCurrencyId && er.SupplierId == null);
        if (generalRate != null) return generalRate.Rate;

        // Fallback via USD bridge
        var from = await _dbContext.Currencies.AsNoTracking().FirstAsync(c => c.Id == fromCurrencyId);
        var to = await _dbContext.Currencies.AsNoTracking().FirstAsync(c => c.Id == toCurrencyId);

        // Convert to USD then to target based on RateToUSD
        // amount_in_usd = amount * (1 / from.RateToUSD)
        // amount_in_target = amount_in_usd * to.RateToUSD
        var rateViaUsd = (1m / (decimal)from.RateToUSD) * (decimal)to.RateToUSD;
        return rateViaUsd;
    }
}


