using Application.DTOs.Products;
using Application.IServices.Base;
using Application.IServices.Products;
using Core.Entities.Products;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Web.Api.Infrastructure;
using Web.Api.Products.Contracts;

namespace Web.Api.Products;

/// <summary>Filters of the products list besides the shared paging/search query.</summary>
public sealed class ProductListFilter
{
    [FromQuery(Name = "categoryId")]
    public int? CategoryId { get; set; }

    [FromQuery(Name = "supplierId")]
    public int? SupplierId { get; set; }

    /// <summary><c>active</c> or <c>inactive</c>; anything else = both.</summary>
    [FromQuery(Name = "status")]
    public string? Status { get; set; }

    /// <summary><c>out</c>, <c>low</c> or <c>ok</c> (see <see cref="ProductStockLevel"/>); anything else = all.</summary>
    [FromQuery(Name = "stock")]
    public string? Stock { get; set; }
}

/// <summary>What the products API needs from the catalogue. Business rules stay in the Application services it calls.</summary>
public interface IProductCatalogService
{
    Task<PagedResult<ProductListItemDto>> ListAsync(PagedQuery query, ProductListFilter filter);

    Task<ProductListSummaryDto> SummaryAsync();

    Task<ProductLookupsDto> LookupsAsync();

    Task<IReadOnlyList<ProductOptionDto>> OptionsAsync(string? q, int take);

    Task<ProductDetailDto?> GetAsync(int id);

    Task<string> GenerateBarcodeAsync();

    /// <summary>The rule violations of a save as <c>errors{field:[message]}</c>; empty when the request may be saved. <paramref name="id"/> is null for a create.</summary>
    Task<Dictionary<string, string[]>> ValidateAsync(ProductSaveRequest request, int? id);

    Task<ProductDetailDto> CreateAsync(ProductSaveRequest request);

    /// <summary>Null when the product does not exist (or was deleted).</summary>
    Task<ProductDetailDto?> UpdateAsync(int id, ProductSaveRequest request);

    /// <summary>False when the product does not exist (or was already deleted).</summary>
    Task<bool> DeleteAsync(int id);
}

public sealed class ProductCatalogService : IProductCatalogService
{
    /// <summary>The old list page counted "low stock" as above 0 and at most 5 units (a literal in its script).</summary>
    public const decimal LowStockThreshold = 5;

    private readonly IProductService _products;
    private readonly IProductCategoryService _categories;
    private readonly ISupplierService _suppliers;
    private readonly ICurrencyService _currencies;
    private readonly IProductPriceService _prices;
    private readonly IStringLocalizer<SharedResource> _text;

    public ProductCatalogService(
        IProductService products,
        IProductCategoryService categories,
        ISupplierService suppliers,
        ICurrencyService currencies,
        IProductPriceService prices,
        IStringLocalizer<SharedResource> text)
    {
        _products = products;
        _categories = categories;
        _suppliers = suppliers;
        _currencies = currencies;
        _prices = prices;
        _text = text;
    }

    public async Task<PagedResult<ProductListItemDto>> ListAsync(PagedQuery query, ProductListFilter filter)
    {
        var rows = (await _products.GetAllAsync() ?? new List<Product>()).AsEnumerable();

        // Same search as the old list: name, barcode or description.
        var term = query.Q?.Trim();
        if (!string.IsNullOrEmpty(term))
        {
            rows = rows.Where(p =>
                Contains(p.Name, term) || Contains(p.Barcode, term) || Contains(p.Description, term));
        }

        if (filter.CategoryId is > 0) rows = rows.Where(p => p.CategoryId == filter.CategoryId);
        if (filter.SupplierId is > 0) rows = rows.Where(p => p.SupplierId == filter.SupplierId);

        rows = filter.Status?.ToLowerInvariant() switch
        {
            "active" => rows.Where(p => p.IsActive),
            "inactive" => rows.Where(p => !p.IsActive),
            _ => rows,
        };

        rows = filter.Stock?.ToLowerInvariant() switch
        {
            ProductStockLevel.Out => rows.Where(p => LevelOf(p) == ProductStockLevel.Out),
            ProductStockLevel.Low => rows.Where(p => LevelOf(p) == ProductStockLevel.Low),
            ProductStockLevel.Ok => rows.Where(p => LevelOf(p) == ProductStockLevel.Ok),
            _ => rows,
        };

        var sorted = Sort(rows, query.Sort, query.Descending).ToList();
        var page = sorted.Skip(query.Skip).Take(query.PageSize).Select(ToListItem).ToList();
        return PagedResult<ProductListItemDto>.Of(page, sorted.Count, query);
    }

    public async Task<ProductListSummaryDto> SummaryAsync()
    {
        var all = await _products.GetAllAsync() ?? new List<Product>();
        return new ProductListSummaryDto
        {
            Total = all.Count,
            Active = all.Count(p => p.IsActive),
            LowStock = all.Count(p => LevelOf(p) == ProductStockLevel.Low),
            OutOfStock = all.Count(p => LevelOf(p) == ProductStockLevel.Out),
            LowStockThreshold = LowStockThreshold,
        };
    }

    public async Task<ProductLookupsDto> LookupsAsync()
    {
        var categories = (await _categories.GetAllAsync()).Where(c => c.IsActive)
            .OrderBy(c => c.Name)
            .Select(c => new ProductCategoryOption { Id = c.Id, Name = c.Name })
            .ToList();

        // "No Supplier" is what a new product starts with when the shop has it (also if it was switched off).
        var allSuppliers = await _suppliers.GetAllAsync();
        var active = allSuppliers.Where(s => s.IsActive).ToList();
        var noSupplier = active.FirstOrDefault(IsNoSupplier) ?? allSuppliers.FirstOrDefault(IsNoSupplier);
        if (noSupplier != null && active.All(s => s.Id != noSupplier.Id)) active.Insert(0, noSupplier);

        var suppliers = active
            .OrderBy(s => s.OrderNumber ?? int.MaxValue)
            .ThenBy(s => s.Name)
            .Select(s => new ProductSupplierOption { Id = s.Id, Name = s.Name, DiscountRate = s.DiscountRate })
            .ToList();

        var currencies = (await _currencies.GetAllAsync())
            .Select(c => new ProductCurrencyOption { Id = c.Id, Code = c.Code, Notation = c.Notation, IsDefault = c.IsDefault })
            .ToList();

        return new ProductLookupsDto
        {
            Categories = categories,
            Suppliers = suppliers,
            Currencies = currencies,
            DefaultSupplierId = noSupplier?.Id ?? suppliers.FirstOrDefault()?.Id,
            DefaultCurrencyId = (await _currencies.GetDefaultAsync())?.Id,
        };
    }

    public async Task<IReadOnlyList<ProductOptionDto>> OptionsAsync(string? q, int take)
    {
        var found = await _products.SearchAsync(q, null, null, onlyActive: true);
        return found.OrderBy(p => p.Name).Take(take)
            .Select(p => new ProductOptionDto { Id = p.Id, Name = p.Name, Barcode = p.Barcode })
            .ToList();
    }

    public async Task<ProductDetailDto?> GetAsync(int id)
    {
        var product = await FindAsync(id);
        return product == null ? null : await ToDetailAsync(product);
    }

    public async Task<string> GenerateBarcodeAsync()
    {
        string barcode;
        var attempts = 0;
        do
        {
            barcode = ProductBarcode.NewEan13(DateTime.Now);
            attempts++;
        }
        while (await _products.GetByBarcodeAsync(barcode) != null && attempts < 10);

        return barcode;
    }

    public async Task<Dictionary<string, string[]>> ValidateAsync(ProductSaveRequest request, int? id)
    {
        var errors = new Dictionary<string, string[]>();

        // Two products sharing a barcode make a POS scan ring up whichever one the lookup finds first.
        if (!string.IsNullOrWhiteSpace(request.Barcode))
        {
            var same = await _products.GetByBarcodeAsync(request.Barcode);
            if (same != null && same.Id != id) errors["barcode"] = new[] { _text["Prod_BarcodeExists"].Value };
        }

        // Extra prices need a name and an amount, and may never be below the purchase price
        // (the floor the till enforces on a custom price). A fully blank row is ignored.
        for (var i = 0; i < request.PriceTiers.Count; i++)
        {
            var tier = request.PriceTiers[i];
            if (string.IsNullOrWhiteSpace(tier.Name) && tier.Price == 0) continue;

            if (string.IsNullOrWhiteSpace(tier.Name))
                errors[$"priceTiers[{i}].name"] = new[] { _text["Price name required"].Value };
            else if (tier.Price <= 0)
                errors[$"priceTiers[{i}].price"] = new[] { _text["Price amount required"].Value };
            else if (tier.Price < request.PurchasePrice)
                errors[$"priceTiers[{i}].price"] = new[] { _text["Price below purchase"].Value };
        }

        // The create form marks both pack fields as required.
        if (id == null && request.IsPack)
        {
            if (request.PackSize is not > 0) errors["packSize"] = new[] { _text["Prod_PackSizeRequired"].Value };
            if (request.SingleProductId is not > 0) errors["singleProductId"] = new[] { _text["Prod_PackSingleRequired"].Value };
        }

        return errors;
    }

    public async Task<ProductDetailDto> CreateAsync(ProductSaveRequest request)
    {
        var product = new Product
        {
            Name = request.Name,
            Description = request.Description,
            Barcode = request.Barcode,
            CostPrice = request.PurchasePrice,
            SellingPrice = request.SellingPrice,
            StockQuantity = request.StockQuantity,
            MinimumStock = request.MinStockLevel,
            CategoryId = request.CategoryId,
            IsPack = request.IsPack,
            PackSize = request.IsPack ? request.PackSize : null,
            SingleProductId = request.IsPack ? request.SingleProductId : null,
            BrandId = 1, // the old form never saved its Brand box either; every product uses the default brand
            SupplierId = request.SupplierId,
            CurrencyId = await ResolveCurrencyAsync(request.CurrencyId, fallback: 1),
            IsActive = request.IsActive,
            Unit = request.Unit,
        };

        var created = await _products.CreateAsync(product);
        await _prices.ReplaceTiersAsync(created.Id, ToInputs(request.PriceTiers));
        return await ToDetailAsync(await _products.GetByIdAsync(created.Id));
    }

    public async Task<ProductDetailDto?> UpdateAsync(int id, ProductSaveRequest request)
    {
        var product = await FindAsync(id);
        if (product == null) return null;

        product.Name = request.Name;
        product.Description = request.Description;
        product.Barcode = request.Barcode;
        product.CostPrice = request.PurchasePrice;
        product.SellingPrice = request.SellingPrice;
        product.StockQuantity = request.StockQuantity;
        product.MinimumStock = request.MinStockLevel;
        product.CategoryId = request.CategoryId;
        product.SupplierId = request.SupplierId;
        product.CurrencyId = await ResolveCurrencyAsync(request.CurrencyId, fallback: product.CurrencyId);
        product.IsActive = request.IsActive;
        product.Unit = request.Unit;

        await _products.UpdateAsync(id, product);
        await _prices.ReplaceTiersAsync(id, ToInputs(request.PriceTiers));
        return await ToDetailAsync(await _products.GetByIdAsync(id));
    }

    public async Task<bool> DeleteAsync(int id)
    {
        if (await FindAsync(id) == null) return false;
        await _products.DeleteAsync(id);
        return true;
    }

    // ---------------------------------------------------------------------------------------------

    /// <summary>The repository throws for an unknown id; for the API that is a plain "not found".</summary>
    private async Task<Product?> FindAsync(int id)
    {
        if (id <= 0) return null;
        try
        {
            return await _products.GetByIdAsync(id);
        }
        catch (ArgumentNullException)
        {
            return null;
        }
    }

    private async Task<int> ResolveCurrencyAsync(int requested, int fallback)
    {
        if (requested > 0) return requested;
        return (await _currencies.GetDefaultAsync())?.Id ?? fallback;
    }

    private static List<ProductPriceInput> ToInputs(IEnumerable<ProductPriceTierDto> tiers)
        => tiers.Select(t => new ProductPriceInput { Name = t.Name, Price = t.Price }).ToList();

    private static bool IsNoSupplier(Supplier s) => s.Name.Equals("No Supplier", StringComparison.OrdinalIgnoreCase);

    private static bool Contains(string? text, string term)
        => text != null && text.Contains(term, StringComparison.OrdinalIgnoreCase);

    private static string LevelOf(Product p)
        => p.StockQuantity <= 0 ? ProductStockLevel.Out
            : p.StockQuantity <= LowStockThreshold ? ProductStockLevel.Low
            : ProductStockLevel.Ok;

    private static IEnumerable<Product> Sort(IEnumerable<Product> rows, string? sort, bool descending)
    {
        Func<Product, object?> key = (sort ?? "name").ToLowerInvariant() switch
        {
            "barcode" => p => p.Barcode ?? "",
            "category" => p => p.Category?.Name ?? "",
            "supplier" => p => p.Supplier?.Name ?? "",
            "purchaseprice" => p => p.CostPrice ?? 0,
            "sellingprice" => p => p.SellingPrice,
            "stock" => p => p.StockQuantity,
            "status" => p => p.IsActive,
            _ => p => p.Name ?? "",
        };

        // Text sorts ignore case, like the old list; the id keeps equal rows in a stable order across pages.
        var comparer = new KeyComparer();
        return descending
            ? rows.OrderByDescending(key, comparer).ThenBy(p => p.Id)
            : rows.OrderBy(key, comparer).ThenBy(p => p.Id);
    }

    private sealed class KeyComparer : IComparer<object?>
    {
        public int Compare(object? x, object? y) => (x, y) switch
        {
            (string a, string b) => string.Compare(a, b, StringComparison.CurrentCultureIgnoreCase),
            (IComparable a, _) when y != null => a.CompareTo(y),
            _ => 0,
        };
    }

    private static ProductListItemDto ToListItem(Product p) => new()
    {
        Id = p.Id,
        Name = p.Name ?? "",
        Barcode = p.Barcode ?? "",
        CategoryId = p.CategoryId,
        CategoryName = p.Category?.Name,
        SupplierId = p.SupplierId,
        SupplierName = p.Supplier?.Name,
        CurrencyId = p.CurrencyId,
        CurrencyNotation = p.Currency?.Notation ?? "",
        PurchasePrice = p.CostPrice ?? 0,
        SellingPrice = p.SellingPrice,
        StockQuantity = p.StockQuantity,
        Unit = p.Unit,
        IsActive = p.IsActive,
        IsPack = p.IsPack,
        StockLevel = LevelOf(p),
    };

    private async Task<ProductDetailDto> ToDetailAsync(Product p)
    {
        var tiers = (await _prices.GetTiersAsync(p.Id))
            .Select(t => new ProductPriceTierDto { Name = t.Name, Price = t.Price })
            .ToList();

        string? singleName = null;
        if (p.SingleProductId is { } singleId)
        {
            singleName = p.SingleProduct?.Name ?? (await FindAsync(singleId))?.Name;
        }

        var cost = p.CostPrice ?? 0;
        return new ProductDetailDto
        {
            Id = p.Id,
            Name = p.Name ?? "",
            Description = p.Description,
            Barcode = p.Barcode ?? "",
            CategoryId = p.CategoryId,
            CategoryName = p.Category?.Name,
            SupplierId = p.SupplierId,
            SupplierName = p.Supplier?.Name,
            SupplierDiscountRate = p.Supplier?.DiscountRate ?? 0,
            CurrencyId = p.CurrencyId,
            CurrencyNotation = p.Currency?.Notation ?? "",
            PurchasePrice = cost,
            SellingPrice = p.SellingPrice,
            ProfitMargin = p.SellingPrice - cost,
            ProfitMarginPercent = cost > 0 ? (p.SellingPrice - cost) / cost * 100 : 0,
            StockValue = p.SellingPrice * p.StockQuantity,
            StockQuantity = p.StockQuantity,
            MinStockLevel = p.MinimumStock ?? 0,
            Unit = p.Unit,
            IsActive = p.IsActive,
            IsPack = p.IsPack,
            PackSize = p.PackSize,
            SingleProductId = p.SingleProductId,
            SingleProductName = singleName,
            PriceTiers = tiers,
            CreatedAt = p.CreatedAt,
            UpdatedAt = p.UpdatedAt,
            StockLevel = LevelOf(p),
        };
    }
}

/// <summary>EAN-13 barcodes in the shop's own range (prefix <c>2</c>), as the old "Generate" button made them.</summary>
public static class ProductBarcode
{
    public static string NewEan13(DateTime now)
    {
        // Prefix "2" (in-store range) + date, then pad with random digits to 12.
        var barcode = "2" + now.ToString("yyMMdd");
        while (barcode.Length < 12) barcode += Random.Shared.Next(0, 10).ToString();

        return barcode + CheckDigit(barcode);
    }

    /// <summary>The EAN-13 check digit of the first twelve digits.</summary>
    public static int CheckDigit(string first12)
    {
        var sum = 0;
        for (var i = 0; i < 12; i++)
        {
            var digit = first12[i] - '0';
            sum += i % 2 == 0 ? digit : digit * 3;
        }

        return (10 - sum % 10) % 10;
    }
}
