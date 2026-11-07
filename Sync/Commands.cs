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

public static class CommandErrorCodes
{
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
