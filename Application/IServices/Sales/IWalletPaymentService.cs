using Core.Entities.Sales;

namespace Application.IServices.Sales;

/// <summary>
/// Online Whish payments from the till (step 2): send a payment request, watch it, cancel it. Checkout then uses a paid request through
/// <see cref="IWalletService.RecordSaleAsync"/>, which links it to the sale so it can never pay for two.
/// </summary>
public interface IWalletPaymentService
{
    /// <summary>
    /// Asks Whish for a payment link. The same <paramref name="idempotencyKey"/> from the same user returns the request already made.
    /// Throws <see cref="WalletPaymentRefusedException"/> when it cannot be sent (not set up, wrong currency, Whish said no).
    /// </summary>
    Task<WalletPaymentRequest> RequestAsync(int walletId, decimal amount, int currencyId, string idempotencyKey, int userId);

    /// <summary>The request, asking Whish for news first when it is still open (at most every couple of seconds). Null if unknown.</summary>
    Task<WalletPaymentRequest?> RefreshAsync(int id);

    /// <summary>The request as stored, without asking Whish. Null if unknown.</summary>
    Task<WalletPaymentRequest?> GetAsync(int id);

    /// <summary>The cashier stopped waiting. A paid or used request is left as it is. Null if unknown.</summary>
    Task<WalletPaymentRequest?> CancelAsync(int id);

    /// <summary>Saves the online settings. <paramref name="secret"/> null keeps the stored one. Null if the wallet is unknown.</summary>
    Task<Wallet?> SetOnlineAsync(int walletId, WalletOnlineMode mode, string? channel, string? secret, string? websiteUrl);

    /// <summary>Paid requests no sale used (the customer paid after the cashier gave up, or the sale then failed), newest first.</summary>
    Task<List<WalletPaymentRequest>> GetPaidWithoutSaleAsync(int walletId);
}

/// <summary>A payment request that could not be sent or used. <see cref="Code"/> is stable for the UI; the message is for the cashier.</summary>
public sealed class WalletPaymentRefusedException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public static class WalletPaymentCodes
{
    public const string NotSetUp = "whish_not_set_up";
    public const string CurrencyNotAccepted = "whish_currency_not_accepted";
    public const string WhishError = "whish_error";
    public const string KeyReused = "idempotency_key_reused";
    public const string NotPaid = "whish_not_paid";
    public const string AlreadyUsed = "whish_already_used";
    public const string Mismatch = "whish_payment_mismatch";
    public const string NotFound = "whish_request_not_found";
}
