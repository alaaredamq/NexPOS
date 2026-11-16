using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Cloud.Data;
using Cloud.Services;
using Cloud.Tests.Support;
using Core.Entities.Products;
using Infrastructure.Repositories;
using Sync;
using Xunit;

namespace Cloud.Tests;

/// <summary>
/// The till's "Online Menu" screen: sections of its own (separate from product categories) decide what the linked online menu shows and
/// in what order. With none, the menu follows the categories as before.
/// </summary>
public class OnlineMenuSectionsTests : IClassFixture<CloudFactory>
{
    private const string Password = "pass-1234-ok";
    private readonly CloudFactory _cloud;

    public OnlineMenuSectionsTests(CloudFactory cloud) => _cloud = cloud;

    /// <summary>A till with Zinger and Fries in one category and Water in another, all active.</summary>
    private static async Task<(TillDb Till, int Food, int Drinks, int Zinger, int Fries, int Water)> TillAsync()
    {
        var till = new TillDb();
        var zinger = await till.AddProductAsync("Zinger", "1");
        var fries = await till.AddProductAsync("Fries", "2");
        var water = await till.AddProductAsync("Water", "3");
        int food, drinks;
        await using (var db = till.CreateContext())
        {
            food = (await db.Products.FindAsync(zinger))!.CategoryId;
            var category = new ProductCategory("Drinks");
            db.ProductCategories.Add(category);
            await db.SaveChangesAsync();
            drinks = category.Id;
        }
        SqlHelper.Exec(till.Path, $"UPDATE Products SET IsActive = 1, SellingPrice = '5' WHERE Id IN ({zinger}, {fries}, {water})");
        SqlHelper.Exec(till.Path, $"UPDATE Products SET CategoryId = {drinks} WHERE Id = {water}");
        return (till, food, drinks, zinger, fries, water);
    }

    [Fact]
    public async Task With_no_sections_the_list_follows_the_categories_and_the_owner_orders_it()
    {
        var (till, food, drinks, _, _, _) = await TillAsync();
        using var tillDb = till;

        var catalog = MenuCatalog.Build(till.ConnectionString);

        Assert.False(catalog.Arranged);
        Assert.Equal(3, catalog.Items.Count);
        Assert.Equal(new[] { $"c{food}", $"c{drinks}" }, catalog.Items.Select(i => i.SectionKey).Distinct().OrderBy(k => k));
    }

    [Fact]
    public async Task Sections_show_exactly_what_they_hold_and_a_product_picked_on_its_own_beats_its_category()
    {
        var (till, food, _, zinger, fries, _) = await TillAsync();
        using var tillDb = till;
        await using (var db = till.CreateContext())
        {
            var repo = new OnlineMenuRepository(db);
            var specials = await repo.AddSectionAsync("Today's specials");
            var burgers = await repo.AddSectionAsync("Burgers");
            await repo.PlaceAsync(burgers.Id, [], [food]);   // the whole Food category: Zinger and Fries...
            await repo.PlaceAsync(specials.Id, [fries], []); // ...but Fries is picked for the specials, so it shows there
        }

        var catalog = MenuCatalog.Build(till.ConnectionString);

        Assert.True(catalog.Arranged);
        Assert.Equal(new[] { ("Today's specials", "Fries"), ("Burgers", "Zinger") }, catalog.Items.Select(i => (i.SectionName!, i.Name)));
        Assert.DoesNotContain(catalog.Items, i => i.Name == "Water"); // in no section, so not on the menu
        Assert.True(catalog.Items[0].SectionOrder < catalog.Items[1].SectionOrder);
    }

    [Fact]
    public async Task Picking_a_product_or_category_again_moves_it_instead_of_showing_it_twice()
    {
        var (till, food, _, zinger, _, _) = await TillAsync();
        using var tillDb = till;
        await using var db = till.CreateContext();
        var repo = new OnlineMenuRepository(db);
        var a = await repo.AddSectionAsync("A");
        var b = await repo.AddSectionAsync("B");

        await repo.PlaceAsync(a.Id, [zinger], [food]);
        await repo.PlaceAsync(b.Id, [zinger], [food]);
        await repo.PlaceAsync(b.Id, [zinger], []); // already there: nothing doubles

        var placements = await repo.GetPlacementsAsync();
        Assert.DoesNotContain(placements, p => p.SectionId == a.Id);
        Assert.Equal(2, placements.Count(p => p.SectionId == b.Id));

        // Deleting a section only takes its picks off the menu; the products stay.
        Assert.True(await repo.DeleteSectionAsync(b.Id));
        Assert.Empty(db.OnlineMenuEntries);
        Assert.NotNull(await db.Products.FindAsync(zinger));
    }

    [Fact]
    public async Task A_section_is_read_a_page_at_a_time_and_a_pick_moves_one_step()
    {
        var (till, _, _, zinger, fries, water) = await TillAsync();
        using var tillDb = till;
        await using var db = till.CreateContext();
        var repo = new OnlineMenuRepository(db);
        var s = await repo.AddSectionAsync("All");
        await repo.PlaceAsync(s.Id, [zinger, fries, water], []);

        var (first, more) = await repo.GetEntriesAsync(s.Id, 0, 2);
        Assert.True(more);
        Assert.Equal(new[] { "Zinger", "Fries" }, first.Select(e => e.Product!.Name));
        var (rest, noMore) = await repo.GetEntriesAsync(s.Id, 2, 2);
        Assert.False(noMore);
        Assert.Equal("Water", rest.Single().Product!.Name);

        Assert.True(await repo.MoveEntryAsync(rest.Single().Id, -1));
        var (all, _) = await repo.GetEntriesAsync(s.Id, 0, 10);
        Assert.Equal(new[] { "Zinger", "Water", "Fries" }, all.Select(e => e.Product!.Name));
        Assert.True(await repo.MoveEntryAsync(all[0].Id, -1)); // already first: nothing happens
        Assert.False(await repo.MoveEntryAsync(999_999, 1));
    }

    [Fact]
    public async Task The_cloud_menu_follows_the_tills_sections_and_their_order_and_falls_back_to_categories_without_them()
    {
        var (till, _, _, zinger, fries, _) = await TillAsync();
        using var tillDb = till;
        int specials, burgers;
        await using (var db = till.CreateContext())
        {
            var repo = new OnlineMenuRepository(db);
            specials = (await repo.AddSectionAsync("Specials")).Id;
            burgers = (await repo.AddSectionAsync("Burgers")).Id;
            await repo.PlaceAsync(specials, [fries], []);
            await repo.PlaceAsync(burgers, [zinger], []);
        }

        var site = await _cloud.SeedSiteAsync(1, "sectionshop");
        var owner = await OwnerAsync(site, "sectionshop");
        await Ok(owner.PostAsJsonAsync("/menu/v1/editor", new { title = "Section Shop" }));
        await Ok(owner.PutAsJsonAsync("/menu/v1/settings", new { linkedSiteId = site.SiteId.ToString(), isPublished = true }));
        var device = _cloud.DeviceClient(site.Token);
        Assert.NotNull(await MenuCatalog.PublishIfChangedAsync(device, till.ConnectionString, null, default));

        var state = await Ok(owner.GetAsync("/menu/v1/editor"));
        Assert.True(state.GetProperty("menu").GetProperty("posArranged").GetBoolean());
        Assert.Equal(new[] { "Specials", "Burgers" }, Names(state, "sections"));
        Assert.Equal(new[] { "Fries", "Zinger" }, (await ItemsOf(owner)).Select(i => i.GetProperty("name").GetString()));

        // The editor cannot reorder what the POS arranges.
        var ids = state.GetProperty("sections").EnumerateArray().Select(s => s.GetProperty("id").GetInt32()).Reverse().ToArray();
        state = await Ok(owner.PutAsJsonAsync("/menu/v1/order", new { sections = ids }));
        Assert.Equal(new[] { "Specials", "Burgers" }, Names(state, "sections"));

        // Reordered at the till: the menu follows at the next send.
        await using (var db = till.CreateContext())
            await new OnlineMenuRepository(db).ReorderSectionsAsync([burgers, specials]);
        var hash = (await device.GetFromJsonAsync<SyncPing>("/api/sync/ping", SyncWire.Json))!.MenuCatalogHash;
        Assert.NotEqual(hash, await MenuCatalog.PublishIfChangedAsync(device, till.ConnectionString, hash, default));
        state = await Ok(owner.GetAsync("/menu/v1/editor"));
        Assert.Equal(new[] { "Burgers", "Specials" }, Names(state, "sections"));

        // No sections left at the till: everything active shows again, by category, and the owner orders it.
        SqlHelper.Exec(till.Path, "DELETE FROM OnlineMenuEntries; DELETE FROM OnlineMenuSections;");
        await MenuCatalog.PublishIfChangedAsync(device, till.ConnectionString, null, default);
        state = await Ok(owner.GetAsync("/menu/v1/editor"));
        Assert.False(state.GetProperty("menu").GetProperty("posArranged").GetBoolean());
        Assert.Equal(3, (await ItemsOf(owner)).Length);
        Assert.Equal(2, state.GetProperty("sections").GetArrayLength());

        var ping = await device.GetFromJsonAsync<SyncPing>("/api/sync/ping", SyncWire.Json);
        Assert.EndsWith("/m/section-shop", ping!.MenuUrl);
    }

    /// <summary>Every item, section by section, the way the editor loads them: the editor state carries counts only.</summary>
    private static async Task<JsonElement[]> ItemsOf(HttpClient c)
    {
        var state = await Ok(c.GetAsync("/menu/v1/editor"));
        var keys = state.GetProperty("sections").EnumerateArray().Select(s => s.GetProperty("id").GetInt32()).ToList();
        if (state.GetProperty("looseCount").GetInt32() > 0) keys.Add(0);
        var all = new List<JsonElement>();
        foreach (var key in keys)
            for (var skip = 0; ; )
            {
                var page = await Ok(c.GetAsync($"/menu/v1/items?section={key}&skip={skip}&take=100"));
                var items = page.GetProperty("items").EnumerateArray().ToArray();
                all.AddRange(items);
                skip += items.Length;
                if (!page.GetProperty("hasMore").GetBoolean()) break;
            }
        return all.ToArray();
    }

    private static string[] Names(JsonElement state, string list)
        => state.GetProperty(list).EnumerateArray().Select(x => x.GetProperty("name").GetString()!).ToArray();

    private async Task<HttpClient> OwnerAsync(SeededSite site, string stem)
    {
        await _cloud.WithControlDbAsync(async db =>
        {
            db.OwnerUsers.Add(new OwnerUser
            {
                ClientId = site.ClientId, Email = $"owner@{stem}.test", DisplayName = "owner", PasswordHash = PasswordHasher.Hash(Password),
                CreatedAt = _cloud.Clock.GetUtcNow().UtcDateTime, Permissions = (int)OwnerPermissions.All,
            });
            return await db.SaveChangesAsync();
        });
        var http = _cloud.CreateClient();
        var login = await http.PostAsJsonAsync("/owner/v1/auth/login", new { email = $"owner@{stem}.test", password = Password });
        login.EnsureSuccessStatusCode();
        var token = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return http;
    }

    private static async Task<JsonElement> Ok(Task<HttpResponseMessage> call)
    {
        var response = await call;
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode is HttpStatusCode.OK or HttpStatusCode.NoContent, $"{(int)response.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement.Clone();
    }
}
