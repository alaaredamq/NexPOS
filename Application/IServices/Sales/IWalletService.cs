using Core.Entities.Sales;

namespace Application.IServices.Sales;

/// <summary>
/// Wallets such as the shop's Whish Money account: the money in them, and every movement that explains it. Checkout and the void call
/// <see cref="RecordSaleAsync"/> / <see cref="ReverseSaleAsync"/> inside their own transaction, so a sale and its wallet row are saved
/// (or undone) together.
/// </summary>
public interface IWalletService
{
    // ---- called by checkout and the void ----------------------------------------------------------------------------

    /// <summary>Puts a sale into the active wallet of its payment method. Returns null (and writes nothing) when that method has no wallet.</summary>
    Task<WalletTransaction?> RecordSaleAsync(int paymentMethodId, int orderId, decimal amount, int currencyId, string? reference, int userId);

    /// <summary>Takes a voided or cancelled sale back out of its wallet. Returns how many rows were reversed (0 = the sale was not in a wallet).</summary>
    Task<int> ReverseSaleAsync(int orderId, int userId, string reason);

    /// <summary>The active wallets, by the payment method that feeds them (the till asks for a Whish reference on those).</summary>
    Task<IReadOnlyDictionary<int, Wallet>> GetActiveByPaymentMethodAsync();

    // ---- the wallet page ---------------------------------------------------------------------------------------------

    Task<List<Wallet>> GetAllAsync();

    Task<Wallet?> GetAsync(int id);

    /// <summary>Money held per currency, and how many rows are not ticked off yet.</summary>
    Task<List<WalletBalance>> GetBalancesAsync(int walletId);

    /// <summary>
    /// Sets up a Whish wallet. Uses the payment method called "Whish" when the shop already made one, else creates it, so checkout offers it
    /// at once. Throws <see cref="InvalidOperationException"/> when a wallet already uses that method.
    /// </summary>
    Task<Wallet> CreateWhishAsync(string name, string? accountNumber, int userId);

    /// <summary>Null when there is no such wallet.</summary>
    Task<Wallet?> UpdateAsync(int id, string name, string? accountNumber, bool isActive);

    Task<WalletTransactionPage> GetTransactionsAsync(int walletId, WalletTransactionFilter filter, int skip, int take);

    /// <summary>One row with its currency, user and supplier (and its receipt number in the page's map); empty page when not found.</summary>
    Task<WalletTransactionPage> GetTransactionAsync(int walletId, int transactionId);

    /// <summary>
    /// A movement recorded by hand. <paramref name="signedAmount"/> is positive for money in. Sale rows only come from checkout, so
    /// <see cref="WalletTransactionType.Sale"/> / <see cref="WalletTransactionType.SaleReversal"/> throw <see cref="ArgumentException"/>.
    /// </summary>
    Task<WalletTransaction> AddMovementAsync(int walletId, WalletMovement movement, int userId);

    /// <summary>Ticks rows off (or back on) against the provider's statement. Returns how many changed; ids of other wallets are ignored.</summary>
    Task<int> SetCheckedAsync(int walletId, IReadOnlyCollection<int> transactionIds, bool isChecked, int userId);
}

public sealed record WalletBalance(int CurrencyId, string CurrencyCode, string CurrencyNotation, decimal Balance, int UncheckedCount, decimal UncheckedAmount);

public sealed record WalletMovement(
    WalletTransactionType Type,
    decimal SignedAmount,
    int CurrencyId,
    string? Reference,
    string? Notes,
    int? SupplierId,
    DateTime? OccurredAtUtc);

public sealed class WalletTransactionFilter
{
    public DateTime? FromUtc { get; init; }

    public DateTime? ToUtc { get; init; }

    public WalletTransactionType? Type { get; init; }

    /// <summary>true = only ticked rows, false = only rows still to check, null = both.</summary>
    public bool? IsChecked { get; init; }

    public int? CurrencyId { get; init; }

    /// <summary>Part of the reference, the notes or the receipt number.</summary>
    public string? Search { get; init; }
}

/// <param name="ReceiptNumbers">The receipt of each sale row, by order id.</param>
public sealed record WalletTransactionPage(List<WalletTransaction> Rows, int Total, IReadOnlyDictionary<int, string> ReceiptNumbers);
