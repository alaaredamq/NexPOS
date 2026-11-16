using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Cloud.Data;
using Cloud.Services;
using Cloud.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sync;
using Xunit;

namespace Cloud.Tests;

/// <summary>
/// The online menu: a public page at /m/{slug} the owner fills by hand, or links to a branch so the shop PC keeps it in step with the POS.
/// </summary>
public class OnlineMenuTests : IClassFixture<CloudFactory>
{
    private const string Password = "pass-1234-ok";
    private readonly CloudFactory _cloud;

    public OnlineMenuTests(CloudFactory cloud) => _cloud = cloud;

    /// <summary>A tiny real PNG (1x1).</summary>
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8DwHwAFBQIAX8jx0gAAAABJRU5ErkJggg==");

    private async Task<(SeededSite Site, HttpClient Owner)> ShopAsync(string stem, string role = "owner")
    {
        var site = await _cloud.SeedSiteAsync(1, stem);
        await _cloud.WithControlDbAsync(async db =>
        {
            db.OwnerUsers.Add(new OwnerUser
            {
                ClientId = site.ClientId, Email = $"{role}@{stem}.test", DisplayName = role, PasswordHash = PasswordHasher.Hash(Password),
                CreatedAt = _cloud.Clock.GetUtcNow().UtcDateTime, Permissions = (int)OwnerRoles.For(role),
            });
            return await db.SaveChangesAsync();
        });
        var http = _cloud.CreateClient(new() { AllowAutoRedirect = false });
        var login = await http.PostAsJsonAsync("/owner/v1/auth/login", new { email = $"{role}@{stem}.test", password = Password });
        login.EnsureSuccessStatusCode();
        var token = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (site, http);
    }

    private static async Task<JsonElement> Ok(Task<HttpResponseMessage> call)
    {
        var response = await call;
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement.Clone();
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
    private static JsonElement[] Sections(JsonElement state) => state.GetProperty("sections").EnumerateArray().ToArray();
    private static string Slug(JsonElement state) => state.GetProperty("menu").GetProperty("slug").GetString()!;

    [Fact]
    public async Task An_owner_builds_a_menu_by_hand_and_it_is_public_only_once_published()
    {
        var (_, owner) = await ShopAsync("burgerhut");

        var state = await Ok(owner.PostAsJsonAsync("/menu/v1/editor", new { title = "Burger Hut" }));
        Assert.Equal("burger-hut", Slug(state));

        state = await Ok(owner.PostAsJsonAsync("/menu/v1/sections", new { name = "Burgers" }));
        var section = Sections(state).Single().GetProperty("id").GetInt32();
        state = await Ok(owner.PostAsJsonAsync("/menu/v1/items", new { name = "Classic burger", description = "Beef, cheddar, pickles", price = 8.5m, sectionId = section }));
        await Ok(owner.PostAsJsonAsync("/menu/v1/items", new { name = "Secret burger", price = 12m, sectionId = section, isHidden = true }));

        // Not published yet: nobody finds it, but the preview link works.
        var anonymous = _cloud.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync("/m/burger-hut")).StatusCode);
        var previewUrl = new Uri(state.GetProperty("menu").GetProperty("previewUrl").GetString()!).PathAndQuery;
        var preview = await anonymous.GetAsync(previewUrl);
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        Assert.Contains("noindex", await preview.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync("/m/burger-hut?preview=wrong")).StatusCode);

        await Ok(owner.PutAsJsonAsync("/menu/v1/settings", new { isPublished = true, tagline = "Since 2009" }));
        var html = await anonymous.GetStringAsync("/m/burger-hut");
        Assert.Contains("Classic burger", html);
        Assert.Contains("Beef, cheddar, pickles", html);
        Assert.Contains("$8.50", html);
        Assert.Contains("Since 2009", html);
        Assert.DoesNotContain("Secret burger", html);

        // Published menus are offered to search engines.
        Assert.Contains("/m/burger-hut</loc>", await anonymous.GetStringAsync("/sitemap.xml"));
    }

    [Fact]
    public async Task A_long_menu_arrives_in_pages_and_search_finds_what_is_not_loaded_yet()
    {
        var (_, owner) = await ShopAsync("bigmenu");
        await Ok(owner.PostAsJsonAsync("/menu/v1/editor", new { title = "Big Menu" }));
        var state = await Ok(owner.PostAsJsonAsync("/menu/v1/sections", new { name = "Everything" }));
        var section = Sections(state).Single().GetProperty("id").GetInt32();
        await _cloud.WithControlDbAsync(async db =>
        {
            var menuId = (await db.Menus.SingleAsync(m => m.Slug == "big-menu")).Id;
            for (var n = 1; n <= 150; n++)
                db.MenuItems.Add(new MenuItem { MenuId = menuId, SectionId = section, Name = $"Dish {n:000}", Price = n, SortOrder = n, IsHidden = n == 149 });
            return await db.SaveChangesAsync();
        });
        await Ok(owner.PutAsJsonAsync("/menu/v1/settings", new { isPublished = true }));
        var anonymous = _cloud.CreateClient();

        // The page itself carries the first 60, and a placeholder for the rest.
        var html = await anonymous.GetStringAsync("/m/big-menu");
        Assert.Contains("Dish 060", html);
        Assert.DoesNotContain("Dish 061", html);
        Assert.Contains("data-skip=\"60\"", html);

        // The rest, a page at a time. The hidden one never shows.
        var page = await Ok(anonymous.GetAsync($"/m/big-menu/items?section={section}&skip=60&take=40"));
        Assert.Equal("Dish 061", page.GetProperty("items")[0].GetProperty("name").GetString());
        Assert.True(page.GetProperty("hasMore").GetBoolean());
        var last = await Ok(anonymous.GetAsync($"/m/big-menu/items?section={section}&skip=140&take=40"));
        Assert.False(last.GetProperty("hasMore").GetBoolean());
        Assert.DoesNotContain(last.GetProperty("items").EnumerateArray(), i => i.GetProperty("name").GetString() == "Dish 149");

        // Search reaches items the page has not loaded.
        var found = await Ok(anonymous.GetAsync("/m/big-menu/items?q=dish%2014"));
        Assert.Equal(9, found.GetProperty("items").GetArrayLength()); // 140-148, without the hidden 149
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync("/m/no-such-menu/items?section=1")).StatusCode);

        // The editor pages too, and moves an item without needing the whole list.
        var first = await Ok(owner.GetAsync($"/menu/v1/items?section={section}&take=2"));
        Assert.Equal(2, first.GetProperty("items").GetArrayLength());
        var second = first.GetProperty("items")[1].GetProperty("id").GetInt32();
        await Ok(owner.PostAsJsonAsync($"/menu/v1/items/{second}/move", new { direction = -1 }));
        first = await Ok(owner.GetAsync($"/menu/v1/items?section={section}&take=2"));
        Assert.Equal("Dish 002", first.GetProperty("items")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task Unpublished_items_need_the_preview_key_too()
    {
        var (_, owner) = await ShopAsync("secretmenu");
        var state = await Ok(owner.PostAsJsonAsync("/menu/v1/editor", new { title = "Secret Menu" }));
        await Ok(owner.PostAsJsonAsync("/menu/v1/items", new { name = "Hidden dish", price = 1 }));
        var key = new Uri(state.GetProperty("menu").GetProperty("previewUrl").GetString()!).Query.Split('=')[1];
        var anonymous = _cloud.CreateClient();

        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync("/m/secret-menu/items?section=0")).StatusCode);
        var page = await Ok(anonymous.GetAsync($"/m/secret-menu/items?section=0&preview={key}"));
        Assert.Equal("Hidden dish", page.GetProperty("items")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task A_login_that_cannot_edit_products_cannot_touch_the_menu()
    {
        var (_, staff) = await ShopAsync("staffshop", "staff");
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PostAsJsonAsync("/menu/v1/editor", new { title = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _cloud.CreateClient().GetAsync("/menu/v1/editor")).StatusCode);
    }

    [Fact]
    public async Task The_provider_edits_a_clients_menu_from_the_admin_portal_and_needs_the_editor_header_to_change_it()
    {
        var site = await _cloud.SeedSiteAsync(0, "Hand Cafe");
        var admin = _cloud.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = true });
        await HtmlForms.PostAsync(admin, "/admin/Account/Login", "/admin/Account/Login", ("Username", CloudFactory.AdminUser), ("Password", CloudFactory.AdminPassword));

        // A form on another site could ride on the cookie; it cannot add a custom header.
        var noHeader = await admin.PostAsJsonAsync($"/menu/v1/editor?client={site.ClientId}", new { title = "Hand Cafe" });
        Assert.Equal(HttpStatusCode.BadRequest, noHeader.StatusCode);

        admin.DefaultRequestHeaders.Add(Cloud.Api.MenuEndpoints.AdminHeader, "1");
        var state = await Ok(admin.PostAsJsonAsync($"/menu/v1/editor?client={site.ClientId}", new { title = "Hand Cafe" }));
        Assert.True(state.GetProperty("admin").GetBoolean());
        Assert.Equal("hand-cafe", Slug(state));
        Assert.Contains("/owner/menu.html?client=" + site.ClientId, await admin.GetStringAsync($"/admin/Clients/Details/{site.ClientId}"));
    }

    [Fact]
    public async Task Pictures_must_be_real_images_and_are_served_for_good()
    {
        var (_, owner) = await ShopAsync("picshop");
        await Ok(owner.PostAsJsonAsync("/menu/v1/editor", new { title = "Pic Shop" }));
        var state = await Ok(owner.PostAsJsonAsync("/menu/v1/items", new { name = "Tea", price = 1 }));
        var id = (await ItemsOf(owner)).Single().GetProperty("id").GetInt32();

        var fake = new ByteArrayContent("<script>alert(1)</script>"u8.ToArray());
        fake.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PutAsync($"/menu/v1/items/{id}/image", fake)).StatusCode);

        var huge = new ByteArrayContent(Png.Concat(new byte[MenuImages.MaxBytes]).ToArray());
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await owner.PutAsync($"/menu/v1/items/{id}/image", huge)).StatusCode);

        state = await Ok(owner.PutAsync($"/menu/v1/items/{id}/image", new ByteArrayContent(Png)));
        var url = (await ItemsOf(owner)).Single().GetProperty("imageUrl").GetString()!;
        Assert.Matches("^/m/img/[0-9a-f]{32}\\.png$", url);
        var image = await _cloud.CreateClient().GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, image.StatusCode);
        Assert.Equal("image/png", image.Content.Headers.ContentType?.MediaType);
        Assert.Contains("immutable", image.Headers.CacheControl?.ToString());

        // Replacing or removing a picture deletes the old file.
        await Ok(owner.DeleteAsync($"/menu/v1/items/{id}/image"));
        Assert.Equal(HttpStatusCode.NotFound, (await _cloud.CreateClient().GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _cloud.CreateClient().GetAsync("/m/img/..%2Fcontrol.db")).StatusCode);
    }

    [Fact]
    public async Task Addresses_are_unique_and_checked()
    {
        var (_, a) = await ShopAsync("slug-a");
        var (_, b) = await ShopAsync("slug-b");
        await Ok(a.PostAsJsonAsync("/menu/v1/editor", new { title = "Same Name" }));
        var second = await Ok(b.PostAsJsonAsync("/menu/v1/editor", new { title = "Same Name" }));
        Assert.Equal("same-name-2", Slug(second));

        Assert.Equal(HttpStatusCode.Conflict, (await b.PutAsJsonAsync("/menu/v1/settings", new { slug = "same-name" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await b.PutAsJsonAsync("/menu/v1/settings", new { slug = "Bad Slug!" })).StatusCode);
        Assert.Equal("my-place", Slug(await Ok(b.PutAsJsonAsync("/menu/v1/settings", new { slug = "my-place" }))));
    }

    [Fact]
    public async Task A_linked_menu_follows_the_POS_and_keeps_the_owners_pictures_and_choices()
    {
        var (site, owner) = await ShopAsync("linkshop");
        using var till = new TillDb();
        var burger = await till.AddProductAsync("Zinger burger", "111", costPrice: 3.10m);
        var fries = await till.AddProductAsync("Fries", "222", costPrice: 0.40m);
        var retired = await till.AddProductAsync("Old item", "333");
        var hidden = await till.AddProductAsync("Napkins", "444");
        SqlHelper.Exec(till.Path, $"UPDATE Products SET IsActive = 1, SellingPrice = '6.5' WHERE Id = {burger}");
        SqlHelper.Exec(till.Path, $"UPDATE Products SET IsActive = 1, SellingPrice = '2' WHERE Id IN ({fries}, {retired}, {hidden})");

        var device = _cloud.DeviceClient(site.Token);

        // Not linked: the shop PC is told so, and a list it sends anyway is refused.
        var ping = await device.GetFromJsonAsync<SyncPing>("/api/sync/ping", SyncWire.Json);
        Assert.False(ping!.MenuLinked);
        Assert.Null(await MenuCatalog.PublishIfChangedAsync(device, till.ConnectionString, null, default));

        await Ok(owner.PostAsJsonAsync("/menu/v1/editor", new { title = "Link Shop" }));
        await Ok(owner.PutAsJsonAsync("/menu/v1/settings", new { linkedSiteId = site.SiteId.ToString(), isPublished = true }));
        ping = await device.GetFromJsonAsync<SyncPing>("/api/sync/ping", SyncWire.Json);
        Assert.True(ping!.MenuLinked);
        Assert.Null(ping.MenuCatalogHash);

        // What travels: names, prices, currency, category. Never the cost.
        var catalog = MenuCatalog.Build(till.ConnectionString);
        Assert.DoesNotContain("3.1", JsonSerializer.Serialize(catalog, SyncWire.Json));
        var hash = await MenuCatalog.PublishIfChangedAsync(device, till.ConnectionString, null, default);
        Assert.Equal(catalog.Hash, hash);
        ping = await device.GetFromJsonAsync<SyncPing>("/api/sync/ping", SyncWire.Json);
        Assert.Equal(hash, ping!.MenuCatalogHash);

        var state = await Ok(owner.GetAsync("/menu/v1/editor"));
        Assert.Equal(4, (await ItemsOf(owner)).Length);
        Assert.All((await ItemsOf(owner)), i => Assert.True(i.GetProperty("linked").GetBoolean()));
        Assert.True(Sections(state).Single().GetProperty("linked").GetBoolean());
        var burgerItem = (await ItemsOf(owner)).Single(i => i.GetProperty("name").GetString() == "Zinger burger").GetProperty("id").GetInt32();
        var napkins = (await ItemsOf(owner)).Single(i => i.GetProperty("name").GetString() == "Napkins").GetProperty("id").GetInt32();

        // The POS owns the price; the owner owns the picture, words and visibility.
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PutAsJsonAsync($"/menu/v1/items/{burgerItem}", new { price = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.DeleteAsync($"/menu/v1/items/{burgerItem}")).StatusCode);
        await Ok(owner.PutAsJsonAsync($"/menu/v1/items/{burgerItem}", new { description = "Crispy and hot" }));
        await Ok(owner.PutAsync($"/menu/v1/items/{burgerItem}/image", new ByteArrayContent(Png)));
        await Ok(owner.PutAsJsonAsync($"/menu/v1/items/{napkins}", new { isHidden = true }));

        // At the shop: a price changes, a product is switched off. Nothing else is sent when nothing changed.
        Assert.Equal(hash, await MenuCatalog.PublishIfChangedAsync(device, till.ConnectionString, hash, default));
        SqlHelper.Exec(till.Path, $"UPDATE Products SET SellingPrice = '7.25' WHERE Id = {burger}");
        SqlHelper.Exec(till.Path, $"UPDATE Products SET IsActive = 0 WHERE Id = {retired}");
        var newHash = await MenuCatalog.PublishIfChangedAsync(device, till.ConnectionString, hash, default);
        Assert.NotEqual(hash, newHash);

        state = await Ok(owner.GetAsync("/menu/v1/editor"));
        Assert.Equal(3, (await ItemsOf(owner)).Length);
        var item = (await ItemsOf(owner)).Single(i => i.GetProperty("id").GetInt32() == burgerItem);
        Assert.Equal(7.25m, item.GetProperty("price").GetDecimal());
        Assert.Equal("Crispy and hot", item.GetProperty("description").GetString());
        Assert.False(item.GetProperty("imageUrl").ValueKind == JsonValueKind.Null);

        var html = await _cloud.CreateClient().GetStringAsync("/m/" + Slug(state));
        Assert.Contains("Zinger burger", html);
        Assert.Contains("7.25", html);
        Assert.DoesNotContain("Napkins", html);
        Assert.DoesNotContain("Old item", html);

        // Unlinking keeps everything, as ordinary items the owner can now edit.
        state = await Ok(owner.PutAsJsonAsync("/menu/v1/settings", new { linkedSiteId = "" }));
        Assert.All((await ItemsOf(owner)), i => Assert.False(i.GetProperty("linked").GetBoolean()));
        await Ok(owner.PutAsJsonAsync($"/menu/v1/items/{burgerItem}", new { price = 8 }));
        Assert.False((await device.GetFromJsonAsync<SyncPing>("/api/sync/ping", SyncWire.Json))!.MenuLinked);
    }

    [Fact]
    public async Task A_menu_cannot_be_linked_to_another_clients_branch()
    {
        var (_, owner) = await ShopAsync("mine");
        var other = await _cloud.SeedSiteAsync(1, "Someone Else");
        await Ok(owner.PostAsJsonAsync("/menu/v1/editor", new { title = "Mine" }));
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PutAsJsonAsync("/menu/v1/settings", new { linkedSiteId = other.SiteId.ToString() })).StatusCode);
    }

    [Fact]
    public async Task Removing_a_client_removes_its_menu_and_pictures()
    {
        var (site, owner) = await ShopAsync("goneshop");
        await Ok(owner.PostAsJsonAsync("/menu/v1/editor", new { title = "Gone Shop" }));
        var state = await Ok(owner.PostAsJsonAsync("/menu/v1/items", new { name = "Tea", price = 1 }));
        state = await Ok(owner.PutAsync($"/menu/v1/items/{(await ItemsOf(owner)).Single().GetProperty("id").GetInt32()}/image", new ByteArrayContent(Png)));
        var url = (await ItemsOf(owner)).Single().GetProperty("imageUrl").GetString()!;

        using (var scope = _cloud.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<SiteRemoval>().RemoveClientAsync(site.ClientId);

        Assert.Equal(0, await _cloud.WithControlDbAsync(db => db.Menus.CountAsync(m => m.ClientId == site.ClientId)));
        Assert.Equal(HttpStatusCode.NotFound, (await _cloud.CreateClient().GetAsync(url)).StatusCode);
    }

    [Theory]
    [InlineData(8.5, "$", "$8.50")]
    [InlineData(8, "$", "$8")]
    [InlineData(450000, "LBP", "450,000 LBP")]
    [InlineData(3.75, null, "3.75")]
    public void Prices_read_naturally(decimal price, string? currency, string expected)
        => Assert.Equal(expected, MenuRules.Price(price, currency));

    [Theory]
    [InlineData("Abu Ali's Grill", "abu-alis-grill")]
    [InlineData("Café & Co.", "cafe-co")]
    public void Addresses_come_from_the_name(string name, string expected) => Assert.Equal(expected, MenuRules.SlugFrom(name));

    [Fact]
    public void A_name_without_latin_letters_gets_a_random_address() => Assert.StartsWith("menu-", MenuRules.SlugFrom("مطعم الشام"));
}
