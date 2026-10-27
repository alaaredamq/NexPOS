namespace Application.DTOs.Products;

/// <summary>An extra price typed into the product form.</summary>
public class ProductPriceInput
{
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
}

/// <summary>
/// One price the cashier can choose. <paramref name="Id"/> is null for the product's default price
/// (<c>Product.SellingPrice</c>); the till labels that one itself.
/// </summary>
public record PriceOption(int? Id, string Name, decimal Price);

/// <summary>
/// Everything the till needs to offer a price choice for one product, already converted into the
/// currency the cart is showing. <paramref name="MinPrice"/> is the lowest custom price allowed
/// (the purchase price); <paramref name="HasCost"/> is false when no purchase price is on file,
/// in which case any price above zero is accepted.
/// </summary>
public record ProductPriceOptions(
    int ProductId,
    int CurrencyId,
    decimal MinPrice,
    bool HasCost,
    IReadOnlyList<PriceOption> Options);
