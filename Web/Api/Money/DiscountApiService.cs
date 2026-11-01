using Application.IServices.Sales;
using Core.Entities.Sales;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Web.Api.Infrastructure;
using Web.Api.Money.Contracts;

namespace Web.Api.Money;

public interface IDiscountApiService
{
    Task<PagedResult<DiscountDto>> ListAsync(PagedQuery query);

    /// <summary>Null when the discount does not exist.</summary>
    Task<DiscountDto?> GetAsync(int id);

    Task<DiscountDto> CreateAsync(DiscountRequest request);

    /// <summary>Null when the discount does not exist.</summary>
    Task<DiscountDto?> UpdateAsync(int id, DiscountRequest request);

    /// <summary>False when the discount does not exist. Throws <see cref="MoneyConflictException"/> when something still points at it.</summary>
    Task<bool> DeleteAsync(int id);
}

/// <summary>
/// Discounts on top of <see cref="IDiscountService"/> (which keeps the lookup cache the till reads). The legacy API also took a supplier; no screen ever set one and nothing reads it,
/// so it is not part of the contract - an edit keeps whatever supplier a row already has.
/// </summary>
public sealed class DiscountApiService : IDiscountApiService
{
    private static readonly string[] SortColumns = { "name", "rate", "isDefault", "createdAt" };

    private readonly IDiscountService _discounts;
    private readonly IStringLocalizer<SharedResource> _text;

    public DiscountApiService(IDiscountService discounts, IStringLocalizer<SharedResource> text)
    {
        _discounts = discounts;
        _text = text;
    }

    public async Task<PagedResult<DiscountDto>> ListAsync(PagedQuery query)
    {
        IEnumerable<Discount> rows = await _discounts.GetAllAsync();

        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var term = query.Q.Trim();
            rows = rows.Where(d => MoneySort.Contains(d.Name, term));
        }

        return PagedResult<DiscountDto>.From(Sort(rows, query).Select(ToDto), query);
    }

    public async Task<DiscountDto?> GetAsync(int id) => await FindAsync(id) is { } found ? ToDto(found) : null;

    public async Task<DiscountDto> CreateAsync(DiscountRequest request)
    {
        var created = await _discounts.CreateAsync(new Discount(request.Name.Trim(), request.Rate, request.IsDefault));
        if (request.IsDefault) await ClearOtherDefaultsAsync(created.Id, created.SupplierId);
        return ToDto(created);
    }

    public async Task<DiscountDto?> UpdateAsync(int id, DiscountRequest request)
    {
        var existing = await FindAsync(id);
        if (existing is null) return null;

        await _discounts.UpdateAsync(id, new Discount(request.Name.Trim(), request.Rate, request.IsDefault, existing.SupplierId));
        if (request.IsDefault) await ClearOtherDefaultsAsync(id, existing.SupplierId);
        return ToDto((await FindAsync(id))!);
    }

    public async Task<bool> DeleteAsync(int id)
    {
        if (await FindAsync(id) is null) return false;

        try
        {
            await _discounts.DeleteAsync(id);
        }
        catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException)
        {
            throw new MoneyConflictException("discount_in_use", _text["Mny_DiscountInUse"].Value);
        }

        return true;
    }

    private async Task<Discount?> FindAsync(int id) => (await _discounts.GetAllAsync()).FirstOrDefault(d => d.Id == id);

    /// <summary>The till preselects "the" default, so a second default would make the choice depend on row order.</summary>
    private async Task ClearOtherDefaultsAsync(int keepId, int? supplierId)
    {
        foreach (var other in (await _discounts.GetAllAsync()).Where(d => d.Id != keepId && d.IsDefault && d.SupplierId == supplierId).ToList())
            await _discounts.UpdateAsync(other.Id, new Discount(other.Name, other.Rate, false, other.SupplierId));
    }

    private static IEnumerable<Discount> Sort(IEnumerable<Discount> rows, PagedQuery query)
    {
        var key = SortColumns.FirstOrDefault(c => string.Equals(c, query.Sort, StringComparison.OrdinalIgnoreCase)) ?? "name";
        var desc = query.Descending;
        return key switch
        {
            "rate" => MoneySort.By(rows, d => d.Rate, desc),
            "isDefault" => MoneySort.By(rows, d => d.IsDefault, desc),
            "createdAt" => MoneySort.By(rows, d => d.CreatedAt, desc),
            _ => MoneySort.ByText(rows, d => d.Name, desc),
        };
    }

    private static DiscountDto ToDto(Discount d) => new() { Id = d.Id, Name = d.Name, Rate = d.Rate, IsDefault = d.IsDefault, CreatedAt = d.CreatedAt };
}
