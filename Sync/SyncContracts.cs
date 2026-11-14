using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sync;

/// <summary>
/// What travels between a till and the cloud. The sync is state-based: a change names a row and carries its
/// CURRENT column values (or says it is gone), so replaying a batch is harmless and a batch never depends on
/// an earlier one except for the very first, which wipes the mirror (<see cref="Initial"/>).
/// </summary>
/// <param name="SchemaVersion">Latest EF migration id applied on the till. The cloud refuses a till that is ahead of it.</param>
/// <param name="FromSeq">Till-side log position this batch starts after (informational).</param>
/// <param name="ThroughSeq">Till-side log position covered once this batch is acknowledged.</param>
/// <param name="Initial">First batch of a full snapshot: the mirror is emptied before the rows are applied.</param>
public sealed record SyncBatch(
    string SchemaVersion,
    long FromSeq,
    long ThroughSeq,
    bool Initial,
    IReadOnlyList<SyncChange> Changes);

/// <param name="Table">Table name; must be one the cloud knows (<see cref="SyncSchema"/>).</param>
/// <param name="Values">Column -> value for an upsert. Numbers, strings, null, or a <see cref="SyncBlob"/> for bytes. Null when <paramref name="Enc"/> is used.</param>
/// <param name="Enc">The row, end-to-end encrypted on the till (<see cref="RowCipher"/>). The cloud stores it as it is and cannot read it.
/// Only the table, row id and "deleted" are visible to the cloud.</param>
public sealed record SyncChange(string Table, long Id, bool Deleted, Dictionary<string, object?>? Values, string? Enc = null);

/// <summary>Binary column value. JSON has no bytes, and a bare base64 string would be indistinguishable from text.</summary>
public sealed record SyncBlob([property: JsonPropertyName("$b64")] string Base64);

public sealed record SyncAck(long ThroughSeq, int Applied, bool ResyncRequested);

public sealed record SyncPing(string SiteName, string ClientName, string SubscriptionStatus, DateTime? ExpiresAtUtc);

/// <summary>What a till sends to turn a one-time enrolment code into a device token.</summary>
/// <param name="MachineId">The till's licence Machine ID. Recorded for support; never used to authenticate.</param>
public sealed record EnrollRequest(string Code, string? MachineId, string? DeviceName);

/// <summary>The device token is returned exactly once, here. The cloud keeps only its hash.</summary>
public sealed record EnrollResponse(string Token, string SiteName, string ClientName);

/// <summary>Error body. <c>Code</c> is stable and machine-readable; <c>Message</c> is for a human.</summary>
public sealed record SyncError(string Code, string Message);

public static class SyncErrorCodes
{
    public const string Unauthorized = "unauthorized";
    public const string SubscriptionInactive = "subscription_inactive";
    public const string SchemaNewer = "schema_newer";
    public const string BadBatch = "bad_batch";

    public const string EnrollCodeInvalid = "enroll_code_invalid";
    public const string EnrollCodeExpired = "enroll_code_expired";
    public const string EnrollCodeUsed = "enroll_code_used";
    public const string SiteDisabled = "site_disabled";
    public const string RateLimited = "rate_limited";
}

public static class SyncWire
{
    /// <summary>One serializer configuration for both ends so they can never drift apart.</summary>
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Converts a deserialized JSON value (a <see cref="JsonElement"/>) into something SQLite can store.</summary>
    public static object ToDbValue(object? value)
    {
        switch (value)
        {
            case null:
                return DBNull.Value;
            case JsonElement e:
                switch (e.ValueKind)
                {
                    case JsonValueKind.Null:
                    case JsonValueKind.Undefined:
                        return DBNull.Value;
                    case JsonValueKind.Number:
                        // Explicit boxing: `cond ? long : double` would promote every integer to double (precision loss, wrong column type).
                        if (e.TryGetInt64(out var l)) return (object)l;
                        return (object)e.GetDouble();
                    case JsonValueKind.String:
                        return e.GetString()!;
                    case JsonValueKind.True:
                        return 1L;
                    case JsonValueKind.False:
                        return 0L;
                    case JsonValueKind.Object when e.TryGetProperty("$b64", out var b64) && b64.ValueKind == JsonValueKind.String:
                        return Convert.FromBase64String(b64.GetString()!);
                    default:
                        throw new SyncBatchException($"Unsupported value kind {e.ValueKind}.");
                }
            case SyncBlob blob:
                return Convert.FromBase64String(blob.Base64);
            default:
                return value; // already a CLR primitive (tests, in-process callers)
        }
    }
}

/// <summary>A batch the cloud cannot apply as sent (unknown table, malformed value). Not retryable.</summary>
public sealed class SyncBatchException(string message) : Exception(message);
