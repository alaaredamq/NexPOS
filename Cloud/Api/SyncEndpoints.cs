using System.Diagnostics;
using Cloud.Data;
using Cloud.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sync;

namespace Cloud.Api;

/// <summary>
/// The only surface a till talks to. Authenticated by device token (not by the admin cookie), and every
/// answer is a stable machine-readable code so the till can tell "renew your subscription" from "retry later".
/// </summary>
public static class SyncEndpoints
{
    private const int MaxChangesPerBatch = 5000;

    public static void MapSyncApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/sync").AllowAnonymous();
        group.MapGet("/ping", Ping);
        group.MapPost("/push", Push);
        DeviceCommandEndpoints.Map(group);
    }

    internal sealed record Device(DeviceToken Token, Site Site, Client Client, SubscriptionState State);

    internal static async Task<(Device? Device, IResult? Failure)> AuthenticateAsync(
        HttpContext http, ControlDbContext db, TimeProvider time)
    {
        var header = http.Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return (null, Error(StatusCodes.Status401Unauthorized, SyncErrorCodes.Unauthorized, "Missing device token."));

        var token = header["Bearer ".Length..].Trim();
        var hash = DeviceTokens.Hash(token);
        var row = await db.DeviceTokens
            .Include(t => t.Site).ThenInclude(s => s.Client).ThenInclude(c => c.Subscription)
            .FirstOrDefaultAsync(t => t.TokenHash == hash && t.RevokedAt == null);

        if (row is null)
            return (null, Error(StatusCodes.Status401Unauthorized, SyncErrorCodes.Unauthorized, "Unknown or revoked device token."));
        if (row.Site.IsDisabled || !row.Site.Client.IsActive)
            return (null, Error(StatusCodes.Status403Forbidden, SyncErrorCodes.Unauthorized, "This installation is disabled."));

        var now = time.GetUtcNow().UtcDateTime;
        if (row.LastUsedAt is null || now - row.LastUsedAt > TimeSpan.FromMinutes(1))
        {
            row.LastUsedAt = now;
            row.Site.LastSeenAt = now;
            await RecordHeartbeatAsync(db, row.SiteId, now);
            await db.SaveChangesAsync();
        }

        var state = SubscriptionRules.Evaluate(row.Site.Client.Subscription, now);
        return (new Device(row, row.Site, row.Site.Client, state), null);
    }

    /// <summary>Counts this contact in its hour (the portal's 24-hour strip) and forgets anything older than a week.</summary>
    private static async Task RecordHeartbeatAsync(ControlDbContext db, Guid siteId, DateTime now)
    {
        var hour = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, DateTimeKind.Utc);
        var beat = await db.SiteHeartbeats.FirstOrDefaultAsync(h => h.SiteId == siteId && h.Hour == hour);
        if (beat is not null) { beat.Contacts++; return; }

        db.SiteHeartbeats.Add(new SiteHeartbeat { SiteId = siteId, Hour = hour, Contacts = 1 });
        var cutoff = hour.AddDays(-7);
        await db.SiteHeartbeats.Where(h => h.SiteId == siteId && h.Hour < cutoff).ExecuteDeleteAsync();
    }

    internal static IResult Error(int status, string code, string message)
        => Results.Json(new SyncError(code, message), SyncWire.Json, statusCode: status);

    internal static IResult InactiveSubscription(Device d)
        => Error(StatusCodes.Status402PaymentRequired, SyncErrorCodes.SubscriptionInactive,
            d.State switch
            {
                SubscriptionState.Suspended => "The online subscription is suspended. Contact your provider.",
                SubscriptionState.Expired => "The online subscription has expired. Renew it to resume syncing; your data is safe and queued.",
                _ => "There is no online subscription for this client.",
            });

    private static async Task<IResult> Ping(HttpContext http, ControlDbContext db, TimeProvider time)
    {
        var (device, failure) = await AuthenticateAsync(http, db, time);
        if (failure is not null) return failure;
        return Results.Json(new SyncPing(device!.Site.Name, device.Client.Name, device.State.ToString(),
            device.Client.Subscription?.ExpiresAt), SyncWire.Json);
    }

    private static async Task<IResult> Push(
        HttpContext http, [FromBody] SyncBatch batch, ControlDbContext db, MirrorStore mirrors, MirrorApplier applier,
        TimeProvider time, ILoggerFactory loggers)
    {
        var (device, failure) = await AuthenticateAsync(http, db, time);
        if (failure is not null) return failure;
        if (!SubscriptionRules.AllowsSync(device!.State)) return InactiveSubscription(device);

        if (batch.Changes is null || batch.Changes.Count > MaxChangesPerBatch)
            return Error(StatusCodes.Status400BadRequest, SyncErrorCodes.BadBatch, $"A batch carries at most {MaxChangesPerBatch} changes.");

        // Ordinal compare of migration ids (timestamp-prefixed): a till ahead of this cloud must wait for the cloud to be upgraded.
        if (string.CompareOrdinal(batch.SchemaVersion, mirrors.LatestMigration) > 0)
            return Error(StatusCodes.Status409Conflict, SyncErrorCodes.SchemaNewer,
                $"The till's database ({batch.SchemaVersion}) is newer than this cloud ({mirrors.LatestMigration}). Upgrade the cloud first.");

        var site = device.Site;
        var started = Stopwatch.StartNew();
        var log = new SyncBatchLog
        {
            SiteId = site.Id,
            ReceivedAt = time.GetUtcNow().UtcDateTime,
            ThroughSeq = batch.ThroughSeq,
            Changes = batch.Changes.Count,
            Initial = batch.Initial,
        };

        var gate = mirrors.LockFor(site.Id);
        await gate.WaitAsync(http.RequestAborted);
        try
        {
            mirrors.EnsureReady(site.Id);
            var applied = applier.Apply(mirrors.PathFor(site.Id), batch);

            var tracked = await db.Sites.FirstAsync(s => s.Id == site.Id);
            var resync = tracked.ResyncRequested && !batch.Initial;
            if (batch.Initial) tracked.ResyncRequested = false;
            tracked.LastSyncAt = log.ReceivedAt;
            tracked.LastThroughSeq = batch.ThroughSeq;
            tracked.LastSchemaVersion = batch.SchemaVersion;

            log.Outcome = "applied";
            log.DurationMs = (int)started.ElapsedMilliseconds;
            db.SyncBatchLogs.Add(log);
            await db.SaveChangesAsync();
            await PruneAsync(db, site.Id);

            return Results.Json(new SyncAck(batch.ThroughSeq, applied, resync), SyncWire.Json);
        }
        catch (SyncBatchException ex)
        {
            await RecordFailureAsync(db, log, started, "rejected", ex.Message);
            return Error(StatusCodes.Status400BadRequest, SyncErrorCodes.BadBatch, ex.Message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            loggers.CreateLogger("Sync").LogError(ex, "Applying a batch for site {Site} failed", site.Id);
            await RecordFailureAsync(db, log, started, "error", ex.Message);
            return Error(StatusCodes.Status500InternalServerError, "server_error", "The cloud could not apply the batch. It will be retried.");
        }
        finally
        {
            gate.Release();
        }
    }

    private static async Task RecordFailureAsync(ControlDbContext db, SyncBatchLog log, Stopwatch started, string outcome, string error)
    {
        db.ChangeTracker.Clear();
        log.Outcome = outcome;
        log.Error = error.Length > 500 ? error[..500] : error;
        log.DurationMs = (int)started.ElapsedMilliseconds;
        db.SyncBatchLogs.Add(log);
        await db.SaveChangesAsync();
    }

    private static async Task PruneAsync(ControlDbContext db, Guid siteId)
    {
        var cutoff = await db.SyncBatchLogs.Where(l => l.SiteId == siteId)
            .OrderByDescending(l => l.Id).Skip(300).Select(l => l.Id).FirstOrDefaultAsync();
        if (cutoff > 0)
            await db.SyncBatchLogs.Where(l => l.SiteId == siteId && l.Id <= cutoff).ExecuteDeleteAsync();
    }
}
