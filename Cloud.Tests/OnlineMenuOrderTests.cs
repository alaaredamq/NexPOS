using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Cloud.Data;
using Cloud.Services;
using Cloud.Tests.Support;
using Sync;
using Xunit;

namespace Cloud.Tests;

/// <summary>
/// Orders sent from the public menu: only when the owner chose to take them, priced by the cloud (never by the phone), followed by the
/// guest, answered by the owner, or delivered to the till of the guest's branch with its POS products.
/// </summary>
public class OnlineMenuOrderTests : IClassFixture<CloudFactory>
{
    private const string Password = "pass-1234-ok";
    private readonly CloudFactory _cloud;

    public OnlineMenuOrderTests(CloudFactory cloud) => _cloud = cloud;

    private async Task<(SeededSite Site, HttpClient Owner)> ShopAsync(string stem)
    {
        var site = await _cloud.SeedSiteAsync(1, stem);
        await _cloud.WithControlDbAsync(async db =>
        {
            db.OwnerUsers.Add(new OwnerUser
            {
                ClientId = site.ClientId, Email = $"owner@{stem}.test", DisplayName = "owner", PasswordHash = PasswordHasher.Hash(Password),
                CreatedAt = _cloud.Clock.GetUtcNow().UtcDateTime, Permissions = (int)OwnerRoles.For("owner"),
            });
            return await db.SaveChangesAsync();
        });
        var http = _cloud.CreateClient(new() { AllowAutoRedirect = false });
        var login = await http.PostAsJsonAsync("/owner/v1/auth/login", new { email = $"owner@{stem}.test", password = Password });
        login.EnsureSuccessStatusCode();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString());
        return (site, http);
    }

    private static async Task<JsonElement> Ok(Task<HttpResponseMessage> call)
    {
        var response = await call;
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {body}");
        return body.Length == 0 ? default : JsonDocument.Parse(body).RootElement.Clone();
    }

    /// <summary>A published hand-made menu with two dishes. Returns its address and the dishes' ids.</summary>
    private static async Task<(string Slug, int Hummus, int Kafta)> MenuAsync(HttpClient owner, string title, string mode)
    {
        var state = await Ok(owner.PostAsJsonAsync("/menu/v1/editor", new { title }));
        var a = await owner.PostAsJsonAsync("/menu/v1/items", new { name = "Hummus", price = 5.5 });
        var b = await owner.PostAsJsonAsync("/menu/v1/items", new { name = "Kafta", price = 12 });
        state = await Ok(owner.PutAsJsonAsync("/menu/v1/settings", new { isPublished = true, orderMode = mode }));
        return (state.GetProperty("menu").GetProperty("slug").GetString()!,
            int.Parse(a.Headers.GetValues("X-Created-Id").Single()), int.Parse(b.Headers.GetValues("X-Created-Id").Single()));
    }

    [Fact]
    public async Task A_menu_that_only_shows_the_order_to_staff_takes_nothing()
    {
        var (_, owner) = await ShopAsync("ord-show");
        var (slug, hummus, _) = await MenuAsync(owner, "Show Only", MenuOrderModes.Show);
        var sent = await _cloud.CreateClient().PostAsJsonAsync($"/m/{slug}/orders", new { name = "Rami", lines = new[] { new { id = hummus, n = 1 } } });
        Assert.Equal(HttpStatusCode.Conflict, sent.StatusCode);
    }

    [Fact]
    public async Task A_guest_sends_an_order_priced_by_the_cloud_and_follows_it_while_the_owner_answers()
    {
        var (_, owner) = await ShopAsync("ord-req");
        var (slug, hummus, kafta) = await MenuAsync(owner, "Order Shop", MenuOrderModes.Request);
        var guest = _cloud.CreateClient();

        // What the phone says the price is does not matter: only ids and counts are read.
        var order = await Ok(guest.PostAsJsonAsync($"/m/{slug}/orders", new
        {
            name = "Rami", phone = "+961 70 123 456", place = "Table 5", note = "No onions",
            lines = new object[] { new { id = hummus, n = 2, price = 0.01 }, new { id = kafta, n = 1 } },
        }));
        Assert.Equal(101, order.GetProperty("number").GetInt32());
        Assert.Equal("new", order.GetProperty("status").GetString());
        Assert.Equal("$23", order.GetProperty("total").GetString()); // 2 x 5.50 + 12
        var token = order.GetProperty("token").GetString()!;

        var list = await Ok(owner.GetAsync("/menu/v1/orders"));
        Assert.Equal(1, list.GetProperty("open").GetInt32());
        var seen = list.GetProperty("orders").EnumerateArray().Single();
        Assert.Equal("Rami", seen.GetProperty("name").GetString());
        Assert.Equal("Table 5", seen.GetProperty("place").GetString());
        Assert.Equal("No onions", seen.GetProperty("note").GetString());
        var id = seen.GetProperty("id").GetInt32();

        await Ok(owner.PutAsJsonAsync($"/menu/v1/orders/{id}", new { status = "accepted" }));
        Assert.Equal("accepted", (await Ok(guest.GetAsync($"/m/{slug}/orders/{token}"))).GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PutAsJsonAsync($"/menu/v1/orders/{id}", new { status = "eaten" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await guest.GetAsync($"/m/{slug}/orders/not-a-token")).StatusCode);

        // The next order gets the next number.
        var second = await Ok(guest.PostAsJsonAsync($"/m/{slug}/orders", new { name = "Nour", lines = new[] { new { id = kafta, n = 1 } } }));
        Assert.Equal(102, second.GetProperty("number").GetInt32());
    }

    [Fact]
    public async Task Orders_that_cannot_be_made_are_refused_with_a_reason()
    {
        var (_, owner) = await ShopAsync("ord-bad");
        var (slug, hummus, kafta) = await MenuAsync(owner, "Picky Shop", MenuOrderModes.Request);
        await Ok(owner.PutAsJsonAsync($"/menu/v1/items/{kafta}", new { isSoldOut = true }));
        var guest = _cloud.CreateClient();
        async Task<HttpStatusCode> Send(object body) => (await guest.PostAsJsonAsync($"/m/{slug}/orders", body)).StatusCode;

        Assert.Equal(HttpStatusCode.BadRequest, await Send(new { name = "", lines = new[] { new { id = hummus, n = 1 } } }));
        Assert.Equal(HttpStatusCode.BadRequest, await Send(new { name = "Rami", lines = Array.Empty<object>() }));
        Assert.Equal(HttpStatusCode.BadRequest, await Send(new { name = "Rami", phone = "call me", lines = new[] { new { id = hummus, n = 1 } } }));
        Assert.Equal(HttpStatusCode.Conflict, await Send(new { name = "Rami", lines = new[] { new { id = kafta, n = 1 } } }));      // sold out
        Assert.Equal(HttpStatusCode.Conflict, await Send(new { name = "Rami", lines = new[] { new { id = 999999, n = 1 } } }));    // not on the menu
    }

    [Fact]
    public async Task One_phone_cannot_flood_a_menu_with_orders()
    {
        var (_, owner) = await ShopAsync("ord-flood");
        var (slug, hummus, _) = await MenuAsync(owner, "Busy Shop", MenuOrderModes.Request);
        var guest = _cloud.CreateClient();
        var codes = new List<HttpStatusCode>();
        for (var n = 0; n < 8; n++)
            codes.Add((await guest.PostAsJsonAsync($"/m/{slug}/orders", new { name = "Spam", lines = new[] { new { id = hummus, n = 1 } } })).StatusCode);
        Assert.Contains(HttpStatusCode.TooManyRequests, codes);
        Assert.Equal(6, codes.Count(c => c == HttpStatusCode.Created));
    }

    [Fact]
    public async Task An_order_sent_to_the_POS_reaches_that_till_with_its_products_and_the_till_moves_it_on()
    {
        var (site, owner) = await ShopAsync("ord-pos");
        await Ok(owner.PostAsJsonAsync("/menu/v1/editor", new { title = "Till Shop" }));
        // Sending to the POS needs the menu to follow one.
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PutAsJsonAsync("/menu/v1/settings", new { orderMode = "pos" })).StatusCode);
        await Ok(owner.PutAsJsonAsync("/menu/v1/settings", new { isPublished = true, branchIds = new[] { site.SiteId.ToString() } }));
        await Ok(_cloud.DeviceClient(site.Token).PostAsJsonAsync("/" + MenuCatalog.Route,
            new MenuCatalogPush("h1", [new MenuCatalogItem(42, "Shawarma", null, 6m, "$", "c1", "Wraps", 0, 0)]), SyncWire.Json));
        var state = await Ok(owner.PutAsJsonAsync("/menu/v1/settings", new { orderMode = "pos" }));
        var slug = state.GetProperty("menu").GetProperty("slug").GetString()!;
        var page = await Ok(owner.GetAsync($"/menu/v1/items?section={state.GetProperty("sections").EnumerateArray().Single().GetProperty("id").GetInt32()}"));
        var dish = page.GetProperty("items").EnumerateArray().Single().GetProperty("id").GetInt32();

        var sent = await Ok(_cloud.CreateClient().PostAsJsonAsync($"/m/{slug}/orders", new { name = "Maya", place = "Table 2", lines = new[] { new { id = dish, n = 3 } } }));
        Assert.Equal("pos", sent.GetProperty("route").GetString());

        var till = _cloud.DeviceClient(site.Token);
        var inbox = await Ok(till.GetAsync("/api/sync/menu-orders"));
        var order = inbox.GetProperty("orders").EnumerateArray().Single();
        var line = order.GetProperty("lines").EnumerateArray().Single();
        Assert.Equal(42, line.GetProperty("posProductId").GetInt32());
        Assert.Equal(3, line.GetProperty("quantity").GetInt32());
        Assert.Equal(6m, line.GetProperty("unitPrice").GetDecimal());

        await Ok(till.PutAsJsonAsync($"/api/sync/menu-orders/{order.GetProperty("id").GetInt32()}", new { status = "done" }));
        Assert.Empty((await Ok(till.GetAsync("/api/sync/menu-orders"))).GetProperty("orders").EnumerateArray());
        Assert.Equal("done", (await Ok(_cloud.CreateClient().GetAsync($"/m/{slug}/orders/{sent.GetProperty("token").GetString()}"))).GetProperty("status").GetString());
    }
}
