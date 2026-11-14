using Application.IServices.Base;
using Application.IServices.Sales;
using Application.IServices.Users;
using Core.Entities.Users;
using Web.Api.Pos.Contracts;

namespace Web.Api.Pos;

/// <summary>The small reference lists and pickers around the till: currencies, payment methods, discounts, customers, the drawer.</summary>
public interface IPosLookupService
{
    Task<IReadOnlyList<PosCurrencyDto>> GetCurrenciesAsync();

    Task<IReadOnlyList<PosPaymentMethodDto>> GetPaymentMethodsAsync();

    Task<IReadOnlyList<PosDiscountDto>> GetDiscountsAsync();

    Task<PosCustomerPageDto> SearchCustomersAsync(string? search, int page, int pageSize);

    Task<PosCustomerDto> QuickCreateCustomerAsync(string firstName, string lastName, string? phoneNumber = null);

    Task<PosDrawerDto> GetDrawerAsync(int userId);
}

public sealed class PosLookupService : IPosLookupService
{
    public const int MaxCustomerPageSize = 50;

    private readonly ICurrencyService _currencies;
    private readonly IPOSCheckoutService _checkout;
    private readonly ICustomerService _customers;
    private readonly ICashDrawerService _drawer;

    public PosLookupService(ICurrencyService currencies, IPOSCheckoutService checkout, ICustomerService customers, ICashDrawerService drawer)
    {
        _currencies = currencies;
        _checkout = checkout;
        _customers = customers;
        _drawer = drawer;
    }

    public async Task<IReadOnlyList<PosCurrencyDto>> GetCurrenciesAsync()
        => (await _currencies.GetAllAsync())
            .Where(c => c.IsActive)
            .OrderByDescending(c => c.IsDefault).ThenBy(c => c.Code)
            .Select(c => new PosCurrencyDto
            {
                Id = c.Id, Code = c.Code, Name = c.Name, Notation = c.Notation, RateToUsd = (decimal)c.RateToUSD, IsDefault = c.IsDefault,
            })
            .ToList();

    public async Task<IReadOnlyList<PosPaymentMethodDto>> GetPaymentMethodsAsync()
        // The service already hides the system "Customer Debt" method: it is only reachable through addToDebt.
        => (await _checkout.GetPaymentMethodsAsync())
            .Select(m => new PosPaymentMethodDto { Id = m.Id, Name = m.Name, IsDefault = m.IsDefault, RequiresChange = m.RequiresChange })
            .ToList();

    public async Task<IReadOnlyList<PosDiscountDto>> GetDiscountsAsync()
        => (await _checkout.GetDiscountsAsync())
            .Select(d => new PosDiscountDto { Id = d.Id, Name = d.Name, Rate = d.Rate, IsDefault = d.IsDefault, SupplierId = d.SupplierId })
            .ToList();

    public async Task<PosCustomerPageDto> SearchCustomersAsync(string? search, int page, int pageSize)
    {
        pageSize = Math.Clamp(pageSize, 1, MaxCustomerPageSize);
        page = Math.Max(page, 1);

        var (items, hasMore) = await _customers.SearchPagedAsync(search, (page - 1) * pageSize, pageSize);
        return new PosCustomerPageDto { Items = items.Select(Map).ToList(), Page = page, PageSize = pageSize, HasMore = hasMore };
    }

    public async Task<PosCustomerDto> QuickCreateCustomerAsync(string firstName, string lastName, string? phoneNumber = null)
    {
        // Same rule as the Customers page: 2-100 characters each. (The DTO attributes check the untrimmed text, so trim and look again.)
        firstName = firstName.Trim();
        lastName = lastName.Trim();
        if (firstName.Length < 2) throw new PosRuleException(StatusCodes.Status400BadRequest, "validation_failed", "Pos_NameTooShort", "firstName");
        if (lastName.Length < 2) throw new PosRuleException(StatusCodes.Status400BadRequest, "validation_failed", "Pos_NameTooShort", "lastName");

        // The phone number is optional: blank means none (stored as null, like the Customers page).
        phoneNumber = string.IsNullOrWhiteSpace(phoneNumber) ? null : phoneNumber.Trim();
        if (phoneNumber is not null && (phoneNumber.Length > 20 || !new System.ComponentModel.DataAnnotations.PhoneAttribute().IsValid(phoneNumber)))
            throw new PosRuleException(StatusCodes.Status400BadRequest, "validation_failed", "Pos_PhoneInvalid", "phoneNumber");

        var created = await _customers.CreateAsync(new Customer(firstName, lastName, string.Empty, phoneNumber));
        return Map(created);
    }

    public async Task<PosDrawerDto> GetDrawerAsync(int userId)
    {
        var session = await _drawer.GetOpenSessionByUserIdAsync(userId);
        return session is null
            ? new PosDrawerDto { IsOpen = false }
            : new PosDrawerDto { IsOpen = true, SessionId = session.Id, OpenedAt = session.OpenedAt, OpeningCash = session.OpeningCash };
    }

    private static PosCustomerDto Map(Customer c)
        => new() { Id = c.Id, Name = $"{c.FirstName} {c.LastName}".Trim(), Email = string.IsNullOrWhiteSpace(c.Email) ? null : c.Email, Balance = c.Balance };
}
