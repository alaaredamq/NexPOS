namespace Application.IServices.Products;

public interface IProductPricingService
{
    decimal GetPriceInSystemDefault(int productId);
    decimal GetPriceForSupplierDefault(int productId);
}


