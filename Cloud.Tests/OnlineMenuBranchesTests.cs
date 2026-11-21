using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Cloud.Data;
using Cloud.Services;
using Cloud.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Sync;
using Xunit;

namespace Cloud.Tests;

/// <summary>
/// One online menu for a shop with several branches: the dishes are shared, each branch's POS sets its own prices and what it sells, and
/// the guest picks the branch (a table QR carries it). Order types (dine in, delivery...) made by the owner price every dish on top.
/// </summary>
public class OnlineMenuBranchesTests : IClassFixture<CloudFactory>
{
    private const string Password = "pass-1234-ok";
    private readonly CloudFactory _cloud;

    public OnlineMenuBranchesTests(CloudFactory cloud) => _cloud = cloud;

    private async Task<(int ClientId, (Guid Id, string Token) Hamra, (Guid Id, string Token) Achrafieh, HttpClient Owner)> ChainAsync(string stem)
    {
        var first = await _cloud.SeedSiteAsync(1, stem);
        var (token, hash, prefix) = DeviceTokens.Generate();
        var second = Guid.NewGuid();
        await _cloud.WithControlDbAsync(async db =>
        {
            var now = _cloud.Clock.GetUtcNow().UtcDateTime;
            (await db.Sites.SingleAsync(s => s.Id == first.SiteId)).Name = "Hamra";
            var site = new Site { Id = second, ClientId = first.ClientId, Name = "Achrafieh", CreatedAt = now };
            site.Tokens.Add(new DeviceToken { TokenHash = hash, Prefix = prefix, CreatedAt = now });
            db.Sites.Add(site);
            db.OwnerUsers.Add(new OwnerUser
            {
                ClientId = first.ClientId, Email = $"owner@{stem}.test", DisplayName = "owner", PasswordHash = PasswordHasher.Hash(Password),
                CreatedAt = now, Permissions = (int)OwnerRoles.For("owner"),
            });
            return await db.SaveChangesAsync();
        });
        var http = _cloud.CreateClient(new() { AllowAutoRedirect = false });
        var login = await http.PostAsJsonAsync("/owner/v1/auth/login", new { email = $"owner@{stem}.test", password = Password });
        login.EnsureSuccessStatusCode();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString());
        return (first.ClientId, (first.SiteId, first.Token), (second, token), http);
    }

    private static async Task<JsonElement> Ok(Task<HttpResponseMessage> call)
    {
        var response = await call;
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {body}");
        return body.Length == 0 ? default : JsonDocument.Parse(body).RootElement.Clone();
    }

    private Task Push(string token, string hash, params (int Id, string Name, decimal Price, string Section)[] products)
    {
        var list = products.Select((p, n) => new MenuCatalogItem(p.Id, p.Name, null, p.Price, "$", "c" + p.Section.Length, p.Section, 0, n)).ToList();
        return Ok(_cloud.DeviceClient(token).PostAsJsonAsync("/" + MenuCatalog.Route, new MenuCatalogPush(hash, list), SyncWire.Json));
    }

    private async Task<string[]> Prices(string slug, string query)
    {
        // Name and price of every dish on the page, as "Name=$1.50", from the item markup.
        var html = await _cloud.CreateClient().GetStringAsync($"/m/{slug}{query}");
        return System.Text.RegularExpressions.Regex.Matches(html, "class=\"open\">([^<]+)</button></h3>[\\s\\S]*?<span dir=\"ltr\">([^<]+)</span>")
            .Select(m => WebUtility.HtmlDecode(m.Groups[1].Value) + "=" + m.Groups[2].Value).OrderBy(s => s, StringComparer.Ordinal).ToArray();
    }

    [Fact]
    public async Task Branches_share_the_dishes_and_each_shows_its_own_prices_and_what_it_sells()
    {
        var (_, hamra, achrafieh, owner) = await ChainAsync("chainshop");
        await Ok(owner.PostAsJsonAsync("/menu/v1/editor", new { title = "Chain Shop" }));
        var state = await Ok(owner.PutAsJsonAsync("/menu/v1/settings", new { isPublished = true, branchIds = new[] { hamra.Id.ToString(), achrafieh.Id.ToString() } }));
        Assert.Equal(new[] { "hamra", "achrafieh" }, state.GetProperty("branches").EnumerateArray().Select(b => b.GetProperty("slug").GetString()));

        // Each till numbers its products its own way; the same name is the same dish.
        await Push(hamra.Token, "h1", (11, "Labneh 500g", 2.90m, "Dairy"), (12, "Akkawi (per kg)", 11.50m, "Cheese"));
        await Push(achrafieh.Token, "a1", (501, "Labneh 500g", 3.10m, "Dairy"), (502, "Halloumi", 6m, "Cheese"));
        await Ok(owner.PostAsJsonAsync("/menu/v1/items", new { name = "Tea", price = 1 })); // made by hand: sold everywhere

        // Without a branch, the guest is asked which one.
        var picker = await _cloud.CreateClient().GetStringAsync("/m/chain-shop");
        Assert.Contains("Which branch are you at?", picker);
        Assert.Contains("href=\"/m/chain-shop?b=hamra\"", picker);
        Assert.Contains("href=\"/m/chain-shop?b=achrafieh\"", picker);
        Assert.DoesNotContain("Labneh", picker);

        Assert.Equal(new[] { "Akkawi (per kg)=$11.50", "Labneh 500g=$2.90", "Tea=$1" }, await Prices("chain-shop", "?b=hamra"));
        Assert.Equal(new[] { "Halloumi=$6", "Labneh 500g=$3.10", "Tea=$1" }, await Prices("chain-shop", "?b=achrafieh"));

        // The items feed (loading more, search) answers for the same branch.
        var found = await Ok(_cloud.CreateClient().GetAsync("/m/chain-shop/items?q=labneh&b=achrafieh"));
        Assert.Equal(3.10m, found.GetProperty("items")[0].GetProperty("price").GetDecimal());
        Assert.Equal(HttpStatusCode.NotFound, (await _cloud.CreateClient().GetAsync("/m/chain-shop/items?q=labneh")).StatusCode);

        // Each till is told its own branch's address, for the QR it shows.
        var ping = await _cloud.DeviceClient(achrafieh.Token).GetFromJsonAsync<SyncPing>("/api/sync/ping", SyncWire.Json);
        Assert.True(ping!.MenuLinked);
        Assert.EndsWith("/m/chain-shop?b=achrafieh", ping.MenuUrl);

        // The editor shows one Labneh, with both branches' prices.
        var section = (await Ok(owner.GetAsync("/menu/v1/editor"))).GetProperty("sections").EnumerateArray().Single(s => s.GetProperty("name").GetString() == "Dairy");
        var labneh = (await Ok(owner.GetAsync($"/menu/v1/items?section={section.GetProperty("id").GetInt32()}"))).GetProperty("items").EnumerateArray().Single();
        Assert.Equal(new[] { "Achrafieh $3.10", "Hamra $2.90" }, labneh.GetProperty("branches").EnumerateArray()
            .Select(b => b.GetProperty("name").GetString() + " " + b.GetProperty("priceText").GetString()));

        // Achrafieh taken off the menu: what only it sold stays (as hand-made), and with one branch there is nothing to pick.
        await Ok(owner.PutAsJsonAsync("/menu/v1/settings", new { branchIds = new[] { hamra.Id.ToString() } }));
        Assert.Equal(new[] { "Akkawi (per kg)=$11.50", "Halloumi=$6", "Labneh 500g=$2.90", "Tea=$1" }, await Prices("chain-shop", ""));
        Assert.False((await _cloud.DeviceClient(achrafieh.Token).GetFromJsonAsync<SyncPing>("/api/sync/ping", SyncWire.Json))!.MenuLinked);
    }

    [Fact]
    public async Task Upgrading_keeps_a_linked_menu_on_its_branch_with_its_POS_products_and_prices()
    {
        var path = Path.Combine(Path.GetTempPath(), $"menu-upgrade-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<ControlDbContext>().UseSqlite($"Data Source={path}").Options;
        var site = Guid.NewGuid().ToString().ToUpperInvariant();
        try
        {
            await using (var db = new ControlDbContext(options))
            {
                // The database as it was before branches: one LinkedSiteId on the menu, PosProductId on its items.
                await db.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>().MigrateAsync("20261116102101_AddMenuListingStyle");
                await db.Database.ExecuteSqlRawAsync($"""
                    INSERT INTO Clients (Id, Name, CreatedAt, IsActive) VALUES (1, 'Old Shop', '2026-11-01 00:00:00', 1);
                    INSERT INTO Sites (Id, ClientId, Name, CreatedAt, IsDisabled, LastThroughSeq, ResyncRequested) VALUES ('{site}', 1, 'Hamra', '2026-11-01 00:00:00', 0, 0, 0);
                    INSERT INTO Menus (Id, ClientId, Slug, Title, Language, Currency, IsPublished, PreviewKey, LinkedSiteId, CatalogHash, PosArranged, CreatedAt, UpdatedAt)
                        VALUES (1, 1, 'old-shop', 'Old Shop', 'en', '$', 1, 'k', '{site}', 'hash-1', 1, '2026-11-01 00:00:00', '2026-11-01 00:00:00');
                    INSERT INTO MenuItems (Id, MenuId, Name, Price, IsHidden, IsSoldOut, SortOrder, PosProductId) VALUES (1, 1, 'Labneh', '2.9', 0, 0, 0, 77);
                    INSERT INTO MenuItems (Id, MenuId, Name, Price, IsHidden, IsSoldOut, SortOrder) VALUES (2, 1, 'Tea', '1', 0, 0, 1);
                    """);
                await db.Database.MigrateAsync();
            }
            await using (var db = new ControlDbContext(options))
            {
                var branch = await db.MenuBranches.SingleAsync();
                Assert.Equal((1, Guid.Parse(site), "main", "hash-1", true), (branch.MenuId, branch.SiteId, branch.Slug, branch.CatalogHash, branch.PosArranged));
                var row = await db.MenuItemBranches.SingleAsync();
                Assert.Equal((1, 77, 2.9m), (row.MenuItemId, row.PosProductId, row.Price));
                Assert.Equal(2, await db.MenuItems.CountAsync()); // the hand-made one stays hand-made
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { File.Delete(path); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task Order_types_price_every_dish_by_their_rule_unless_the_dish_has_its_own_price()
    {
        var (_, hamra, achrafieh, owner) = await ChainAsync("typeshop");
        await Ok(owner.PostAsJsonAsync("/menu/v1/editor", new { title = "Type Shop" }));
        await Ok(owner.PutAsJsonAsync("/menu/v1/settings", new { isPublished = true, branchIds = new[] { hamra.Id.ToString(), achrafieh.Id.ToString() } }));
        await Push(hamra.Token, "h1", (11, "Labneh 500g", 2.90m, "Dairy"), (12, "Akkawi (per kg)", 11.50m, "Cheese"));
        await Push(achrafieh.Token, "a1", (501, "Labneh 500g", 3.10m, "Dairy"));
        await Ok(owner.PostAsJsonAsync("/menu/v1/items", new { name = "Tea", price = 1 }));

        await Ok(owner.PostAsJsonAsync("/menu/v1/types", new { name = "Dine in" }));
        await Ok(owner.PostAsJsonAsync("/menu/v1/types", new { name = "Take out", adjust = "amount", adjustValue = -0.5m }));
        var state = await Ok(owner.PostAsJsonAsync("/menu/v1/types", new { name = "Delivery", adjust = "percent", adjustValue = 10 }));
        var types = state.GetProperty("orderTypes").EnumerateArray().ToDictionary(t => t.GetProperty("name").GetString()!, t => t.GetProperty("id").GetInt32());
        Assert.Equal(new[] { "Dine in", "Take out", "Delivery" }, types.Keys);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync("/menu/v1/types", new { name = "Odd", adjust = "double" })).StatusCode);

        // Labneh has its own delivery price, the same at every branch.
        var dairy = (await Ok(owner.GetAsync("/menu/v1/editor"))).GetProperty("sections").EnumerateArray().Single(s => s.GetProperty("name").GetString() == "Dairy").GetProperty("id").GetInt32();
        var labnehId = (await Ok(owner.GetAsync($"/menu/v1/items?section={dairy}"))).GetProperty("items")[0].GetProperty("id").GetInt32();
        await Ok(owner.PutAsJsonAsync($"/menu/v1/items/{labnehId}", new { typePrices = new Dictionary<string, decimal?> { [types["Delivery"].ToString()] = 4 } }));

        // No type in the address: the first one.
        Assert.Equal(new[] { "Akkawi (per kg)=$11.50", "Labneh 500g=$2.90", "Tea=$1" }, await Prices("type-shop", "?b=hamra"));
        Assert.Equal(new[] { "Akkawi (per kg)=$11", "Labneh 500g=$2.40", "Tea=$0.50" }, await Prices("type-shop", $"?b=hamra&t={types["Take out"]}"));
        Assert.Equal(new[] { "Akkawi (per kg)=$12.65", "Labneh 500g=$4", "Tea=$1.10" }, await Prices("type-shop", $"?b=hamra&t={types["Delivery"]}"));
        Assert.Equal(new[] { "Labneh 500g=$4", "Tea=$1.10" }, await Prices("type-shop", $"?b=achrafieh&t={types["Delivery"]}"));

        // The page shows the choice, with the one in use marked.
        var page = await _cloud.CreateClient().GetStringAsync($"/m/type-shop?b=hamra&t={types["Delivery"]}");
        Assert.Contains($"href=\"/m/type-shop?b=hamra&amp;t={types["Delivery"]}\" dir=\"auto\" aria-current=\"true\">Delivery</a>", page);

        // Clearing the dish's own price falls back to the rule; deleting a type takes its prices with it.
        await Ok(owner.PutAsJsonAsync($"/menu/v1/items/{labnehId}", new { typePrices = new Dictionary<string, decimal?> { [types["Delivery"].ToString()] = null } }));
        Assert.Contains("Labneh 500g=$3.19", await Prices("type-shop", $"?b=hamra&t={types["Delivery"]}"));
        await Ok(owner.PutAsJsonAsync($"/menu/v1/items/{labnehId}", new { typePrices = new Dictionary<string, decimal?> { [types["Delivery"].ToString()] = 4 } }));
        await Ok(owner.DeleteAsync($"/menu/v1/types/{types["Delivery"]}"));
        Assert.False(await _cloud.WithControlDbAsync(db => db.MenuItemTypePrices.AnyAsync(p => p.MenuItemId == labnehId)));
    }
}
