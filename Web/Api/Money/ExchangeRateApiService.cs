using Application.IServices.Base;
using Application.IServices.Products;
using Core.Entities.Sales;
using Microsoft.Extensions.Localization;
using Web.Api.Infrastructure;
using Web.Api.Money.Contracts;

namespace Web.Api.Money;

public interface IExchangeRateApiService
{
    Task<PagedResult<ExchangeRateDto>> ListAsync(PagedQuery query, int? supplierId);

    /// <summary>Null when the rate does not exist.</summary>
    Task<ExchangeRateDto?> GetAsync(int id);

    /// <summary>Throws <see cref="MoneyRuleException"/> for an unknown currency or supplier, a pair of the same currency or a pair that already has a rate.</summary>
    Task<ExchangeRateDto> CreateAsync(ExchangeRateRequest request);

    /// <summary>Null when the rate does not exist. Same rules as <see cref="CreateAsync"/>.</summary>
    Task<ExchangeRateDto?> UpdateAsync(int id, ExchangeRateRequest request);

    /// <summary>False when the rate does not exist.</summary>
    Task<bool> DeleteAsync(int id);

    /// <summary>Suppliers for the picker of the form.</summary>
    Task<IReadOnlyList<MoneyOptionDto>> SupplierOptionsAsync();
}

/// <summary>
/// Pair-specific rates on top of <see cref="IExchangeRateService"/>. <c>CurrencyConversionService</c> (cart and product prices) tries a rate for the
/// supplier, then the store-wide one, then falls back to the USD bridge of <c>Currency.RateToUSD</c> - which is all the till's own conversion uses.
/// The table's unique index treats NULL suppliers as distinct, so "one store-wide rate per pair" is checked here.
/// </summary>
public sealed class ExchangeRateApiService : IExchangeRateApiService
{
    private static readonly string[] SortColumns = { "from", "to", "rate", "supplier", "createdAt" };

    private readonly IExchangeRateService _rates;
    private readonly ICurrencyService _currencies;
    private readonly ISupplierService _suppliers;
    private readonly IStringLocalizer<SharedResource> _text;

    public ExchangeRateApiService(IExchangeRateService rates, ICurrencyService currencies, ISupplierService suppliers, IStringLocalizer<SharedResource> text)
    {
        _rates = rates;
        _currencies = currencies;
        _suppliers = suppliers;
        _text = text;
    }

    public async Task<PagedResult<ExchangeRateDto>> ListAsync(PagedQuery query, int? supplierId)
    {
        var lookups = await LookupsAsync();
        IEnumerable<ExchangeRateDto> rows = (await _rates.GetAllAsync(supplierId)).Select(r => ToDto(r, lookups));

        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var term = query.Q.Trim();
            rows = rows.Where(r => MoneySort.Contains(r.FromCurrencyCode, term) || MoneySort.Contains(r.ToCurrencyCode, term) || MoneySort.Contains(r.SupplierName, term));
        }

        return PagedResult<ExchangeRateDto>.From(Sort(rows, query), query);
    }

    public async Task<ExchangeRateDto?> GetAsync(int id)
    {
        var rate = await FindAsync(id);
        return rate is null ? null : ToDto(rate, await LookupsAsync());
    }

    public async Task<ExchangeRateDto> CreateAsync(ExchangeRateRequest request)
    {
        var lookups = await LookupsAsync();
        await CheckRulesAsync(request, lookups, id: null);

        var created = await _rates.CreateAsync(new ExchangeRate(request.FromCurrencyId, request.ToCurrencyId, request.Rate, request.SupplierId));
        return ToDto(created, lookups);
    }

    public async Task<ExchangeRateDto?> UpdateAsync(int id, ExchangeRateRequest request)
    {
        if (await FindAsync(id) is null) return null;

        var lookups = await LookupsAsync();
        await CheckRulesAsync(request, lookups, id);

        await _rates.UpdateAsync(id, new ExchangeRate(request.FromCurrencyId, request.ToCurrencyId, request.Rate, request.SupplierId));
        return ToDto((await FindAsync(id))!, lookups);
    }

    public async Task<bool> DeleteAsync(int id)
    {
        if (await FindAsync(id) is null) return false;

        await _rates.DeleteAsync(id);
        return true;
    }

    public async Task<IReadOnlyList<MoneyOptionDto>> SupplierOptionsAsync()
        => (await _suppliers.GetAllAsync())
            .OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(s => new MoneyOptionDto { Id = s.Id, Name = s.Name })
            .ToList();

    private async Task<ExchangeRate?> FindAsync(int id) => (await _rates.GetAllAsync()).FirstOrDefault(r => r.Id == id);

    private async Task CheckRulesAsync(ExchangeRateRequest request, Lookups lookups, int? id)
    {
        if (!lookups.Currencies.ContainsKey(request.FromCurrencyId))
            throw new MoneyRuleException(nameof(request.FromCurrencyId), _text["Mny_UnknownCurrency"].Value);
        if (!lookups.Currencies.ContainsKey(request.ToCurrencyId))
            throw new MoneyRuleException(nameof(request.ToCurrencyId), _text["Mny_UnknownCurrency"].Value);
        if (request.FromCurrencyId == request.ToCurrencyId)
            throw new MoneyRuleException(nameof(request.ToCurrencyId), _text["Mny_RateSameCurrency"].Value);
        if (request.SupplierId is { } supplierId && !lookups.Suppliers.ContainsKey(supplierId))
            throw new MoneyRuleException(nameof(request.SupplierId), _text["Mny_UnknownSupplier"].Value);

        var clash = (await _rates.GetAllAsync()).Any(r =>
            r.Id != id && r.FromCurrencyId == request.FromCurrencyId && r.ToCurrencyId == request.ToCurrencyId && r.SupplierId == request.SupplierId);
        if (clash) throw new MoneyRuleException(nameof(request.ToCurrencyId), _text["Mny_RateExists"].Value);
    }

    private async Task<Lookups> LookupsAsync()
        => new(
            (await _currencies.GetAllAsync()).ToDictionary(c => c.Id, c => c.Code),
            (await _suppliers.GetAllAsync()).ToDictionary(s => s.Id, s => s.Name));

    private static IEnumerable<ExchangeRateDto> Sort(IEnumerable<ExchangeRateDto> rows, PagedQuery query)
    {
        var key = SortColumns.FirstOrDefault(c => string.Equals(c, query.Sort, StringComparison.OrdinalIgnoreCase)) ?? "from";
        var desc = query.Descending;
        return key switch
        {
            "to" => MoneySort.ByText(rows, r => r.ToCurrencyCode, desc),
            "rate" => MoneySort.By(rows, r => r.Rate, desc),
            "supplier" => MoneySort.ByText(rows, r => r.SupplierName ?? string.Empty, desc),
            "createdAt" => MoneySort.By(rows, r => r.CreatedAt, desc),
            _ => MoneySort.ByText(rows, r => r.FromCurrencyCode, desc),
        };
    }

    private static ExchangeRateDto ToDto(ExchangeRate r, Lookups lookups) => new()
    {
        Id = r.Id,
        FromCurrencyId = r.FromCurrencyId,
        FromCurrencyCode = lookups.Currencies.GetValueOrDefault(r.FromCurrencyId, string.Empty),
        ToCurrencyId = r.ToCurrencyId,
        ToCurrencyCode = lookups.Currencies.GetValueOrDefault(r.ToCurrencyId, string.Empty),
        Rate = r.Rate,
        SupplierId = r.SupplierId,
        SupplierName = r.SupplierId is { } supplierId ? lookups.Suppliers.GetValueOrDefault(supplierId) : null,
        CreatedAt = r.CreatedAt,
        UpdatedAt = r.UpdatedAt,
    };

    private sealed record Lookups(Dictionary<int, string> Currencies, Dictionary<int, string> Suppliers);
}
