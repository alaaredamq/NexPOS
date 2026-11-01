using Application.IServices.Base;
using Application.IServices.Products;
using Core.Entities.Products;
using Web.Api.Catalog.Contracts;

namespace Web.Api.Catalog;

public interface IDisplayOrderApiService
{
    Task<DisplayOrderDto> GetAsync();

    /// <summary>Puts the listed rows first, in that order, and the rows not listed after them (so a stale list never leaves two rows with the same position).</summary>
    Task ReorderAsync(IReadOnlyList<int> ids);

    /// <summary>Adds the product at the end. Throws <see cref="DisplayOrderException"/> when it is unknown or already there.</summary>
    Task<DisplayOrderItemDto> AddAsync(int productId);

    /// <summary>False when the row does not exist.</summary>
    Task<bool> RemoveAsync(int id);

    Task<AvailableProductsDto> AvailableAsync(string? term, int skip, int take);

    Task<QuickAddOptionsDto> QuickAddOptionsAsync();

    Task<QuickAddResultDto> QuickAddAsync(QuickAddProductRequest request);
}

public sealed class DisplayOrderException : Exception
{
    public const string ProductNotFound = "product_not_found";
    public const string AlreadyInDisplayOrder = "already_in_display_order";

    public DisplayOrderException(string code, string message) : base(message) => Code = code;

    public string Code { get; }
}

/// <summary>
/// The POS main screen's product list. The cashier's grid (<c>MainProductsItemComponent</c>) reads <c>ProductDisplayOrder</c>, so this -
/// and not the separate <c>MainScreenProduct</c> table - is the live "pinned products" list.
/// </summary>
public sealed class DisplayOrderApiService : IDisplayOrderApiService
{
    public const int MaxTake = 50;

    // The old quick-add used these ids when the form did not choose one; kept so both UIs create the same product.
    private const int DefaultCategoryId = 1;
    private const int DefaultSupplierId = 1;
    private const int DefaultBrandId = 1;
    private const decimal DefaultMinimumStock = 5;
    private const decimal DefaultCostRatio = 0.7m;

    private readonly IProductDisplayOrderService _order;
    private readonly IProductService _products;
    private readonly ICurrencyService _currencies;

    public DisplayOrderApiService(IProductDisplayOrderService order, IProductService products, ICurrencyService currencies)
    {
        _order = order;
        _products = products;
        _currencies = currencies;
    }

    public async Task<DisplayOrderDto> GetAsync()
    {
        var rows = await ActiveRowsAsync();
        var items = rows.Select(ToDto).ToList();

        return new DisplayOrderDto
        {
            Items = items,
            Total = items.Count,
            Active = items.Count(i => i.IsActive),
            Inactive = items.Count(i => !i.IsActive),
            LowStock = items.Count(i => i.IsLowStock),
        };
    }

    public async Task ReorderAsync(IReadOnlyList<int> ids)
    {
        var rows = await ActiveRowsAsync();
        var known = rows.Select(r => r.Id).ToHashSet();

        var first = ids.Where(known.Contains).Distinct().ToList();
        var chosen = first.ToHashSet();
        var ordered = first.Concat(rows.Select(r => r.Id).Where(id => !chosen.Contains(id))).ToList();

        // The repository copies only DisplayOrder + IsActive onto the stored rows; only active rows are listed, so IsActive stays true.
        var payload = ordered.Select((id, index) => new ProductDisplayOrder { Id = id, DisplayOrder = index + 1, IsActive = true }).ToList();
        await _order.UpdateDisplayOrderAsync(payload);
    }

    public async Task<DisplayOrderItemDto> AddAsync(int productId)
    {
        var product = await FindProductAsync(productId)
            ?? throw new DisplayOrderException(DisplayOrderException.ProductNotFound, "Product not found");

        var all = (await _order.GetAllAsync()).ToList();
        if (all.Any(d => d.ProductId == productId))
            throw new DisplayOrderException(DisplayOrderException.AlreadyInDisplayOrder, "This product is already on the main screen.");

        var created = await _order.CreateAsync(new ProductDisplayOrder
        {
            ProductId = productId,
            DisplayOrder = NextPosition(all),
            IsActive = true,
        });

        created.Product = product;
        return ToDto(created);
    }

    public async Task<bool> RemoveAsync(int id)
    {
        var all = await _order.GetAllAsync();
        if (all.All(d => d.Id != id)) return false;

        await _order.DeleteAsync(id);
        return true;
    }

    public async Task<AvailableProductsDto> AvailableAsync(string? term, int skip, int take)
    {
        skip = Math.Max(skip, 0);
        take = Math.Clamp(take, 1, MaxTake);

        // One extra row tells the client whether another page exists.
        var page = await _products.SearchAvailableForDisplayOrderAsync(term, skip, take + 1);

        return new AvailableProductsDto
        {
            HasMore = page.Count > take,
            Items = page.Take(take).Select(p => new AvailableProductDto
            {
                Id = p.Id,
                Name = p.Name,
                Image = p.Image,
                CategoryName = p.Category?.Name,
                Price = p.SellingPrice,
                CurrencyNotation = p.Currency?.Notation ?? "$",
            }).ToList(),
        };
    }

    public async Task<QuickAddOptionsDto> QuickAddOptionsAsync()
    {
        var currencies = await _currencies.GetAllAsync();
        var fallback = await _currencies.GetDefaultAsync();

        return new QuickAddOptionsDto
        {
            DefaultCurrencyId = fallback?.Id ?? 0,
            Currencies = currencies.Select(c => new CurrencyOptionDto { Id = c.Id, Code = c.Code, Notation = c.Notation }).ToList(),
        };
    }

    public async Task<QuickAddResultDto> QuickAddAsync(QuickAddProductRequest request)
    {
        var barcode = request.Barcode?.Trim();
        if (string.IsNullOrEmpty(barcode) || request.GenerateBarcode) barcode = GenerateBarcode(DateTime.Now, Random.Shared);

        var currencyId = request.CurrencyId;
        if (currencyId <= 0) currencyId = (await _currencies.GetDefaultAsync())?.Id ?? 1;

        var product = await _products.CreateAsync(new Product
        {
            Name = request.Name.Trim(),
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            Barcode = barcode,
            SellingPrice = request.Price,
            CostPrice = request.Price * DefaultCostRatio,
            CategoryId = DefaultCategoryId,
            SupplierId = DefaultSupplierId,
            BrandId = DefaultBrandId,
            CurrencyId = currencyId,
            StockQuantity = request.StockQuantity > 0 ? request.StockQuantity : 0,
            MinimumStock = DefaultMinimumStock,
            IsActive = true,
        });

        var created = await _order.CreateAsync(new ProductDisplayOrder
        {
            ProductId = product.Id,
            DisplayOrder = NextPosition((await _order.GetAllAsync()).ToList()),
            IsActive = true,
        });

        return new QuickAddResultDto { ProductId = product.Id, DisplayOrderId = created.Id, Barcode = barcode };
    }

    /// <summary>A 13-digit EAN-13: <c>2</c> + the date as yyMMdd + 5 random digits + the check digit. Same recipe as the Razor page.</summary>
    public static string GenerateBarcode(DateTime today, Random random)
    {
        var digits = "2" + today.ToString("yyMMdd");
        while (digits.Length < 12) digits += random.Next(0, 10).ToString();
        return digits + CheckDigit(digits);
    }

    /// <summary>EAN-13 check digit of the first 12 digits.</summary>
    public static int CheckDigit(string twelveDigits)
    {
        var sum = 0;
        for (var i = 0; i < 12; i++)
        {
            var digit = twelveDigits[i] - '0';
            sum += i % 2 == 0 ? digit : digit * 3;
        }

        return (10 - sum % 10) % 10;
    }

    private async Task<List<ProductDisplayOrder>> ActiveRowsAsync()
        => (await _order.GetActiveDisplayOrderAsync()).Where(r => r.Product is not null).OrderBy(r => r.DisplayOrder).ThenBy(r => r.Id).ToList();

    // After the largest position of any row, active or not, as the old page did.
    private static int NextPosition(IReadOnlyCollection<ProductDisplayOrder> all) => all.Count == 0 ? 1 : all.Max(d => d.DisplayOrder) + 1;

    // The repository throws ArgumentNullException (not null) for an unknown id.
    private async Task<Product?> FindProductAsync(int id)
    {
        try
        {
            return await _products.GetByIdAsync(id);
        }
        catch (ArgumentNullException)
        {
            return null;
        }
    }

    private static DisplayOrderItemDto ToDto(ProductDisplayOrder row) => new()
    {
        Id = row.Id,
        ProductId = row.ProductId,
        Order = row.DisplayOrder,
        IsActive = row.IsActive,
        Name = row.Product.Name,
        Description = row.Product.Description,
        Image = row.Product.Image,
        CategoryName = row.Product.Category?.Name,
        Price = row.Product.SellingPrice,
        CurrencyNotation = row.Product.Currency?.Notation ?? "$",
        StockQuantity = row.Product.StockQuantity,
        IsLowStock = row.Product.StockQuantity <= (row.Product.MinimumStock ?? 0),
    };
}
