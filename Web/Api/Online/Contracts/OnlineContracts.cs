using System.ComponentModel.DataAnnotations;

namespace Web.Api.Online.Contracts;

/// <summary>
/// The Online screen: whether this till is connected to NexPOS Online, to whom, and how the last synchronisation went.
/// The device token is never part of it.
/// </summary>
public sealed record OnlineStatusDto
{
    /// <summary>True when the till holds a credential and is syncing (or trying to).</summary>
    public required bool Connected { get; init; }

    /// <summary>Address of the cloud, e.g. <c>cloud.example.com</c>.</summary>
    public string? CloudHost { get; init; }

    /// <summary>The installation's name as set up by the provider (e.g. "Main till").</summary>
    public string? SiteName { get; init; }

    /// <summary>The client (shop) the installation belongs to.</summary>
    public string? ClientName { get; init; }

    public DateTime? ConnectedAtUtc { get; init; }

    public DateTime? LastAttemptUtc { get; init; }

    /// <summary>When the cloud last confirmed receiving changes. Null until the first successful sync.</summary>
    public DateTime? LastSuccessUtc { get; init; }

    /// <summary>
    /// Stable machine code of the problem, or null while all is well: <c>network</c>, <c>timeout</c>, <c>subscription_inactive</c>,
    /// <c>unauthorized</c>, <c>schema_newer</c>, <c>bad_batch</c>, <c>server_error</c>. The screen maps it to a sentence in the user's language.
    /// </summary>
    public string? LastErrorCode { get; init; }

    /// <summary>English detail from the cloud, for support. The screen shows its own localized sentence for <c>lastErrorCode</c>.</summary>
    public string? LastError { get; init; }

    /// <summary>Changes waiting to be sent. They are safe locally; selling never waits for them.</summary>
    public required long PendingChanges { get; init; }

    /// <summary><c>Active</c>, <c>Grace</c>, <c>Expired</c>, <c>Suspended</c> or <c>None</c>, as last reported by the cloud; null until known.</summary>
    public string? SubscriptionStatus { get; init; }

    public DateTime? SubscriptionExpiresAtUtc { get; init; }

    /// <summary>Set when the settings file exists but cannot be read; Online then stays off until it is fixed or the till is reconnected.</summary>
    public string? SettingsProblem { get; init; }
}

public sealed record OnlineConnectRequest
{
    /// <summary>The connection link from the provider (<c>https://.../enroll/XXXX-XXXX-XXXX</c>). Pasting the whole WhatsApp message also works.</summary>
    [Required, StringLength(4000)]
    public string Link { get; init; } = "";
}
