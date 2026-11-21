using Application.IServices.Sales;
using Core.Entities.Sales;
using Core.Time;
using Web.Api.Infrastructure;
using Web.Api.Wallets.Contracts;

namespace Web.Api.Wallets;

/// <summary>The "Whish Wallet" page: the wallet, its balance per currency, its movements, and ticking them off.</summary>
public interface IWalletApiService
{
    Task<IReadOnlyList<WalletDto>> GetAllAsync();

    /// <summary>Null when there is no such wallet.</summary>
    Task<WalletDto?> GetAsync(int id);

    /// <summary>Throws <see cref="InvalidOperationException"/> when the shop already has a Whish wallet.</summary>
    Task<WalletDto> CreateWhishAsync(CreateWalletRequest request, int userId);

    Task<WalletDto?> UpdateAsync(int id, UpdateWalletRequest request);

    Task<PagedResult<WalletTransactionDto>> GetTransactionsAsync(int walletId, WalletTransactionQuery filter, PagedQuery paging);

    /// <summary>
    /// Records money moved by hand. Throws <see cref="KeyNotFoundException"/> for an unknown wallet and <see cref="ArgumentException"/>
    /// (with the field as its parameter name) for a refused value.
    /// </summary>
    Task<WalletTransactionDto> AddMovementAsync(int walletId, WalletMovementRequest request, int userId);

    Task<int> SetCheckedAsync(int walletId, WalletCheckRequest request, int userId);
}

/// <summary>The filters of the movements list. Dates are local days and include both ends.</summary>
public sealed class WalletTransactionQuery
{
    public DateOnly? From { get; init; }

    public DateOnly? To { get; init; }

    public string? Type { get; init; }

    /// <summary><c>checked</c>, <c>unchecked</c> or empty for both.</summary>
    public string? Status { get; init; }

    public int? CurrencyId { get; init; }
}

public sealed class WalletApiService : IWalletApiService
{
    /// <summary>The types a person may record; Sale and SaleReversal come from checkout and the void only.</summary>
    private static readonly WalletTransactionType[] ManualTypes =
    [
        WalletTransactionType.Deposit, WalletTransactionType.Withdrawal, WalletTransactionType.SupplierPayment,
        WalletTransactionType.Fee, WalletTransactionType.Adjustment,
    ];

    private readonly IWalletService _wallets;

    public WalletApiService(IWalletService wallets) => _wallets = wallets;

    public async Task<IReadOnlyList<WalletDto>> GetAllAsync()
    {
        var list = new List<WalletDto>();
        foreach (var wallet in await _wallets.GetAllAsync())
            list.Add(await ToDtoAsync(wallet));
        return list;
    }

    public async Task<WalletDto?> GetAsync(int id) =>
        await _wallets.GetAsync(id) is { } wallet ? await ToDtoAsync(wallet) : null;

    public async Task<WalletDto> CreateWhishAsync(CreateWalletRequest request, int userId)
    {
        var name = string.IsNullOrWhiteSpace(request.Name) ? "Whish" : request.Name;
        var wallet = await _wallets.CreateWhishAsync(name, request.AccountNumber, userId);
        return await ToDtoAsync(wallet);
    }

    public async Task<WalletDto?> UpdateAsync(int id, UpdateWalletRequest request) =>
        await _wallets.UpdateAsync(id, request.Name, request.AccountNumber, request.IsActive) is { } wallet ? await ToDtoAsync(wallet) : null;

    public async Task<PagedResult<WalletTransactionDto>> GetTransactionsAsync(int walletId, WalletTransactionQuery filter, PagedQuery paging)
    {
        var page = await _wallets.GetTransactionsAsync(walletId, new WalletTransactionFilter
        {
            // The pickers choose local calendar days; rows are stored in UTC.
            FromUtc = filter.From is { } f ? AppTime.LocalDayStartUtc(f.ToDateTime(TimeOnly.MinValue)) : null,
            ToUtc = filter.To is { } t ? AppTime.LocalDayStartUtc(t.ToDateTime(TimeOnly.MinValue).AddDays(1)).AddTicks(-1) : null,
            Type = ParseType(filter.Type),
            IsChecked = filter.Status?.ToLowerInvariant() switch { "checked" => true, "unchecked" => false, _ => null },
            CurrencyId = filter.CurrencyId,
            Search = paging.Q,
        }, paging.Skip, paging.PageSize);

        var items = page.Rows.Select(r => ToDto(r, page.ReceiptNumbers)).ToList();
        return PagedResult<WalletTransactionDto>.Of(items, page.Total, paging);
    }

    public async Task<WalletTransactionDto> AddMovementAsync(int walletId, WalletMovementRequest request, int userId)
    {
        var type = ParseType(request.Type);
        if (type is null || !ManualTypes.Contains(type.Value))
            throw new ArgumentException("Pick what kind of movement this is.", "type");

        var amount = Math.Round(request.Amount, 2);
        var moneyIn = type switch
        {
            WalletTransactionType.Deposit => true,
            WalletTransactionType.Adjustment => request.MoneyIn,
            _ => false,
        };

        DateTime? occurredAt = null;
        if (request.Date is { } date)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow.UtcToLocal());
            if (date > today) throw new ArgumentException("The date cannot be in the future.", "date");
            // An earlier day is recorded at midday of that day, so it never slides into the day before or after when shown in local time.
            if (date < today) occurredAt = date.ToDateTime(new TimeOnly(12, 0)).LocalToUtc();
        }

        var row = await _wallets.AddMovementAsync(walletId, new WalletMovement(type.Value, moneyIn ? amount : -amount, request.CurrencyId,
            request.Reference, request.Notes, request.SupplierId, occurredAt), userId);

        // Read back with its currency, user and supplier filled in.
        var page = await _wallets.GetTransactionAsync(walletId, row.Id);
        var saved = page.Rows.FirstOrDefault() ?? row;
        return ToDto(saved, page.ReceiptNumbers);
    }

    public Task<int> SetCheckedAsync(int walletId, WalletCheckRequest request, int userId) =>
        _wallets.SetCheckedAsync(walletId, request.Ids.Distinct().ToList(), request.Checked, userId);

    private async Task<WalletDto> ToDtoAsync(Wallet wallet)
    {
        var balances = await _wallets.GetBalancesAsync(wallet.Id);
        return new WalletDto
        {
            Id = wallet.Id,
            Name = wallet.Name,
            Provider = wallet.Provider.ToString(),
            AccountNumber = wallet.AccountNumber,
            PaymentMethodId = wallet.PaymentMethodId,
            PaymentMethodName = wallet.PaymentMethod?.Name ?? string.Empty,
            IsActive = wallet.IsActive,
            Balances = balances.Select(b => new WalletBalanceDto
            {
                CurrencyId = b.CurrencyId,
                CurrencyCode = b.CurrencyCode,
                CurrencyNotation = b.CurrencyNotation,
                Balance = b.Balance,
                UncheckedCount = b.UncheckedCount,
                UncheckedAmount = b.UncheckedAmount,
            }).ToList(),
        };
    }

    private static WalletTransactionDto ToDto(WalletTransaction t, IReadOnlyDictionary<int, string> receipts) => new()
    {
        Id = t.Id,
        Type = t.Type.ToString(),
        Amount = t.Amount,
        CurrencyId = t.CurrencyId,
        CurrencyNotation = t.Currency?.Notation ?? string.Empty,
        Reference = t.Reference,
        Notes = t.Notes,
        OrderId = t.OrderId,
        ReceiptNumber = t.OrderId is { } orderId ? receipts.GetValueOrDefault(orderId) : null,
        SupplierId = t.SupplierId,
        SupplierName = t.Supplier?.Name,
        UserName = t.User is null ? string.Empty : FullName(t.User),
        OccurredAt = t.OccurredAt,
        IsChecked = t.IsChecked,
        CheckedAt = t.CheckedAt,
    };

    private static string FullName(Core.Entities.Users.User user)
    {
        var name = $"{user.FirstName} {user.LastName}".Trim();
        return name.Length > 0 ? name : user.Username;
    }

    private static WalletTransactionType? ParseType(string? text) =>
        Enum.TryParse<WalletTransactionType>(text, ignoreCase: true, out var type) && Enum.IsDefined(type) ? type : null;
}
