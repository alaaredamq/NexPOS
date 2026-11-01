using Application.IServices.Products;
using Core.Entities.Products;
using Core.Enum;
using Microsoft.Extensions.Localization;
using Web.Api.Infrastructure;
using Web.Api.Money.Contracts;

namespace Web.Api.Money;

public interface ITaxApiService
{
    Task<PagedResult<TaxRateDto>> ListAsync(PagedQuery query);

    /// <summary>Null when the tax does not exist.</summary>
    Task<TaxRateDto?> GetAsync(int id);

    /// <summary>Throws <see cref="MoneyRuleException"/> for an unknown category or an end date before the start date.</summary>
    Task<TaxRateDto> CreateAsync(TaxRateRequest request);

    /// <summary>Null when the tax does not exist. Same rules as <see cref="CreateAsync"/>.</summary>
    Task<TaxRateDto?> UpdateAsync(int id, TaxRateRequest request);

    /// <summary>False when the tax does not exist.</summary>
    Task<bool> DeleteAsync(int id);

    /// <summary>Makes this the only default tax. Null when the tax does not exist.</summary>
    Task<TaxRateDto?> SetDefaultAsync(int id);

    /// <summary>Switches the tax off and ends it today. Null when the tax does not exist.</summary>
    Task<TaxRateDto?> DeactivateAsync(int id);

    /// <summary>Throws <see cref="MoneyRuleException"/> for an unknown tax.</summary>
    Task<TaxCalculationDto> CalculateAsync(TaxCalculationRequest request);

    Task<IReadOnlyList<MoneyOptionDto>> CategoryOptionsAsync();
}

/// <summary>
/// Tax rates (<c>Core.Entities.Products.TaxRate</c>, the entity behind the Razor Tax page) on top of the product <see cref="ITaxService"/>.
/// Nothing at the till reads these yet: <c>POSCheckoutService</c> adds no tax. The other "Tax" entity (<c>Sales.Tax</c>, <c>api/sales-taxes</c>) has no screen and no consumer and is not part of the SPA.
/// </summary>
public sealed class TaxApiService : ITaxApiService
{
    private static readonly string[] SortColumns = { "name", "rate", "type", "isDefault", "isActive", "effectiveFrom", "effectiveTo", "category" };

    private readonly ITaxService _taxes;
    private readonly IProductCategoryService _categories;
    private readonly IStringLocalizer<SharedResource> _text;

    public TaxApiService(ITaxService taxes, IProductCategoryService categories, IStringLocalizer<SharedResource> text)
    {
        _taxes = taxes;
        _categories = categories;
        _text = text;
    }

    public async Task<PagedResult<TaxRateDto>> ListAsync(PagedQuery query)
    {
        var names = await CategoryNamesAsync();
        IEnumerable<TaxRateDto> rows = (await _taxes.GetAllTaxesAsync()).Select(t => ToDto(t, names));

        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var term = query.Q.Trim();
            rows = rows.Where(t =>
                MoneySort.Contains(t.Name, term) || MoneySort.Contains(t.CategoryName, term) ||
                MoneySort.Contains(t.TaxCode, term) || MoneySort.Contains(t.Description, term));
        }

        return PagedResult<TaxRateDto>.From(Sort(rows, query), query);
    }

    public async Task<TaxRateDto?> GetAsync(int id)
    {
        var tax = await FindAsync(id);
        return tax is null ? null : ToDto(tax, await CategoryNamesAsync());
    }

    public async Task<TaxRateDto> CreateAsync(TaxRateRequest request)
    {
        var names = await CategoryNamesAsync();
        CheckRules(request, names);

        var tax = new TaxRate(request.Name.Trim(), request.Rate, (TaxType)request.Type, request.IsDefault, request.IsActive);
        Apply(tax, request);

        // The service clears the previous default first when this one is the default.
        return ToDto(await _taxes.CreateTaxAsync(tax), names);
    }

    public async Task<TaxRateDto?> UpdateAsync(int id, TaxRateRequest request)
    {
        var tax = await FindAsync(id);
        if (tax is null) return null;

        var names = await CategoryNamesAsync();
        CheckRules(request, names);

        tax.Name = request.Name.Trim();
        tax.Rate = request.Rate;
        tax.Type = (TaxType)request.Type;
        tax.IsDefault = request.IsDefault;
        tax.IsActive = request.IsActive;
        Apply(tax, request);

        return ToDto(await _taxes.UpdateTaxAsync(id, tax), names);
    }

    public async Task<bool> DeleteAsync(int id)
    {
        if (await FindAsync(id) is null) return false;

        await _taxes.DeleteTaxAsync(id);
        return true;
    }

    // These two go straight to the database (ExecuteUpdate), so the tax is read afterwards, not before: an entity tracked earlier in the request would still hold the old flags.
    public async Task<TaxRateDto?> SetDefaultAsync(int id)
        => await _taxes.SetDefaultTaxAsync(id) ? await GetAsync(id) : null;

    public async Task<TaxRateDto?> DeactivateAsync(int id)
        => await _taxes.DeactivateTaxAsync(id) ? await GetAsync(id) : null;

    public async Task<TaxCalculationDto> CalculateAsync(TaxCalculationRequest request)
    {
        if (request.TaxId is { } taxId && await FindAsync(taxId) is null)
            throw new MoneyRuleException(nameof(request.TaxId), _text["Mny_UnknownTax"].Value);

        var type = request.TaxType is { } t ? (TaxType?)t : null;
        return new TaxCalculationDto
        {
            BaseAmount = request.BaseAmount,
            TaxAmount = await _taxes.CalculateTaxAmountAsync(request.BaseAmount, request.TaxId, type),
            TotalAmount = await _taxes.CalculateAmountWithTaxAsync(request.BaseAmount, request.TaxId, type),
        };
    }

    public async Task<IReadOnlyList<MoneyOptionDto>> CategoryOptionsAsync()
        => (await _categories.GetAllAsync())
            .OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(c => new MoneyOptionDto { Id = c.Id, Name = c.Name })
            .ToList();

    private async Task<TaxRate?> FindAsync(int id) => (await _taxes.GetAllTaxesAsync()).FirstOrDefault(t => t.Id == id);

    private async Task<Dictionary<int, string>> CategoryNamesAsync() => (await _categories.GetAllAsync()).ToDictionary(c => c.Id, c => c.Name);

    private void CheckRules(TaxRateRequest request, Dictionary<int, string> categories)
    {
        if (request.CategoryId is { } categoryId && !categories.ContainsKey(categoryId))
            throw new MoneyRuleException(nameof(request.CategoryId), _text["Mny_UnknownCategory"].Value);
        if (request is { EffectiveFrom: { } from, EffectiveTo: { } to } && to < from)
            throw new MoneyRuleException(nameof(request.EffectiveTo), _text["Mny_TaxDatesOrder"].Value);
    }

    private static void Apply(TaxRate tax, TaxRateRequest request)
    {
        tax.Description = Clean(request.Description);
        tax.TaxCode = Clean(request.TaxCode);
        tax.IsInclusive = request.IsInclusive;
        tax.ApplyToAllProducts = request.ApplyToAllProducts;
        tax.CategoryId = request.CategoryId;
        tax.EffectiveFrom = request.EffectiveFrom;
        tax.EffectiveTo = request.EffectiveTo;
    }

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static IEnumerable<TaxRateDto> Sort(IEnumerable<TaxRateDto> rows, PagedQuery query)
    {
        var key = SortColumns.FirstOrDefault(c => string.Equals(c, query.Sort, StringComparison.OrdinalIgnoreCase)) ?? "name";
        var desc = query.Descending;
        return key switch
        {
            "rate" => MoneySort.By(rows, t => t.Rate, desc),
            "type" => MoneySort.By(rows, t => t.Type, desc),
            "isDefault" => MoneySort.By(rows, t => t.IsDefault, desc),
            "isActive" => MoneySort.By(rows, t => t.IsActive, desc),
            "effectiveFrom" => MoneySort.By(rows, t => t.EffectiveFrom, desc),
            "effectiveTo" => MoneySort.By(rows, t => t.EffectiveTo, desc),
            "category" => MoneySort.ByText(rows, t => t.CategoryName ?? string.Empty, desc),
            _ => MoneySort.ByText(rows, t => t.Name, desc),
        };
    }

    private static TaxRateDto ToDto(TaxRate t, Dictionary<int, string> categories) => new()
    {
        Id = t.Id,
        Name = t.Name,
        Description = t.Description,
        Rate = t.Rate,
        Type = (int)t.Type,
        TaxCode = t.TaxCode,
        IsDefault = t.IsDefault,
        IsActive = t.IsActive,
        IsInclusive = t.IsInclusive,
        ApplyToAllProducts = t.ApplyToAllProducts,
        CategoryId = t.CategoryId,
        CategoryName = t.CategoryId is { } id ? categories.GetValueOrDefault(id) : null,
        EffectiveFrom = t.EffectiveFrom,
        EffectiveTo = t.EffectiveTo,
        CreatedAt = t.CreatedAt,
    };
}
