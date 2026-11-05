using Application.DTOs.Products;
using Core.Entities.Products;

namespace Application.IServices.Products;

public interface IProductPriceService
{
    /// <summary>The price choices for a product, converted into <paramref name="displayCurrencyId"/>. Null if the product doesn't exist.</summary>
    Task<ProductPriceOptions?> GetOptionsAsync(int productId, int displayCurrencyId);

    /// <summary>The product's extra prices in display order (not including its default selling price).</summary>
    Task<List<ProductPrice>> GetTiersAsync(int productId);

    /// <summary>Every product's extra prices in display order, keyed by product id (products without any are absent).</summary>
    Task<Dictionary<int, List<ProductPrice>>> GetAllTiersAsync();

    /// <summary>Replaces the product's extra prices. Rows with a blank name or a price of zero or less are dropped.</summary>
    Task ReplaceTiersAsync(int productId, IEnumerable<ProductPriceInput> tiers);

    /// <summary>Replaces the extra prices of several products in one save. Products not in <paramref name="tiersByProduct"/> are left alone.</summary>
    Task ReplaceTiersAsync(IReadOnlyDictionary<int, List<ProductPriceInput>> tiersByProduct);
}
