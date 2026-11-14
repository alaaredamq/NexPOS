using System.Text.Json;

namespace Sync;

/// <summary>
/// A change the owner asked for from the phone. The cloud only queues it; the shop PC applies it through the same
/// services the till's own screens use, so validation, product history and the audit trail stay in one place.
/// </summary>
/// <param name="Type">Stable name such as <c>customer.create</c>; picks the handler on the shop PC.</param>
/// <param name="Payload">The same body the till's own API takes for this action (for example a <c>CustomerRequest</c>).</param>
/// <param name="RequestedBy">Who asked, for the audit trail ("Layla").</param>
public sealed record CommandEnvelope(Guid Id, string Type, JsonElement Payload, string RequestedBy, DateTime CreatedAtUtc);

public sealed record CommandBatchResponse(IReadOnlyList<CommandEnvelope> Commands);

/// <summary>What the shop PC reports back. <c>Outcome</c> is <see cref="Applied"/> or <see cref="Rejected"/>; a rejection is final and carries a reason a person can read.</summary>
public sealed record CommandResult(string Outcome, string? Code = null, string? Message = null, JsonElement? Data = null)
{
    public const string Applied = "applied";
    public const string Rejected = "rejected";

    public bool IsApplied => Outcome == Applied;

    public static CommandResult Ok(object? data = null)
        => new(Applied, Data: data is null ? null : JsonSerializer.SerializeToElement(data, SyncWire.Json));

    public static CommandResult Reject(string code, string message, object? data = null)
        => new(Rejected, code, message, data is null ? null : JsonSerializer.SerializeToElement(data, SyncWire.Json));
}

/// <summary>
/// How a change travels when the shop has a data key (always, once online sync is on). The phone sends
/// <c>{ "enc": "e1..." }</c>: the sealed form of <c>{ "k": idempotencyKey, "body": ... }</c>, bound to the kind of change. The cloud stores and
/// forwards it without being able to read or alter it, and the shop PC refuses anything unsealed, so the cloud cannot forge changes either.
/// <c>k</c> lets the shop PC refuse a replay: the cloud cannot make the same sealed change run twice by queueing it under a new id.
/// The answer comes back the same way, so product names and refusal reasons stay unreadable to the cloud.
/// </summary>
public static class CommandSeal
{
    public const string Field = "enc";
    public const string CommandPurpose = "command";
    public const string ResultPurpose = "result";

    /// <summary>Opens a sealed change. Null when the payload is not sealed, is not valid for this key and kind, or lacks its key.</summary>
    public static (JsonElement Body, string Key)? Open(RowCipher cipher, string type, JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object || !payload.TryGetProperty(Field, out var enc) || enc.ValueKind != JsonValueKind.String) return null;
        try
        {
            var plain = cipher.Open(CommandPurpose, type, enc.GetString()!);
            using var doc = JsonDocument.Parse(plain);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("k", out var k) || k.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(k.GetString())
                || !root.TryGetProperty("body", out var body)) return null;
            return (body.Clone(), k.GetString()!);
        }
        catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or JsonException or FormatException)
        {
            return null;
        }
    }

    /// <summary>The answer as the cloud may see it: the outcome and code, a generic message, and the real message and data sealed to the command's id.</summary>
    public static CommandResult SealResult(RowCipher cipher, Guid commandId, CommandResult plain)
    {
        var inner = new Dictionary<string, object?> { ["message"] = plain.Message, ["data"] = plain.Data };
        var sealedText = cipher.Seal(ResultPurpose, commandId.ToString("D"), JsonSerializer.SerializeToUtf8Bytes(inner, SyncWire.Json));
        var generic = plain.IsApplied ? null : "The shop PC did not apply this change. Open the app on your phone for the reason.";
        return new CommandResult(plain.Outcome, plain.Code, generic,
            JsonSerializer.SerializeToElement(new Dictionary<string, string> { [Field] = sealedText }, SyncWire.Json));
    }
}

public static class CommandErrorCodes
{
    /// <summary>The change was not sealed with the shop's key (or would not open). The shop PC refuses it: only the owner's phone has the key.</summary>
    public const string NotSealed = "not_sealed";

    /// <summary>The shop refused the values (a required field is empty, a barcode is taken). <c>Data</c> holds <c>{ errors: { field: [messages] } }</c>.</summary>
    public const string Validation = "validation";
    public const string NotFound = "not_found";
    public const string Conflict = "conflict";
    public const string UnknownType = "unknown_type";
    public const string BadPayload = "bad_payload";
    public const string ServerError = "server_error";
}

/// <summary>Who is asking and which command this is, passed to a handler.</summary>
public sealed record CommandContext(Guid CommandId, string RequestedBy);

/// <summary>
/// Applies one kind of command on the shop PC. Implementations live in the Web project (they need its services) and are
/// registered as scoped. A handler must be safe to run once per command: the service records the outcome, so a command
/// delivered twice is answered from that record instead of running again.
/// </summary>
public interface IOnlineCommandHandler
{
    string Type { get; }

    Task<CommandResult> HandleAsync(JsonElement payload, CommandContext context, CancellationToken ct);
}
