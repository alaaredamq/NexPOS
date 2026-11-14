using System.Text.Json;
using Cloud.Data;
using Cloud.Services;
using Microsoft.EntityFrameworkCore;
using Sync;

namespace Cloud.Api;

/// <summary>
/// What the owner's phone talks to. Signs in with a cloud account (never a till PIN), and every call is bound to that
/// account's one client: there is no id in any route that could name another client's shop.
/// Changes are never written to shop data here; they are queued for the shop PC (see <see cref="DeviceCommandEndpoints"/>).
/// </summary>
public static class OwnerEndpoints
{
    private const int MaxPayloadChars = 64 * 1024;

    /// <summary>Each owner action and the command it queues. The body is passed to the shop PC untouched; the shop PC judges it.</summary>
    private static readonly (string Route, string Type)[] Actions =
    [
        ("/customers", "customer.create"),
        ("/suppliers", "supplier.create"),
        ("/products", "product.create"),
    ];

    public static void MapOwnerApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/owner/v1").AllowAnonymous();
        group.MapPost("/auth/login", Login);
        group.MapPost("/auth/logout", Logout);
        group.MapGet("/me", Me);
        OwnerReadEndpoints.Map(group);

        foreach (var (route, type) in Actions)
        {
            var commandType = type;
            group.MapPost(route, (HttpContext http, JsonElement body, ControlDbContext db, TimeProvider time, CommandBroker broker)
                => Enqueue(commandType, http, body, db, time, broker));
        }

        group.MapGet("/commands", ListCommands);
        group.MapGet("/commands/{id:guid}", GetCommand);
        group.MapDelete("/commands/{id:guid}", Discard);
    }

    public sealed record LoginRequest(string? Email, string? Password);

    public sealed record OwnerCommandView(
        Guid Id, string Type, string Status, string RequestedBy, DateTime CreatedAtUtc, DateTime? CompletedAtUtc,
        string? Code, string? Message, JsonElement? Data, JsonElement Payload);

    private static IResult Error(int status, string code, string message)
        => Results.Json(new SyncError(code, message), SyncWire.Json, statusCode: status);

    private static IResult Unauthorized()
        => Error(StatusCodes.Status401Unauthorized, SyncErrorCodes.Unauthorized, "Please sign in again.");

    // ---- sign in ----

    private static readonly Lazy<string> DummyHash = new(() => PasswordHasher.Hash("not-a-real-password"));

    private static async Task<IResult> Login(
        LoginRequest request, HttpContext http, ControlDbContext db, TimeProvider time, LoginThrottle throttle)
    {
        var email = request?.Email?.Trim().ToLowerInvariant();
        var password = request?.Password ?? "";
        var key = "owner:" + (http.Connection.RemoteIpAddress?.ToString() ?? "unknown");
        if (throttle.IsBlocked(key))
            return Error(StatusCodes.Status429TooManyRequests, SyncErrorCodes.RateLimited, "Too many attempts. Try again in a few minutes.");

        var user = string.IsNullOrEmpty(email) ? null : await db.OwnerUsers
            .Include(u => u.Client).ThenInclude(c => c.Sites)
            .FirstOrDefaultAsync(u => u.Email == email);

        // Verify against a dummy hash when the account is unknown, so a wrong email and a wrong password take the same time.
        var ok = PasswordHasher.Verify(password, user?.PasswordHash ?? DummyHash.Value) && user is not null;
        if (!ok || !user!.IsActive || !user.Client.IsActive)
        {
            throttle.RecordFailure(key);
            return Error(StatusCodes.Status401Unauthorized, "invalid_credentials", "The email or password is not right.");
        }

        throttle.Reset(key);
        var now = time.GetUtcNow().UtcDateTime;
        var (token, hash) = OwnerTokens.Generate();
        db.OwnerSessions.Add(new OwnerSession
        {
            OwnerUserId = user.Id, TokenHash = hash, CreatedAt = now, ExpiresAt = now + OwnerTokens.SessionLifetime,
        });
        user.LastSignInAt = now;
        await db.SaveChangesAsync();

        var site = user.Client.Sites.Where(s => !s.IsDisabled).OrderBy(s => s.CreatedAt).FirstOrDefault();
        return Results.Json(new
        {
            token,
            expiresAtUtc = now + OwnerTokens.SessionLifetime,
            displayName = user.DisplayName,
            clientName = user.Client.Name,
            siteName = site?.Name,
        }, SyncWire.Json);
    }

    private static async Task<IResult> Logout(HttpContext http, ControlDbContext db, TimeProvider time)
    {
        var who = await OwnerAuth.AuthenticateAsync(http, db, time);
        if (who is null) return Unauthorized();
        var session = await db.OwnerSessions.FirstAsync(s => s.Id == who.Session.Id);
        session.RevokedAt = time.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> Me(HttpContext http, ControlDbContext db, TimeProvider time)
    {
        var who = await OwnerAuth.AuthenticateAsync(http, db, time);
        if (who is null) return Unauthorized();
        return Results.Json(new
        {
            displayName = who.User.DisplayName,
            email = who.User.Email,
            clientName = who.Client.Name,
            siteName = who.Site?.Name,
            subscription = who.State.ToString(),
            expiresAtUtc = who.Client.Subscription?.ExpiresAt,
            shopLastSeenUtc = who.Site?.LastSeenAt,
            shopLastSyncUtc = who.Site?.LastSyncAt,
        }, SyncWire.Json);
    }

    // ---- changes ----

    private static async Task<IResult> Enqueue(
        string type, HttpContext http, JsonElement body, ControlDbContext db, TimeProvider time, CommandBroker broker)
    {
        var who = await OwnerAuth.AuthenticateAsync(http, db, time);
        if (who is null) return Unauthorized();
        if (who.Site is null)
            return Error(StatusCodes.Status409Conflict, "no_shop", "No shop PC is connected to this account yet.");
        if (!SubscriptionRules.AllowsSync(who.State))
            return Error(StatusCodes.Status402PaymentRequired, SyncErrorCodes.SubscriptionInactive,
                "The online plan is not active, so changes cannot be sent to the shop. Contact your provider.");

        var key = http.Request.Headers["Idempotency-Key"].ToString().Trim();
        if (key.Length is < 8 or > 100)
            return Error(StatusCodes.Status400BadRequest, "idempotency_key_required",
                "Send an Idempotency-Key header of 8 to 100 characters, new for each change and reused when you retry it.");

        if (body.ValueKind != JsonValueKind.Object)
            return Error(StatusCodes.Status400BadRequest, CommandErrorCodes.BadPayload, "The body must be a JSON object.");
        var raw = body.GetRawText();
        if (raw.Length > MaxPayloadChars)
            return Error(StatusCodes.Status413PayloadTooLarge, CommandErrorCodes.BadPayload, "That request is too large.");

        var site = who.Site;
        var existing = await db.SiteCommands.FirstOrDefaultAsync(c => c.SiteId == site.Id && c.IdempotencyKey == key);
        if (existing is not null) return Results.Json(ToView(existing), SyncWire.Json, statusCode: StatusCodes.Status200OK);

        var command = new SiteCommand
        {
            Id = Guid.NewGuid(),
            SiteId = site.Id,
            Type = type,
            PayloadJson = raw,
            IdempotencyKey = key,
            OwnerUserId = who.User.Id,
            RequestedBy = who.User.DisplayName,
            Status = CommandStatus.Queued,
            CreatedAt = time.GetUtcNow().UtcDateTime,
        };
        db.SiteCommands.Add(command);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Two requests with the same key raced: the other one won, so hand back its command.
            db.ChangeTracker.Clear();
            var winner = await db.SiteCommands.FirstOrDefaultAsync(c => c.SiteId == site.Id && c.IdempotencyKey == key);
            if (winner is not null) return Results.Json(ToView(winner), SyncWire.Json, statusCode: StatusCodes.Status200OK);
            throw;
        }

        broker.Notify(site.Id);
        return Results.Json(ToView(command), SyncWire.Json, statusCode: StatusCodes.Status202Accepted);
    }

    private static async Task<IResult> ListCommands(HttpContext http, ControlDbContext db, TimeProvider time, int? limit)
    {
        var who = await OwnerAuth.AuthenticateAsync(http, db, time);
        if (who is null) return Unauthorized();
        if (who.Site is null) return Results.Json(Array.Empty<OwnerCommandView>(), SyncWire.Json);

        var take = Math.Clamp(limit ?? 50, 1, 200);
        var rows = await db.SiteCommands.Where(c => c.SiteId == who.Site.Id)
            .OrderByDescending(c => c.CreatedAt).Take(take).ToListAsync();
        return Results.Json(rows.Select(ToView), SyncWire.Json);
    }

    private static async Task<IResult> GetCommand(Guid id, HttpContext http, ControlDbContext db, TimeProvider time)
    {
        var who = await OwnerAuth.AuthenticateAsync(http, db, time);
        if (who is null) return Unauthorized();
        var siteId = who.Site?.Id;
        var command = siteId is null ? null : await db.SiteCommands.FirstOrDefaultAsync(c => c.Id == id && c.SiteId == siteId);
        return command is null
            ? Error(StatusCodes.Status404NotFound, "not_found", "No such change.")
            : Results.Json(ToView(command), SyncWire.Json);
    }

    private static async Task<IResult> Discard(Guid id, HttpContext http, ControlDbContext db, TimeProvider time)
    {
        var who = await OwnerAuth.AuthenticateAsync(http, db, time);
        if (who is null) return Unauthorized();
        var siteId = who.Site?.Id;
        var command = siteId is null ? null : await db.SiteCommands.FirstOrDefaultAsync(c => c.Id == id && c.SiteId == siteId);
        if (command is null) return Error(StatusCodes.Status404NotFound, "not_found", "No such change.");

        // Only before the shop PC has taken it. After that it may already be applied, and withdrawing it would be a lie.
        if (command.Status != CommandStatus.Queued)
            return Error(StatusCodes.Status409Conflict, "already_sent", "The shop PC already has this change, so it can no longer be withdrawn.");

        command.Status = CommandStatus.Discarded;
        command.CompletedAt = time.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync();
        return Results.Json(ToView(command), SyncWire.Json);
    }

    private static OwnerCommandView ToView(SiteCommand c) => new(
        c.Id, c.Type, c.Status.ToString().ToLowerInvariant(), c.RequestedBy,
        DateTime.SpecifyKind(c.CreatedAt, DateTimeKind.Utc),
        c.CompletedAt is null ? null : DateTime.SpecifyKind(c.CompletedAt.Value, DateTimeKind.Utc),
        c.ResultCode, c.ResultMessage,
        string.IsNullOrEmpty(c.ResultDataJson) ? null : JsonDocument.Parse(c.ResultDataJson).RootElement.Clone(),
        JsonDocument.Parse(c.PayloadJson).RootElement.Clone());
}
