using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ClosedXML.Excel;
using Cloud.Data;
using Cloud.Services;
using Cloud.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Cloud.Tests;

/// <summary>The provider fills a shop's online menu for them: every menu listed, an Excel template, and the items import (admin API only).</summary>
public class MenuImportTests
{
    private const string Api = "/api/admin/v1";

    private static async Task<HttpClient> AdminAsync(CloudFactory cloud)
    {
        var c = cloud.CreateClient(new() { AllowAutoRedirect = false });
        var login = await c.PostAsJsonAsync($"{Api}/auth/login", new { username = CloudFactory.AdminUser, password = CloudFactory.AdminPassword, deviceName = "Console (web)" });
        var token = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return c;
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage r, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var body = await r.Content.ReadAsStringAsync();
        Assert.True(r.StatusCode == expected, $"Expected {expected}, got {r.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    /// <summary>An .xlsx with an Items sheet: the header, then the rows.</summary>
    private static ByteArrayContent Sheet(string[] header, params object?[][] rows)
    {
        using var book = new XLWorkbook();
        var s = book.AddWorksheet("Items");
        for (var c = 0; c < header.Length; c++) s.Cell(1, c + 1).Value = header[c];
        for (var r = 0; r < rows.Length; r++)
        for (var c = 0; c < rows[r].Length; c++)
        {
            switch (rows[r][c])
            {
                case string t: s.Cell(r + 2, c + 1).Value = t; break;
                case double d: s.Cell(r + 2, c + 1).Value = d; break;
                case int n: s.Cell(r + 2, c + 1).Value = n; break;
            }
        }
        using var ms = new MemoryStream();
        book.SaveAs(ms);
        var content = new ByteArrayContent(ms.ToArray());
        content.Headers.ContentType = new MediaTypeHeaderValue(MenuSpreadsheet.ContentType);
        return content;
    }

    private static readonly string[] Header = ["Section", "Name", "Description", "Price", "Hidden", "Sold out"];

    [Fact]
    public async Task A_sheet_is_checked_first_then_imported_and_importing_again_changes_nothing()
    {
        using var cloud = new CloudFactory();
        var seeded = await cloud.SeedSiteAsync(clientName: "Abu Ali Grill");
        var admin = await AdminAsync(cloud);

        object?[][] rows =
        [
            ["Breakfast", "Manakish zaatar", "Thyme and olive oil", 2.5, "", ""],
            ["Breakfast", "Labneh sandwich", "", "$3.00", "no", "yes"],
            ["مشروبات", "Fresh orange juice", "", "450,000", "", ""],
            ["", "Water", "", 1, "", ""],
            ["Breakfast", "manakish ZAATAR", "", 2.5, "", ""],   // the same dish again
            ["Breakfast", "Foul", "", "", "", ""],               // a new dish needs a price
            ["Breakfast", "Fatteh", "", "cheap", "", ""],
            ["", "", "", "", "", ""],                            // blank rows are ignored
        ];

        // The check saves nothing, and there is no menu yet.
        var check = await JsonAsync(await admin.PostAsync($"{Api}/clients/{seeded.ClientId}/menu/import", Sheet(Header, rows)));
        Assert.False(check.GetProperty("applied").GetBoolean());
        Assert.Equal(7, check.GetProperty("rows").GetInt32());
        Assert.Equal(4, check.GetProperty("added").GetInt32());
        Assert.Equal(3, check.GetProperty("skipped").GetInt32());
        Assert.Equal(["Breakfast", "مشروبات"], check.GetProperty("newSections").EnumerateArray().Select(x => x.GetString()).ToArray());
        var issues = check.GetProperty("issues").EnumerateArray().ToList();
        Assert.Equal([6, 7, 8], issues.Select(i => i.GetProperty("row").GetInt32()).ToArray());
        Assert.Contains("row 2", issues[0].GetProperty("message").GetString());
        Assert.Contains("Price is required", issues[1].GetProperty("message").GetString());
        Assert.Contains("\"cheap\" is not a number", issues[2].GetProperty("message").GetString());
        Assert.False(await cloud.WithControlDbAsync(db => db.Menus.AnyAsync()));

        // Applied: the menu is made, with its sections in file order and the dishes in them.
        var done = await JsonAsync(await admin.PostAsync($"{Api}/clients/{seeded.ClientId}/menu/import?apply=true", Sheet(Header, rows)));
        Assert.True(done.GetProperty("applied").GetBoolean());
        Assert.Equal(4, done.GetProperty("added").GetInt32());
        var (sections, items) = await cloud.WithControlDbAsync(async db =>
        {
            var menu = await db.Menus.SingleAsync();
            return (await db.MenuSections.Where(s => s.MenuId == menu.Id).OrderBy(s => s.SortOrder).ToListAsync(),
                await db.MenuItems.Where(i => i.MenuId == menu.Id).OrderBy(i => i.SortOrder).ToListAsync());
        });
        Assert.Equal(["Breakfast", "مشروبات"], sections.Select(s => s.Name).ToArray());
        Assert.Equal(["Manakish zaatar", "Labneh sandwich", "Fresh orange juice", "Water"], items.Select(i => i.Name).ToArray());
        Assert.Equal(sections[0].Id, items[0].SectionId);
        Assert.Equal(sections[1].Id, items[2].SectionId);
        Assert.Null(items[3].SectionId);
        Assert.Equal(3.00m, items[1].Price);
        Assert.True(items[1].IsSoldOut);
        Assert.Equal(450_000m, items[2].Price);
        Assert.Equal("Thyme and olive oil", items[0].Description);
        Assert.False(items[0].IsHidden);
        Assert.Contains(await cloud.WithControlDbAsync(db => db.ClientEvents.Select(e => e.Text).ToListAsync()), t => t.Contains("4 added"));

        // The same file again: nothing new, nothing changed.
        var again = await JsonAsync(await admin.PostAsync($"{Api}/clients/{seeded.ClientId}/menu/import?apply=true", Sheet(Header, rows)));
        Assert.Equal(0, again.GetProperty("added").GetInt32());
        Assert.Equal(0, again.GetProperty("updated").GetInt32());
        Assert.Equal(4, again.GetProperty("unchanged").GetInt32());
        Assert.Equal(0, again.GetProperty("newSections").GetArrayLength());
        Assert.Equal(4, await cloud.WithControlDbAsync(db => db.MenuItems.CountAsync()));
    }

    [Fact]
    public async Task The_rows_are_reviewed_edited_and_only_saved_once_confirmed()
    {
        using var cloud = new CloudFactory();
        var seeded = await cloud.SeedSiteAsync(clientName: "Review Shop");
        var admin = await AdminAsync(cloud);

        // The check lists every row as typed, with what would happen to it.
        var check = await JsonAsync(await admin.PostAsync($"{Api}/clients/{seeded.ClientId}/menu/import", Sheet(Header,
            ["Mains", "Burger", "", 8, "", ""],
            ["Mains", "Fries", "", "cheap", "", ""],
            ["Mains", "burger", "", 8, "", ""],
            ["Drinks", "Cola", "", "", "", "maybe"])));
        var items = check.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(["add", "skip", "skip", "skip"], items.Select(i => i.GetProperty("status").GetString()).ToArray());
        Assert.Equal("cheap", items[1].GetProperty("price").GetString());
        Assert.Equal("maybe", items[3].GetProperty("soldOut").GetString());
        Assert.Equal("$8", items[0].GetProperty("priceText").GetString());
        Assert.Equal(0, await cloud.WithControlDbAsync(db => db.MenuItems.CountAsync()));

        // Edited in the review: Fries gets a price, the repeated burger is removed, Cola gets a price and a proper Yes.
        object[] Edited() =>
        [
            new { row = 2, section = "Mains", name = "Burger", price = "8" },
            new { row = 3, section = "Mains", name = "Fries", price = "2.5" },
            new { row = 5, section = "Drinks", name = "Cola", price = "1", soldOut = "yes" },
        ];
        var again = await JsonAsync(await admin.PostAsJsonAsync($"{Api}/clients/{seeded.ClientId}/menu/import/rows", new { rows = Edited() }));
        Assert.False(again.GetProperty("applied").GetBoolean());
        Assert.Equal(3, again.GetProperty("added").GetInt32());
        Assert.Equal(0, again.GetProperty("skipped").GetInt32());
        Assert.Equal(0, await cloud.WithControlDbAsync(db => db.MenuItems.CountAsync()));

        // Confirmed.
        var done = await JsonAsync(await admin.PostAsJsonAsync($"{Api}/clients/{seeded.ClientId}/menu/import/rows?apply=true", new { rows = Edited() }));
        Assert.True(done.GetProperty("applied").GetBoolean());
        var saved = await cloud.WithControlDbAsync(db => db.MenuItems.OrderBy(i => i.SortOrder).ToListAsync());
        Assert.Equal(["Burger", "Fries", "Cola"], saved.Select(i => i.Name).ToArray());
        Assert.Equal(2.5m, saved[1].Price);
        Assert.True(saved[2].IsSoldOut);
        Assert.Equal(2, await cloud.WithControlDbAsync(db => db.MenuSections.CountAsync()));

        // No rows at all is refused, and the owner's token never reaches this.
        var empty = await JsonAsync(await admin.PostAsJsonAsync($"{Api}/clients/{seeded.ClientId}/menu/import/rows?apply=true", new { rows = Array.Empty<object>() }), HttpStatusCode.BadRequest);
        Assert.Equal("bad_file", empty.GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await cloud.CreateClient().PostAsJsonAsync($"{Api}/clients/{seeded.ClientId}/menu/import/rows?apply=true", new { rows = Edited() })).StatusCode);
    }

    [Fact]
    public async Task A_dish_already_on_the_menu_is_updated_but_a_POS_dish_keeps_its_price()
    {
        using var cloud = new CloudFactory();
        var seeded = await cloud.SeedSiteAsync(clientName: "Corner Cafe");
        var (sectionId, typeId) = await cloud.WithControlDbAsync(async db =>
        {
            var now = DateTime.UtcNow;
            var menu = new Menu { ClientId = seeded.ClientId, Title = "Corner Cafe", Slug = "corner-cafe", PreviewKey = "k", CreatedAt = now, UpdatedAt = now };
            db.Menus.Add(menu);
            await db.SaveChangesAsync();
            var section = new MenuSection { MenuId = menu.Id, Name = "Coffee", SortOrder = 0 };
            var type = new MenuOrderType { MenuId = menu.Id, Name = "Delivery", SortOrder = 0 };
            db.AddRange(section, type);
            await db.SaveChangesAsync();
            db.MenuItems.Add(new MenuItem { MenuId = menu.Id, SectionId = section.Id, Name = "Espresso", Price = 2, SortOrder = 0 });
            var pos = new MenuItem { MenuId = menu.Id, SectionId = section.Id, Name = "Latte", Price = 3, SortOrder = 1 };
            pos.Branches.Add(new MenuItemBranch { SiteId = seeded.SiteId, PosProductId = 7, Price = 3 });
            db.MenuItems.Add(pos);
            await db.SaveChangesAsync();
            return (section.Id, type.Id);
        });
        var admin = await AdminAsync(cloud);

        // The template knows this menu: its order type has a price column, its section is in the dropdown.
        var template = await admin.GetAsync($"{Api}/clients/{seeded.ClientId}/menu/template");
        Assert.Equal(HttpStatusCode.OK, template.StatusCode);
        Assert.Equal(MenuSpreadsheet.ContentType, template.Content.Headers.ContentType?.MediaType);
        Assert.Equal("menu-items-corner-cafe.xlsx", template.Content.Headers.ContentDisposition?.FileNameStar ?? template.Content.Headers.ContentDisposition?.FileName);
        using (var book = new XLWorkbook(new MemoryStream(await template.Content.ReadAsByteArrayAsync())))
        {
            var items = book.Worksheet("Items");
            Assert.Equal(["Section", "Name", "Description", "Price", "Price: Delivery", "Hidden", "Sold out"],
                Enumerable.Range(1, 7).Select(c => items.Cell(1, c).GetString()).ToArray());
            Assert.Equal("Coffee", book.Worksheet("Lists").Cell(2, 1).GetString());
            Assert.True(book.Worksheets.Contains("Guide"));
        }

        string[] header = ["Section", "Name", "Description", "Price", "Price: delivery", "Hidden"];
        var result = await JsonAsync(await admin.PostAsync($"{Api}/clients/{seeded.ClientId}/menu/import?apply=true", Sheet(header,
            ["coffee", "espresso", "Double shot", 2.25, 2.75, "Yes"],
            ["Coffee", "Latte", "Silky", 9, "", ""])));
        Assert.Equal(2, result.GetProperty("updated").GetInt32());
        Assert.Equal(0, result.GetProperty("added").GetInt32());
        var note = Assert.Single(result.GetProperty("issues").EnumerateArray());
        Assert.False(note.GetProperty("skipped").GetBoolean());
        Assert.Contains("comes from the POS", note.GetProperty("message").GetString());

        var dishes = await cloud.WithControlDbAsync(db => db.MenuItems.Include(i => i.TypePrices).OrderBy(i => i.SortOrder).ToListAsync());
        Assert.Equal(2.25m, dishes[0].Price);
        Assert.Equal("Double shot", dishes[0].Description);
        Assert.True(dishes[0].IsHidden);
        Assert.Equal(2.75m, Assert.Single(dishes[0].TypePrices).Price);
        Assert.Equal(typeId, dishes[0].TypePrices[0].OrderTypeId);
        Assert.Equal(sectionId, dishes[0].SectionId);
        Assert.Equal(3m, dishes[1].Price);
        Assert.Equal("Silky", dishes[1].Description);

        // A price column for an order type the menu does not have refuses the whole file.
        var unknown = await JsonAsync(await admin.PostAsync($"{Api}/clients/{seeded.ClientId}/menu/import",
            Sheet(["Name", "Price", "Price: Takeaway"], ["Tea", 1, 1])), HttpStatusCode.BadRequest);
        Assert.Equal("bad_file", unknown.GetProperty("code").GetString());
        Assert.Contains("Takeaway", unknown.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Every_menu_is_listed_for_the_provider_and_nobody_else_reaches_any_of_it()
    {
        using var cloud = new CloudFactory();
        var withMenu = await cloud.SeedSiteAsync(clientName: "Has A Menu");
        var without = await cloud.SeedSiteAsync(clientName: "No Menu Yet");
        await cloud.WithControlDbAsync(async db =>
        {
            var now = DateTime.UtcNow;
            db.Menus.Add(new Menu { ClientId = withMenu.ClientId, Title = "The Menu", Slug = "the-menu", PreviewKey = "abc", IsPublished = true, CreatedAt = now, UpdatedAt = now });
            return await db.SaveChangesAsync();
        });
        var admin = await AdminAsync(cloud);

        var list = (await JsonAsync(await admin.GetAsync($"{Api}/menus"))).EnumerateArray().ToList();
        Assert.Equal(["Has A Menu", "No Menu Yet"], list.Select(r => r.GetProperty("clientName").GetString()).ToArray());
        Assert.Equal("published", list[0].GetProperty("state").GetString());
        Assert.EndsWith("/m/the-menu?preview=abc", list[0].GetProperty("menu").GetProperty("previewUrl").GetString());
        Assert.Equal($"/owner/menu.html?client={withMenu.ClientId}&from=console", list[0].GetProperty("editorUrl").GetString());
        Assert.Equal("none", list[1].GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, list[1].GetProperty("menu").ValueKind);

        // Not a spreadsheet at all.
        var junk = new ByteArrayContent("Name,Price\nTea,1"u8.ToArray());
        var refused = await JsonAsync(await admin.PostAsync($"{Api}/clients/{without.ClientId}/menu/import", junk), HttpStatusCode.BadRequest);
        Assert.Contains(".xlsx", refused.GetProperty("message").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"{Api}/clients/99999/menu/template")).StatusCode);

        // Without the provider's token: nothing. (A shop owner's sign-in is a different token, which this API never accepts.)
        var stranger = cloud.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await stranger.GetAsync($"{Api}/menus")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await stranger.GetAsync($"{Api}/clients/{withMenu.ClientId}/menu/template")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await stranger.PostAsync($"{Api}/clients/{withMenu.ClientId}/menu/import?apply=true", Sheet(Header, ["", "Tea", "", 1, "", ""]))).StatusCode);
        stranger.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "nxo_not-an-admin");
        Assert.Equal(HttpStatusCode.Unauthorized, (await stranger.GetAsync($"{Api}/menus")).StatusCode);
        Assert.Equal(0, await cloud.WithControlDbAsync(db => db.MenuItems.CountAsync()));
    }
}
