namespace Cloud.Data;

/// <summary>Someone who can sign in to the admin portal (you, and later anyone who helps you). Not a client.</summary>
public class AdminUser
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

/// <summary>A shop you sell to. Owns sites (installations) and one online subscription.</summary>
public class Client
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }

    public Subscription? Subscription { get; set; }
    public List<Site> Sites { get; set; } = [];
    public List<ClientEvent> Events { get; set; } = [];
}

/// <summary>The paid "Online" service for one client. It gates the cloud mirror only; the till itself never depends on it.</summary>
public class Subscription
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public Client Client { get; set; } = null!;
    public string Plan { get; set; } = "Online";
    public DateTime StartsAt { get; set; }
    /// <summary>UTC. Last moment the subscription is fully active; a grace week follows (see SubscriptionRules).</summary>
    public DateTime ExpiresAt { get; set; }
    public bool IsSuspended { get; set; }
}

/// <summary>
/// One installation (one till = one local database = one mirror database). The mirror lives in its own file,
/// named after <see cref="Id"/>, so no client's data can ever share a database with another's.
/// </summary>
public class Site
{
    public Guid Id { get; set; }
    public int ClientId { get; set; }
    public Client Client { get; set; } = null!;
    public string Name { get; set; } = "";
    /// <summary>The till's license Machine ID, kept as a reference for support. Not used for authentication.</summary>
    public string? MachineId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastSyncAt { get; set; }
    /// <summary>The last time this till contacted the cloud for any reason (a data push or its regular check-in). Health is judged on this, not on LastSyncAt, because a quiet shop sends no data.</summary>
    public DateTime? LastSeenAt { get; set; }
    public long LastThroughSeq { get; set; }
    public string? LastSchemaVersion { get; set; }
    /// <summary>Set from the portal; the till sees it in its next acknowledgement and re-sends everything.</summary>
    public bool ResyncRequested { get; set; }
    public bool IsDisabled { get; set; }

    public List<DeviceToken> Tokens { get; set; } = [];
    public List<EnrollmentCode> EnrollmentCodes { get; set; } = [];
    public List<SiteHeartbeat> Heartbeats { get; set; } = [];
}

/// <summary>The credential a till uses. Only a SHA-256 hash is stored; the token itself is shown once, at issue time.</summary>
public class DeviceToken
{
    public int Id { get; set; }
    public Guid SiteId { get; set; }
    public Site Site { get; set; } = null!;
    public string TokenHash { get; set; } = "";
    /// <summary>First characters of the token, so an admin can tell tokens apart without being able to use one.</summary>
    public string Prefix { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
}

/// <summary>Support trail: what each push did. Trimmed to the latest few hundred per site.</summary>
public class SyncBatchLog
{
    public long Id { get; set; }
    public Guid SiteId { get; set; }
    public DateTime ReceivedAt { get; set; }
    public long ThroughSeq { get; set; }
    public int Changes { get; set; }
    public bool Initial { get; set; }
    public int DurationMs { get; set; }
    /// <summary>applied | rejected | error</summary>
    public string Outcome { get; set; } = "";
    public string? Error { get; set; }
}

/// <summary>"Extended 12 months", "Suspended", "Token rotated": a plain history you can read back when a client calls.</summary>
public class ClientEvent
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public DateTime At { get; set; }
    public string Text { get; set; } = "";
}

/// <summary>
/// A one-time code that lets a till enrol itself: the owner pastes a link containing it, the till redeems it for a
/// device token. Only the hash is stored (the code is shown once, in the portal), it works once, and it expires.
/// </summary>
public class EnrollmentCode
{
    public int Id { get; set; }
    public Guid SiteId { get; set; }
    public Site Site { get; set; } = null!;
    public string CodeHash { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }
}

/// <summary>
/// How often a till was heard from in one hour (UTC). It feeds the 24-hour contact strip in the portal, which shows at a glance
/// when a shop's PC was reachable (and so when it lost power or internet). Rows older than a week are removed.
/// </summary>
public class SiteHeartbeat
{
    public int Id { get; set; }
    public Guid SiteId { get; set; }
    public Site Site { get; set; } = null!;
    /// <summary>Start of the hour, UTC.</summary>
    public DateTime Hour { get; set; }
    public int Contacts { get; set; }
}
