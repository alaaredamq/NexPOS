using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Cloud.Data;
using Cloud.Services;
using Cloud.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Sync;
using Xunit;

namespace Cloud.Tests;

/// <summary>
/// Staff logins with limits. The cloud enforces them: a table a login may not read is never handed out, a change it may not make is
/// never queued, and a limited login sees only the changes it made itself.
/// </summary>
public class OwnerRolesTests : IClassFixture<CloudFactory>
{
    private const string Password = "pass-1234-ok";
    private readonly CloudFactory _cloud;

    public OwnerRolesTests(CloudFactory cloud) => _cloud = cloud;

    private async Task<HttpClient> SignInAsync(string email)
    {
        var http = _cloud.CreateClient();
        var login = await http.PostAsJsonAsync("/owner/v1/auth/login", new { email, password = Password });
        login.EnsureSuccessStatusCode();
        var token = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return http;
    }

    /// <summary>One shop with an owner login and extra logins of the given roles. Returns clients by role name.</summary>
    private async Task<(SeededSite Site, Dictionary<string, HttpClient> By)> ShopAsync(string stem, params string[] roles)
    {
        var site = await _cloud.SeedSiteAsync(1, stem);
        var all = new[] { "owner" }.Concat(roles).ToArray();
        await _cloud.WithControlDbAsync(async db =>
        {
            foreach (var role in all)
                db.OwnerUsers.Add(new OwnerUser
                {
                    ClientId = site.ClientId, Email = $"{role}@{stem}.test", DisplayName = role,
                    PasswordHash = PasswordHasher.Hash(Password), CreatedAt = _cloud.Clock.GetUtcNow().UtcDateTime,
                    Permissions = (int)OwnerRoles.For(role),
                });
            await db.SaveChangesAsync();
            return 0;
        });
        var by = new Dictionary<string, HttpClient>();
        foreach (var role in all) by[role] = await SignInAsync($"{role}@{stem}.test");
        return (site, by);
    }

    private async Task PushRowsAsync(SeededSite site, params string[] tables)
    {
        var cipher = DataKey.Generate().Cipher();
        var changes = tables.Select((t, i) => new SyncChange(t, i + 1, false, null,
            cipher.Encrypt(t, i + 1, new Dictionary<string, object?> { ["Name"] = t }))).ToList();
        var batch = new SyncBatch("", 0, changes.Count, true, changes);
        var pushed = await _cloud.DeviceClient(site.Token).PostAsJsonAsync("api/sync/push", batch, SyncWire.Json);
        Assert.True(pushed.StatusCode == HttpStatusCode.OK, await pushed.Content.ReadAsStringAsync());
    }

    private static HttpRequestMessage Post(string url, object body, string key)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", key);
        return request;
    }

    // ---- presets ----

    [Fact]
    public void The_three_roles_mean_what_the_portal_says()
    {
        Assert.Equal(OwnerPermissions.All, OwnerRoles.For("owner"));
        Assert.Equal(OwnerPermissions.All, OwnerRoles.For(null)); // a login from before roles existed
        Assert.Equal(OwnerPermissions.All, OwnerRoles.For("nonsense")); // never silently narrower than the owner chose... by mistake it stays full

        var manager = OwnerRoles.For("manager");
        Assert.True(manager.HasFlag(OwnerPermissions.Sales | OwnerPermissions.Stock | OwnerPermissions.Debts | OwnerPermissions.Add | OwnerPermissions.Edit));
        Assert.False(manager.HasFlag(OwnerPermissions.Financials));

        var staff = OwnerRoles.For("staff");
        Assert.Equal(OwnerPermissions.Sales | OwnerPermissions.Stock, staff);

        Assert.Equal("owner", OwnerRoles.NameOf(OwnerPermissions.All));
        Assert.Equal("manager", OwnerRoles.NameOf(manager));
        Assert.Equal("staff", OwnerRoles.NameOf(staff));
        Assert.Equal("custom", OwnerRoles.NameOf(OwnerPermissions.Sales | OwnerPermissions.Debts));
    }

    [Fact]
    public async Task Signing_in_tells_the_phone_what_the_login_may_do()
    {
        var shop = await ShopAsync("rolesme", "manager", "staff");

        foreach (var (role, expected) in new[] { ("owner", "owner"), ("manager", "manager"), ("staff", "staff") })
        {
            var me = await shop.By[role].GetFromJsonAsync<JsonElement>("/owner/v1/me");
            Assert.Equal(expected, me.GetProperty("role").GetString());
            Assert.Equal((int)OwnerRoles.For(role), me.GetProperty("permissions").GetInt32());
        }
    }

    // ---- reading ----

    [Fact]
    public async Task A_login_only_gets_the_tables_its_role_allows()
    {
        var shop = await ShopAsync("rolesread", "manager", "staff");
        await PushRowsAsync(shop.Site, "Receipts", "Products", "Customers", "CashDrawerSession", "Currencies");

        async Task<HttpStatusCode> Read(string role, string table) => (await shop.By[role].GetAsync($"/owner/v1/rows?table={table}")).StatusCode;

        // The owner reads everything.
        foreach (var t in new[] { "Receipts", "Products", "Customers", "CashDrawerSession", "Currencies" })
            Assert.Equal(HttpStatusCode.OK, await Read("owner", t));

        // A manager: no cash drawer.
        Assert.Equal(HttpStatusCode.OK, await Read("manager", "Receipts"));
        Assert.Equal(HttpStatusCode.OK, await Read("manager", "Customers"));
        Assert.Equal(HttpStatusCode.Forbidden, await Read("manager", "CashDrawerSession"));

        // Staff: sales and stock, not customers' debts or the drawer; names of currencies for everyone.
        Assert.Equal(HttpStatusCode.OK, await Read("staff", "Receipts"));
        Assert.Equal(HttpStatusCode.OK, await Read("staff", "Products"));
        Assert.Equal(HttpStatusCode.OK, await Read("staff", "Currencies"));
        Assert.Equal(HttpStatusCode.Forbidden, await Read("staff", "Customers"));
        Assert.Equal(HttpStatusCode.Forbidden, await Read("staff", "CustomerDebtTransactions"));
        Assert.Equal(HttpStatusCode.Forbidden, await Read("staff", "Suppliers"));
        Assert.Equal(HttpStatusCode.Forbidden, await Read("staff", "CashDrawerSession"));
        Assert.Equal(HttpStatusCode.Forbidden, await Read("staff", "SomethingNew")); // unknown tables are for the owner only
    }

    [Fact]
    public async Task A_refused_table_is_refused_for_the_row_list_too_and_the_refusal_says_so()
    {
        var shop = await ShopAsync("rolesids", "staff");
        await PushRowsAsync(shop.Site, "Customers");

        var response = await shop.By["staff"].GetAsync("/owner/v1/row-ids?table=Customers");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("forbidden", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    // ---- changing ----

    [Fact]
    public async Task A_login_can_only_queue_the_changes_its_role_allows()
    {
        var shop = await ShopAsync("rolesacts", "manager", "staff");
        var sealedBody = new { enc = "e1.x" };

        async Task<HttpStatusCode> Send(string role, string route, string key) => (await shop.By[role].SendAsync(Post($"/owner/v1/{route}", sealedBody, key))).StatusCode;

        Assert.Equal(HttpStatusCode.Accepted, await Send("owner", "stock-changes", "role-owner-stock1"));
        Assert.Equal(HttpStatusCode.Accepted, await Send("manager", "stock-changes", "role-manager-stock1"));
        Assert.Equal(HttpStatusCode.Accepted, await Send("manager", "customers", "role-manager-cust1"));

        Assert.Equal(HttpStatusCode.Forbidden, await Send("staff", "stock-changes", "role-staff-stock1"));
        Assert.Equal(HttpStatusCode.Forbidden, await Send("staff", "product-edits", "role-staff-edit1"));
        Assert.Equal(HttpStatusCode.Forbidden, await Send("staff", "customers", "role-staff-cust1"));
        Assert.Equal(HttpStatusCode.Forbidden, await Send("staff", "products", "role-staff-prod1"));

        var queued = await _cloud.WithControlDbAsync(db => db.SiteCommands.CountAsync(c => c.SiteId == shop.Site.SiteId));
        Assert.Equal(3, queued); // the four refused ones left nothing behind
    }

    [Fact]
    public async Task A_limited_login_sees_only_its_own_changes_and_cannot_withdraw_anothers()
    {
        var shop = await ShopAsync("rolesown", "manager");
        var ownerChange = await (await shop.By["owner"].SendAsync(Post("/owner/v1/customers", new { enc = "e1.a" }, "own-by-owner-0001"))).Content.ReadFromJsonAsync<JsonElement>();
        var managerChange = await (await shop.By["manager"].SendAsync(Post("/owner/v1/customers", new { enc = "e1.b" }, "own-by-manager-01"))).Content.ReadFromJsonAsync<JsonElement>();
        var ownerId = ownerChange.GetProperty("id").GetGuid();
        var managerId = managerChange.GetProperty("id").GetGuid();

        var ownerSees = await shop.By["owner"].GetFromJsonAsync<JsonElement>("/owner/v1/commands");
        var managerSees = await shop.By["manager"].GetFromJsonAsync<JsonElement>("/owner/v1/commands");

        Assert.Equal(2, ownerSees.GetArrayLength()); // the owner sees everyone's
        Assert.Equal(managerId, Assert.Single(managerSees.EnumerateArray()).GetProperty("id").GetGuid());

        Assert.Equal(HttpStatusCode.NotFound, (await shop.By["manager"].GetAsync($"/owner/v1/commands/{ownerId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await shop.By["manager"].DeleteAsync($"/owner/v1/commands/{ownerId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await shop.By["manager"].DeleteAsync($"/owner/v1/commands/{managerId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await shop.By["owner"].GetAsync($"/owner/v1/commands/{managerId}")).StatusCode);
    }

    // ---- migration safety ----

    [Fact]
    public async Task A_login_made_before_roles_existed_is_a_full_owner()
    {
        var site = await _cloud.SeedSiteAsync(1, "rolesold");
        await _cloud.WithControlDbAsync(async db =>
        {
            // Not setting Permissions: the column's default (63) is what an old row gets.
            db.OwnerUsers.Add(new OwnerUser
            {
                ClientId = site.ClientId, Email = "old@rolesold.test", DisplayName = "Old",
                PasswordHash = PasswordHasher.Hash(Password), CreatedAt = _cloud.Clock.GetUtcNow().UtcDateTime,
            });
            await db.SaveChangesAsync();
            return 0;
        });

        var me = await (await SignInAsync("old@rolesold.test")).GetFromJsonAsync<JsonElement>("/owner/v1/me");

        Assert.Equal("owner", me.GetProperty("role").GetString());
    }
}
