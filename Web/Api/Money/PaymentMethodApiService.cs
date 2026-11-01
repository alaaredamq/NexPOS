using Application.IServices.Sales;
using Application.Services.Sales;
using Core.Entities.Sales;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Web.Api.Infrastructure;
using Web.Api.Money.Contracts;

namespace Web.Api.Money;

public interface IPaymentMethodApiService
{
    Task<PagedResult<PaymentMethodItemDto>> ListAsync(PagedQuery query);

    /// <summary>Null when the method does not exist.</summary>
    Task<PaymentMethodItemDto?> GetAsync(int id);

    /// <summary>Throws <see cref="MoneyRuleException"/> for a taken name (or the system name).</summary>
    Task<PaymentMethodItemDto> CreateAsync(PaymentMethodRequest request);

    /// <summary>Null when the method does not exist. Throws <see cref="MoneyRuleException"/> for a taken name, <see cref="MoneyConflictException"/> for the system method.</summary>
    Task<PaymentMethodItemDto?> UpdateAsync(int id, PaymentMethodRequest request);

    /// <summary>False when the method does not exist. Throws <see cref="MoneyConflictException"/> for the system method or one that is on receipts.</summary>
    Task<bool> DeleteAsync(int id);
}

/// <summary>
/// Payment methods on top of <see cref="IPaymentMethodService"/> (which keeps the lookup cache the till reads). Two names are load-bearing and the old API let anyone break them:
/// "Customer Debt" is how <c>POSCheckoutService</c> finds the method behind "add to customer debt", and a name containing "cash" is what makes the till work out change.
/// </summary>
public sealed class PaymentMethodApiService : IPaymentMethodApiService
{
    private static readonly string[] SortColumns = { "name", "isDefault", "createdAt" };

    private readonly IPaymentMethodService _methods;
    private readonly IStringLocalizer<SharedResource> _text;

    public PaymentMethodApiService(IPaymentMethodService methods, IStringLocalizer<SharedResource> text)
    {
        _methods = methods;
        _text = text;
    }

    public async Task<PagedResult<PaymentMethodItemDto>> ListAsync(PagedQuery query)
    {
        IEnumerable<PaymentMethod> rows = await _methods.GetAllAsync();

        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var term = query.Q.Trim();
            rows = rows.Where(m => MoneySort.Contains(m.Name, term));
        }

        return PagedResult<PaymentMethodItemDto>.From(Sort(rows, query).Select(ToDto), query);
    }

    public async Task<PaymentMethodItemDto?> GetAsync(int id) => await FindAsync(id) is { } found ? ToDto(found) : null;

    public async Task<PaymentMethodItemDto> CreateAsync(PaymentMethodRequest request)
    {
        var name = request.Name.Trim();
        await CheckNameAsync(name, id: null);

        var created = await _methods.CreateAsync(new PaymentMethod(name, request.IsDefault));
        if (request.IsDefault) await ClearOtherDefaultsAsync(created.Id, created.SupplierId);
        return ToDto(created);
    }

    public async Task<PaymentMethodItemDto?> UpdateAsync(int id, PaymentMethodRequest request)
    {
        var existing = await FindAsync(id);
        if (existing is null) return null;
        if (IsSystem(existing.Name)) throw new MoneyConflictException("payment_method_system", _text["Mny_PaymentMethodSystem"].Value);

        var name = request.Name.Trim();
        await CheckNameAsync(name, id);

        await _methods.UpdateAsync(id, new PaymentMethod(name, request.IsDefault, existing.SupplierId));
        if (request.IsDefault) await ClearOtherDefaultsAsync(id, existing.SupplierId);
        return ToDto((await FindAsync(id))!);
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var existing = await FindAsync(id);
        if (existing is null) return false;
        if (IsSystem(existing.Name)) throw new MoneyConflictException("payment_method_system", _text["Mny_PaymentMethodSystem"].Value);

        try
        {
            await _methods.DeleteAsync(id);
        }
        catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException)
        {
            // Receipts and receipt payments keep pointing at the method they were paid with.
            throw new MoneyConflictException("payment_method_in_use", _text["Mny_PaymentMethodInUse"].Value);
        }

        return true;
    }

    private async Task<PaymentMethod?> FindAsync(int id) => (await _methods.GetAllAsync()).FirstOrDefault(m => m.Id == id);

    private async Task CheckNameAsync(string name, int? id)
    {
        if (IsSystem(name)) throw new MoneyRuleException(nameof(PaymentMethodRequest.Name), _text["Mny_PaymentMethodSystemName"].Value);
        if ((await _methods.GetAllAsync()).Any(m => m.Id != id && string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)))
            throw new MoneyRuleException(nameof(PaymentMethodRequest.Name), _text["Mny_PaymentMethodNameTaken"].Value);
    }

    /// <summary>The till preselects "the" default, so a second default would make the choice depend on row order.</summary>
    private async Task ClearOtherDefaultsAsync(int keepId, int? supplierId)
    {
        foreach (var other in (await _methods.GetAllAsync()).Where(m => m.Id != keepId && m.IsDefault && m.SupplierId == supplierId).ToList())
            await _methods.UpdateAsync(other.Id, new PaymentMethod(other.Name, false, other.SupplierId));
    }

    private static bool IsSystem(string name) => name.Equals(POSCheckoutService.DebtPaymentMethodName, StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<PaymentMethod> Sort(IEnumerable<PaymentMethod> rows, PagedQuery query)
    {
        var key = SortColumns.FirstOrDefault(c => string.Equals(c, query.Sort, StringComparison.OrdinalIgnoreCase)) ?? "name";
        var desc = query.Descending;
        return key switch
        {
            "isDefault" => MoneySort.By(rows, m => m.IsDefault, desc),
            "createdAt" => MoneySort.By(rows, m => m.CreatedAt, desc),
            _ => MoneySort.ByText(rows, m => m.Name, desc),
        };
    }

    private static PaymentMethodItemDto ToDto(PaymentMethod m) => new()
    {
        Id = m.Id,
        Name = m.Name,
        IsDefault = m.IsDefault,
        IsSystem = IsSystem(m.Name),
        GivesChange = m.Name.Contains("cash", StringComparison.OrdinalIgnoreCase),
        CreatedAt = m.CreatedAt,
    };
}
