using Sync;
using Web.Api.Online.Contracts;

namespace Web.Api.Online;

public interface IOnlineApiService
{
    OnlineStatusDto Get();

    /// <summary>Throws <see cref="OnlineConnectException"/> when the link is refused; nothing changes then.</summary>
    Task<OnlineStatusDto> ConnectAsync(string link, CancellationToken ct);

    Task<OnlineStatusDto> DisconnectAsync(CancellationToken ct);

    OnlineStatusDto SyncNow();
}

/// <summary>Maps the sync library's connection to the Online screen's DTO. The library owns the behaviour; this only translates.</summary>
public sealed class OnlineApiService : IOnlineApiService
{
    private readonly IOnlineConnection _online;

    public OnlineApiService(IOnlineConnection online) => _online = online;

    public OnlineStatusDto Get() => ToDto(_online.GetStatus());

    public async Task<OnlineStatusDto> ConnectAsync(string link, CancellationToken ct)
        => ToDto(await _online.ConnectAsync(link, ct));

    public async Task<OnlineStatusDto> DisconnectAsync(CancellationToken ct)
        => ToDto(await _online.DisconnectAsync(ct));

    public OnlineStatusDto SyncNow()
    {
        _online.SyncNow();
        return Get();
    }

    private static OnlineStatusDto ToDto(OnlineStatusInfo s) => new()
    {
        Connected = s.Enabled,
        CloudHost = s.CloudHost,
        SiteName = s.SiteName,
        ClientName = s.ClientName,
        ConnectedAtUtc = s.ConnectedAtUtc,
        LastAttemptUtc = s.LastAttemptUtc,
        LastSuccessUtc = s.LastSuccessUtc,
        LastErrorCode = s.LastErrorCode,
        LastError = s.LastError,
        PendingChanges = s.PendingChanges,
        SubscriptionStatus = s.SubscriptionStatus,
        SubscriptionExpiresAtUtc = s.SubscriptionExpiresAtUtc,
        SettingsProblem = s.SettingsProblem,
        SealingState = s.SealingState,
        SealKeyId = s.SealKeyId,
    };
}
