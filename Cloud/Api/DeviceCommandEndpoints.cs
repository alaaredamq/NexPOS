using System.Text.Json;
using Cloud.Data;
using Cloud.Services;
using Microsoft.EntityFrameworkCore;
using Sync;

namespace Cloud.Api;

/// <summary>
/// What a shop PC uses to collect the changes its owner made on the phone and to say how each one went. Same device token and
/// the same subscription rule as the data push: while the plan is not active nothing is handed out, and the owner's requests stay queued.
/// </summary>
internal static class DeviceCommandEndpoints
{
    /// <summary>A command handed out but never answered (the shop PC lost power) is handed out again after this long.</summary>
    private static readonly TimeSpan RedeliverAfter = TimeSpan.FromMinutes(2);
    private const int MaxAttempts = 10;
    private const int MaxPerFetch = 20;
    private const int MaxResultBytes = 16 * 1024;

    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/commands", Fetch);
        group.MapPost("/commands/{id:guid}/result", Report);
    }

    private static async Task<IResult> Fetch(
        HttpContext http, ControlDbContext db, TimeProvider time, CommandBroker broker, int? wait)
    {
        var (device, failure) = await SyncEndpoints.AuthenticateAsync(http, db, time);
        if (failure is not null) return failure;
        if (!SubscriptionRules.AllowsSync(device!.State)) return SyncEndpoints.InactiveSubscription(device);

        var siteId = device.Site.Id;
        var seconds = Math.Clamp(wait ?? 0, 0, 55);
        var started = System.Diagnostics.Stopwatch.StartNew();

        while (true)
        {
            var signal = broker.Current(siteId); // before looking, so nothing queued in between is missed
            var claimed = await ClaimAsync(db, siteId, time);
            if (claimed.Count > 0)
            {
                var envelopes = claimed.Select(c => new CommandEnvelope(
                    c.Id, c.Type, JsonDocument.Parse(c.PayloadJson).RootElement.Clone(), c.RequestedBy, AsUtc(c.CreatedAt))).ToList();
                return Results.Json(new CommandBatchResponse(envelopes), SyncWire.Json);
            }

            var remaining = TimeSpan.FromSeconds(seconds) - started.Elapsed;
            if (remaining <= TimeSpan.Zero) return Results.NoContent();

            try { await Task.WhenAny(signal, Task.Delay(remaining, http.RequestAborted)); }
            catch (OperationCanceledException) { return Results.NoContent(); }
            if (http.RequestAborted.IsCancellationRequested) return Results.NoContent();
        }
    }

    private static async Task<List<SiteCommand>> ClaimAsync(ControlDbContext db, Guid siteId, TimeProvider time)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var stale = now - RedeliverAfter;
        db.ChangeTracker.Clear();

        var due = await db.SiteCommands
            .Where(c => c.SiteId == siteId
                        && (c.Status == CommandStatus.Queued || (c.Status == CommandStatus.Delivered && c.DeliveredAt < stale)))
            .OrderBy(c => c.CreatedAt)
            .Take(MaxPerFetch)
            .ToListAsync();

        var give = new List<SiteCommand>();
        foreach (var command in due)
        {
            if (command.Attempts >= MaxAttempts)
            {
                // Handed out over and over with no answer: stop, and tell the owner rather than loop forever.
                command.Status = CommandStatus.Rejected;
                command.CompletedAt = now;
                command.ResultCode = "no_answer";
                command.ResultMessage = "The shop PC kept receiving this change but never confirmed it. Check the shop PC and send it again.";
                continue;
            }
            command.Status = CommandStatus.Delivered;
            command.DeliveredAt = now;
            command.Attempts++;
            give.Add(command);
        }
        if (due.Count > 0) await db.SaveChangesAsync();
        return give;
    }

    private static async Task<IResult> Report(
        Guid id, CommandResult result, HttpContext http, ControlDbContext db, TimeProvider time)
    {
        var (device, failure) = await SyncEndpoints.AuthenticateAsync(http, db, time);
        if (failure is not null) return failure;

        if (result is null || (result.Outcome != CommandResult.Applied && result.Outcome != CommandResult.Rejected))
            return SyncEndpoints.Error(StatusCodes.Status400BadRequest, SyncErrorCodes.BadBatch, "A result must be 'applied' or 'rejected'.");

        // Scoped to this device's own site: a token can never answer another shop's command.
        var command = await db.SiteCommands.FirstOrDefaultAsync(c => c.Id == id && c.SiteId == device!.Site.Id);
        if (command is null)
            return SyncEndpoints.Error(StatusCodes.Status404NotFound, SyncErrorCodes.BadBatch, "No such command for this installation.");

        // Answered already (a retried report): keep the first answer.
        if (command.Status is CommandStatus.Applied or CommandStatus.Rejected) return Results.Ok();
        if (command.Status == CommandStatus.Discarded) return Results.Ok();

        command.Status = result.IsApplied ? CommandStatus.Applied : CommandStatus.Rejected;
        command.CompletedAt = time.GetUtcNow().UtcDateTime;
        command.ResultCode = Trim(result.Code, 60);
        command.ResultMessage = Trim(result.Message, 500);
        var data = result.Data is { ValueKind: not JsonValueKind.Undefined and not JsonValueKind.Null } d ? d.GetRawText() : null;
        command.ResultDataJson = data is not null && data.Length <= MaxResultBytes ? data : null;
        await db.SaveChangesAsync();
        return Results.Ok();
    }

    private static string? Trim(string? text, int max) => text is null ? null : text.Length <= max ? text : text[..max];

    private static DateTime AsUtc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
