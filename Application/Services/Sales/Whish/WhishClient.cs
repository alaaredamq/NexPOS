using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Core.Entities.Sales;
using Microsoft.Extensions.Logging;

namespace Application.Services.Sales.Whish;

/// <summary>The three values Whish gives with a merchant account, and which of its systems to use.</summary>
public sealed record WhishCredentials(WalletOnlineMode Mode, string Channel, string Secret, string WebsiteUrl);

public sealed record WhishCollectRequest(decimal Amount, string Currency, long ExternalId, string Invoice, string ReturnUrl);

/// <summary>What the customer opens to pay.</summary>
public sealed record WhishCollectStarted(string CollectUrl);

public enum WhishCollectOutcome
{
    Pending,
    Paid,
    Failed,
}

public sealed record WhishCollectState(WhishCollectOutcome Outcome, string? PayerPhone);

/// <summary>Whish said no, or could not be reached. <see cref="Exception.Message"/> is fit to show the cashier.</summary>
public sealed class WhishException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Whish's "collect" service: ask for a payment link, then ask whether it was paid. There is no push to the till (it has no public address),
/// so the till asks again every few seconds while the cashier waits.
/// </summary>
public interface IWhishClient
{
    Task<WhishCollectStarted> StartCollectAsync(WhishCredentials credentials, WhishCollectRequest request, CancellationToken cancellationToken = default);

    Task<WhishCollectState> GetCollectStateAsync(WhishCredentials credentials, string currency, long externalId, CancellationToken cancellationToken = default);
}

/// <summary>Where Whish's systems are. The defaults are the addresses known to work today; Whish's own documents win if they differ.</summary>
public sealed class WhishOptions
{
    public const string Section = "Whish";

    public string SandboxBaseUrl { get; set; } = "https://lb.sandbox.whish.money/itel-service/api";

    public string LiveBaseUrl { get; set; } = "https://whish.money/itel-service/api";

    /// <summary>Use <see cref="FakeWhishClient"/> instead of the real Whish (demos, tests, trying the screens before a merchant account exists).</summary>
    public bool Fake { get; set; }

    /// <summary>With <see cref="Fake"/>: a request counts as paid this many seconds after it was sent.</summary>
    public int FakePaysAfterSeconds { get; set; } = 8;
}

/// <summary>
/// The real Whish. Every call is a JSON POST with the headers <c>channel</c>, <c>secret</c> and <c>websiteUrl</c>; every answer is
/// <c>{ status, dialog: { message }, data }</c>. Built from a working public integration, since Whish shares its documents only with
/// merchants: check the field names against those documents when the account arrives.
/// </summary>
public sealed class WhishHttpClient : IWhishClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly WhishOptions _options;
    private readonly ILogger<WhishHttpClient> _logger;

    public WhishHttpClient(HttpClient http, Microsoft.Extensions.Options.IOptions<WhishOptions> options, ILogger<WhishHttpClient> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
        _http.Timeout = TimeSpan.FromSeconds(15);
    }

    public async Task<WhishCollectStarted> StartCollectAsync(WhishCredentials credentials, WhishCollectRequest request, CancellationToken cancellationToken = default)
    {
        var data = await PostAsync(credentials, "/payment/whish", new
        {
            amount = request.Amount,
            currency = request.Currency,
            invoice = request.Invoice,
            externalId = request.ExternalId,
            successCallbackUrl = request.ReturnUrl,
            failureCallbackUrl = request.ReturnUrl,
            successRedirectUrl = request.ReturnUrl,
            failureRedirectUrl = request.ReturnUrl,
        }, cancellationToken);

        var url = data.TryGetProperty("collectUrl", out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        if (string.IsNullOrWhiteSpace(url))
            throw new WhishException("Whish did not send a payment link. Try again.");
        return new WhishCollectStarted(url);
    }

    public async Task<WhishCollectState> GetCollectStateAsync(WhishCredentials credentials, string currency, long externalId, CancellationToken cancellationToken = default)
    {
        var data = await PostAsync(credentials, "/payment/collect/status", new { currency, externalId }, cancellationToken);

        var status = data.TryGetProperty("collectStatus", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null;
        var phone = data.TryGetProperty("payerPhoneNumber", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
        var outcome = status?.Trim().ToLowerInvariant() switch
        {
            "success" => WhishCollectOutcome.Paid,
            "failed" => WhishCollectOutcome.Failed,
            _ => WhishCollectOutcome.Pending,
        };
        return new WhishCollectState(outcome, phone);
    }

    private async Task<JsonElement> PostAsync(WhishCredentials credentials, string path, object body, CancellationToken cancellationToken)
    {
        var baseUrl = (credentials.Mode == WalletOnlineMode.Live ? _options.LiveBaseUrl : _options.SandboxBaseUrl).TrimEnd('/');
        using var message = new HttpRequestMessage(HttpMethod.Post, baseUrl + path) { Content = JsonContent.Create(body, options: Json) };
        message.Headers.TryAddWithoutValidation("channel", credentials.Channel);
        message.Headers.TryAddWithoutValidation("secret", credentials.Secret);
        message.Headers.TryAddWithoutValidation("websiteUrl", credentials.WebsiteUrl);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(message, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Whish {Path} could not be reached", path);
            throw new WhishException("Whish could not be reached. Check the internet connection and try again.", ex);
        }

        using (response)
        {
            JsonElement root;
            try
            {
                root = (await response.Content.ReadFromJsonAsync<JsonElement>(Json, cancellationToken)).Clone();
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Whish {Path} answered {Status} with something that is not JSON", path, (int)response.StatusCode);
                throw new WhishException($"Whish answered with an error ({(int)response.StatusCode}). Try again later.", ex);
            }

            var ok = response.IsSuccessStatusCode && root.TryGetProperty("status", out var status)
                && (status.ValueKind == JsonValueKind.True || (status.ValueKind == JsonValueKind.String && status.GetString() is "true" or "success"));
            if (!ok)
            {
                var said = root.TryGetProperty("dialog", out var dialog) && dialog.ValueKind == JsonValueKind.Object
                    && dialog.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null;
                _logger.LogWarning("Whish {Path} refused ({Status}): {Message}", path, (int)response.StatusCode, said);
                throw new WhishException(string.IsNullOrWhiteSpace(said) ? $"Whish refused the request ({(int)response.StatusCode})." : $"Whish: {said}");
            }

            if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object) return data;
            if (root.TryGetProperty("extra", out var extra) && extra.ValueKind == JsonValueKind.Object) return extra;
            return root;
        }
    }
}

/// <summary>
/// A pretend Whish for demos and tests: every request is "paid" <see cref="WhishOptions.FakePaysAfterSeconds"/> after it was sent, by
/// <c>96170000000</c>. Tests can decide a request's fate with <see cref="Settle"/>. Never used unless <c>Whish:Fake</c> is on.
/// </summary>
public sealed class FakeWhishClient : IWhishClient
{
    private readonly ConcurrentDictionary<long, (DateTime StartedUtc, WhishCollectOutcome? Settled)> _requests = new();
    private readonly WhishOptions _options;

    public FakeWhishClient(Microsoft.Extensions.Options.IOptions<WhishOptions> options) => _options = options.Value;

    /// <summary>Makes the request paid or failed at once (tests).</summary>
    public void Settle(long externalId, bool paid) =>
        _requests.AddOrUpdate(externalId, _ => (DateTime.UtcNow, paid ? WhishCollectOutcome.Paid : WhishCollectOutcome.Failed),
            (_, old) => (old.StartedUtc, paid ? WhishCollectOutcome.Paid : WhishCollectOutcome.Failed));

    public Task<WhishCollectStarted> StartCollectAsync(WhishCredentials credentials, WhishCollectRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Amount <= 0) throw new WhishException("Whish: the amount must be above zero.");
        _requests.TryAdd(request.ExternalId, (DateTime.UtcNow, null));
        return Task.FromResult(new WhishCollectStarted($"https://pay.whish.test/collect/{request.ExternalId}"));
    }

    public Task<WhishCollectState> GetCollectStateAsync(WhishCredentials credentials, string currency, long externalId, CancellationToken cancellationToken = default)
    {
        if (!_requests.TryGetValue(externalId, out var entry))
            return Task.FromResult(new WhishCollectState(WhishCollectOutcome.Failed, null));

        var outcome = entry.Settled
            ?? (_options.FakePaysAfterSeconds >= 0 && DateTime.UtcNow - entry.StartedUtc >= TimeSpan.FromSeconds(_options.FakePaysAfterSeconds)
                ? WhishCollectOutcome.Paid
                : WhishCollectOutcome.Pending);
        return Task.FromResult(new WhishCollectState(outcome, outcome == WhishCollectOutcome.Paid ? "96170000000" : null));
    }
}
