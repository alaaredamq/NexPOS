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
    private static async Task<IResult> PublicItems(string slug, HttpContext http, ControlDbContext db, int? section, int? skip, int? take, string? q, string? preview)
    {
        var menu = await db.Menus.AsNoTracking().Include(m => m.Client).FirstOrDefaultAsync(m => m.Slug == slug.ToLower());
        if (menu is null || !menu.Client.IsActive || (!menu.IsPublished && (string.IsNullOrEmpty(preview) || preview != menu.PreviewKey)))
            return Results.NotFound();

        var sectionIds = db.MenuSections.Where(s => s.MenuId == menu.Id).Select(s => (int?)s.Id);
        var hiddenIds = db.MenuSections.Where(s => s.MenuId == menu.Id && s.IsHidden).Select(s => (int?)s.Id);
        var items = db.MenuItems.AsNoTracking().Where(i => i.MenuId == menu.Id && !i.IsHidden && (i.SectionId == null || !hiddenIds.Contains(i.SectionId)));
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

        var rows = await items.OrderBy(i => i.SortOrder).ThenBy(i => i.Id).Skip(Math.Max(0, skip ?? 0)).Take(size + 1).ToListAsync();
        http.Response.Headers.CacheControl = menu.IsPublished ? "public, max-age=60" : "no-store";
        return Results.Json(new
        {
            items = rows.Take(size).Select(i => new
            {
                name = i.Name,
                description = i.Description ?? i.PosDescription,
                priceText = MenuRules.Price(i.Price, i.Currency ?? menu.Currency),
                imageUrl = i.ImageFile is null ? null : "/m/img/" + i.ImageFile,
                soldOut = i.IsSoldOut,
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
        return new
        {
            clientName = editor.Client.Name,
            admin = editor.IsAdmin,
            sites,
            menu = new
            {
                slug = menu.Slug, url = origin + "/m/" + menu.Slug, previewUrl = origin + "/m/" + menu.Slug + "?preview=" + menu.PreviewKey, title = menu.Title, tagline = menu.Tagline, note = menu.Note,
                language = menu.Language, currency = menu.Currency, logoUrl = menu.LogoFile is null ? null : "/m/img/" + menu.LogoFile,
                isPublished = menu.IsPublished, linkedSiteId = menu.LinkedSiteId, posArranged = menu.LinkedSiteId != null && menu.PosArranged,
                catalogAtUtc = menu.CatalogAt is null ? (DateTime?)null : DateTime.SpecifyKind(menu.CatalogAt.Value, DateTimeKind.Utc),
            },
            sections,
            looseCount,
        };
    }

    public const int PageSize = 40;

    private static object ItemView(MenuItem i, Menu menu) => new
    {
        id = i.Id, sectionId = i.SectionId, name = i.Name, description = i.Description, posDescription = i.PosDescription,
        price = i.Price, currency = i.Currency, priceText = MenuRules.Price(i.Price, i.Currency ?? menu.Currency),
        imageUrl = i.ImageFile is null ? null : "/m/img/" + i.ImageFile,
        isHidden = i.IsHidden, isSoldOut = i.IsSoldOut, linked = i.PosProductId != null,
    };

    /// <summary>One section's items, a page at a time. <c>section=0</c> = items in no section.</summary>
    private static async Task<IResult> ListItems(HttpContext http, ControlDbContext db, TimeProvider time, int? client, int section, int? skip, int? take)
    {
        var (_, menu, failure) = await MenuAsync(http, db, time, client);
        if (failure is not null) return failure;
        var size = Math.Clamp(take ?? PageSize, 1, 100);
        var query = db.MenuItems.AsNoTracking().Where(i => i.MenuId == menu!.Id);
        if (section == 0)
        {
            var sectionIds = db.MenuSections.Where(s => s.MenuId == menu!.Id).Select(s => (int?)s.Id);
            query = query.Where(i => i.SectionId == null || !sectionIds.Contains(i.SectionId));
        }
        else query = query.Where(i => i.SectionId == section);
        var rows = await query.OrderBy(i => i.SortOrder).ThenBy(i => i.Id).Skip(Math.Max(0, skip ?? 0)).Take(size + 1).ToListAsync();
        return Results.Json(new { items = rows.Take(size).Select(i => ItemView(i, menu!)), hasMore = rows.Count > size }, SyncWire.Json);
    }

    public sealed record MoveRequest(int Direction);

    /// <summary>Swaps an item with its neighbour in its section. Works on a partly loaded list, unlike sending a whole new order.</summary>
    private static async Task<IResult> MoveItem(int id, MoveRequest body, HttpContext http, ControlDbContext db, TimeProvider time, int? client)
    {
        var (editor, menu, failure) = await MenuAsync(http, db, time, client);
        if (failure is not null) return failure;
        var item = await db.MenuItems.FirstOrDefaultAsync(i => i.Id == id && i.MenuId == menu!.Id);
        if (item is null) return Error(StatusCodes.Status404NotFound, "not_found", "No such item.");
        if (menu!.PosArranged && item.PosProductId is not null)
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
        string? LinkedSiteId);

    private static async Task<IResult> SaveSettings(SettingsRequest body, HttpContext http, ControlDbContext db, TimeProvider time,
        MenuCatalogMerge merge, int? client)
    {
        var (editor, menu, failure) = await MenuAsync(http, db, time, client);
        if (failure is not null) return failure;

        if (body.Title is not null) menu!.Title = MenuRules.Clean(body.Title, 120) ?? menu.Title;
        if (body.Tagline is not null) menu!.Tagline = MenuRules.Clean(body.Tagline, 200);
        if (body.Note is not null) menu!.Note = MenuRules.Clean(body.Note, 1000);
        if (body.Language is "en" or "ar") menu!.Language = body.Language;
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

        if (body.LinkedSiteId is not null)
        {
            Guid? wanted = body.LinkedSiteId.Length == 0 ? null : Guid.TryParse(body.LinkedSiteId, out var g) ? g : Guid.Empty;
            if (wanted == Guid.Empty || (wanted is not null && !await db.Sites.AnyAsync(s => s.Id == wanted && s.ClientId == editor!.Client.Id && !s.IsDisabled)))
                return Error(StatusCodes.Status400BadRequest, "bad_site", "That branch is not one of this shop's.");
            if (wanted != menu!.LinkedSiteId)
            {
                if (menu.LinkedSiteId is not null) await merge.DetachAsync(menu);
                menu.LinkedSiteId = wanted;
                menu.CatalogHash = null; // the shop PC sends its list at its next check-in
            }
        }

        return await Saved(db, menu!, time, editor!, http);
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
        string? Name, string? Description, decimal? Price, string? Currency, int? SectionId, bool? IsHidden, bool? IsSoldOut);

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

        // The POS owns a linked item's name, price and section; the owner owns the rest.
        var linked = item.PosProductId is not null;
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
        return await Saved(db, menu!, time, editor!, http);
    }

    private static async Task<IResult> DeleteItem(int id, HttpContext http, ControlDbContext db, TimeProvider time, MenuImages images, int? client)
    {
        var (editor, menu, failure) = await MenuAsync(http, db, time, client);
        if (failure is not null) return failure;
        var item = await db.MenuItems.FirstOrDefaultAsync(i => i.Id == id && i.MenuId == menu!.Id);
        if (item is null) return Error(StatusCodes.Status404NotFound, "not_found", "No such item.");
        if (item.PosProductId is not null)
            return Error(StatusCodes.Status409Conflict, "linked", "This item comes from the POS. Hide it instead.");
        images.Delete(item.ImageFile);
        db.MenuItems.Remove(item);
        return await Saved(db, menu!, time, editor!, http);
    }

    public sealed record OrderRequest(int[]? Sections, int[]? Items);

    /// <summary>The ids in their new order. Ids that are not this menu's are ignored, and so are POS ones while the POS arranges the menu.</summary>
    private static async Task<IResult> Reorder(OrderRequest body, HttpContext http, ControlDbContext db, TimeProvider time, int? client)
    {
        var (editor, menu, failure) = await MenuAsync(http, db, time, client);
        if (failure is not null) return failure;
        if (body.Sections is { Length: > 0 } sids)
        {
            var rows = await db.MenuSections.Where(s => s.MenuId == menu!.Id).ToDictionaryAsync(s => s.Id);
            for (var i = 0; i < sids.Length; i++) if (rows.TryGetValue(sids[i], out var s) && !(menu!.PosArranged && s.PosKey is not null)) s.SortOrder = i;
        }
        if (body.Items is { Length: > 0 } iids)
        {
            var rows = await db.MenuItems.Where(x => x.MenuId == menu!.Id).ToDictionaryAsync(x => x.Id);
            for (var i = 0; i < iids.Length; i++) if (rows.TryGetValue(iids[i], out var x) && !(menu!.PosArranged && x.PosProductId is not null)) x.SortOrder = i;
        }
        return await Saved(db, menu!, time, editor!, http);
    }

    // ---- the shop PC ----

    private static async Task<IResult> PushCatalog(MenuCatalogPush? body, HttpContext http, ControlDbContext db, TimeProvider time, MenuCatalogMerge merge)
    {
        var (device, failure) = await SyncEndpoints.AuthenticateAsync(http, db, time);
        if (failure is not null) return failure;
        if (!SubscriptionRules.AllowsSync(device!.State)) return SyncEndpoints.InactiveSubscription(device);

        var menu = await db.Menus.FirstOrDefaultAsync(m => m.LinkedSiteId == device.Site.Id && m.ClientId == device.Client.Id);
        if (menu is null) return Error(StatusCodes.Status409Conflict, "menu_not_linked", "No online menu is linked to this branch.");
        if (body?.Items is null || body.Items.Count > MenuCatalog.MaxItems || string.IsNullOrWhiteSpace(body.Hash) || body.Hash.Length > 64)
            return Error(StatusCodes.Status400BadRequest, SyncErrorCodes.BadBatch, "Bad product list.");

        await merge.ApplyAsync(menu, body);
        return Results.NoContent();
    }
}
