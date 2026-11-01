using Application.IServices.Base;
using Core.Entities.Sales;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Web.Api.Infrastructure;
using Web.Api.Money.Contracts;
using Web.Api.Pos;

namespace Web.Api.Money;

public interface ICurrencyApiService
{
    Task<PagedResult<CurrencyDto>> ListAsync(PagedQuery query);

    /// <summary>Null when the currency does not exist.</summary>
    Task<CurrencyDto?> GetAsync(int id);

    /// <summary>Throws <see cref="MoneyRuleException"/> for a taken code or an inactive default.</summary>
    Task<CurrencyDto> CreateAsync(CurrencyRequest request);

    /// <summary>Null when the currency does not exist. Throws <see cref="MoneyRuleException"/> (taken code, inactive default) or <see cref="MoneyConflictException"/> (un-defaulting the default).</summary>
    Task<CurrencyDto?> UpdateAsync(int id, CurrencyRequest request);

    /// <summary>Null when the currency does not exist. Throws <see cref="MoneyConflictException"/> for an inactive currency.</summary>
    Task<CurrencyDto?> SetDefaultAsync(int id);

    /// <summary>False when the currency does not exist. Throws <see cref="MoneyConflictException"/> for the default currency or one that is in use.</summary>
    Task<bool> DeleteAsync(int id);

    /// <summary>Throws <see cref="MoneyRuleException"/> for an unknown currency.</summary>
    Task<CurrencyConversionDto> ConvertAsync(CurrencyConvertRequest request);
}

/// <summary>
/// Currencies on top of <see cref="ICurrencyService"/>, which keeps the "one default" bookkeeping. The till converts with <c>RateToUSD</c>
/// (see <c>PriceRules.ConvertViaUsd</c>), so a rate of zero or a changed USD row would silently reprice everything: the rate must be positive.
/// </summary>
public sealed class CurrencyApiService : ICurrencyApiService
{
    private static readonly string[] SortColumns = { "code", "name", "notation", "rateToUsd", "isDefault", "isActive", "createdAt" };

    private readonly ICurrencyService _currencies;
    private readonly IStringLocalizer<SharedResource> _text;

    public CurrencyApiService(ICurrencyService currencies, IStringLocalizer<SharedResource> text)
    {
        _currencies = currencies;
        _text = text;
    }

    public async Task<PagedResult<CurrencyDto>> ListAsync(PagedQuery query)
    {
        IEnumerable<Currency> rows = await _currencies.GetAllAsync();

        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var term = query.Q.Trim();
            rows = rows.Where(c => MoneySort.Contains(c.Code, term) || MoneySort.Contains(c.Name, term) || MoneySort.Contains(c.Notation, term));
        }

        return PagedResult<CurrencyDto>.From(Sort(rows, query).Select(ToDto), query);
    }

    public async Task<CurrencyDto?> GetAsync(int id) => await FindAsync(id) is { } found ? ToDto(found) : null;

    public async Task<CurrencyDto> CreateAsync(CurrencyRequest request)
    {
        var all = await _currencies.GetAllAsync();
        CheckRules(request, all, id: null);

        var created = await _currencies.CreateAsync(New(request), request.IsDefault);
        return ToDto(created);
    }

    public async Task<CurrencyDto?> UpdateAsync(int id, CurrencyRequest request)
    {
        var all = await _currencies.GetAllAsync();
        var existing = all.FirstOrDefault(c => c.Id == id);
        if (existing is null) return null;

        CheckRules(request, all, id);
        // The service refuses this with an exception; say it with a code the screen can show next to the switch.
        if (existing.IsDefault && !request.IsDefault)
            throw new MoneyConflictException("currency_default_required", _text["Mny_CurrencyDefaultRequired"].Value);

        await _currencies.UpdateAsync(id, New(request), request.IsDefault);
        return ToDto((await FindAsync(id))!);
    }

    public async Task<CurrencyDto?> SetDefaultAsync(int id)
    {
        var existing = await FindAsync(id);
        if (existing is null) return null;
        if (!existing.IsActive) throw new MoneyConflictException("currency_inactive", _text["Mny_CurrencyDefaultInactive"].Value);

        if (!existing.IsDefault) await _currencies.SetDefaultAsync(id);
        return ToDto((await FindAsync(id))!);
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var existing = await FindAsync(id);
        if (existing is null) return false;
        if (existing.IsDefault) throw new MoneyConflictException("currency_is_default", _text["Mny_CurrencyDeleteDefault"].Value);

        try
        {
            await _currencies.DeleteAsync(id);
        }
        catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException)
        {
            // Suppliers, receipts, products or exchange rates still point at it.
            throw new MoneyConflictException("currency_in_use", _text["Mny_CurrencyInUse"].Value);
        }

        return true;
    }

    public async Task<CurrencyConversionDto> ConvertAsync(CurrencyConvertRequest request)
    {
        var all = await _currencies.GetAllAsync();
        var from = all.FirstOrDefault(c => c.Id == request.FromCurrencyId)
            ?? throw new MoneyRuleException(nameof(request.FromCurrencyId), _text["Mny_UnknownCurrency"].Value);
        var to = all.FirstOrDefault(c => c.Id == request.ToCurrencyId)
            ?? throw new MoneyRuleException(nameof(request.ToCurrencyId), _text["Mny_UnknownCurrency"].Value);

        // The till's own arithmetic, so the preview can never differ from what a cashier sees.
        return new CurrencyConversionDto
        {
            Amount = request.Amount,
            Converted = PosPricingMath.ToCartCurrency(request.Amount, from, to),
            FromCode = from.Code,
            ToCode = to.Code,
        };
    }

    private async Task<Currency?> FindAsync(int id) => (await _currencies.GetAllAsync()).FirstOrDefault(c => c.Id == id);

    private void CheckRules(CurrencyRequest request, IEnumerable<Currency> all, int? id)
    {
        var code = request.Code.Trim();
        if (code.Length == 0) throw new MoneyRuleException(nameof(request.Code), _text["Mny_CurrencyCodeRequired"].Value);
        if (all.Any(c => c.Id != id && string.Equals(c.Code, code, StringComparison.OrdinalIgnoreCase)))
            throw new MoneyRuleException(nameof(request.Code), _text["Mny_CurrencyCodeTaken"].Value);
        if (request.IsDefault && !request.IsActive)
            throw new MoneyRuleException(nameof(request.IsActive), _text["Mny_CurrencyDefaultInactive"].Value);
    }

    private static Currency New(CurrencyRequest request)
        => new(request.Name.Trim(), (float)request.RateToUsd, request.IsDefault, request.IsActive, request.Code.Trim().ToUpperInvariant(), request.Notation.Trim());

    private static IEnumerable<Currency> Sort(IEnumerable<Currency> rows, PagedQuery query)
    {
        var key = SortColumns.FirstOrDefault(c => string.Equals(c, query.Sort, StringComparison.OrdinalIgnoreCase)) ?? "code";
        var desc = query.Descending;
        return key switch
        {
            "name" => MoneySort.ByText(rows, c => c.Name, desc),
            "notation" => MoneySort.ByText(rows, c => c.Notation, desc),
            "rateToUsd" => MoneySort.By(rows, c => c.RateToUSD, desc),
            "isDefault" => MoneySort.By(rows, c => c.IsDefault, desc),
            "isActive" => MoneySort.By(rows, c => c.IsActive, desc),
            "createdAt" => MoneySort.By(rows, c => c.CreatedAt, desc),
            _ => MoneySort.ByText(rows, c => c.Code, desc),
        };
    }

    private static CurrencyDto ToDto(Currency c) => new()
    {
        Id = c.Id,
        Code = c.Code,
        Name = c.Name,
        Notation = c.Notation,
        RateToUsd = (decimal)c.RateToUSD,
        IsDefault = c.IsDefault,
        IsActive = c.IsActive,
        CreatedAt = c.CreatedAt,
        UpdatedAt = c.UpdatedAt,
    };
}
