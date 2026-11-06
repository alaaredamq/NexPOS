using System.Net;
using System.Net.Http.Json;
using Common.Licensing;

namespace Sync;

/// <summary>What the Online screen shows. Never contains the device token.</summary>
public sealed record OnlineStatusInfo(
    bool Enabled,
    string? CloudHost,
    string? SiteName,
    string? ClientName,
    DateTime? ConnectedAtUtc,
    DateTime? LastAttemptUtc,
    DateTime? LastSuccessUtc,
    string? LastError,
    string? LastErrorCode,
    long PendingChanges,
    string? SubscriptionStatus,
    DateTime? SubscriptionExpiresAtUtc,
    string? SettingsProblem);

/// <summary>Connecting failed; <see cref="Code"/> is stable for the UI, <see cref="Exception.Message"/> is plain English for logs and fallback.</summary>
public sealed class OnlineConnectException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public static class OnlineConnectCodes
{
    public const string LinkInvalid = "enroll_link_invalid";
    public const string Insecure = "cloud_insecure_url";
    public const string Unreachable = "cloud_unreachable";
    public const string CloudError = "cloud_error";
}

public interface IOnlineConnection
{
    OnlineStatusInfo GetStatus();

    /// <summary>Redeems the pasted link, stores the credential and starts syncing (no restart).</summary>
    Task<OnlineStatusInfo> ConnectAsync(string link, CancellationToken ct);

    /// <summary>Stops syncing and forgets the credential. Local data is untouched; the cloud keeps its copy until the provider removes it.</summary>
    Task<OnlineStatusInfo> DisconnectAsync(CancellationToken ct);

    /// <summary>Asks the background loop to push now instead of at the next interval.</summary>
    void SyncNow();
}

/// <summary>Lets the screen wake the background loop immediately (after connecting, disconnecting or "Sync now").</summary>
public sealed class OnlineSignal
{
    private readonly SemaphoreSlim _gate = new(0, 1);

    public void Wake()
    {
        try { _gate.Release(); }
        catch (SemaphoreFullException) { /* already signalled */ }
    }

    /// <returns>True when woken, false when the timeout passed.</returns>
    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken ct) => _gate.WaitAsync(timeout, ct);
}

public sealed class OnlineConnection(
    OnlineSettingsStore store,
    SyncStatus status,
    IHttpClientFactory httpFactory,
    OnlineSignal signal,
    TimeProvider time,
    IMachineIdProvider? machineIdProvider = null) : IOnlineConnection
{
    public OnlineStatusInfo GetStatus()
    {
        var s = store.Current;
        var snapshot = status.Snapshot();
        var plan = status.Plan();
        var active = s.Enabled && s.IsConfigured;

        string? host = null;
        if (active && Uri.TryCreate(s.CloudUrl, UriKind.Absolute, out var uri)) host = uri.Authority;

        return new OnlineStatusInfo(
            Enabled: active,
            CloudHost: host,
            SiteName: active ? s.SiteName : null,
            ClientName: active ? s.ClientName : null,
            ConnectedAtUtc: active ? s.ConnectedAtUtc : null,
            LastAttemptUtc: snapshot.LastAttemptUtc,
            LastSuccessUtc: snapshot.LastSuccessUtc,
            LastError: snapshot.LastError,
            LastErrorCode: snapshot.LastErrorCode,
            PendingChanges: active ? snapshot.Pending : 0,
            SubscriptionStatus: active ? plan.Status : null,
            SubscriptionExpiresAtUtc: active ? plan.ExpiresAtUtc : null,
            SettingsProblem: store.LoadError);
    }

    public async Task<OnlineStatusInfo> ConnectAsync(string link, CancellationToken ct)
    {
        var problem = EnrollmentLink.TryParse(link, out var parsed);
        if (problem == EnrollmentLink.Problem.Insecure)
            throw new OnlineConnectException(OnlineConnectCodes.Insecure, "The link must start with https://.");
        if (problem != EnrollmentLink.Problem.None || parsed is null)
            throw new OnlineConnectException(OnlineConnectCodes.LinkInvalid, "That does not look like a NexPOS Online link.");

        var request = new EnrollRequest(parsed.Code, ReadMachineId(), Environment.MachineName);
        EnrollResponse response;
        try
        {
            using var http = httpFactory.CreateClient(OnlineSyncService.HttpClientName);
            http.Timeout = TimeSpan.FromSeconds(20);
            using var reply = await http.PostAsJsonAsync(new Uri(parsed.BaseUri, "api/enroll"), request, SyncWire.Json, ct);
            if (!reply.IsSuccessStatusCode)
                throw await FailureAsync(reply, ct);
            response = await reply.Content.ReadFromJsonAsync<EnrollResponse>(SyncWire.Json, ct)
                       ?? throw new OnlineConnectException(OnlineConnectCodes.CloudError, "The cloud sent an empty answer.");
        }
        catch (HttpRequestException ex)
        {
            throw new OnlineConnectException(OnlineConnectCodes.Unreachable, $"Could not reach {parsed.BaseUri.Host}: {ex.Message}");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new OnlineConnectException(OnlineConnectCodes.Unreachable, $"{parsed.BaseUri.Host} did not answer in time.");
        }

        status.Reset();
        store.Save(store.Current with
        {
            Enabled = true,
            CloudUrl = parsed.BaseUri.ToString().TrimEnd('/'),
            DeviceToken = response.Token,
            SiteName = response.SiteName,
            ClientName = response.ClientName,
            ConnectedAtUtc = time.GetUtcNow().UtcDateTime,
        });
        signal.Wake();
        return GetStatus();
    }

    public Task<OnlineStatusInfo> DisconnectAsync(CancellationToken ct)
    {
        store.Save(store.Current with
        {
            Enabled = false, CloudUrl = "", DeviceToken = "", SiteName = null, ClientName = null, ConnectedAtUtc = null,
        });
        status.Reset();
        signal.Wake();
        return Task.FromResult(GetStatus());
    }

    public void SyncNow() => signal.Wake();

    private string? ReadMachineId()
    {
        try { return machineIdProvider?.GetMachineId().ToString(); }
        catch { return null; } // hardware lookup failed: the id is only a support aid
    }

    private static async Task<OnlineConnectException> FailureAsync(HttpResponseMessage reply, CancellationToken ct)
    {
        SyncError? error = null;
        try { error = await reply.Content.ReadFromJsonAsync<SyncError>(SyncWire.Json, ct); } catch { /* not JSON */ }

        // The cloud's own codes pass through (enroll_code_invalid / _expired / _used, site_disabled, rate_limited).
        if (error is not null) return new OnlineConnectException(error.Code, error.Message);
        return reply.StatusCode == HttpStatusCode.NotFound
            ? new OnlineConnectException(SyncErrorCodes.EnrollCodeInvalid, "The cloud does not know this link.")
            : new OnlineConnectException(OnlineConnectCodes.CloudError, $"The cloud answered {(int)reply.StatusCode}.");
    }
}
