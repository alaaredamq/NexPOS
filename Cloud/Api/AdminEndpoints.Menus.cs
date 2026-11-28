using Cloud.Data;
using Cloud.Services;
using Microsoft.EntityFrameworkCore;

namespace Cloud.Api;

/// <summary>
/// Every client's online menu, for the provider filling one in for a shop: the list, the Excel template and the items import.
/// Owners have none of this; the menu editor itself is <c>/menu/v1</c> (<see cref="MenuEndpoints"/>), opened with this API's token.
/// </summary>
public static partial class AdminEndpoints
{
    private static void MapMenus(RouteGroupBuilder g)
    {
        g.MapGet("/menus", ListMenus);
        g.MapGet("/clients/{id:int}/menu/template", MenuTemplate);
        g.MapPost("/clients/{id:int}/menu/import", ImportMenuItems);
        g.MapPost("/clients/{id:int}/menu/import/rows", ImportMenuRows);
    }

    /// <summary>
    /// Every client with Online or a menu, menus first by when they last changed. <c>state</c>: none (no menu yet), draft (not
    /// published), published; <c>live</c> says whether the public page answers (Online time running and the client active).
    /// </summary>
    private static async Task<IResult> ListMenus(HttpContext http, ControlDbContext db, TimeProvider time)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var origin = PublicSite.OriginOf(http);
        var clients = await db.Clients.AsNoTracking().Include(c => c.Subscription).ToListAsync();
        var menus = await db.Menus.AsNoTracking().ToDictionaryAsync(m => m.ClientId);
        var ids = menus.Values.Select(m => m.Id).ToList();
        var items = (await db.MenuItems.Where(i => ids.Contains(i.MenuId))
                .GroupBy(i => i.MenuId)
                .Select(g => new { g.Key, Count = g.Count(), Hidden = g.Count(i => i.IsHidden), NoPicture = g.Count(i => !i.IsHidden && i.ImageFile == null) })
                .ToListAsync())
            .ToDictionary(x => x.Key);
        var sections = (await db.MenuSections.Where(s => ids.Contains(s.MenuId)).GroupBy(s => s.MenuId).Select(g => new { g.Key, Count = g.Count() }).ToListAsync())
            .ToDictionary(x => x.Key, x => x.Count);
        var fromPos = (await db.MenuBranches.Where(b => ids.Contains(b.MenuId)).Select(b => b.MenuId).Distinct().ToListAsync()).ToHashSet();

        var rows = clients
            .Where(c => c.HasOnline || menus.ContainsKey(c.Id))
            .Select(c =>
            {
                var m = menus.GetValueOrDefault(c.Id);
                var count = m is null ? null : items.GetValueOrDefault(m.Id);
                return new
                {
                    clientId = c.Id, clientName = c.Name, isActive = c.IsActive, type = c.HasPos ? (c.HasOnline ? "online" : "pos") : "menu",
                    live = SubscriptionRules.MenuIsLive(c, now),
                    state = m is null ? "none" : m.IsPublished ? "published" : "draft",
                    menu = m is null ? null : new
                    {
                        title = m.Title, slug = m.Slug, url = origin + "/m/" + m.Slug, previewUrl = origin + "/m/" + m.Slug + "?preview=" + m.PreviewKey,
                        sections = sections.GetValueOrDefault(m.Id), items = count?.Count ?? 0, hidden = count?.Hidden ?? 0, noPicture = count?.NoPicture ?? 0,
                        fromPos = fromPos.Contains(m.Id), currency = m.Currency,
                        updatedAtUtc = DateTime.SpecifyKind(m.UpdatedAt, DateTimeKind.Utc),
                    },
                    editorUrl = "/owner/menu.html?client=" + c.Id + "&from=console",
                };
            })
            .OrderBy(r => r.menu is null)
            .ThenByDescending(r => r.menu?.updatedAtUtc)
            .ThenBy(r => r.clientName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        return Json(rows);
    }

    /// <summary>The Excel template for this client's menu: its sections in a dropdown and a price column per order type.</summary>
    private static async Task<IResult> MenuTemplate(int id, ControlDbContext db, MenuSpreadsheet sheets)
    {
        var client = await db.Clients.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id);
        if (client is null) return NotFound("The client");
        var menu = await db.Menus.AsNoTracking().FirstOrDefaultAsync(m => m.ClientId == id);
        var file = await sheets.TemplateAsync(menu);
        var name = "menu-items-" + (menu?.Slug ?? MenuRules.SlugFrom(client.Name)) + ".xlsx";
        return Results.File(file, MenuSpreadsheet.ContentType, name);
    }

    /// <summary>
    /// Reads an .xlsx of dishes (the body, as is) for the client's menu. <c>apply=false</c> (the default) only checks it: the answer lists
    /// every row as read, with what would happen to it, for the console to review and edit; then the rows go to <see cref="ImportMenuRows"/>.
    /// <c>apply=true</c> saves the good rows straight away (and makes the menu if the client has none).
    /// </summary>
    private static async Task<IResult> ImportMenuItems(int id, bool? apply, HttpContext http, ControlDbContext db, MenuSpreadsheet sheets, TimeProvider time)
    {
        var client = await db.Clients.FirstOrDefaultAsync(c => c.Id == id);
        if (client is null) return NotFound("The client");
        if (http.Request.ContentLength > MenuSpreadsheet.MaxBytes) return Refused("bad_file", "The file is larger than 10 MB. Split it into smaller files.");
        return await ImportReplyAsync(id, http, db, time, () => sheets.ImportAsync(client, http.Request.Body, apply == true));
    }

    public sealed record ImportRowsRequest(List<MenuSpreadsheet.ImportRow>? Rows);

    /// <summary>
    /// The rows of a checked file, after the review (some edited, some removed), checked again with the same rules: <c>apply=false</c>
    /// to see the new outcome, <c>apply=true</c> once the provider confirmed, to save them.
    /// </summary>
    private static async Task<IResult> ImportMenuRows(int id, bool? apply, ImportRowsRequest body, HttpContext http, ControlDbContext db, MenuSpreadsheet sheets, TimeProvider time)
    {
        var client = await db.Clients.FirstOrDefaultAsync(c => c.Id == id);
        if (client is null) return NotFound("The client");
        return await ImportReplyAsync(id, http, db, time, () => sheets.ImportAsync(client, body.Rows ?? [], apply == true));
    }

    private static async Task<IResult> ImportReplyAsync(int id, HttpContext http, ControlDbContext db, TimeProvider time, Func<Task<MenuSpreadsheet.Result>> import)
    {
        MenuSpreadsheet.Result result;
        try { result = await import(); }
        catch (MenuSpreadsheet.FileRefused refused) { return Refused("bad_file", refused.Message); }

        if (result.Applied && (result.Added > 0 || result.Updated > 0))
        {
            // On the client's history, like every other change made from the portal.
            db.ClientEvents.Add(new ClientEvent { ClientId = id, At = time.GetUtcNow().UtcDateTime, Text = $"Menu items imported from Excel by {Who(http)}: {result.Added} added, {result.Updated} updated." });
            await db.SaveChangesAsync();
        }
        return Json(new
        {
            applied = result.Applied, rows = result.Rows, added = result.Added, updated = result.Updated, unchanged = result.Unchanged,
            skipped = result.Skipped, newSections = result.NewSections,
            issues = result.Issues.Select(i => new { row = i.Row, message = i.Message, skipped = i.Skipped }), moreIssues = result.MoreIssues,
            currency = result.Currency,
            sections = result.Sections,
            orderTypes = result.OrderTypes.Select(t => new { id = t.Id, name = t.Name }),
            items = result.Items.Select(r => new
            {
                row = r.Input.Row, section = r.Input.Section ?? "", name = r.Input.Name ?? "", description = r.Input.Description ?? "",
                price = r.Input.Price ?? "", typePrices = r.Input.TypePrices ?? [], hidden = r.Input.Hidden ?? "", soldOut = r.Input.SoldOut ?? "",
                status = r.Status, message = r.Message, fromPos = r.FromPos, priceText = r.PriceText,
                typePriceTexts = r.TypePriceTexts.Select(t => new { type = t.Type, price = t.Price }),
            }),
        });
    }
}
