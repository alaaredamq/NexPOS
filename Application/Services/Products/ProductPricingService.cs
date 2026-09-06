using Application.IServices.Base;
using Application.IServices.Products;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Application.Services.Products;

public class ProductPricingService : IProductPricingService
{
    private readonly ErpDbContext _dbContext;
    private readonly ICurrencyConversionService _conversionService;

    public ProductPricingService(ErpDbContext dbContext, ICurrencyConversionService conversionService)
    {
        _dbContext = dbContext;
        _conversionService = conversionService;
    }

    public decimal GetPriceInSystemDefault(int productId)
    {
        var product = _dbContext.Products.AsNoTracking().First(p => p.Id == productId);
        var systemCurrencyId = _conversionService.GetSystemDefaultCurrencyId();
        return _conversionService.ConvertAmount(product.SellingPrice, product.CurrencyId, systemCurrencyId, product.SupplierId);
    }

    public decimal GetPriceForSupplierDefault(int productId)
    {
        var product = _dbContext.Products.AsNoTracking().First(p => p.Id == productId);
        var supplierCurrencyId = _conversionService.GetSupplierDefaultCurrencyId(product.SupplierId);
        return _conversionService.ConvertAmount(product.SellingPrice, product.CurrencyId, supplierCurrencyId, product.SupplierId);
    }
}


