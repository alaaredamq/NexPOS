using Application.IServices.Base;
using Application.IServices.Products;
using Core.Entities.Products;
using Core.Enum;
using Web.Api.Infrastructure;
using Web.Api.Suppliers.Contracts;

namespace Web.Api.Suppliers;

public interface ISupplierApiService
{
    Task<PagedResult<SupplierDto>> ListAsync(PagedQuery query);

    Task<SupplierSummaryDto> SummaryAsync();

    Task<SupplierOptionsDto> OptionsAsync();

    Task<SupplierDto?> GetAsync(int id);

    /// <summary>Throws <see cref="SupplierCurrencyException"/> for a currency that is not in the list.</summary>
    Task<SupplierDto> CreateAsync(SupplierRequest request);

    /// <summary>Null when the supplier does not exist. Throws <see cref="SupplierCurrencyException"/> for a currency that is not in the list.</summary>
    Task<SupplierDto?> UpdateAsync(int id, SupplierRequest request);

    /// <summary>False when the supplier does not exist. Throws <see cref="SupplierHasBalanceException"/> or <see cref="SupplierInUseException"/>.</summary>
    Task<bool> DeleteAsync(int id);

    /// <summary>Null when the supplier does not exist. The ledger, newest first.</summary>
    Task<PagedResult<SupplierDebtEntryDto>?> LedgerAsync(int id, PagedQuery query);

    /// <summary>Null when the supplier does not exist.</summary>
    Task<SupplierDebtResultDto?> AddDebtAsync(int id, int userId, SupplierDebtRequest request);
}

/// <summary>The chosen currency is not one of the shop's currencies.</summary>
public sealed class SupplierCurrencyException : Exception
{
    public SupplierCurrencyException() : base("Unknown currency.")
    {
    }
}

/// <summary>The supplier still has a balance: settle it or deactivate the supplier.</summary>
public sealed class SupplierHasBalanceException : Exception
{
    public SupplierHasBalanceException(decimal balance, string notation) : base("The supplier has an outstanding balance.")
    {
        Balance = balance;
        Notation = notation;
    }

    public decimal Balance { get; }

    public string Notation { get; }
}

/// <summary>Products (deleted ones kept for sales history included) or debt records still point at the supplier.</summary>
public sealed class SupplierInUseException : Exception
{
    public SupplierInUseException(Exception inner) : base(inner.Message, inner)
    {
    }
}

/// <summary>Maps the shared supplier services to the API's DTOs. The rules (balance before delete, "still referenced") stay in <see cref="ISupplierService"/>.</summary>
public sealed class SupplierApiService : ISupplierApiService
{
    private static readonly string[] SortColumns = { "name", "email", "phone", "balance", "isActive", "discount", "products" };

    private readonly ISupplierService _suppliers;
    private readonly ISupplierDebtService _debt;
    private readonly ICurrencyService _currencies;
    private readonly IProductService _products;

    public SupplierApiService(ISupplierService suppliers, ISupplierDebtService debt, ICurrencyService currencies, IProductService products)
    {
        _suppliers = suppliers;
        _debt = debt;
        _currencies = currencies;
        _products = products;
    }

    public async Task<PagedResult<SupplierDto>> ListAsync(PagedQuery query)
    {
        var all = await _suppliers.GetAllAsync();
        var lookups = await LookupsAsync();

        IEnumerable<Supplier> filtered = all;
        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var term = query.Q.Trim();
            filtered = filtered.Where(s =>
                s.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                (s.Email ?? string.Empty).Contains(term, StringComparison.OrdinalIgnoreCase) ||
                (s.Phone ?? string.Empty).Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        var rows = filtered.Select(s => ToDto(s, lookups));
        return PagedResult<SupplierDto>.From(Sort(rows, query), query);
    }

    public async Task<SupplierSummaryDto> SummaryAsync()
    {
        var all = await _suppliers.GetAllAsync();
        var active = all.Count(s => s.IsActive);
        return new SupplierSummaryDto { Total = all.Count, Active = active, Inactive = all.Count - active, WithBalance = all.Count(s => s.Balance != 0) };
    }

    public async Task<SupplierOptionsDto> OptionsAsync()
    {
        var currencies = (await _currencies.GetAllAsync()).Where(c => c.IsActive).OrderBy(c => c.Name).ToList();
        return new SupplierOptionsDto
        {
            Currencies = currencies.Select(c => new SupplierCurrencyOptionDto { Id = c.Id, Name = c.Name, Notation = c.Notation }).ToList(),
            DefaultCurrencyId = (await _currencies.GetDefaultAsync())?.Id,
        };
    }

    public async Task<SupplierDto?> GetAsync(int id)
    {
        var supplier = await FindAsync(id);
        return supplier is null ? null : ToDto(supplier, await LookupsAsync());
    }

    public async Task<SupplierDto> CreateAsync(SupplierRequest request)
    {
        await EnsureCurrencyAsync(request.CurrencyId);

        var supplier = new Supplier(request.Name.Trim(), Clean(request.Email), Clean(request.Phone), Clean(request.Address), Clean(request.City), Clean(request.Country), Clean(request.PostalCode))
        {
            IsActive = request.IsActive,
            CurrencyId = request.CurrencyId,
            DiscountRate = request.DiscountRate,
        };

        var created = await _suppliers.CreateAsync(supplier);
        return ToDto(await FindAsync(created.Id) ?? created, await LookupsAsync());
    }

    public async Task<SupplierDto?> UpdateAsync(int id, SupplierRequest request)
    {
        if (await FindAsync(id) is null) return null;
        await EnsureCurrencyAsync(request.CurrencyId);

        // The service copies these fields onto the tracked entity; the balance is never touched by an edit.
        await _suppliers.UpdateAsync(id, new Supplier(request.Name.Trim(), Clean(request.Email), Clean(request.Phone), Clean(request.Address), Clean(request.City), Clean(request.Country), Clean(request.PostalCode))
        {
            IsActive = request.IsActive,
            CurrencyId = request.CurrencyId,
            DiscountRate = request.DiscountRate,
        });

        return ToDto(await FindAsync(id) ?? throw new InvalidOperationException("Supplier vanished after update."), await LookupsAsync());
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var supplier = await FindAsync(id);
        if (supplier is null) return false;

        if (supplier.Balance != 0)
        {
            var notation = (await LookupsAsync()).Notations.GetValueOrDefault(supplier.CurrencyId, string.Empty);
            throw new SupplierHasBalanceException(Math.Abs(supplier.Balance), notation);
        }

        try
        {
            await _suppliers.DeleteAsync(id);
        }
        catch (InvalidOperationException ex)
        {
            throw new SupplierInUseException(ex);
        }

        return true;
    }

    public async Task<PagedResult<SupplierDebtEntryDto>?> LedgerAsync(int id, PagedQuery query)
    {
        if (await FindAsync(id) is null) return null;

        var entries = await _debt.GetTransactionsAsync(id);
        return PagedResult<SupplierDebtEntryDto>.From(entries.OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id).Select(ToEntry), query);
    }

    public async Task<SupplierDebtResultDto?> AddDebtAsync(int id, int userId, SupplierDebtRequest request)
    {
        if (await FindAsync(id) is null) return null;

        var signed = request.IOweMore ? request.Amount : -request.Amount;
        var saved = await _debt.AddManualTransactionAsync(id, userId, signed, Clean(request.Note));

        // The saved row has no user loaded; the ledger read brings it with its name.
        var entry = (await _debt.GetTransactionsAsync(id)).FirstOrDefault(t => t.Id == saved.Id) ?? saved;
        var supplier = await FindAsync(id);
        return new SupplierDebtResultDto { Entry = ToEntry(entry), Balance = supplier?.Balance ?? 0 };
    }

    // ---- helpers -----------------------------------------------------------------------------------------------

    private sealed record Lookups(Dictionary<int, (string Name, string Notation)> Currencies, Dictionary<int, int> ProductCounts)
    {
        public Dictionary<int, string> Notations => Currencies.ToDictionary(c => c.Key, c => c.Value.Notation);
    }

    // The list query does not load Supplier.Currency / Products reliably (the old controller worked around it the same way), so both are resolved by id.
    private async Task<Lookups> LookupsAsync()
    {
        var currencies = (await _currencies.GetAllAsync()).ToDictionary(c => c.Id, c => (c.Name, c.Notation));
        var counts = (await _products.GetAllAsync()).GroupBy(p => p.SupplierId).ToDictionary(g => g.Key, g => g.Count());
        return new Lookups(currencies, counts);
    }

    private async Task EnsureCurrencyAsync(int currencyId)
    {
        if (!(await _currencies.GetAllAsync()).Any(c => c.Id == currencyId)) throw new SupplierCurrencyException();
    }

    // The repository throws ArgumentNullException (not null) for an unknown id.
    private async Task<Supplier?> FindAsync(int id)
    {
        try
        {
            return await _suppliers.GetByIdAsync(id);
        }
        catch (ArgumentNullException)
        {
            return null;
        }
    }

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static SupplierDto ToDto(Supplier s, Lookups lookups)
    {
        lookups.Currencies.TryGetValue(s.CurrencyId, out var currency);
        return new SupplierDto
        {
            Id = s.Id,
            Name = s.Name,
            Email = s.Email,
            Phone = s.Phone,
            Address = s.Address,
            City = s.City,
            Country = s.Country,
            PostalCode = s.PostalCode,
            DiscountRate = s.DiscountRate,
            IsActive = s.IsActive,
            CurrencyId = s.CurrencyId,
            CurrencyName = currency.Name,
            CurrencyNotation = currency.Notation ?? string.Empty,
            Balance = s.Balance,
            ProductCount = lookups.ProductCounts.GetValueOrDefault(s.Id),
        };
    }

    private static SupplierDebtEntryDto ToEntry(SupplierDebtTransaction t) => new()
    {
        Id = t.Id,
        CreatedAt = t.CreatedAt,
        Kind = t.Type switch
        {
            SupplierDebtTransactionType.Purchase => SupplierDebtKind.Purchase,
            SupplierDebtTransactionType.Payment => SupplierDebtKind.Payment,
            _ => SupplierDebtKind.Adjustment,
        },
        Amount = t.Amount,
        Note = t.Note,
        UserName = t.User is null ? null : DisplayName(t.User),
    };

    private static string DisplayName(Core.Entities.Users.User user)
    {
        var full = $"{user.FirstName} {user.LastName}".Trim();
        return full.Length > 0 ? full : user.Username;
    }

    private static IEnumerable<SupplierDto> Sort(IEnumerable<SupplierDto> rows, PagedQuery query)
    {
        var column = SortColumns.FirstOrDefault(c => string.Equals(c, query.Sort, StringComparison.OrdinalIgnoreCase)) ?? "name";
        var desc = query.Descending;

        return column switch
        {
            "email" => Order(rows, s => s.Email ?? string.Empty, desc),
            "phone" => Order(rows, s => s.Phone ?? string.Empty, desc),
            "balance" => Order(rows, s => s.Balance, desc),
            "isActive" => Order(rows, s => s.IsActive, desc),
            "discount" => Order(rows, s => s.DiscountRate, desc),
            "products" => Order(rows, s => s.ProductCount, desc),
            _ => Order(rows, s => s.Name, desc),
        };

        static IEnumerable<SupplierDto> Order<TKey>(IEnumerable<SupplierDto> source, Func<SupplierDto, TKey> key, bool descending)
        {
            var ordered = descending ? source.OrderByDescending(key, Comparer<TKey>.Default) : source.OrderBy(key, Comparer<TKey>.Default);
            return ordered.ThenBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(s => s.Id);
        }
    }
}
