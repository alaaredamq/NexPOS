using Core.Entities.Base;
using Core.Entities.Users;

namespace Core.Entities.Sales;

/// <summary>Which company holds the wallet's money. Only Whish today; OMT or a bank account can join later.</summary>
public enum WalletProvider
{
    Whish = 1,
}

/// <summary>
/// A money account the shop holds outside the cash drawer, such as the shop's Whish Money account. It is tied to one payment method:
/// every sale paid with that method puts money in, and its void takes it back out. The balance is never stored; it is the sum of the
/// wallet's <see cref="WalletTransaction"/> rows, kept per currency (a Whish account holds dollars and pounds apart).
/// </summary>
public class Wallet : ISSoftDeletable, IEntityBase<int>
{
    public string Name { get; set; } = null!;

    public WalletProvider Provider { get; set; }

    /// <summary>The shop's number on the provider (for Whish, the phone number customers send money to). Shown to the cashier.</summary>
    public string? AccountNumber { get; set; }

    /// <summary>The payment method whose sales land in this wallet.</summary>
    public int PaymentMethodId { get; set; }

    public PaymentMethod PaymentMethod { get; set; } = null!;

    /// <summary>Turned off: sales with its payment method no longer land here (the history stays).</summary>
    public bool IsActive { get; set; } = true;

    public ICollection<WalletTransaction> Transactions { get; set; } = new List<WalletTransaction>();

    // ---- online payments (step 2): the till asks Whish for a payment link and checks it itself --------------------------

    /// <summary>Off: the cashier types the Whish number by hand. Sandbox / Live: the till can send a payment request to Whish.</summary>
    public WalletOnlineMode OnlineMode { get; set; } = WalletOnlineMode.Off;

    /// <summary>The "channel" Whish gives with a merchant account.</summary>
    public string? ApiChannel { get; set; }

    /// <summary>The "secret" Whish gives with a merchant account. Never sent to a browser; redacted in the audit trail.</summary>
    [Core.Auditing.AuditSensitive]
    public string? ApiSecret { get; set; }

    /// <summary>The website Whish registered for the merchant (sent with every request, and where the customer returns after paying).</summary>
    public string? ApiWebsiteUrl { get; set; }

    public Wallet(string name, WalletProvider provider, int paymentMethodId, string? accountNumber)
    {
        Name = name;
        Provider = provider;
        PaymentMethodId = paymentMethodId;
        AccountNumber = accountNumber;
        CreatedAt = DateTime.UtcNow;
    }

    protected Wallet() { }
}

/// <summary>Why money moved in or out of a wallet. The numbers are stored, so never renumber them.</summary>
public enum WalletTransactionType
{
    /// <summary>A sale paid into the wallet (made by checkout).</summary>
    Sale = 1,

    /// <summary>A voided or cancelled sale taken back out (made by the void).</summary>
    SaleReversal = 2,

    /// <summary>Money put into the wallet by hand, e.g. a top-up.</summary>
    Deposit = 3,

    /// <summary>Money taken out, e.g. cashed out at an agent or sent to the bank.</summary>
    Withdrawal = 4,

    /// <summary>A supplier paid from the wallet.</summary>
    SupplierPayment = 5,

    /// <summary>A fee the provider took.</summary>
    Fee = 6,

    /// <summary>A correction, in or out, so the balance matches the provider's app (also the opening balance).</summary>
    Adjustment = 7,
}

/// <summary>
/// One movement of money in a wallet. Append-only: a mistake is fixed with an <see cref="WalletTransactionType.Adjustment"/>, never by
/// editing or deleting a row, so the history always explains the balance. Only <see cref="IsChecked"/> changes after the fact, when the
/// owner ticks the row off against the provider's own statement.
/// </summary>
public class WalletTransaction : BaseEntity, IEntityBase<int>
{
    public int WalletId { get; set; }

    public Wallet Wallet { get; set; } = null!;

    public WalletTransactionType Type { get; set; }

    /// <summary>Signed: positive is money in, negative is money out. In <see cref="CurrencyId"/>.</summary>
    public decimal Amount { get; set; }

    public int CurrencyId { get; set; }

    public Currency Currency { get; set; } = null!;

    /// <summary>The provider's own transfer number (from the customer's Whish screen), so the row can be found in the Whish app.</summary>
    public string? Reference { get; set; }

    public string? Notes { get; set; }

    /// <summary>The sale this row belongs to (Sale and SaleReversal rows).</summary>
    public int? OrderId { get; set; }

    public Order? Order { get; set; }

    /// <summary>The supplier paid (SupplierPayment rows).</summary>
    public int? SupplierId { get; set; }

    public Products.Supplier? Supplier { get; set; }

    /// <summary>The row this one undoes (SaleReversal rows point at their Sale).</summary>
    public int? ReversesId { get; set; }

    /// <summary>Who recorded it.</summary>
    public int UserId { get; set; }

    public User User { get; set; } = null!;

    /// <summary>When the money moved (UTC). Usually the moment it was recorded.</summary>
    public DateTime OccurredAt { get; set; }

    /// <summary>Ticked off against the provider's statement.</summary>
    public bool IsChecked { get; set; }

    public DateTime? CheckedAt { get; set; }

    public int? CheckedByUserId { get; set; }

    public WalletTransaction(int walletId, WalletTransactionType type, decimal amount, int currencyId, int userId,
        string? reference = null, string? notes = null)
    {
        WalletId = walletId;
        Type = type;
        Amount = amount;
        CurrencyId = currencyId;
        UserId = userId;
        Reference = reference;
        Notes = notes;
        OccurredAt = DateTime.UtcNow;
        CreatedAt = OccurredAt;
    }

    protected WalletTransaction() { }
}
