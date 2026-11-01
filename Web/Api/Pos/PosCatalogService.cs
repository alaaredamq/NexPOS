using Application.IServices.Base;
using Application.IServices.Products;
using Application.Services.Products;
using Core.DTOs;
using Web.Api.Infrastructure;
using Web.Api.Pos.Contracts;

namespace Web.Api.Pos;

/// <summary>What the product grid, the search box and the price picker read.</summary>
public interface IPosCatalogService
{
    Task<IReadOnlyList<PosCategoryDto>> GetCategoriesAsync();

    /// <remarks><paramref name="mainScreen"/> asks for the shop's configured display order (every active product when none is configured).</remarks>
    Task<PosProductPageDto> GetProductsAsync(int? categoryId, bool mainScreen, PagedQuery query);

    Task<PosSearchResultDto> SearchAsync(string term, bool barcodeOnly, int skip);

    Task<PosProductDto?> GetProductAsync(int id);

    Task<PosPriceOptionsDto?> GetPriceOptionsAsync(int productId, int currencyId);
}

public sealed class PosCatalogService : IPosCatalogService
{
    /// <summary>Name matches per search request, as in the old till (the box asks for the next page as it scrolls).</summary>
    public const int SearchPageSize = 20;

    private readonly IFastProductSearchService _search;
    private readonly IProductCategoryService _categories;
    private readonly IProductDisplayOrderService _displayOrder;
    private readonly IProductPriceService _prices;
    private readonly ICurrencyService _currencies;

    public PosCatalogService(
        IFastProductSearchService search,
        IProductCategoryService categories,
        IProductDisplayOrderService displayOrder,
        IProductPriceService prices,
        ICurrencyService currencies)
    {
        _search = search;
        _categories = categories;
        _displayOrder = displayOrder;
        _prices = prices;
        _currencies = currencies;
    }

    public async Task<IReadOnlyList<PosCategoryDto>> GetCategoriesAsync()
    {
        var active = (await _search.GetAllActiveProductsAsync()).ToList();
        var counts = active.GroupBy(p => p.CategoryId).ToDictionary(g => g.Key, g => g.Count());

        // A chip for a category with nothing to sell is only noise.
        return (await _categories.GetAllAsync())
            .Where(c => counts.ContainsKey(c.Id))
            .OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(c => new PosCategoryDto { Id = c.Id, Name = c.Name, ProductCount = counts[c.Id] })
            .ToList();
    }

    public async Task<PosProductPageDto> GetProductsAsync(int? categoryId, bool mainScreen, PagedQuery query)
    {
        IEnumerable<ProductDTO> source;
        var isMainScreen = false;

        if (mainScreen)
        {
            var ordered = (await _displayOrder.GetActiveDisplayOrderAsync())
                .OrderBy(d => d.DisplayOrder)
                .Select(d => d.ProductId)
                .ToList();
            if (ordered.Count > 0)
            {
                // The display order holds the product ids; the cached list has the sellable (active, current-stock) rows.
                var byId = (await _search.GetAllActiveProductsAsync()).ToDictionary(p => p.Id);
                source = ordered.Where(byId.ContainsKey).Select(id => byId[id]);
                isMainScreen = true;
            }
            else
            {
                source = (await _search.GetAllActiveProductsAsync()).OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase);
            }
        }
        else
        {
            source = (await _search.GetAllActiveProductsAsync()).OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase);
        }

        if (categoryId is > 0) source = source.Where(p => p.CategoryId == categoryId);
        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var term = query.Q.Trim();
            source = source.Where(p =>
                p.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrWhiteSpace(p.Barcode) && p.Barcode.Contains(term, StringComparison.OrdinalIgnoreCase)));
        }

        var all = source.ToList();
        return new PosProductPageDto
        {
            Items = all.Skip(query.Skip).Take(query.PageSize).Select(Map).ToList(),
            Total = all.Count,
            Page = query.Page,
            PageSize = query.PageSize,
            IsMainScreen = isMainScreen,
        };
    }

    public async Task<PosSearchResultDto> SearchAsync(string term, bool barcodeOnly, int skip)
    {
        term = term.Trim();
        skip = Math.Max(skip, 0);

        // An exact barcode is a single hit and never a later page - the fastest path, so it goes first.
        if (skip == 0)
        {
            var hit = await _search.SearchByBarcodeAsync(term);
            if (hit is not null)
                return new PosSearchResultDto { SearchType = "barcode", Items = new[] { Map(hit) }, HasMore = false };
        }

        if (barcodeOnly)
            return new PosSearchResultDto { SearchType = "barcode", Items = Array.Empty<PosProductDto>(), HasMore = false };

        // One extra row tells whether another page exists.
        var rows = (await _search.SearchByNameAsync(term, SearchPageSize + 1, skip)).ToList();
        var hasMore = rows.Count > SearchPageSize;
        if (hasMore) rows.RemoveAt(SearchPageSize);

        return new PosSearchResultDto { SearchType = "name", Items = rows.Select(Map).ToList(), HasMore = hasMore };
    }

    public async Task<PosProductDto?> GetProductAsync(int id)
    {
        var product = await _search.GetByIdAsync(id);
        return product is null ? null : Map(product);
    }

    public async Task<PosPriceOptionsDto?> GetPriceOptionsAsync(int productId, int currencyId)
    {
        // Same rule as the cart: asking for a currency that does not exist or is switched off is a mistake of the caller.
        var currency = (await _currencies.GetAllAsync()).FirstOrDefault(c => c.Id == currencyId && c.IsActive);
        if (currency is null) throw new PosRuleException(StatusCodes.Status400BadRequest, PosCodes.CurrencyNotFound, "Pos_CurrencyNotFound", "currencyId");

        var options = await _prices.GetOptionsAsync(productId, currencyId);
        if (options is null) return null;

        return new PosPriceOptionsDto
        {
            ProductId = options.ProductId,
            CurrencyId = options.CurrencyId,
            MinPrice = options.MinPrice,
            HasCost = options.HasCost,
            Options = options.Options.Select(o => new PosPriceOptionDto { Id = o.Id, Name = o.Name, Price = o.Price }).ToList(),
        };
    }

    // ---- mapping -------------------------------------------------------------------------------------------------

    internal static PosProductDto Map(ProductDTO p) => new()
    {
        Id = p.Id,
        Name = p.Name,
        Barcode = p.Barcode ?? string.Empty,
        CategoryId = p.CategoryId,
        CategoryName = p.Category?.Name ?? string.Empty,
        CurrencyId = p.CurrencyId,
        CurrencyNotation = p.Currency?.Notation ?? string.Empty,
        SellingPrice = p.SellingPrice,
        SupplierDiscountRate = p.SupplierDiscountRate,
        StockQuantity = p.StockQuantity,
        Unit = p.Unit,
        UnitKind = UnitKindOf(p.Unit),
        IsPack = p.IsPack,
        PackSize = p.PackSize,
        SingleProductId = p.SingleProductId,
        CanChangePrice = !p.IsPack,
    };

    private static readonly HashSet<string> WeightUnits = new(StringComparer.OrdinalIgnoreCase)
        { "kg", "kilogram", "g", "gram", "mg", "milligram", "lb", "pound", "oz", "ounce" };

    private static readonly HashSet<string> VolumeUnits = new(StringComparer.OrdinalIgnoreCase)
        { "liter", "l", "litre", "ml", "milliliter", "gallon", "floz" };

    /// <summary>The same unit lists as the old <c>UnitConverter.js</c>: anything else is sold by the piece.</summary>
    internal static string UnitKindOf(string? unit)
    {
        var u = unit?.Trim();
        if (string.IsNullOrEmpty(u)) return "count";
        if (WeightUnits.Contains(u)) return "weight";
        if (VolumeUnits.Contains(u)) return "volume";
        return "count";
    }
}
