using Application.IServices.Sales;
using Application.Services.Sales.Whish;
using Core.Auditing;
using Core.Entities.Sales;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Services.Sales;

public sealed class WalletPaymentService : IWalletPaymentService
{
    /// <summary>Whish collects in these only.</summary>
    private static readonly string[] Currencies = ["USD", "LBP"];

    /// <summary>The till polls every few seconds; Whish is asked at most this often per request whatever the screens do.</summary>
    private static readonly TimeSpan MinCheckInterval = TimeSpan.FromSeconds(2);

    /// <summary>A cancelled request is still checked for this long, in case the customer paid after all.</summary>
    private static readonly TimeSpan LateCheckWindow = TimeSpan.FromHours(24);

    private readonly ErpDbContext _db;
    private readonly IWhishClient _whish;
    private readonly IAuditTrail _audit;
    private readonly ILogger<WalletPaymentService> _logger;

    public WalletPaymentService(ErpDbContext db, IWhishClient whish, IAuditTrail audit, ILogger<WalletPaymentService> logger)
    {
        _db = db;
        _whish = whish;
        _audit = audit;
        _logger = logger;
    }

    public async Task<WalletPaymentRequest> RequestAsync(int walletId, decimal amount, int currencyId, string idempotencyKey, int userId)
    {
        amount = Math.Round(amount, 2);
        var existing = await _db.WalletPaymentRequests.Include(r => r.Currency)
            .FirstOrDefaultAsync(r => r.UserId == userId && r.IdempotencyKey == idempotencyKey);
        if (existing is not null)
        {
            if (existing.WalletId != walletId || existing.Amount != amount || existing.CurrencyId != currencyId)
                throw new WalletPaymentRefusedException(WalletPaymentCodes.KeyReused, "This request key was already used for another amount.");
            return existing;
        }

        var wallet = await _db.Wallets.AsNoTracking().FirstOrDefaultAsync(w => w.Id == walletId && !w.IsDeleted);
        var credentials = wallet is null ? null : CredentialsOf(wallet);
        if (wallet is null || !wallet.IsActive || credentials is null)
            throw new WalletPaymentRefusedException(WalletPaymentCodes.NotSetUp, "Online Whish payments are not set up. Ask the owner to add the Whish keys in Whish Wallet settings.");

        var currency = await _db.Currencies.AsNoTracking().FirstOrDefaultAsync(c => c.Id == currencyId);
        var code = currency?.Code?.Trim().ToUpperInvariant();
        if (code is null || !Currencies.Contains(code))
            throw new WalletPaymentRefusedException(WalletPaymentCodes.CurrencyNotAccepted, "Whish only takes dollars (USD) or Lebanese pounds (LBP). Switch the sale's currency.");
        if (amount <= 0)
            throw new WalletPaymentRefusedException(WalletPaymentCodes.Mismatch, "The amount must be above zero.");

        var request = new WalletPaymentRequest
        {
            WalletId = walletId,
            ExternalId = await NewExternalIdAsync(),
            Amount = amount,
            CurrencyId = currencyId,
            CurrencyCode = code,
            IdempotencyKey = idempotencyKey,
            UserId = userId,
            Status = WalletPaymentStatus.Pending,
        };

        // Saved before Whish is asked: if the answer is lost, the request (and its number at Whish) is still known and can be checked.
        _db.WalletPaymentRequests.Add(request);
        await _db.SaveChangesAsync();

        try
        {
            var started = await _whish.StartCollectAsync(credentials,
                new WhishCollectRequest(amount, code, request.ExternalId, $"{wallet.Name} payment {request.ExternalId}", ReturnUrl(credentials)));
            request.CollectUrl = started.CollectUrl;
        }
        catch (WhishException ex)
        {
            request.Status = WalletPaymentStatus.Failed;
            request.FailureMessage = Clip(ex.Message, 500);
            request.CompletedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            throw new WalletPaymentRefusedException(WalletPaymentCodes.WhishError, ex.Message);
        }

        await _db.SaveChangesAsync();
        _audit.LogBusiness(AuditActions.WalletPaymentRequested,
            $"Whish payment request {request.ExternalId} for {amount:0.##} {code} sent", nameof(WalletPaymentRequest),
            entityId: request.Id, entityLabel: request.ExternalId.ToString(),
            details: new { request.Id, request.ExternalId, amount, code, walletId, mode = credentials.Mode.ToString() });

        request.Currency = currency!;
        return request;
    }

    public async Task<WalletPaymentRequest?> RefreshAsync(int id)
    {
        var request = await _db.WalletPaymentRequests.Include(r => r.Currency).Include(r => r.Wallet).FirstOrDefaultAsync(r => r.Id == id);
        if (request is null) return null;

        var open = request.Status == WalletPaymentStatus.Pending
            || (request.Status == WalletPaymentStatus.Cancelled && DateTime.UtcNow - request.CreatedAt < LateCheckWindow);
        if (!open || request.CollectUrl is null) return request;
        if (request.LastCheckedAt is { } last && DateTime.UtcNow - last < MinCheckInterval) return request;

        var credentials = CredentialsOf(request.Wallet);
        if (credentials is null) return request;

        try
        {
            var state = await _whish.GetCollectStateAsync(credentials, request.CurrencyCode, request.ExternalId);
            request.LastCheckedAt = DateTime.UtcNow;
            if (state.Outcome == WhishCollectOutcome.Paid)
            {
                var wasCancelled = request.Status == WalletPaymentStatus.Cancelled;
                request.Status = WalletPaymentStatus.Paid;
                request.PayerPhone = Clip(state.PayerPhone, 40);
                request.CompletedAt = DateTime.UtcNow;
                _audit.LogBusiness(AuditActions.WalletPaymentPaid,
                    $"Whish payment {request.ExternalId} paid ({request.Amount:0.##} {request.CurrencyCode})", nameof(WalletPaymentRequest),
                    entityId: request.Id, entityLabel: request.ExternalId.ToString(),
                    details: new { request.Id, request.ExternalId, request.Amount, request.CurrencyCode, request.PayerPhone, wasCancelled });
            }
            else if (state.Outcome == WhishCollectOutcome.Failed && request.Status == WalletPaymentStatus.Pending)
            {
                request.Status = WalletPaymentStatus.Failed;
                request.FailureMessage = "Whish says the payment did not go through.";
                request.CompletedAt = DateTime.UtcNow;
            }

            await _db.SaveChangesAsync();
        }
        catch (WhishException ex)
        {
            // Not reachable right now: keep waiting, the next poll tries again.
            _logger.LogInformation("Whish status of {ExternalId} not available yet: {Message}", request.ExternalId, ex.Message);
            request.LastCheckedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }

        return request;
    }

    public async Task<WalletPaymentRequest?> GetAsync(int id) =>
        await _db.WalletPaymentRequests.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id);

    public async Task<WalletPaymentRequest?> CancelAsync(int id)
    {
        var request = await _db.WalletPaymentRequests.Include(r => r.Currency).FirstOrDefaultAsync(r => r.Id == id);
        if (request is null) return null;
        if (request.Status == WalletPaymentStatus.Pending)
        {
            request.Status = WalletPaymentStatus.Cancelled;
            await _db.SaveChangesAsync();
        }

        return request;
    }

    public async Task<Wallet?> SetOnlineAsync(int walletId, WalletOnlineMode mode, string? channel, string? secret, string? websiteUrl)
    {
        var wallet = await _db.Wallets.Include(w => w.PaymentMethod).FirstOrDefaultAsync(w => w.Id == walletId && !w.IsDeleted);
        if (wallet is null) return null;

        wallet.OnlineMode = mode;
        wallet.ApiChannel = Clip(channel, 200);
        if (!string.IsNullOrWhiteSpace(secret)) wallet.ApiSecret = Clip(secret, 500);
        wallet.ApiWebsiteUrl = Clip(websiteUrl, 300);
        wallet.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return wallet;
    }

    public async Task<List<WalletPaymentRequest>> GetPaidWithoutSaleAsync(int walletId) =>
        await _db.WalletPaymentRequests.AsNoTracking().Include(r => r.Currency)
            .Where(r => r.WalletId == walletId && r.Status == WalletPaymentStatus.Paid && r.OrderId == null)
            .OrderByDescending(r => r.Id)
            .Take(50)
            .ToListAsync();

    /// <summary>The keys to talk to Whish with, or null while online payments are off or a key is missing.</summary>
    public static WhishCredentials? CredentialsOf(Wallet wallet) =>
        wallet.OnlineMode == WalletOnlineMode.Off
        || string.IsNullOrWhiteSpace(wallet.ApiChannel) || string.IsNullOrWhiteSpace(wallet.ApiSecret) || string.IsNullOrWhiteSpace(wallet.ApiWebsiteUrl)
            ? null
            : new WhishCredentials(wallet.OnlineMode, wallet.ApiChannel.Trim(), wallet.ApiSecret.Trim(), wallet.ApiWebsiteUrl.Trim());

    /// <summary>Where the customer's browser goes after paying: the shop's registered website (the till itself is not reachable from outside).</summary>
    private static string ReturnUrl(WhishCredentials credentials) =>
        credentials.WebsiteUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? credentials.WebsiteUrl : "https://" + credentials.WebsiteUrl;

    /// <summary>A number Whish has never seen from this shop: milliseconds since 1970 times 100 plus a random tail (stays below 2^53 for JS).</summary>
    private async Task<long> NewExternalIdAsync()
    {
        for (var attempt = 0; ; attempt++)
        {
            var id = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 100 + Random.Shared.Next(100);
            if (!await _db.WalletPaymentRequests.AnyAsync(r => r.ExternalId == id) || attempt > 20) return id;
        }
    }

    private static string? Clip(string? text, int max)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var trimmed = text.Trim();
        return trimmed.Length > max ? trimmed[..max] : trimmed;
    }
}
