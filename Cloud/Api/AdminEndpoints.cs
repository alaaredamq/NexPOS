using System.Globalization;
using System.Security.Claims;
using Cloud.Data;
using Cloud.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Sync;

namespace Cloud.Api;

/// <summary>
/// The admin app's API: everything the provider's portal (/admin) can do, as JSON, for a phone or desktop app.
/// Sign in with POST /api/admin/v1/auth/login, then send <c>Authorization: Bearer nxa_...</c> on every call. The portal's cookie is
/// never accepted here, so a web page cannot make a signed-in browser call it. The rules live in <see cref="AdminOperations"/>,
/// <see cref="FinanceBook"/> and the dashboard builders, which the portal pages use too. Errors are <c>{ code, message }</c>.
/// Times ending in "Utc" are UTC; "dates" (yyyy-MM-dd) are days in the provider's own calendar. See docs/admin-api.md.
/// </summary>
public static partial class AdminEndpoints
{
    public const string Prefix = "/api/admin/v1";

    public static void MapAdminApi(this IEndpointRouteBuilder app)
    {
        var policy = new AuthorizationPolicyBuilder(AdminTokenHandler.SchemeName).RequireAuthenticatedUser().Build();
        var g = app.MapGroup(Prefix).RequireAuthorization(policy);

        g.MapPost("/auth/login", Login).AllowAnonymous();
        g.MapPost("/auth/logout", Logout);
        g.MapGet("/me", Me);
        g.MapGet("/sessions", Sessions);
        g.MapDelete("/sessions/{id:int}", RevokeSession);

        g.MapGet("/overview", Overview);

        g.MapGet("/clients", ListClients);
        g.MapPost("/clients", CreateClient);
        g.MapGet("/clients/{id:int}", GetClient);
        g.MapPut("/clients/{id:int}", UpdateClient);
        g.MapDelete("/clients/{id:int}", RemoveClient);
        g.MapPost("/clients/{id:int}/activate", (int id, AdminOperations ops) => SetActive(id, true, ops));
        g.MapPost("/clients/{id:int}/deactivate", (int id, AdminOperations ops) => SetActive(id, false, ops));
        g.MapGet("/clients/{id:int}/events", Events);

        g.MapPost("/clients/{id:int}/subscription/extend", Extend);
        g.MapPost("/clients/{id:int}/subscription/suspend", (int id, AdminOperations ops, PortalFormat fmt) => Suspend(id, true, ops, fmt));
        g.MapPost("/clients/{id:int}/subscription/resume", (int id, AdminOperations ops, PortalFormat fmt) => Suspend(id, false, ops, fmt));

        g.MapPost("/clients/{id:int}/sites", AddSite);
        g.MapGet("/sites/{id:guid}", GetSite);
        g.MapPut("/sites/{id:guid}", UpdateSite);
        g.MapDelete("/sites/{id:guid}", RemoveSite);
        g.MapPost("/sites/{id:guid}/link", NewLink);
        g.MapPost("/sites/{id:guid}/disconnect", Disconnect);
        g.MapPost("/sites/{id:guid}/token", IssueToken);
        g.MapPost("/sites/{id:guid}/resync", Resync);
        g.MapPost("/sites/{id:guid}/enable", (Guid id, AdminOperations ops, ControlDbContext db, MirrorStore m, PortalFormat f) => SetDisabled(id, false, ops, db, m, f));
        g.MapPost("/sites/{id:guid}/disable", (Guid id, AdminOperations ops, ControlDbContext db, MirrorStore m, PortalFormat f) => SetDisabled(id, true, ops, db, m, f));

        g.MapGet("/clients/{id:int}/logins", ListLogins);
        g.MapPost("/clients/{id:int}/logins", AddLogin);
        g.MapPut("/clients/{id:int}/logins/{loginId:int}", UpdateLogin);
        g.MapPost("/clients/{id:int}/logins/{loginId:int}/reset-password", ResetLogin);
        g.MapDelete("/clients/{id:int}/logins/{loginId:int}", RemoveLogin);

        MapFinance(g);
    }

    // ---- replies ----

    internal static IResult Error(int status, string code, string message)
        => Results.Json(new SyncError(code, message), SyncWire.Json, statusCode: status);

    internal static IResult NotFound(string what = "That") => Error(StatusCodes.Status404NotFound, "not_found", $"{what} was not found.");

    internal static IResult Refused(string code, string message) => Error(StatusCodes.Status400BadRequest, code, message);

    internal static IResult Json(object value, int status = StatusCodes.Status200OK) => Results.Json(value, SyncWire.Json, statusCode: status);

    /// <summary>Runs an operation and turns a refused request into a 400 with its code and sentence.</summary>
    private static async Task<IResult> Try(Func<Task<IResult>> work)
    {
        try { return await work(); }
        catch (AdminRuleException refused) { return Refused(refused.Code, refused.Message); }
    }

    private static string Who(HttpContext http) => http.User.Identity?.Name ?? "";

    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    // ---- sign in ----

    public sealed record LoginRequest(string? Username, string? Password, string? DeviceName);

    private static async Task<IResult> Login(LoginRequest? request, HttpContext http, AdminSignIn signIn, ControlDbContext db, TimeProvider time)
    {
        var (outcome, admin) = await signIn.CheckAsync(request?.Username, request?.Password, http.Connection.RemoteIpAddress?.ToString());
        if (outcome == AdminSignIn.Outcome.Locked)
            return Error(StatusCodes.Status429TooManyRequests, SyncErrorCodes.RateLimited, "Too many failed attempts. Try again in a few minutes.");
        if (admin is null)
            return Error(StatusCodes.Status401Unauthorized, "invalid_credentials", "Wrong username or password.");

        var now = time.GetUtcNow().UtcDateTime;
        var (token, hash) = AdminTokens.Generate();
        var device = request?.DeviceName?.Trim();
        var session = new AdminSession
        {
            AdminUserId = admin.Id, TokenHash = hash, CreatedAt = now, LastUsedAt = now, ExpiresAt = now + AdminTokens.SessionLifetime,
            DeviceName = string.IsNullOrEmpty(device) ? null : device.Length > 100 ? device[..100] : device,
        };
        db.AdminSessions.Add(session);
        await db.SaveChangesAsync();
        return Json(new { token, expiresAtUtc = session.ExpiresAt, username = admin.Username, sessionId = session.Id });
    }

    private static int SessionId(HttpContext http) => int.Parse(http.User.FindFirstValue(AdminTokenHandler.SessionClaim)!, CultureInfo.InvariantCulture);

    private static async Task<IResult> Logout(HttpContext http, ControlDbContext db, TimeProvider time)
    {
        var id = SessionId(http);
        await db.AdminSessions.Where(s => s.Id == id && s.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, time.GetUtcNow().UtcDateTime));
        return Results.NoContent();
    }

    private static async Task<IResult> Me(HttpContext http, ControlDbContext db, PortalFormat fmt)
    {
        var session = await db.AdminSessions.AsNoTracking().FirstAsync(s => s.Id == SessionId(http));
        return Json(new
        {
            username = Who(http), sessionId = session.Id, deviceName = session.DeviceName, expiresAtUtc = session.ExpiresAt,
            currency = fmt.Currency, timeZone = fmt.Zone.Id, today = Iso(fmt.Today),
        });
    }

    /// <summary>The admin's signed-in apps, so a lost phone can be found and signed out.</summary>
    private static async Task<IResult> Sessions(HttpContext http, ControlDbContext db, TimeProvider time)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var me = SessionId(http);
        var adminId = int.Parse(http.User.FindFirstValue(ClaimTypes.NameIdentifier)!, CultureInfo.InvariantCulture);
        var rows = await db.AdminSessions.AsNoTracking()
            .Where(s => s.AdminUserId == adminId && s.RevokedAt == null && s.ExpiresAt > now)
            .OrderByDescending(s => s.LastUsedAt).ToListAsync();
        return Json(rows.Select(s => new
        {
            id = s.Id, deviceName = s.DeviceName, createdAtUtc = s.CreatedAt, lastUsedAtUtc = s.LastUsedAt, expiresAtUtc = s.ExpiresAt,
            current = s.Id == me,
        }));
    }

    private static async Task<IResult> RevokeSession(int id, HttpContext http, ControlDbContext db, TimeProvider time)
    {
        var adminId = int.Parse(http.User.FindFirstValue(ClaimTypes.NameIdentifier)!, CultureInfo.InvariantCulture);
        var done = await db.AdminSessions.Where(s => s.Id == id && s.AdminUserId == adminId && s.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, time.GetUtcNow().UtcDateTime));
        return done == 0 ? NotFound("That session") : Results.NoContent();
    }

    // ---- overview and clients ----

    private static object Pulse(PulseData pulse) => pulse.Cells.Select(c => new { hourUtc = c.HourUtc, contacts = c.Contacts });

    private static object Attention(AttentionItem a) => new
    {
        severity = a.Severity == AttentionSeverity.Bad ? "bad" : "warn", title = a.Client, reason = a.Reason, action = a.Action,
        // The portal link, and the client it is about when there is one, so the app can open its own screen instead.
        portalPath = a.Href.Length == 0 ? null : a.Href,
        clientId = ClientIdIn(a.Href),
    };

    private static int? ClientIdIn(string href)
    {
        const string marker = "/admin/Clients/Details/";
        if (!href.StartsWith(marker, StringComparison.Ordinal)) return null;
        var rest = href[marker.Length..];
        var end = rest.IndexOfAny(['#', '?']);
        return int.TryParse(end < 0 ? rest : rest[..end], NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : null;
    }

    private static object Row(ClientRow r) => new
    {
        id = r.Id, name = r.Name, isActive = r.IsActive, phone = r.Phone, email = r.Email, branches = r.Branches,
        subscription = new { state = r.State.ToString(), expiresAtUtc = r.ExpiresAt, suspended = r.Suspended, endingSoon = r.EndingSoon, note = r.PlanNote },
        lastSeenAtUtc = r.LastSeenAt, seen = r.Seen, tags = r.Tags, products = r.Products, orders = r.Orders,
        pulse = Pulse(r.Pulse),
    };

    private static async Task<IResult> Overview(OverviewBuilder builder, FinanceDashboard finance, PortalFormat fmt)
    {
        var o = await builder.BuildAsync();
        var money = (await finance.BuildAsync(FinancePeriod.Month(fmt.Today))).Report;
        return Json(new
        {
            summary = o.Summary,
            clients = o.Rows.Count, connected = o.Connected,
            plans = new { active = o.PlansActive, endingSoon = o.PlansEnding, ended = o.PlansEnded },
            productsHeld = o.ProductsTotal, ordersHeld = o.OrdersTotal,
            pulse = Pulse(o.Overall), litHours = o.Overall.LitHours,
            attention = o.Attention.Select(Attention),
            thisMonth = new { period = money.Period.Query, collected = money.Income, spent = money.Expenses, profit = money.Profit, balance = money.Balance },
        });
    }

    /// <summary>All clients. <c>search</c> matches the name, phone or email; <c>filter</c> is connected, waiting or attention.</summary>
    private static async Task<IResult> ListClients(OverviewBuilder builder, string? search, string? filter)
    {
        IEnumerable<ClientRow> rows = (await builder.BuildAsync()).Rows;
        if (!string.IsNullOrWhiteSpace(search))
        {
            var q = search.Trim();
            rows = rows.Where(r => r.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                                   || (r.Phone?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
                                   || (r.Email?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false));
        }
        if (!string.IsNullOrWhiteSpace(filter) && filter != "all") rows = rows.Where(r => r.Tags.Contains(filter.Trim().ToLowerInvariant()));
        return Json(rows.Select(Row));
    }

    public sealed record ClientRequest(string? Name, string? Phone, string? Email, string? Notes, int? Months);

    private static Task<IResult> CreateClient(ClientRequest body, HttpContext http, AdminOperations ops, ControlDbContext db, MirrorStore mirrors, PortalFormat fmt)
        => Try(async () =>
        {
            var created = await ops.CreateClientAsync(body.Name, body.Phone, body.Email, body.Notes, body.Months ?? 0, ops.BaseUrl(http.Request));
            var detail = await ClientDetailAsync(created.Client.Id, db, fmt);
            return Json(new
            {
                client = detail,
                siteId = created.Site.Id,
                // Shown once: send it to the owner; they paste it in Coduo POS > Settings > Online.
                link = new { url = created.Link, expiresAtUtc = created.LinkExpiresAt },
            }, StatusCodes.Status201Created);
        });

    private static async Task<object?> ClientDetailAsync(int id, ControlDbContext db, PortalFormat fmt)
    {
        var c = await db.Clients.AsNoTracking().Include(x => x.Subscription)
            .Include(x => x.Sites).ThenInclude(s => s.Tokens)
            .Include(x => x.Sites).ThenInclude(s => s.EnrollmentCodes)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (c is null) return null;

        var now = fmt.Now;
        var since = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, DateTimeKind.Utc).AddHours(-23);
        var ids = c.Sites.Select(s => s.Id).ToList();
        var beats = (await db.SiteHeartbeats.AsNoTracking().Where(h => ids.Contains(h.SiteId) && h.Hour >= since).ToListAsync()).ToLookup(h => h.SiteId);
        var owners = await db.OwnerUsers.AsNoTracking().Where(o => o.ClientId == id).OrderBy(o => o.CreatedAt).ToListAsync();
        var payments = (await db.Payments.AsNoTracking().Where(p => p.ClientId == id).ToListAsync()).OrderByDescending(p => p.ReceivedOn).ThenByDescending(p => p.Id).ToList();
        var state = SubscriptionRules.Evaluate(c.Subscription, now);

        return new
        {
            id = c.Id, name = c.Name, phone = c.Phone, email = c.Email, notes = c.Notes, isActive = c.IsActive, createdAtUtc = c.CreatedAt,
            subscription = c.Subscription is null ? null : new
            {
                state = state.ToString(), plan = c.Subscription.Plan, startsAtUtc = c.Subscription.StartsAt, expiresAtUtc = c.Subscription.ExpiresAt,
                suspended = c.Subscription.IsSuspended,
                graceEndsAtUtc = c.Subscription.ExpiresAt.AddDays(SubscriptionRules.GraceDays),
            },
            subscriptionState = state.ToString(),
            sites = c.Sites.OrderBy(s => s.CreatedAt).Select(s => SiteSummary(s, PulseData.Build(beats[s.Id], now), now)),
            logins = owners.Select(LoginView),
            payments = new
            {
                count = payments.Count,
                total = payments.Sum(p => p.Amount),
                last12Months = payments.Where(p => p.ReceivedOn > fmt.Today.AddYears(-1)).Sum(p => p.Amount),
                last = payments.FirstOrDefault() is { } last ? Payment(last) : null,
            },
            attention = AttentionRules.For(c, now, fmt).Select(Attention),
        };
    }

    private static object SiteSummary(Site s, PulseData pulse, DateTime now)
    {
        var connection = SiteInsight.Connection(s, now);
        return new
        {
            id = s.Id, clientId = s.ClientId, name = s.Name, machineId = s.MachineId, isDisabled = s.IsDisabled, createdAtUtc = s.CreatedAt,
            connection = connection.ToString(), connectionLabel = SiteInsight.Label(connection), connectionHelp = SiteInsight.Help(connection),
            health = SyncHealthRules.Classify(s.LastSeenAt, now).ToString(),
            lastSeenAtUtc = s.LastSeenAt, lastSyncAtUtc = s.LastSyncAt, resyncRequested = s.ResyncRequested,
            shop = new { plan = s.ShopPlan, licenseKind = s.ShopLicenseKind, licenseUntil = s.ShopLicenseUntil is { } u ? Iso(u) : null, seats = s.ShopLicenseSeats },
            pulse = Pulse(pulse),
        };
    }

    private static object LoginView(OwnerUser o) => new
    {
        id = o.Id, signIn = o.Email, displayName = o.DisplayName, role = OwnerRoles.NameOf(o.Can), permissions = o.Permissions,
        isActive = o.IsActive, createdAtUtc = o.CreatedAt, lastSignInAtUtc = o.LastSignInAt,
    };

    private static async Task<IResult> GetClient(int id, ControlDbContext db, MirrorStore mirrors, PortalFormat fmt)
        => await ClientDetailAsync(id, db, fmt) is { } detail ? Json(detail) : NotFound("That client");

    private static Task<IResult> UpdateClient(int id, ClientRequest body, AdminOperations ops, ControlDbContext db, MirrorStore mirrors, PortalFormat fmt)
        => Try(async () => await ops.UpdateClientAsync(id, body.Name, body.Phone, body.Email, body.Notes) is null
            ? NotFound("That client")
            : Json((await ClientDetailAsync(id, db, fmt))!));

    private static async Task<IResult> SetActive(int id, bool active, AdminOperations ops)
        => await ops.SetClientActiveAsync(id, active) is { } c ? Json(new { id = c.Id, isActive = c.IsActive }) : NotFound("That client");

    private static async Task<IResult> RemoveClient(int id, AdminOperations ops)
        => await ops.RemoveClientAsync(id) is null ? NotFound("That client") : Results.NoContent();

    private static async Task<IResult> Events(int id, ControlDbContext db, int? take)
    {
        if (!await db.Clients.AnyAsync(c => c.Id == id)) return NotFound("That client");
        var n = Math.Clamp(take ?? 50, 1, 500);
        var rows = await db.ClientEvents.AsNoTracking().Where(e => e.ClientId == id).OrderByDescending(e => e.At).ThenByDescending(e => e.Id).Take(n).ToListAsync();
        return Json(rows.Select(e => new { id = e.Id, atUtc = e.At, text = e.Text }));
    }

    // ---- the Online subscription ----

    public sealed record ExtendRequest(int Months);

    private static object Subscription(Subscription s, DateTime now) => new
    {
        state = SubscriptionRules.Evaluate(s, now).ToString(), startsAtUtc = s.StartsAt, expiresAtUtc = s.ExpiresAt, suspended = s.IsSuspended,
    };

    private static Task<IResult> Extend(int id, ExtendRequest body, AdminOperations ops, PortalFormat fmt)
        => Try(async () => await ops.ExtendAsync(id, body.Months) is { } s ? Json(Subscription(s, fmt.Now)) : NotFound("That client"));

    private static async Task<IResult> Suspend(int id, bool suspended, AdminOperations ops, PortalFormat fmt)
        => await ops.SetSuspendedAsync(id, suspended) is { } s ? Json(Subscription(s, fmt.Now)) : NotFound("That client's subscription");

    // ---- branches ----

    public sealed record SiteRequest(string? Name, string? MachineId);

    private static async Task<object?> SiteDetailAsync(Guid id, ControlDbContext db, MirrorStore mirrors, PortalFormat fmt)
    {
        var s = await db.Sites.AsNoTracking().Include(x => x.Tokens).Include(x => x.EnrollmentCodes).FirstOrDefaultAsync(x => x.Id == id);
        if (s is null) return null;
        var now = fmt.Now;
        var since = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, DateTimeKind.Utc).AddHours(-23);
        var pulse = PulseData.Build(await db.SiteHeartbeats.AsNoTracking().Where(h => h.SiteId == id && h.Hour >= since).ToListAsync(), now);
        var counts = mirrors.CountRows(id, ["Products", "Customers", "Suppliers", "Orders", "Purchases", "Receipts"]);
        var tokens = s.Tokens.Where(t => t.RevokedAt == null).Select(t => new { prefix = t.Prefix, createdAtUtc = t.CreatedAt, lastUsedAtUtc = t.LastUsedAt });
        return new
        {
            site = SiteSummary(s, pulse, now),
            data = new { held = mirrors.Exists(id), encrypted = mirrors.HasEncryptedRows(id), counts },
            tokens,
            openLink = s.EnrollmentCodes.Where(c => c.UsedAt == null && c.ExpiresAt > now).Select(c => new { createdAtUtc = c.CreatedAt, expiresAtUtc = c.ExpiresAt }).FirstOrDefault(),
            syncLog = (await db.SyncBatchLogs.AsNoTracking().Where(l => l.SiteId == id).OrderByDescending(l => l.Id).Take(20).ToListAsync())
                .Select(l => new { receivedAtUtc = l.ReceivedAt, throughSeq = l.ThroughSeq, changes = l.Changes, initial = l.Initial, durationMs = l.DurationMs, outcome = l.Outcome, error = l.Error }),
        };
    }

    private static Task<IResult> AddSite(int id, SiteRequest body, AdminOperations ops, ControlDbContext db, MirrorStore mirrors, PortalFormat fmt)
        => Try(async () => await ops.AddSiteAsync(id, body.Name, body.MachineId) is { } site
            ? Json((await SiteDetailAsync(site.Id, db, mirrors, fmt))!, StatusCodes.Status201Created)
            : NotFound("That client"));

    private static async Task<IResult> GetSite(Guid id, ControlDbContext db, MirrorStore mirrors, PortalFormat fmt)
        => await SiteDetailAsync(id, db, mirrors, fmt) is { } detail ? Json(detail) : NotFound("That branch");

    private static Task<IResult> UpdateSite(Guid id, SiteRequest body, AdminOperations ops, ControlDbContext db, MirrorStore mirrors, PortalFormat fmt)
        => Try(async () => await ops.RenameSiteAsync(id, body.Name, body.MachineId) is null
            ? NotFound("That branch")
            : Json((await SiteDetailAsync(id, db, mirrors, fmt))!));

    private static async Task<IResult> RemoveSite(Guid id, AdminOperations ops)
        => await ops.RemoveSiteAsync(id) is null ? NotFound("That branch") : Results.NoContent();

    /// <summary>A new connection link (shown once). <c>whatsAppUrl</c> opens WhatsApp with the usual message to the owner.</summary>
    private static async Task<IResult> NewLink(Guid id, HttpContext http, AdminOperations ops)
    {
        var issued = await ops.NewLinkAsync(id, ops.BaseUrl(http.Request));
        if (issued is null) return NotFound("That branch");
        var whatsApp = "https://wa.me/?text=" + Uri.EscapeDataString(
            "Coduo POS Online: open Coduo POS > Settings > Online, paste this link and press Connect.\n" +
            "لتفعيل Coduo POS Online: افتح الإعدادات ثم Online والصق هذا الرابط واضغط Connect.\n" + issued.Url);
        return Json(new { url = issued.Url, code = issued.Code, expiresAtUtc = issued.ExpiresAt, whatsAppUrl = whatsApp }, StatusCodes.Status201Created);
    }

    private static async Task<IResult> Disconnect(Guid id, AdminOperations ops, ControlDbContext db, MirrorStore mirrors, PortalFormat fmt)
        => await ops.DisconnectAsync(id) is null ? NotFound("That branch") : Json((await SiteDetailAsync(id, db, mirrors, fmt))!);

    /// <summary>Support only: a device token to type into the shop PC by hand. Shown once; the old one stops working.</summary>
    private static async Task<IResult> IssueToken(Guid id, AdminOperations ops)
        => await ops.IssueTokenAsync(id) is { } token ? Json(new { token }, StatusCodes.Status201Created) : NotFound("That branch");

    private static async Task<IResult> Resync(Guid id, AdminOperations ops, ControlDbContext db, MirrorStore mirrors, PortalFormat fmt)
        => await ops.RequestResyncAsync(id) is null ? NotFound("That branch") : Json((await SiteDetailAsync(id, db, mirrors, fmt))!);

    private static async Task<IResult> SetDisabled(Guid id, bool disabled, AdminOperations ops, ControlDbContext db, MirrorStore mirrors, PortalFormat fmt)
        => await ops.SetSiteDisabledAsync(id, disabled) is null ? NotFound("That branch") : Json((await SiteDetailAsync(id, db, mirrors, fmt))!);

    // ---- phone logins ----

    public sealed record LoginCreateRequest(string? SignIn, string? DisplayName, string? Password, string? Role);
    public sealed record LoginUpdateRequest(string? Role, bool? IsActive);

    private static async Task<IResult> ListLogins(int id, ControlDbContext db)
    {
        if (!await db.Clients.AnyAsync(c => c.Id == id)) return NotFound("That client");
        return Json((await db.OwnerUsers.AsNoTracking().Where(o => o.ClientId == id).OrderBy(o => o.CreatedAt).ToListAsync()).Select(LoginView));
    }

    /// <summary>The password comes back once (made up when none was sent), with a ready message to send the owner.</summary>
    private static object HandOver(OwnerHandOver h) => new
    {
        login = LoginView(h.Owner),
        password = h.Password,
        whatsAppUrl = "https://wa.me/?text=" + Uri.EscapeDataString(
            $"Coduo POS app login\nSign in with: {h.Owner.Email}\nPassword: {h.Password}\n\n" +
            "After signing in the app asks for the recovery code. Find it on the shop PC: Coduo POS > Settings > Online > Show recovery code.\n" +
            "بعد تسجيل الدخول سيطلب التطبيق رمز الاسترداد. تجده على جهاز المحل: Coduo POS > الإعدادات > Online > إظهار رمز الاسترداد."),
    };

    private static Task<IResult> AddLogin(int id, LoginCreateRequest body, AdminOperations ops)
        => Try(async () => await ops.AddOwnerAsync(id, body.SignIn, body.DisplayName, body.Password, body.Role) is { } h
            ? Json(HandOver(h), StatusCodes.Status201Created)
            : NotFound("That client"));

    private static async Task<IResult> UpdateLogin(int id, int loginId, LoginUpdateRequest body, AdminOperations ops)
    {
        OwnerUser? owner = null;
        if (body.Role is not null && (owner = await ops.SetOwnerRoleAsync(id, loginId, body.Role)) is null) return NotFound("That login");
        if (body.IsActive is { } active && (owner = await ops.SetOwnerActiveAsync(id, loginId, active)) is null) return NotFound("That login");
        if (owner is null) return Refused("nothing_to_change", "Send a role, isActive, or both.");
        return Json(LoginView(owner));
    }

    private static async Task<IResult> ResetLogin(int id, int loginId, AdminOperations ops)
        => await ops.ResetOwnerPasswordAsync(id, loginId) is { } h ? Json(HandOver(h)) : NotFound("That login");

    private static async Task<IResult> RemoveLogin(int id, int loginId, AdminOperations ops)
        => await ops.RemoveOwnerAsync(id, loginId) ? Results.NoContent() : NotFound("That login");
}
