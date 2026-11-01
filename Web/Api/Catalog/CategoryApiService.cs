using Application.IServices.Base;
using Application.IServices.Products;
using Core.Entities.Products;
using Web.Api.Catalog.Contracts;
using Web.Api.Infrastructure;

namespace Web.Api.Catalog;

public interface ICategoryApiService
{
    Task<PagedResult<CategoryDto>> ListAsync(PagedQuery query);

    Task<CategorySummaryDto> SummaryAsync();

    Task<CategoryDetailsDto?> GetDetailsAsync(int id);

    Task<CategoryDto> CreateAsync(CategoryRequest request);

    /// <summary>Null when the category does not exist.</summary>
    Task<CategoryDto?> UpdateAsync(int id, CategoryRequest request);

    /// <summary>Throws <see cref="CategoryInUseException"/> while products still point at the category; false when it does not exist.</summary>
    Task<bool> DeleteAsync(int id);
}

/// <summary>The category is still referenced by products (deleted ones kept for sales history included): deactivate it instead.</summary>
public sealed class CategoryInUseException : Exception
{
    public CategoryInUseException(string message, Exception inner) : base(message, inner)
    {
    }
}

/// <summary>Maps the shared category service to the API's DTOs. The rules (cache, "still used by products") stay in <see cref="IProductCategoryService"/>.</summary>
public sealed class CategoryApiService : ICategoryApiService
{
    /// <summary>The details page shows this many products; the rest is "view all" in the products list.</summary>
    public const int DetailsProductLimit = 20;

    private static readonly string[] SortColumns = { "name", "description", "isActive", "productCount" };

    private readonly IProductCategoryService _categories;
    private readonly ICurrencyService _currencies;

    public CategoryApiService(IProductCategoryService categories, ICurrencyService currencies)
    {
        _categories = categories;
        _currencies = currencies;
    }

    public async Task<PagedResult<CategoryDto>> ListAsync(PagedQuery query)
    {
        var all = await _categories.GetAllAsync();
        var counts = await _categories.GetProductCountsAsync();

        IEnumerable<ProductCategory> filtered = all;
        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var term = query.Q.Trim();
            filtered = filtered.Where(c =>
                c.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                (c.Description ?? string.Empty).Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        var rows = filtered.Select(c => ToDto(c, counts.GetValueOrDefault(c.Id)));
        return PagedResult<CategoryDto>.From(Sort(rows, query), query);
    }

    public async Task<CategorySummaryDto> SummaryAsync()
    {
        var all = await _categories.GetAllAsync();
        var counts = await _categories.GetProductCountsAsync();
        var active = all.Count(c => c.IsActive);
        return new CategorySummaryDto { Total = all.Count, Active = active, Inactive = all.Count - active, Products = counts.Values.Sum() };
    }

    public async Task<CategoryDetailsDto?> GetDetailsAsync(int id)
    {
        var category = await _categories.GetByIdWithProductsAsync(id);
        if (category is null) return null;

        var products = category.Products.Where(p => !p.IsDeleted).ToList();
        var notation = (await _currencies.GetDefaultAsync())?.Notation ?? "$";

        return new CategoryDetailsDto
        {
            Category = ToDto(category, products.Count),
            ActiveProducts = products.Count(p => p.IsActive),
            TotalStock = products.Sum(p => p.StockQuantity),
            TotalValue = products.Sum(p => p.StockQuantity * p.SellingPrice),
            CurrencyNotation = notation,
            Products = products.OrderBy(p => p.Name).Take(DetailsProductLimit).Select(p => new CategoryProductDto
            {
                Id = p.Id,
                Name = p.Name,
                Description = p.Description,
                Barcode = p.Barcode,
                SellingPrice = p.SellingPrice,
                CurrencyNotation = p.Currency?.Notation ?? notation,
                StockQuantity = p.StockQuantity,
                MinimumStock = p.MinimumStock,
                IsActive = p.IsActive,
            }).ToList(),
        };
    }

    public async Task<CategoryDto> CreateAsync(CategoryRequest request)
    {
        var category = new ProductCategory(request.Name.Trim(), Clean(request.Description)) { IsActive = request.IsActive };
        var created = await _categories.CreateAsync(category);
        return ToDto(created, 0);
    }

    public async Task<CategoryDto?> UpdateAsync(int id, CategoryRequest request)
    {
        if (await FindAsync(id) is null) return null;

        // The service copies Name / Description / IsActive onto the tracked entity and clears the category cache.
        await _categories.UpdateAsync(id, new ProductCategory(request.Name.Trim(), Clean(request.Description)) { IsActive = request.IsActive });

        var counts = await _categories.GetProductCountsAsync();
        var updated = await _categories.GetByIdAsync(id);
        return ToDto(updated, counts.GetValueOrDefault(id));
    }

    public async Task<bool> DeleteAsync(int id)
    {
        if (await FindAsync(id) is null) return false;

        try
        {
            await _categories.DeleteAsync(id);
        }
        catch (InvalidOperationException ex)
        {
            throw new CategoryInUseException(ex.Message, ex);
        }

        return true;
    }

    // The repository throws ArgumentNullException (not null) for an unknown id.
    private async Task<ProductCategory?> FindAsync(int id)
    {
        try
        {
            return await _categories.GetByIdAsync(id);
        }
        catch (ArgumentNullException)
        {
            return null;
        }
    }

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static CategoryDto ToDto(ProductCategory c, int productCount) => new()
    {
        Id = c.Id,
        Name = c.Name,
        Description = c.Description,
        IsActive = c.IsActive,
        ProductCount = productCount,
        CreatedAt = c.CreatedAt,
    };

    private static IEnumerable<CategoryDto> Sort(IEnumerable<CategoryDto> rows, PagedQuery query)
    {
        var column = SortColumns.FirstOrDefault(c => string.Equals(c, query.Sort, StringComparison.OrdinalIgnoreCase)) ?? "name";
        var desc = query.Descending;

        return column switch
        {
            "description" => Order(rows, c => c.Description ?? string.Empty, desc),
            "isActive" => Order(rows, c => c.IsActive, desc),
            "productCount" => Order(rows, c => c.ProductCount, desc),
            _ => Order(rows, c => c.Name, desc),
        };

        static IEnumerable<CategoryDto> Order<TKey>(IEnumerable<CategoryDto> source, Func<CategoryDto, TKey> key, bool descending)
        {
            // Name is the tie-breaker, so equal rows never swap between pages.
            var ordered = descending
                ? source.OrderByDescending(key, Comparer<TKey>.Default)
                : source.OrderBy(key, Comparer<TKey>.Default);
            return ordered.ThenBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(c => c.Id);
        }
    }
}
