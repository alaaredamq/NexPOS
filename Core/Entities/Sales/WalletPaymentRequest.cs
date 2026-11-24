using Core.Entities.Base;
using Core.Entities.Users;

namespace Core.Entities.Sales;

/// <summary>Whether the till sends payment requests to the wallet's company itself. Stored as a number: never renumber.</summary>
public enum WalletOnlineMode
{
    Off = 0,

    /// <summary>Whish's test system: no real money moves (Whish gives test numbers).</summary>
    Sandbox = 1,

    Live = 2,
}

/// <summary>Where a payment request stands. Stored as a number: never renumber.</summary>
public enum WalletPaymentStatus
{
    /// <summary>Sent to Whish; the customer has not paid yet.</summary>
    Pending = 1,

    /// <summary>Whish says the customer paid. Ready to complete a sale with (once).</summary>
    Paid = 2,

    /// <summary>Whish says the payment failed or was refused.</summary>
    Failed = 3,

    /// <summary>The cashier gave up waiting. If Whish later reports it paid, it becomes <see cref="Paid"/> with no sale (the wallet page shows it).</summary>
    Cancelled = 4,
}

/// <summary>
/// One request for money sent to Whish from the till (Whish calls it a "collect"). The customer opens <see cref="CollectUrl"/> (the till shows
/// it as a QR code) and pays; the till asks Whish for the result by <see cref="ExternalId"/>. A paid request completes exactly one sale:
/// <see cref="OrderId"/> is set in the checkout transaction, and the wallet's Sale row comes already ticked, because Whish confirmed it.
/// </summary>
public class WalletPaymentRequest : BaseEntity, IEntityBase<int>
{
    public int WalletId { get; set; }

    public Wallet Wallet { get; set; } = null!;

    /// <summary>Our number for this request at Whish ("externalId"). Unique; also the reference printed on the receipt.</summary>
    public long ExternalId { get; set; }

    public decimal Amount { get; set; }

    public int CurrencyId { get; set; }

    public Currency Currency { get; set; } = null!;

    /// <summary>"USD" or "LBP": the only currencies Whish collects in.</summary>
    public string CurrencyCode { get; set; } = null!;

    /// <summary>The page Whish made for the customer to pay on.</summary>
    public string? CollectUrl { get; set; }

    public WalletPaymentStatus Status { get; set; } = WalletPaymentStatus.Pending;

    /// <summary>The Whish account that paid, as Whish reports it.</summary>
    public string? PayerPhone { get; set; }

    /// <summary>The till's own key for this attempt, so a repeated click does not send a second request.</summary>
    public string IdempotencyKey { get; set; } = null!;

    /// <summary>Who sent it.</summary>
    public int UserId { get; set; }

    public User User { get; set; } = null!;

    public DateTime? LastCheckedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    /// <summary>The sale it paid for; null until checkout used it.</summary>
    public int? OrderId { get; set; }

    public Order? Order { get; set; }

    /// <summary>Whish's own message when it refused or failed.</summary>
    public string? FailureMessage { get; set; }
}
