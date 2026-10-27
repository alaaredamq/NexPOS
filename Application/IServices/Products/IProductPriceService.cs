using Application.DTOs.Products;
using Core.Entities.Products;

namespace Application.IServices.Products;

public interface IProductPriceService
{
    /// <summary>The price choices for a product, converted into <paramref name="displayCurrencyId"/>. Null if the product doesn't exist.</summary>
    Task<ProductPriceOptions?> GetOptionsAsync(int productId, int displayCurrencyId);

    /// <summary>The product's extra prices in display order (not including its default selling price).</summary>
    Task<List<ProductPrice>> GetTiersAsync(int productId);

    /// <summary>Replaces the product's extra prices. Rows with a blank name or a price of zero or less are dropped.</summary>
    Task ReplaceTiersAsync(int productId, IEnumerable<ProductPriceInput> tiers);
}
