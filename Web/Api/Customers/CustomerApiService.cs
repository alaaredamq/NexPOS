using Application.IServices.Base;
using Application.IServices.Users;
using Core.Entities.Sales;
using Core.Entities.Users;
using Core.Enum;
using Microsoft.Extensions.Localization;
using Web.Api.Customers.Contracts;
using Web.Api.Infrastructure;

namespace Web.Api.Customers;

public interface ICustomerApiService
{
    Task<PagedResult<CustomerDto>> ListAsync(PagedQuery query);

    Task<CustomerSummaryDto> SummaryAsync();

    Task<CustomerDetailsDto?> GetDetailsAsync(int id);

    /// <summary>Throws <see cref="CustomerRuleException"/> for a name that is too short once trimmed.</summary>
    Task<CustomerDto> CreateAsync(CustomerRequest request);

    /// <summary>Null when the customer does not exist.</summary>
    Task<CustomerDto?> UpdateAsync(int id, CustomerRequest request);

    /// <summary>Throws <see cref="CustomerHasBalanceException"/> while the customer owes or is owed money; false when the customer does not exist.</summary>
    Task<bool> DeleteAsync(int id);

    /// <summary>The customer's ledger, newest first. Null when the customer does not exist.</summary>
    Task<PagedResult<CustomerDebtTransactionDto>?> ListDebtTransactionsAsync(int id, PagedQuery query);

    /// <summary>Records a manual debt entry. Null when the customer does not exist.</summary>
    Task<CustomerDebtResultDto?> AddDebtTransactionAsync(int id, int userId, CustomerDebtRequest request);
}

/// <summary>A field the DTO attributes cannot judge (they see the untrimmed text) is wrong; <see cref="Field"/> is the request property.</summary>
public sealed class CustomerRuleException : Exception
{
    public string Field { get; }

    public CustomerRuleException(string field, string message) : base(message) => Field = field;
}

/// <summary>The customer has an outstanding balance (either direction): settle it, or deactivate the customer.</summary>
public sealed class CustomerHasBalanceException : Exception
{
    public CustomerHasBalanceException(string message) : base(message)
    {
    }
}

/// <summary>Maps the shared customer and debt services to the API's DTOs. The rules (balance kept in step with the ledger, soft delete) stay in those services.</summary>
public sealed class CustomerApiService : ICustomerApiService
{
    /// <summary>The details page shows this many orders; the full list is the Orders page filtered by customer.</summary>
    public const int DetailsOrderLimit = 10;

    private static readonly string[] SortColumns = { "name", "email", "phoneNumber", "address", "ordersCount", "totalSpent", "balance", "isActive" };

    private readonly ICustomerService _customers;
    private readonly ICustomerDebtService _debts;
    private readonly ICurrencyService _currencies;
    private readonly IStringLocalizer<SharedResource> _text;

    public CustomerApiService(ICustomerService customers, ICustomerDebtService debts, ICurrencyService currencies, IStringLocalizer<SharedResource> text)
    {
        _customers = customers;
        _debts = debts;
        _currencies = currencies;
        _text = text;
    }

    public async Task<PagedResult<CustomerDto>> ListAsync(PagedQuery query)
    {
        var all = await _customers.GetAllAsync();
        var totals = await _customers.GetPurchaseTotalsAsync();

        IEnumerable<Customer> filtered = all;
        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var term = query.Q.Trim();
            filtered = filtered.Where(c =>
                $"{c.FirstName} {c.LastName}".Contains(term, StringComparison.OrdinalIgnoreCase) ||
                c.Email.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                (c.PhoneNumber ?? string.Empty).Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        var rows = filtered.Select(c => ToDto(c, totals));
        return PagedResult<CustomerDto>.From(Sort(rows, query), query);
    }

    public async Task<CustomerSummaryDto> SummaryAsync()
    {
        var all = await _customers.GetAllAsync();
        var totals = await _customers.GetPurchaseTotalsAsync();
        var notation = (await _currencies.GetDefaultAsync())?.Notation ?? "$";

        return new CustomerSummaryDto
        {
            Total = all.Count,
            Active = all.Count(c => c.IsActive),
            Owing = all.Count(c => c.Balance > 0),
            TotalOwed = all.Where(c => c.Balance > 0).Sum(c => c.Balance),
            TotalSpent = all.Sum(c => totals.TryGetValue(c.Id, out var t) ? t.TotalSpent : 0m),
            CurrencyNotation = notation,
        };
    }

    public async Task<CustomerDetailsDto?> GetDetailsAsync(int id)
    {
        // Loads the customer with all of their orders (and the orders' currencies).
        var customer = await FindAsync(id);
        if (customer is null) return null;

        var notation = (await _currencies.GetDefaultAsync())?.Notation ?? "$";
        var counted = customer.Orders.Where(IsPurchase).ToList();
        var totals = new Dictionary<int, (int OrdersCount, decimal TotalSpent)> { [customer.Id] = (counted.Count, counted.Sum(o => o.TotalAmount)) };
        var newestFirst = customer.Orders.OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Id).ToList();

        return new CustomerDetailsDto
        {
            Customer = ToDto(customer, totals),
            City = customer.City,
            Country = customer.Country,
            PostalCode = customer.PostalCode,
            CurrencyNotation = notation,
            AverageOrder = counted.Count == 0 ? 0m : counted.Sum(o => o.TotalAmount) / counted.Count,
            LastOrderAt = newestFirst.Count == 0 ? null : newestFirst[0].CreatedAt,
            AllOrdersCount = customer.Orders.Count,
            RecentOrders = newestFirst.Take(DetailsOrderLimit).Select(o => new CustomerOrderDto
            {
                Id = o.Id,
                CreatedAt = o.CreatedAt,
                TotalAmount = o.TotalAmount,
                CurrencyNotation = o.Currency?.Notation ?? notation,
                Status = ((OrderStatus)o.Status).ToString(),
            }).ToList(),
        };
    }

    public async Task<CustomerDto> CreateAsync(CustomerRequest request)
    {
        var (first, last) = CleanNames(request);
        var customer = new Customer(first, last, CleanEmail(request.Email), Clean(request.PhoneNumber), Clean(request.Address))
        {
            IsActive = request.IsActive,
        };

        var created = await _customers.CreateAsync(customer);
        return ToDto(created, new Dictionary<int, (int, decimal)>());
    }

    public async Task<CustomerDto?> UpdateAsync(int id, CustomerRequest request)
    {
        var existing = await FindAsync(id);
        if (existing is null) return null;

        var (first, last) = CleanNames(request);
        existing.FirstName = first;
        existing.LastName = last;
        existing.Email = CleanEmail(request.Email);
        existing.PhoneNumber = Clean(request.PhoneNumber);
        existing.Address = Clean(request.Address);
        existing.IsActive = request.IsActive;

        // City / country / postal code are not editable anywhere in the UI; the service copies them back unchanged.
        await _customers.UpdateAsync(id, existing);

        var totals = await _customers.GetPurchaseTotalsAsync();
        return ToDto(existing, totals);
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var existing = await FindAsync(id);
        if (existing is null) return false;

        if (existing.Balance != 0)
        {
            var owed = existing.Balance > 0;
            throw new CustomerHasBalanceException(_text[owed ? "Cust_HasBalanceOwes" : "Cust_HasBalanceOwed", Math.Abs(existing.Balance).ToString("N2")]);
        }

        try
        {
            await _customers.DeleteAsync(id);
        }
        catch (InvalidOperationException ex)
        {
            // The service re-checks the balance (a debt recorded between our read and the delete): same answer.
            throw new CustomerHasBalanceException(ex.Message);
        }

        return true;
    }

    public async Task<PagedResult<CustomerDebtTransactionDto>?> ListDebtTransactionsAsync(int id, PagedQuery query)
    {
        if (await FindAsync(id) is null) return null;

        var ledger = await _debts.GetTransactionsAsync(id);
        return PagedResult<CustomerDebtTransactionDto>.From(ledger.OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id).Select(ToDto), query);
    }

    public async Task<CustomerDebtResultDto?> AddDebtTransactionAsync(int id, int userId, CustomerDebtRequest request)
    {
        if (await FindAsync(id) is null) return null;

        var signed = request.CustomerOwesMore ? request.Amount : -request.Amount;
        var saved = await _debts.AddManualTransactionAsync(id, userId, signed, Clean(request.Note));

        // Re-read: the ledger entry with its author, and the balance the debt service just moved.
        var entry = (await _debts.GetTransactionsAsync(id)).First(t => t.Id == saved.Id);
        var balance = (await FindAsync(id))!.Balance;
        return new CustomerDebtResultDto { Transaction = ToDto(entry), Balance = balance };
    }

    // The repository throws ArgumentNullException (not null) for an unknown or deleted id.
    private async Task<Customer?> FindAsync(int id)
    {
        try
        {
            return await _customers.GetByIdAsync(id);
        }
        catch (ArgumentNullException)
        {
            return null;
        }
    }

    private static bool IsPurchase(Order o) => o.Status != (int)OrderStatus.Cancelled && o.Status != (int)OrderStatus.Returned;

    private (string First, string Last) CleanNames(CustomerRequest request)
    {
        // The DTO attributes measure the untrimmed text, so look again after trimming.
        var first = request.FirstName.Trim();
        var last = request.LastName.Trim();
        if (first.Length < 2) throw new CustomerRuleException("firstName", _text["Pos_NameTooShort"]);
        if (last.Length < 2) throw new CustomerRuleException("lastName", _text["Pos_NameTooShort"]);
        return (first, last);
    }

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    // The column is not nullable: "no email" is stored as an empty string.
    private static string CleanEmail(string? email) => email?.Trim() ?? string.Empty;

    private static CustomerDto ToDto(Customer c, IReadOnlyDictionary<int, (int OrdersCount, decimal TotalSpent)> totals)
    {
        totals.TryGetValue(c.Id, out var t);
        return new CustomerDto
        {
            Id = c.Id,
            FirstName = c.FirstName,
            LastName = c.LastName,
            Email = string.IsNullOrWhiteSpace(c.Email) ? null : c.Email,
            PhoneNumber = c.PhoneNumber,
            Address = c.Address,
            IsActive = c.IsActive,
            Balance = c.Balance,
            OrdersCount = t.OrdersCount,
            TotalSpent = t.TotalSpent,
            CreatedAt = c.CreatedAt,
        };
    }

    private static CustomerDebtTransactionDto ToDto(CustomerDebtTransaction t) => new()
    {
        Id = t.Id,
        CreatedAt = t.CreatedAt,
        Type = t.Type.ToString(),
        Amount = t.Amount,
        Note = t.Note,
        OrderId = t.OrderId,
        RecordedBy = t.User?.Username,
    };

    private static IEnumerable<CustomerDto> Sort(IEnumerable<CustomerDto> rows, PagedQuery query)
    {
        var column = SortColumns.FirstOrDefault(c => string.Equals(c, query.Sort, StringComparison.OrdinalIgnoreCase)) ?? "name";
        var desc = query.Descending;

        return column switch
        {
            "email" => Order(rows, c => c.Email ?? string.Empty, desc),
            "phoneNumber" => Order(rows, c => c.PhoneNumber ?? string.Empty, desc),
            "address" => Order(rows, c => c.Address ?? string.Empty, desc),
            "ordersCount" => Order(rows, c => c.OrdersCount, desc),
            "totalSpent" => Order(rows, c => c.TotalSpent, desc),
            "balance" => Order(rows, c => c.Balance, desc),
            "isActive" => Order(rows, c => c.IsActive, desc),
            _ => Order(rows, c => c.FirstName + " " + c.LastName, desc),
        };

        static IEnumerable<CustomerDto> Order<TKey>(IEnumerable<CustomerDto> source, Func<CustomerDto, TKey> key, bool descending)
        {
            // Name is the tie-breaker, so equal rows never swap between pages.
            var ordered = descending
                ? source.OrderByDescending(key, Comparer<TKey>.Default)
                : source.OrderBy(key, Comparer<TKey>.Default);
            return ordered.ThenBy(c => c.FirstName, StringComparer.CurrentCultureIgnoreCase).ThenBy(c => c.LastName, StringComparer.CurrentCultureIgnoreCase).ThenBy(c => c.Id);
        }
    }
}
