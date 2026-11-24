using System.Collections.Concurrent;
using System.Security.Cryptography;
using Cloud.Data;
using Cloud.Pages.Public;
using Cloud.Services;
using Microsoft.EntityFrameworkCore;
using Sync;

namespace Cloud.Api;

/// <summary>
/// Orders a guest sends from the public menu, when the owner chose to receive them (<see cref="MenuOrderModes"/>): to the owner's order
/// list in the menu editor, or to the till of the guest's branch. Three sides:
/// <list type="bullet">
/// <item>the guest: <c>POST /m/{slug}/orders</c> sends one, <c>GET /m/{slug}/orders/{token}</c> follows it (the token is in their phone only);</item>
/// <item>the owner: <c>GET /menu/v1/orders</c>, <c>PUT /menu/v1/orders/{id}</c> to accept, mark ready, done, or turn it down;</item>
/// <item>the till: <c>GET /api/sync/menu-orders</c> and <c>PUT /api/sync/menu-orders/{id}</c>, for the orders sent to its branch.</item>
/// </list>
/// Prices are worked out here from the menu as it is when the order arrives; the guest's phone only says which dishes and how many.
/// </summary>
public static partial class MenuEndpoints
{
    public const int MaxOrderLines = 40;
    public const int MaxQuantity = 99;

    private static void MapOrders(IEndpointRouteBuilder app, RouteGroupBuilder g)
    {
        app.MapPost("/m/{slug}/orders", SendOrder).AllowAnonymous();
        app.MapGet("/m/{slug}/orders/{token}", FollowOrder).AllowAnonymous();
        g.MapGet("/orders", ListOrders);
        g.MapPut("/orders/{id:int}", MoveOrder);
    }

    private static void MapDeviceOrders(RouteGroupBuilder sync)
    {
        sync.MapGet("/menu-orders", TillOrders);
        sync.MapPut("/menu-orders/{id:int}", TillMoveOrder);
    }

    // ---- the guest ----

    public sealed record OrderLineRequest(int Id, int N);
    public sealed record SendOrderRequest(
        string? B, int? T, string? Preview, string? Name, string? Phone, string? Place, string? Note, OrderLineRequest[]? Lines);

    private static async Task<IResult> SendOrder(string slug, SendOrderRequest body, HttpContext http, ControlDbContext db, TimeProvider time,
        MenuOrderThrottle throttle)
    {
        var menu = await db.Menus.AsNoTracking().Include(m => m.Client).FirstOrDefaultAsync(m => m.Slug == slug.ToLower());
        if (menu is null || !menu.Client.IsActive || (!menu.IsPublished && (string.IsNullOrEmpty(body.Preview) || body.Preview != menu.PreviewKey)))
            return Results.NotFound();
        if (menu.OrderMode is not (MenuOrderModes.Request or MenuOrderModes.Pos))
            return Error(StatusCodes.Status409Conflict, "orders_off", "This menu does not take orders online. Show your order to a member of staff.");

        var name = MenuRules.Clean(body.Name, 80);
        if (name is null) return Error(StatusCodes.Status400BadRequest, "name_required", "Write your name, so the staff know whose order it is.");
        var phone = MenuRules.Clean(body.Phone, 30);
        if (phone is not null && !MenuContact.IsPhone(phone)) return Error(StatusCodes.Status400BadRequest, "bad_phone", "That phone number does not look right.");
        var wanted = (body.Lines ?? []).Where(l => l.N > 0).GroupBy(l => l.Id).Select(x => (Id: x.Key, N: Math.Min(MaxQuantity, x.Sum(l => l.N)))).ToList();
        if (wanted.Count == 0) return Error(StatusCodes.Status400BadRequest, "empty", "The order is empty.");
        if (wanted.Count > MaxOrderLines) return Error(StatusCodes.Status400BadRequest, "too_many", $"An order can hold up to {MaxOrderLines} different items.");

        // A few orders per phone per few minutes is plenty for a table; more is someone playing with the page.
        var who = (http.Connection.RemoteIpAddress?.ToString() ?? "unknown") + "|" + menu.Id;
        if (!throttle.TryTake(who))
            return Error(StatusCodes.Status429TooManyRequests, SyncErrorCodes.RateLimited, "Too many orders from this phone. Wait a few minutes, or ask a member of staff.");

        var context = await MenuContext.LoadAsync(db, menu, body.B, body.T);
        if (context.Branches.Count > 1 && context.Branch is null)
            return Error(StatusCodes.Status400BadRequest, "no_branch", "Pick your branch first.");
        var site = context.Branch?.SiteId;
        var ids = wanted.Select(w => w.Id).ToList();
        var items = await MenuContext.WithPrices(MenuRules.Visible(db, menu.Id, site).AsNoTracking().Where(i => ids.Contains(i.Id)), site, context.Type).ToListAsync();
        var lines = new List<MenuOrderLine>();
        foreach (var (id, n) in wanted)
        {
            var item = items.FirstOrDefault(i => i.Id == id);
            if (item is null) return Error(StatusCodes.Status409Conflict, "gone", "An item in your order is no longer on the menu. Remove it and send again.");
            if (item.IsSoldOut) return Error(StatusCodes.Status409Conflict, "sold_out", $"{item.Name} is sold out. Remove it and send again.");
            var (price, currency) = MenuRules.PriceAt(item, site, context.Type, menu.Currency);
            lines.Add(new MenuOrderLine
            {
                MenuItemId = item.Id, Name = item.Name, Quantity = n, UnitPrice = price, Currency = currency,
                PosProductId = site is null ? null : item.Branches.FirstOrDefault(r => r.SiteId == site)?.PosProductId,
            });
        }

        // To the till only when there is one to send it to: the guest's branch, with a POS.
        var route = menu.OrderMode == MenuOrderModes.Pos && site is not null ? MenuOrderModes.Pos : MenuOrderModes.Request;
        var now = time.GetUtcNow().UtcDateTime;
        var order = new MenuOrder
        {
            MenuId = menu.Id, Route = route, SiteId = site, BranchName = context.Branches.Count > 1 ? context.Branch?.Name : null,
            OrderType = context.Type?.Name, GuestName = name, Phone = phone, Place = MenuRules.Clean(body.Place, 300), Note = MenuRules.Clean(body.Note, 500),
            Token = Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant(), Status = MenuOrderStatus.New,
            CreatedAt = now, UpdatedAt = now, Lines = lines,
        };
        // The number counts up per menu; two orders at the same moment can pick the same one, and the unique index sends one round again.
        for (var attempt = 0; ; attempt++)
        {
            order.Number = (await db.MenuOrders.Where(o => o.MenuId == menu.Id).MaxAsync(o => (int?)o.Number) ?? 100) + 1;
            db.MenuOrders.Add(order);
            try { await db.SaveChangesAsync(); break; }
            catch (DbUpdateException) when (attempt < 4) { db.ChangeTracker.Clear(); order.Id = 0; foreach (var l in lines) l.Id = 0; }
        }
        return Results.Json(GuestView(order), SyncWire.Json, statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> FollowOrder(string slug, string token, ControlDbContext db, HttpContext http)
    {
        var order = await db.MenuOrders.AsNoTracking().Include(o => o.Lines).Include(o => o.Menu)
            .FirstOrDefaultAsync(o => o.Token == token && o.Menu.Slug == slug.ToLower());
        http.Response.Headers.CacheControl = "no-store";
        return order is null ? Results.NotFound() : Results.Json(GuestView(order), SyncWire.Json);
    }

    /// <summary>What the guest's phone shows: the number, where it stands, and what is in it.</summary>
    private static object GuestView(MenuOrder o) => new
    {
        number = o.Number, token = o.Token, status = o.Status, route = o.Route, orderType = o.OrderType, branch = o.BranchName,
        createdAtUtc = DateTime.SpecifyKind(o.CreatedAt, DateTimeKind.Utc),
        lines = o.Lines.OrderBy(l => l.Id).Select(l => new { name = l.Name, quantity = l.Quantity, total = MenuRules.Price(l.UnitPrice * l.Quantity, l.Currency) }),
        total = OrderTotal(o),
    };

    /// <summary>One total per currency ("$12 + 450,000 LBP"), never a wrong sum across currencies.</summary>
    private static string OrderTotal(MenuOrder o)
        => string.Join(" + ", o.Lines.GroupBy(l => l.Currency).Select(g => MenuRules.Price(g.Sum(l => l.UnitPrice * l.Quantity), g.Key)));

    /// <summary>What the owner and the till see: everything, including how to reach the guest and the POS product of each line.</summary>
    private static object StaffView(MenuOrder o) => new
    {
        id = o.Id, number = o.Number, status = o.Status, route = o.Route, siteId = o.SiteId, branch = o.BranchName, orderType = o.OrderType,
        name = o.GuestName, phone = o.Phone, place = o.Place, note = o.Note,
        createdAtUtc = DateTime.SpecifyKind(o.CreatedAt, DateTimeKind.Utc), updatedAtUtc = DateTime.SpecifyKind(o.UpdatedAt, DateTimeKind.Utc),
        lines = o.Lines.OrderBy(l => l.Id).Select(l => new
        {
            name = l.Name, quantity = l.Quantity, unitPrice = l.UnitPrice, currency = l.Currency, posProductId = l.PosProductId,
            total = MenuRules.Price(l.UnitPrice * l.Quantity, l.Currency),
        }),
        total = OrderTotal(o),
    };

    // ---- the owner ----

    public sealed record MoveOrderRequest(string? Status);

    /// <summary>The menu's orders, newest first: the open ones, then those closed in the last day (so a mistake can be put right).</summary>
    private static async Task<IResult> ListOrders(HttpContext http, ControlDbContext db, TimeProvider time, int? client)
    {
        var (_, menu, failure) = await MenuAsync(http, db, time, client);
        if (failure is not null) return failure;
        var since = time.GetUtcNow().UtcDateTime.AddDays(-1);
        var rows = await db.MenuOrders.AsNoTracking().Include(o => o.Lines)
            .Where(o => o.MenuId == menu!.Id && (MenuOrderStatus.Open.Contains(o.Status) || o.UpdatedAt >= since))
            .OrderByDescending(o => o.CreatedAt).Take(100).AsSplitQuery().ToListAsync();
        http.Response.Headers.CacheControl = "no-store";
        return Results.Json(new { orders = rows.Select(StaffView), open = rows.Count(o => MenuOrderStatus.Open.Contains(o.Status)) }, SyncWire.Json);
    }

    private static async Task<IResult> MoveOrder(int id, MoveOrderRequest body, HttpContext http, ControlDbContext db, TimeProvider time, int? client)
    {
        var (_, menu, failure) = await MenuAsync(http, db, time, client);
        if (failure is not null) return failure;
        var order = await db.MenuOrders.Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == id && o.MenuId == menu!.Id);
        return await MoveAsync(order, body, db, time);
    }

    private static async Task<IResult> MoveAsync(MenuOrder? order, MoveOrderRequest body, ControlDbContext db, TimeProvider time)
    {
        if (order is null) return Error(StatusCodes.Status404NotFound, "not_found", "No such order.");
        if (!MenuOrderStatus.IsValid(body.Status) || body.Status == MenuOrderStatus.New)
            return Error(StatusCodes.Status400BadRequest, "bad_status", "Accept, ready, done or rejected.");
        order.Status = body.Status!;
        order.UpdatedAt = time.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync();
        return Results.Json(StaffView(order), SyncWire.Json);
    }

    // ---- the till ----

    /// <summary>The open orders sent to this shop PC's branch, oldest first (the order they should be made in).</summary>
    private static async Task<IResult> TillOrders(HttpContext http, ControlDbContext db, TimeProvider time)
    {
        var (device, failure) = await SyncEndpoints.AuthenticateAsync(http, db, time);
        if (failure is not null) return failure;
        var rows = await db.MenuOrders.AsNoTracking().Include(o => o.Lines)
            .Where(o => o.SiteId == device!.Site.Id && o.Route == MenuOrderModes.Pos && MenuOrderStatus.Open.Contains(o.Status))
            .OrderBy(o => o.CreatedAt).Take(100).AsSplitQuery().ToListAsync();
        return Results.Json(new { orders = rows.Select(StaffView) }, SyncWire.Json);
    }

    private static async Task<IResult> TillMoveOrder(int id, MoveOrderRequest body, HttpContext http, ControlDbContext db, TimeProvider time)
    {
        var (device, failure) = await SyncEndpoints.AuthenticateAsync(http, db, time);
        if (failure is not null) return failure;
        var order = await db.MenuOrders.Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == id && o.SiteId == device!.Site.Id);
        return await MoveAsync(order, body, db, time);
    }
}

/// <summary>Holds back a phone that sends order after order: a handful per menu in a few minutes. In memory; a restart clears it.</summary>
public sealed class MenuOrderThrottle(TimeProvider time)
{
    private const int Allowed = 6;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(10);
    private readonly ConcurrentDictionary<string, (int Count, DateTime Since)> _seen = new();

    public bool TryTake(string key)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var entry = _seen.AddOrUpdate(key, (1, now), (_, old) => now - old.Since > Window ? (1, now) : (old.Count + 1, old.Since));
        if (_seen.Count > 50_000) foreach (var stale in _seen.Where(p => now - p.Value.Since > Window).Select(p => p.Key).ToList()) _seen.TryRemove(stale, out _);
        return entry.Count <= Allowed;
    }
}
