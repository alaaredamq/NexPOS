using Cloud.Data;
using Cloud.Services;
using Microsoft.EntityFrameworkCore;
using Sync;

namespace Cloud.Api;

/// <summary>
/// The online menu. Three audiences:
/// <list type="bullet">
/// <item>the menu editor (<c>/menu/v1</c>): the shop owner signed in on the owner website (a login allowed to edit products), or you from the
/// admin portal (admin cookie plus <c>?client=</c>). Every call is bound to one client's single menu; there is no menu id to tamper with.</item>
/// <item>the shop PC (<c>POST /api/sync/menu-catalog</c>, device token): its product list, when the owner linked the branch.</item>
/// <item>everyone: the page itself (Pages/Public/Menu) and its pictures (<c>/m/img/{file}</c>).</item>
/// </list>
/// </summary>
public static class MenuEndpoints
{
    /// <summary>The admin portal's calls ride on its cookie; this header (which a cross-site form cannot send) keeps another site from making them.</summary>
    public const string AdminHeader = "X-Coduo-Menu";

    public static void MapMenuApi(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/menu/v1").AllowAnonymous();
        g.MapGet("/editor", GetEditor);
        g.MapPost("/editor", CreateMenu);
        g.MapPut("/settings", SaveSettings);
        g.MapPut("/logo", (HttpContext h, ControlDbContext db, TimeProvider t, MenuImages img, int? client) => SetLogo(h, db, t, img, client));
        g.MapDelete("/logo", (HttpContext h, ControlDbContext db, TimeProvider t, MenuImages img, int? client) => DeleteLogo(h, db, t, img, client));
        g.MapPost("/sections", AddSection);
        g.MapPut("/sections/{id:int}", EditSection);
        g.MapDelete("/sections/{id:int}", DeleteSection);
        g.MapPost("/items", AddItem);
        g.MapPut("/items/{id:int}", EditItem);
        g.MapDelete("/items/{id:int}", DeleteItem);
        g.MapPut("/items/{id:int}/image", SetItemImage);
        g.MapDelete("/items/{id:int}/image", DeleteItemImage);
        g.MapPut("/order", Reorder);
        g.MapPost("/types", AddOrderType);
        g.MapPut("/types/{id:int}", EditOrderType);
        g.MapDelete("/types/{id:int}", DeleteOrderType);
        g.MapGet("/items", ListItems);
        g.MapPost("/items/{id:int}/move", MoveItem);

        app.MapGet("/m/{slug}/items", PublicItems).AllowAnonymous();

        app.MapGet("/m/img/{name}", (string name, MenuImages images) =>
        {
            var path = images.PathOf(name);
            if (path is null || !File.Exists(path)) return Results.NotFound();
            return Results.File(path, MenuImages.ContentType(name), enableRangeProcessing: false);
        }).AllowAnonymous().AddEndpointFilter(async (ctx, next) =>
        {
            // The name changes whenever the picture does, so it can be cached for good.
            ctx.HttpContext.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
            return await next(ctx);
        });
    }

    /// <summary>
    /// The public menu's items a page at a time, for menu-page.js: the rest of a section (<c>section</c> + <c>skip</c>; 0 = items in no section)
    /// or a search (<c>q</c>). Only what the page itself would show: published (or the right preview key), visible sections, visible items.
    /// </summary>
    private static async Task<IResult> PublicItems(string slug, HttpContext http, ControlDbContext db, int? section, int? skip, int? take, string? q, string? preview,
        string? b, int? t)
    {
        var menu = await db.Menus.AsNoTracking().Include(m => m.Client).FirstOrDefaultAsync(m => m.Slug == slug.ToLower());
        if (menu is null || !menu.Client.IsActive || (!menu.IsPublished && (string.IsNullOrEmpty(preview) || preview != menu.PreviewKey)))
            return Results.NotFound();
        // The same branch and order type as the page that asks.
        var context = await Cloud.Pages.Public.MenuContext.LoadAsync(db, menu, b, t);
        if (context.Branches.Count > 1 && context.Branch is null) return Results.NotFound();
        var site = context.Branch?.SiteId;

        var sectionIds = db.MenuSections.Where(s => s.MenuId == menu.Id).Select(s => (int?)s.Id);
        var items = MenuRules.Visible(db, menu.Id, site).AsNoTracking();
        var size = Math.Clamp(take ?? PageSize, 1, 60);

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = "%" + q.Trim().Replace("%", "").Replace("_", "") + "%";
            items = items.Where(i => EF.Functions.Like(i.Name, term) || EF.Functions.Like(i.Description ?? i.PosDescription ?? "", term));
        }
        else if (section is null or 0)
            items = items.Where(i => i.SectionId == null || !sectionIds.Contains(i.SectionId));
        else
            items = items.Where(i => i.SectionId == section);

        var rows = await Cloud.Pages.Public.MenuContext.WithPrices(items.OrderBy(i => i.SortOrder).ThenBy(i => i.Id).Skip(Math.Max(0, skip ?? 0)).Take(size + 1), site, context.Type)
            .ToListAsync();
        http.Response.Headers.CacheControl = menu.IsPublished ? "public, max-age=60" : "no-store";
        return Results.Json(new
        {
            items = rows.Take(size).Select(i => (Item: i, At: MenuRules.PriceAt(i, site, context.Type, menu.Currency))).Select(x => new
            {
                // id, price and currency let the page add the dish to the guest's order and total it.
                id = x.Item.Id,
                price = x.At.Price,
                currency = x.At.Currency,
                name = x.Item.Name,
                description = x.Item.Description ?? x.Item.PosDescription,
                priceText = MenuRules.Price(x.At.Price, x.At.Currency),
                imageUrl = x.Item.ImageFile is null ? null : "/m/img/" + x.Item.ImageFile,
                soldOut = x.Item.IsSoldOut,
            }),
            hasMore = rows.Count > size,
        }, SyncWire.Json);
    }

    /// <summary>On the till API group: the shop PC hands over its product list for the menu its branch is linked to.</summary>
    public static void MapDevice(RouteGroupBuilder sync) => sync.MapPost("/menu-catalog", PushCatalog);

    // ---- who is editing ----

    private sealed record Editor(Client Client, bool IsAdmin);

    private static IResult Error(int status, string code, string message)
        => Results.Json(new SyncError(code, message), SyncWire.Json, statusCode: status);

    private static async Task<(Editor? Editor, IResult? Failure)> EditorAsync(HttpContext http, ControlDbContext db, TimeProvider time, int? client)
    {
        if (http.Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var who = await OwnerAuth.AuthenticateAsync(http, db, time);
            if (who is null) return (null, Error(StatusCodes.Status401Unauthorized, SyncErrorCodes.Unauthorized, "Please sign in again."));
            if (!who.User.Can.HasFlag(OwnerPermissions.Edit))
                return (null, Error(StatusCodes.Status403Forbidden, "forbidden", "This login is not allowed to change the menu."));
            return (new Editor(who.Client, false), null);
        }

        if (http.User.Identity?.IsAuthenticated == true)
        {
            if (!HttpMethods.IsGet(http.Request.Method) && http.Request.Headers[AdminHeader] != "1")
                return (null, Error(StatusCodes.Status400BadRequest, "bad_request", "Missing editor header."));
            var row = client is null ? null : await db.Clients.Include(c => c.Sites).FirstOrDefaultAsync(c => c.Id == client);
            return row is null
                ? (null, Error(StatusCodes.Status404NotFound, "not_found", "No such client."))
                : (new Editor(row, true), null);
        }

        return (null, Error(StatusCodes.Status401Unauthorized, SyncErrorCodes.Unauthorized, "Please sign in."));
    }

    private static async Task<(Editor? Editor, Menu? Menu, IResult? Failure)> MenuAsync(HttpContext http, ControlDbContext db, TimeProvider time, int? client)
    {
        var (editor, failure) = await EditorAsync(http, db, time, client);
        if (failure is not null) return (null, null, failure);
        var menu = await db.Menus.FirstOrDefaultAsync(m => m.ClientId == editor!.Client.Id);
        return menu is null
            ? (editor, null, Error(StatusCodes.Status404NotFound, "no_menu", "There is no menu yet."))
            : (editor, menu, null);
    }

    private static async Task<IResult> Saved(ControlDbContext db, Menu menu, TimeProvider time, Editor editor, HttpContext http)
    {
        menu.UpdatedAt = time.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync();
        return Results.Json(await StateAsync(db, editor, menu, http), SyncWire.Json);
    }

    // ---- what the editor shows ----

    private static async Task<object> StateAsync(ControlDbContext db, Editor editor, Menu? menu, HttpContext http)
    {
        var sites = await db.Sites.Where(s => s.ClientId == editor.Client.Id && !s.IsDisabled).OrderBy(s => s.CreatedAt)
            .Select(s => new { id = s.Id, name = s.Name, lastSeenAtUtc = s.LastSeenAt }).ToListAsync();
        var origin = PublicSite.OriginOf(http);
        if (menu is null)
            return new { clientName = editor.Client.Name, admin = editor.IsAdmin, sites, menu = (object?)null, sections = Array.Empty<object>(), looseCount = 0 };

        // Counts only: a menu can hold thousands of items, so the editor loads each section's items a page at a time (GET items).
        var counts = await db.MenuItems.Where(i => i.MenuId == menu.Id).GroupBy(i => i.SectionId)
            .Select(g => new { g.Key, Count = g.Count() }).ToListAsync();
        var countOf = counts.Where(c => c.Key != null).ToDictionary(c => c.Key!.Value, c => c.Count);
        var sections = (await db.MenuSections.Where(s => s.MenuId == menu.Id).OrderBy(s => s.SortOrder).ThenBy(s => s.Id).ToListAsync())
            .Select(s => new { id = s.Id, name = s.Name, isHidden = s.IsHidden, linked = s.PosKey != null, itemCount = countOf.GetValueOrDefault(s.Id) })
            .ToList();
        var known = sections.Select(s => s.id).ToHashSet();
        var looseCount = counts.Where(c => c.Key is null || !known.Contains(c.Key.Value)).Sum(c => c.Count);
        var branches = (await db.MenuBranches.Where(b => b.MenuId == menu.Id).OrderBy(b => b.SortOrder).ThenBy(b => b.Id)
            .Select(b => new { b.SiteId, b.Slug, b.Site.Name, b.CatalogAt, b.PosArranged }).ToListAsync())
            .Select(b => new
            {
                siteId = b.SiteId, name = b.Name, slug = b.Slug, url = origin + "/m/" + menu.Slug + "?b=" + b.Slug,
                catalogAtUtc = b.CatalogAt is null ? (DateTime?)null : DateTime.SpecifyKind(b.CatalogAt.Value, DateTimeKind.Utc),
            }).ToList();
        var main = await MainBranchAsync(db, menu.Id);
        var types = await db.MenuOrderTypes.Where(x => x.MenuId == menu.Id).OrderBy(x => x.SortOrder).ThenBy(x => x.Id)
            .Select(x => new { id = x.Id, name = x.Name, adjust = x.Adjust, adjustValue = x.AdjustValue }).ToListAsync();
        return new
        {
            clientName = editor.Client.Name,
            admin = editor.IsAdmin,
            sites,
            menu = new
            {
                slug = menu.Slug, url = origin + "/m/" + menu.Slug, previewUrl = origin + "/m/" + menu.Slug + "?preview=" + menu.PreviewKey, title = menu.Title, tagline = menu.Tagline, note = menu.Note,
                language = menu.Language, currency = menu.Currency, listingStyle = menu.ListingStyle, logoUrl = menu.LogoFile is null ? null : "/m/img/" + menu.LogoFile,
                isPublished = menu.IsPublished,
                // The main branch (the first); kept under its old name for clients that knew only one.
                linkedSiteId = main?.SiteId, posArranged = main is { PosArranged: true },
                catalogAtUtc = branches.Max(b => b.catalogAtUtc),
            },
            branches,
            orderTypes = types,
            sections,
            looseCount,
        };
    }

    /// <summary>The menu's main branch (the first), or null when no branch fills it.</summary>
    private static Task<MenuBranch?> MainBranchAsync(ControlDbContext db, int menuId)
        => db.MenuBranches.Where(b => b.MenuId == menuId).OrderBy(b => b.SortOrder).ThenBy(b => b.Id).FirstOrDefaultAsync();

    /// <summary>Whether the main branch's till decides the order, so the editor may not reorder what came from a POS.</summary>
    private static async Task<bool> PosArrangedAsync(ControlDbContext db, int menuId) => (await MainBranchAsync(db, menuId)) is { PosArranged: true };

    public const int PageSize = 40;

    private static object ItemView(MenuItem i, Menu menu, IReadOnlyDictionary<Guid, string> branchNames) => new
    {
        id = i.Id, sectionId = i.SectionId, name = i.Name, description = i.Description, posDescription = i.PosDescription,
        price = i.Price, currency = i.Currency, priceText = MenuRules.Price(i.Price, i.Currency ?? menu.Currency),
        imageUrl = i.ImageFile is null ? null : "/m/img/" + i.ImageFile,
        isHidden = i.IsHidden, isSoldOut = i.IsSoldOut, linked = i.Branches.Count > 0,
        // Where it is sold and at what price (POS dishes), and its own prices per order type.
        branches = i.Branches.Where(r => branchNames.ContainsKey(r.SiteId)).OrderBy(r => branchNames[r.SiteId])
            .Select(r => new { siteId = r.SiteId, name = branchNames[r.SiteId], priceText = MenuRules.Price(r.Price, r.Currency ?? menu.Currency) }),
        typePrices = i.TypePrices.ToDictionary(p => p.OrderTypeId.ToString(System.Globalization.CultureInfo.InvariantCulture), p => p.Price),
    };

    private static async Task<IReadOnlyDictionary<Guid, string>> BranchNamesAsync(ControlDbContext db, int menuId)
        => await db.MenuBranches.Where(b => b.MenuId == menuId).ToDictionaryAsync(b => b.SiteId, b => b.Site.Name);

    /// <summary>One section's items, a page at a time. <c>section=0</c> = items in no section.</summary>
    private static async Task<IResult> ListItems(HttpContext http, ControlDbContext db, TimeProvider time, int? client, int section, int? skip, int? take)
    {
        var (_, menu, failure) = await MenuAsync(http, db, time, client);
        if (failure is not null) return failure;
        var size = Math.Clamp(take ?? PageSize, 1, 100);
        var query = db.MenuItems.AsNoTracking().Include(i => i.Branches).Include(i => i.TypePrices).Where(i => i.MenuId == menu!.Id);
        if (section == 0)
        {
            var sectionIds = db.MenuSections.Where(s => s.MenuId == menu!.Id).Select(s => (int?)s.Id);
            query = query.Where(i => i.SectionId == null || !sectionIds.Contains(i.SectionId));
        }
        else query = query.Where(i => i.SectionId == section);
        var rows = await query.OrderBy(i => i.SortOrder).ThenBy(i => i.Id).Skip(Math.Max(0, skip ?? 0)).Take(size + 1).AsSplitQuery().ToListAsync();
        var names = await BranchNamesAsync(db, menu!.Id);
        return Results.Json(new { items = rows.Take(size).Select(i => ItemView(i, menu, names)), hasMore = rows.Count > size }, SyncWire.Json);
    }

    public sealed record MoveRequest(int Direction);

    /// <summary>Swaps an item with its neighbour in its section. Works on a partly loaded list, unlike sending a whole new order.</summary>
    private static async Task<IResult> MoveItem(int id, MoveRequest body, HttpContext http, ControlDbContext db, TimeProvider time, int? client)
    {
        var (editor, menu, failure) = await MenuAsync(http, db, time, client);
        if (failure is not null) return failure;
        var item = await db.MenuItems.FirstOrDefaultAsync(i => i.Id == id && i.MenuId == menu!.Id);
        if (item is null) return Error(StatusCodes.Status404NotFound, "not_found", "No such item.");
        if (await PosArrangedAsync(db, menu!.Id) && await db.MenuItemBranches.AnyAsync(r => r.MenuItemId == id))
            return Error(StatusCodes.Status409Conflict, "linked", "The order comes from the POS. Change it there.");
        var siblings = await db.MenuItems.Where(i => i.MenuId == menu.Id && i.SectionId == item.SectionId)
            .OrderBy(i => i.SortOrder).ThenBy(i => i.Id).ToListAsync();
        var at = siblings.FindIndex(i => i.Id == id);
        var to = at + Math.Sign(body.Direction);
        if (to >= 0 && to < siblings.Count && body.Direction != 0)
        {
            // Swap the two places. Items in other sections share the numbering, so only these two numbers change.
            var (a, b) = (siblings[at], siblings[to]);
            (a.SortOrder, b.SortOrder) = (b.SortOrder, a.SortOrder);
            if (a.SortOrder == b.SortOrder) { if (to > at) a.SortOrder++; else b.SortOrder++; }
        }
        return await Saved(db, menu, time, editor!, http);
    }

    private static async Task<IResult> GetEditor(HttpContext http, ControlDbContext db, TimeProvider time, int? client)
    {
        var (editor, failure) = await EditorAsync(http, db, time, client);
        if (failure is not null) return failure;
        var menu = await db.Menus.FirstOrDefaultAsync(m => m.ClientId == editor!.Client.Id);
        return Results.Json(await StateAsync(db, editor!, menu, http), SyncWire.Json);
    }

    public sealed record CreateRequest(string? Title);

    private static async Task<IResult> CreateMenu(CreateRequest? body, HttpContext http, ControlDbContext db, TimeProvider time, int? client)
    {
        var (editor, failure) = await EditorAsync(http, db, time, client);
        if (failure is not null) return failure;
        var existing = await db.Menus.FirstOrDefaultAsync(m => m.ClientId == editor!.Client.Id);
        if (existing is not null) return Results.Json(await StateAsync(db, editor!, existing, http), SyncWire.Json);

        var title = MenuRules.Clean(body?.Title, 120) ?? editor!.Client.Name;
        var now = time.GetUtcNow().UtcDateTime;
        var menu = new Menu
        {
            ClientId = editor!.Client.Id, Title = title, Slug = await MenuRules.FreeSlugAsync(db, MenuRules.SlugFrom(title)),
            PreviewKey = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(12)).ToLowerInvariant(),
            CreatedAt = now, UpdatedAt = now,
        };
        db.Menus.Add(menu);
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException)
        {
            // Two first clicks at once: the other request made it.
            db.ChangeTracker.Clear();
            menu = await db.Menus.FirstAsync(m => m.ClientId == editor.Client.Id);
        }
        return Results.Json(await StateAsync(db, editor, menu, http), SyncWire.Json);
    }

    public sealed record SettingsRequest(
        string? Title, string? Tagline, string? Note, string? Language, string? Currency, string? Slug, bool? IsPublished,
        string? LinkedSiteId, string? ListingStyle = null, string[]? BranchIds = null);

    private static async Task<IResult> SaveSettings(SettingsRequest body, HttpContext http, ControlDbContext db, TimeProvider time,
        MenuCatalogMerge merge, int? client)
    {
        var (editor, menu, failure) = await MenuAsync(http, db, time, client);
        if (failure is not null) return failure;

        if (body.Title is not null) menu!.Title = MenuRules.Clean(body.Title, 120) ?? menu.Title;
        if (body.Tagline is not null) menu!.Tagline = MenuRules.Clean(body.Tagline, 200);
        if (body.Note is not null) menu!.Note = MenuRules.Clean(body.Note, 1000);
        if (body.Language is "en" or "ar") menu!.Language = body.Language;
        if (body.ListingStyle is not null)
        {
            if (!MenuListingStyles.IsValid(body.ListingStyle))
                return Error(StatusCodes.Status400BadRequest, "bad_style", "Unknown listing style.");
            menu!.ListingStyle = body.ListingStyle;
        }
        if (body.Currency is not null) menu!.Currency = MenuRules.Clean(body.Currency, 10) ?? "";
        if (body.IsPublished is { } published) menu!.IsPublished = published;

        if (body.Slug is not null)
        {
            var slug = body.Slug.Trim().ToLowerInvariant();
            if (!MenuRules.IsValidSlug(slug))
                return Error(StatusCodes.Status400BadRequest, "bad_slug", "The address can use small English letters, digits and dashes (3 to 60).");
            if (slug != menu!.Slug && await db.Menus.AnyAsync(m => m.Slug == slug))
                return Error(StatusCodes.Status409Conflict, "slug_taken", "Another menu already uses that address.");
            menu!.Slug = slug;
        }

        // Which branches fill the menu, main one first. LinkedSiteId is the one-branch form ("" = none).
        var wantedIds = body.BranchIds ?? (body.LinkedSiteId is null ? null : body.LinkedSiteId.Length == 0 ? [] : [body.LinkedSiteId]);
        if (wantedIds is not null)
        {
            var wanted = new List<Guid>();
            foreach (var raw in wantedIds)
            {
                if (!Guid.TryParse(raw, out var g) || !await db.Sites.AnyAsync(s => s.Id == g && s.ClientId == editor!.Client.Id && !s.IsDisabled))
                    return Error(StatusCodes.Status400BadRequest, "bad_site", "That branch is not one of this shop's.");
                if (!wanted.Contains(g)) wanted.Add(g);
            }
            await SetBranchesAsync(db, merge, menu!, wanted);
        }

        return await Saved(db, menu!, time, editor!, http);
    }

    /// <summary>
    /// Makes <paramref name="wanted"/> the menu's branches, in that order. A branch taken off loses its prices (what only it sold stays,
    /// as hand-made dishes); a new one sends its list at its shop PC's next check-in. When the main branch changes, every branch sends
    /// again so the new main one takes over names and places.
    /// </summary>
    private static async Task SetBranchesAsync(ControlDbContext db, MenuCatalogMerge merge, Menu menu, List<Guid> wanted)
    {
        await db.SaveChangesAsync(); // the other settings of this request, before the tracker is cleared below
        var current =await db.MenuBranches.Where(b => b.MenuId == menu.Id).OrderBy(b => b.SortOrder).ThenBy(b => b.Id).ToListAsync();
        foreach (var gone in current.Where(b => !wanted.Contains(b.SiteId)).ToList())
        {
            await merge.DetachAsync(menu, gone.SiteId);
            current.Remove(gone);
        }
        db.ChangeTracker.Clear(); // DetachAsync worked in bulk; start from what is saved
        current = await db.MenuBranches.Where(b => b.MenuId == menu.Id).ToListAsync();
        var mainBefore = current.OrderBy(b => b.SortOrder).ThenBy(b => b.Id).FirstOrDefault()?.SiteId;
        var names = await db.Sites.Where(s => wanted.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Name);
        for (var n = 0; n < wanted.Count; n++)
        {
            var row = current.FirstOrDefault(b => b.SiteId == wanted[n]);
            if (row is null)
            {
                row = new MenuBranch
                {
                    MenuId = menu.Id, SiteId = wanted[n],
                    Slug = MenuRules.BranchSlug(names.GetValueOrDefault(wanted[n]), current.Select(b => b.Slug)),
                };
                db.MenuBranches.Add(row);
                current.Add(row);
            }
            row.SortOrder = n;
        }
        // A branch carried over from the one-branch menu has the placeholder word "main"; once there are several, it gets its name.
        if (current.Count > 1)
            foreach (var old in current.Where(b => b.Slug == "main"))
                old.Slug = MenuRules.BranchSlug(names.GetValueOrDefault(old.SiteId), current.Where(b => b != old).Select(b => b.Slug));
        if (wanted.Count > 0 && mainBefore is { } before && before != wanted[0])
            foreach (var b in current) b.CatalogHash = null;
        await db.SaveChangesAsync();
        db.Entry(menu).State = EntityState.Unchanged; // tracked again (the menu row only, not its client and sites) for the save that follows
    }

    // ---- pictures ----

    private static async Task<(byte[]? Data, string? Ext, IResult? Failure)> ReadImageAsync(HttpContext http)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[64 * 1024];
        int read;
        while ((read = await http.Request.Body.ReadAsync(chunk)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > MenuImages.MaxBytes)
                return (null, null, Error(StatusCodes.Status413PayloadTooLarge, "too_large", "That picture is too large (2 MB at most)."));
        }
        var data = buffer.ToArray();
        var ext = MenuImages.Sniff(data);
        return ext is null
            ? (null, null, Error(StatusCodes.Status400BadRequest, "not_an_image", "Use a JPEG, PNG or WebP picture."))
            : (data, ext, null);
    }

    private static async Task<IResult> SetLogo(HttpContext http, ControlDbContext db, TimeProvider time, MenuImages images, int? client)
    {
        var (editor, menu, failure) = await MenuAsync(http, db, time, client);
        if (failure is not null) return failure;
        var (data, ext, bad) = await ReadImageAsync(http);
        if (bad is not null) return bad;
        var old = menu!.LogoFile;
        menu.LogoFile = await images.SaveAsync(data!, ext!);
        var result = await Saved(db, menu, time, editor!, http);
        images.Delete(old);
        return result;
    }

    private static async Task<IResult> DeleteLogo(HttpContext http, ControlDbContext db, TimeProvider time, MenuImages images, int? client)
    {
        var (editor, menu, failure) = await MenuAsync(http, db, time, client);
        if (failure is not null) return failure;
        var old = menu!.LogoFile;
        menu.LogoFile = null;
        var result = await Saved(db, menu, time, editor!, http);
        images.Delete(old);
        return result;
    }

    private static async Task<IResult> SetItemImage(int id, HttpContext http, ControlDbContext db, TimeProvider time, MenuImages images, int? client)
    {
        var (editor, menu, failure) = await MenuAsync(http, db, time, client);
        if (failure is not null) return failure;
        var item = await db.MenuItems.FirstOrDefaultAsync(i => i.Id == id && i.MenuId == menu!.Id);
        if (item is null) return Error(StatusCodes.Status404NotFound, "not_found", "No such item.");
        var (data, ext, bad) = await ReadImageAsync(http);
        if (bad is not null) return bad;
        var old = item.ImageFile;
        item.ImageFile = await images.SaveAsync(data!, ext!);
        var result = await Saved(db, menu!, time, editor!, http);
        images.Delete(old);
        return result;
    }

    private static async Task<IResult> DeleteItemImage(int id, HttpContext http, ControlDbContext db, TimeProvider time, MenuImages images, int? client)
    {
        var (editor, menu, failure) = await MenuAsync(http, db, time, client);
        if (failure is not null) return failure;
        var item = await db.MenuItems.FirstOrDefaultAsync(i => i.Id == id && i.MenuId == menu!.Id);
        if (item is null) return Error(StatusCodes.Status404NotFound, "not_found", "No such item.");
        var old = item.ImageFile;
        item.ImageFile = null;
        var result = await Saved(db, menu!, time, editor!, http);
        images.Delete(old);
        return result;
    }

    // ---- sections ----

    public sealed record SectionRequest(string? Name, bool? IsHidden);

    private static async Task<IResult> AddSection(SectionRequest body, HttpContext http, ControlDbContext db, TimeProvider time, int? client)
    {
        var (editor, menu, failure) = await MenuAsync(http, db, time, client);
        if (failure is not null) return failure;
        var name = MenuRules.Clean(body.Name, 120);
        if (name is null) return Error(StatusCodes.Status400BadRequest, "name_required", "Give the section a name.");
        var next = await db.MenuSections.Where(s => s.MenuId == menu!.Id).Select(s => (int?)s.SortOrder).MaxAsync() ?? -1;
        db.MenuSections.Add(new MenuSection { MenuId = menu!.Id, Name = name, SortOrder = next + 1 });
        return await Saved(db, menu, time, editor!, http);
    }

    private static async Task<IResult> EditSection(int id, SectionRequest body, HttpContext http, ControlDbContext db, TimeProvider time, int? client)
    {
        var (editor, menu, failure) = await MenuAsync(http, db, time, client);
        if (failure is not null) return failure;
        var section = await db.MenuSections.FirstOrDefaultAsync(s => s.Id == id && s.MenuId == menu!.Id);
        if (section is null) return Error(StatusCodes.Status404NotFound, "not_found", "No such section.");
        if (body.Name is not null)
        {
            if (section.PosKey is not null)
                return Error(StatusCodes.Status409Conflict, "linked", "This section's name comes from the POS. Rename the category there.");
            section.Name = MenuRules.Clean(body.Name, 120) ?? section.Name;
        }
        if (body.IsHidden is { } hidden) section.IsHidden = hidden;
        return await Saved(db, menu!, time, editor!, http);
    }

    private static async Task<IResult> DeleteSection(int id, HttpContext http, ControlDbContext db, TimeProvider time, MenuImages images, int? client)
    {
        var (editor, menu, failure) = await MenuAsync(http, db, time, client);
        if (failure is not null) return failure;
        var section = await db.MenuSections.FirstOrDefaultAsync(s => s.Id == id && s.MenuId == menu!.Id);
        if (section is null) return Error(StatusCodes.Status404NotFound, "not_found", "No such section.");
        if (section.PosKey is not null)
            return Error(StatusCodes.Status409Conflict, "linked", "This section comes from the POS. Hide it instead.");
        // Hand-made items go with it; POS items in it (added by hand to a POS section is not possible) are not touched.
        var items = await db.MenuItems.Where(i => i.MenuId == menu!.Id && i.SectionId == id).ToListAsync();
        foreach (var i in items) images.Delete(i.ImageFile);
        db.MenuItems.RemoveRange(items);
        db.MenuSections.Remove(section);
        return await Saved(db, menu!, time, editor!, http);
    }

    // ---- items ----

    public sealed record ItemRequest(
        string? Name, string? Description, decimal? Price, string? Currency, int? SectionId, bool? IsHidden, bool? IsSoldOut,
        Dictionary<string, decimal?>? TypePrices = null);

    private static async Task<IResult> AddItem(ItemRequest body, HttpContext http, ControlDbContext db, TimeProvider time, int? client)
    {
        var (editor, menu, failure) = await MenuAsync(http, db, time, client);
        if (failure is not null) return failure;
        var name = MenuRules.Clean(body.Name, 200);
        if (name is null) return Error(StatusCodes.Status400BadRequest, "name_required", "Give the item a name.");
        if (body.Price is < 0 or > 1_000_000_000_000m) return Error(StatusCodes.Status400BadRequest, "bad_price", "Enter a price of 0 or more.");
        if (body.SectionId is { } sid and not 0 && !await db.MenuSections.AnyAsync(s => s.Id == sid && s.MenuId == menu!.Id))
            return Error(StatusCodes.Status400BadRequest, "bad_section", "No such section.");

        var next = await db.MenuItems.Where(i => i.MenuId == menu!.Id).Select(i => (int?)i.SortOrder).MaxAsync() ?? -1;
        var item = new MenuItem
        {
            MenuId = menu!.Id, Name = name, Description = MenuRules.Clean(body.Description, 500), Price = body.Price ?? 0,
            Currency = MenuRules.Clean(body.Currency, 10), SectionId = body.SectionId is 0 ? null : body.SectionId, SortOrder = next + 1,
            IsHidden = body.IsHidden ?? false, IsSoldOut = body.IsSoldOut ?? false,
        };
        db.MenuItems.Add(item);
        if (body.TypePrices is not null && await SetTypePricesAsync(db, menu, item, body.TypePrices) is { } bad) return bad;
        var result = await Saved(db, menu, time, editor!, http);
        // The editor loads items a page at a time, so it learns the new item's id from here (to upload its picture).
        http.Response.Headers["X-Created-Id"] = item.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return result;
    }

    private static async Task<IResult> EditItem(int id, ItemRequest body, HttpContext http, ControlDbContext db, TimeProvider time, int? client)
    {
        var (editor, menu, failure) = await MenuAsync(http, db, time, client);
        if (failure is not null) return failure;
        var item = await db.MenuItems.FirstOrDefaultAsync(i => i.Id == id && i.MenuId == menu!.Id);
        if (item is null) return Error(StatusCodes.Status404NotFound, "not_found", "No such item.");

        // The POS owns a linked item's name, price and section; the owner owns the rest, and its prices per order type.
        var linked = await db.MenuItemBranches.AnyAsync(r => r.MenuItemId == id);
        if (linked && (body.Name is not null || body.Price is not null || body.Currency is not null || body.SectionId is not null))
            return Error(StatusCodes.Status409Conflict, "linked", "Name, price and section come from the POS. Change them there.");

        if (body.Name is not null) item.Name = MenuRules.Clean(body.Name, 200) ?? item.Name;
        if (body.Description is not null) item.Description = MenuRules.Clean(body.Description, 500);
        if (body.Price is { } price)
        {
            if (price is < 0 or > 1_000_000_000_000m) return Error(StatusCodes.Status400BadRequest, "bad_price", "Enter a price of 0 or more.");
            item.Price = price;
        }
        if (body.Currency is not null) item.Currency = MenuRules.Clean(body.Currency, 10);
        if (body.SectionId is { } sid)
        {
            if (sid == 0) item.SectionId = null;
            else if (!await db.MenuSections.AnyAsync(s => s.Id == sid && s.MenuId == menu!.Id))
                return Error(StatusCodes.Status400BadRequest, "bad_section", "No such section.");
            else item.SectionId = sid;
        }
        if (body.IsHidden is { } hidden) item.IsHidden = hidden;
        if (body.IsSoldOut is { } soldOut) item.IsSoldOut = soldOut;
        if (body.TypePrices is not null && await SetTypePricesAsync(db, menu!, item, body.TypePrices) is { } bad) return bad;
        return await Saved(db, menu!, time, editor!, http);
    }

    /// <summary>
    /// A dish's own prices per order type: <c>{"typeId": price}</c>; null (or a type left out of the map) keeps the type's own rule.
    /// Only the types named are touched.
    /// </summary>
    private static async Task<IResult?> SetTypePricesAsync(ControlDbContext db, Menu menu, MenuItem item, Dictionary<string, decimal?> prices)
    {
        var types = await db.MenuOrderTypes.Where(t => t.MenuId == menu.Id).Select(t => t.Id).ToListAsync();
        var mine = item.Id == 0 ? [] : await db.MenuItemTypePrices.Where(p => p.MenuItemId == item.Id).ToListAsync();
        foreach (var (key, value) in prices)
        {
            if (!int.TryParse(key, out var typeId) || !types.Contains(typeId))
                return Error(StatusCodes.Status400BadRequest, "bad_type", "No such order type.");
            if (value is < 0 or > 1_000_000_000_000m) return Error(StatusCodes.Status400BadRequest, "bad_price", "Enter a price of 0 or more.");
            var row = mine.FirstOrDefault(p => p.OrderTypeId == typeId);
            if (value is null) { if (row is not null) db.MenuItemTypePrices.Remove(row); }
            else if (row is null) db.MenuItemTypePrices.Add(new MenuItemTypePrice { MenuItem = item, OrderTypeId = typeId, Price = value.Value });
            else row.Price = value.Value;
        }
        return null;
    }

    // ---- order types (dine in, take out, delivery...) ----

    public sealed record OrderTypeRequest(string? Name, string? Adjust, decimal? AdjustValue);

    private static async Task<IResult> AddOrderType(OrderTypeRequest body, HttpContext http, ControlDbContext db, TimeProvider time, int? client)
    {
        var (editor, menu, failure) = await MenuAsync(http, db, time, client);
        if (failure is not null) return failure;
        var name = MenuRules.Clean(body.Name, 60);
        if (name is null) return Error(StatusCodes.Status400BadRequest, "name_required", "Give the order type a name.");
        if (await db.MenuOrderTypes.CountAsync(t => t.MenuId == menu!.Id) >= 12)
            return Error(StatusCodes.Status400BadRequest, "too_many", "A menu can have up to 12 order types.");
        var type = new MenuOrderType { MenuId = menu!.Id, Name = name };
        if (ApplyRule(type, body) is { } bad) return bad;
        type.SortOrder = (await db.MenuOrderTypes.Where(t => t.MenuId == menu.Id).Select(t => (int?)t.SortOrder).MaxAsync() ?? -1) + 1;
        db.MenuOrderTypes.Add(type);
        return await Saved(db, menu, time, editor!, http);
    }

    private static async Task<IResult> EditOrderType(int id, OrderTypeRequest body, HttpContext http, ControlDbContext db, TimeProvider time, int? client)
    {
        var (editor, menu, failure) = await MenuAsync(http, db, time, client);
        if (failure is not null) return failure;
        var type = await db.MenuOrderTypes.FirstOrDefaultAsync(t => t.Id == id && t.MenuId == menu!.Id);
        if (type is null) return Error(StatusCodes.Status404NotFound, "not_found", "No such order type.");
        if (body.Name is not null) type.Name = MenuRules.Clean(body.Name, 60) ?? type.Name;
        if (ApplyRule(type, body) is { } bad) return bad;
        return await Saved(db, menu!, time, editor!, http);
    }

    private static IResult? ApplyRule(MenuOrderType type, OrderTypeRequest body)
    {
        if (body.Adjust is not null)
        {
            if (!MenuPriceAdjust.IsValid(body.Adjust)) return Error(StatusCodes.Status400BadRequest, "bad_adjust", "Unknown price rule.");
            type.Adjust = body.Adjust;
        }
        if (body.AdjustValue is { } v)
        {
            if (v is < -1_000_000_000m or > 1_000_000_000m || (type.Adjust == MenuPriceAdjust.Percent && v <= -100))
                return Error(StatusCodes.Status400BadRequest, "bad_adjust", "That change is out of range.");
            type.AdjustValue = v;
        }
        if (type.Adjust == MenuPriceAdjust.None) type.AdjustValue = 0;
        return null;
    }

    private static async Task<IResult> DeleteOrderType(int id, HttpContext http, ControlDbContext db, TimeProvider time, int? client)
    {
        var (editor, menu, failure) = await MenuAsync(http, db, time, client);
        if (failure is not null) return failure;
        var type = await db.MenuOrderTypes.FirstOrDefaultAsync(t => t.Id == id && t.MenuId == menu!.Id);
        if (type is null) return Error(StatusCodes.Status404NotFound, "not_found", "No such order type.");
        await db.MenuItemTypePrices.Where(p => p.OrderTypeId == id).ExecuteDeleteAsync();
        db.MenuOrderTypes.Remove(type);
        return await Saved(db, menu!, time, editor!, http);
    }

    private static async Task<IResult> DeleteItem(int id, HttpContext http, ControlDbContext db, TimeProvider time, MenuImages images, int? client)
    {
        var (editor, menu, failure) = await MenuAsync(http, db, time, client);
        if (failure is not null) return failure;
        var item = await db.MenuItems.FirstOrDefaultAsync(i => i.Id == id && i.MenuId == menu!.Id);
        if (item is null) return Error(StatusCodes.Status404NotFound, "not_found", "No such item.");
        if (await db.MenuItemBranches.AnyAsync(r => r.MenuItemId == id))
            return Error(StatusCodes.Status409Conflict, "linked", "This item comes from the POS. Hide it instead.");
        images.Delete(item.ImageFile);
        db.MenuItems.Remove(item);
        return await Saved(db, menu!, time, editor!, http);
    }

    public sealed record OrderRequest(int[]? Sections, int[]? Items, int[]? Types = null);

    /// <summary>The ids in their new order. Ids that are not this menu's are ignored, and so are POS ones while the POS arranges the menu.</summary>
    private static async Task<IResult> Reorder(OrderRequest body, HttpContext http, ControlDbContext db, TimeProvider time, int? client)
    {
        var (editor, menu, failure) = await MenuAsync(http, db, time, client);
        if (failure is not null) return failure;
        var arranged = await PosArrangedAsync(db, menu!.Id);
        if (body.Sections is { Length: > 0 } sids)
        {
            var rows = await db.MenuSections.Where(s => s.MenuId == menu.Id).ToDictionaryAsync(s => s.Id);
            for (var i = 0; i < sids.Length; i++) if (rows.TryGetValue(sids[i], out var s) && !(arranged && s.PosKey is not null)) s.SortOrder = i;
        }
        if (body.Items is { Length: > 0 } iids)
        {
            var rows = await db.MenuItems.Where(x => x.MenuId == menu.Id).Select(x => new { Item = x, Linked = x.Branches.Any() }).ToDictionaryAsync(x => x.Item.Id);
            for (var i = 0; i < iids.Length; i++) if (rows.TryGetValue(iids[i], out var x) && !(arranged && x.Linked)) x.Item.SortOrder = i;
        }
        if (body.Types is { Length: > 0 } tids)
        {
            var rows = await db.MenuOrderTypes.Where(t => t.MenuId == menu.Id).ToDictionaryAsync(t => t.Id);
            for (var i = 0; i < tids.Length; i++) if (rows.TryGetValue(tids[i], out var t)) t.SortOrder = i;
        }
        return await Saved(db, menu, time, editor!, http);
    }

    // ---- the shop PC ----

    private static async Task<IResult> PushCatalog(MenuCatalogPush? body, HttpContext http, ControlDbContext db, TimeProvider time, MenuCatalogMerge merge)
    {
        var (device, failure) = await SyncEndpoints.AuthenticateAsync(http, db, time);
        if (failure is not null) return failure;
        if (!SubscriptionRules.AllowsSync(device!.State)) return SyncEndpoints.InactiveSubscription(device);

        var branch = await db.MenuBranches.Include(b => b.Menu).FirstOrDefaultAsync(b => b.SiteId == device.Site.Id && b.Menu.ClientId == device.Client.Id);
        if (branch is null) return Error(StatusCodes.Status409Conflict, "menu_not_linked", "No online menu is linked to this branch.");
        if (body?.Items is null || body.Items.Count > MenuCatalog.MaxItems || string.IsNullOrWhiteSpace(body.Hash) || body.Hash.Length > 64)
            return Error(StatusCodes.Status400BadRequest, SyncErrorCodes.BadBatch, "Bad product list.");

        await merge.ApplyAsync(branch.Menu, branch, body);
        return Results.NoContent();
    }
}
