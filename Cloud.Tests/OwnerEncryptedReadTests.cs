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
/// The phone reads the shop's encrypted copy as ciphertext and decrypts it itself. These tests are about what the cloud hands out:
/// only the signed-in owner's own rows, in pages that can be resumed, and never anything it could read.
/// </summary>
public class OwnerEncryptedReadTests : IClassFixture<CloudFactory>
{
    private readonly CloudFactory _cloud;

    public OwnerEncryptedReadTests(CloudFactory cloud) => _cloud = cloud;

    private async Task<(SeededSite Site, HttpClient Owner)> SeedOwnerAsync(string email, string clientName)
    {
        var site = await _cloud.SeedSiteAsync(1, clientName);
        await _cloud.WithControlDbAsync(async db =>
        {
            db.OwnerUsers.Add(new OwnerUser
            {
                ClientId = site.ClientId, Email = email, DisplayName = "Owner",
                PasswordHash = PasswordHasher.Hash("pass-1234-ok"), CreatedAt = _cloud.Clock.GetUtcNow().UtcDateTime,
            });
            await db.SaveChangesAsync();
            return 0;
        });
        var http = _cloud.CreateClient();
        var login = await http.PostAsJsonAsync("/owner/v1/auth/login", new { email, password = "pass-1234-ok" });
        var token = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (site, http);
    }

    /// <summary>What a till does: encrypts each row with the shop's key and pushes it through the real sync API.</summary>
    private async Task PushAsync(SeededSite site, RowCipher cipher, long from, params (string Table, long Id, string Name)[] rows)
    {
        var changes = rows.Select(r => new SyncChange(r.Table, r.Id, false, null,
            cipher.Encrypt(r.Table, r.Id, new Dictionary<string, object?> { ["Name"] = r.Name }))).ToList();
        var batch = new SyncBatch("", from, from + rows.Length, from == 0, changes);
        var response = await _cloud.DeviceClient(site.Token).PostAsJsonAsync("api/sync/push", batch, SyncWire.Json);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static JsonElement[] Rows(JsonElement page) => page.GetProperty("rows").EnumerateArray().ToArray();

    [Fact]
    public async Task Reading_encrypted_rows_needs_a_sign_in()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _cloud.CreateClient().GetAsync("/owner/v1/rows?table=Products")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _cloud.CreateClient().GetAsync("/owner/v1/row-ids?table=Products")).StatusCode);
    }

    [Fact]
    public async Task The_owner_gets_back_ciphertext_that_only_the_shops_key_opens()
    {
        var key = DataKey.Generate();
        var owner = await SeedOwnerAsync("rows1@shop.test", "Rows One");
        await PushAsync(owner.Site, key.Cipher(), 0, ("Products", 7, "Zaatar Manakish"));

        var page = await owner.Owner.GetFromJsonAsync<JsonElement>("/owner/v1/rows?table=Products");

        var row = Assert.Single(Rows(page));
        var payload = row.GetProperty("payload").GetString()!;
        Assert.Equal(7, row.GetProperty("id").GetInt32());
        Assert.StartsWith("e1.", payload);
        Assert.DoesNotContain("Zaatar", payload);
        Assert.Equal("Zaatar Manakish", key.Cipher().Decrypt("Products", 7, payload)["Name"] is JsonElement name ? name.GetString() : null);
        Assert.False(page.GetProperty("hasMore").GetBoolean());
        Assert.True(page.TryGetProperty("asOfUtc", out _), "every read says how fresh it is");
    }

    [Fact]
    public async Task A_big_table_comes_in_pages_that_resume_where_the_last_one_stopped()
    {
        var key = DataKey.Generate();
        var owner = await SeedOwnerAsync("rows2@shop.test", "Rows Two");
        await PushAsync(owner.Site, key.Cipher(), 0, Enumerable.Range(1, 5).Select(i => ("Products", (long)i, $"P{i}")).ToArray());

        var seen = new List<int>();
        string? cursor = null;
        var pages = 0;
        while (true)
        {
            var url = "/owner/v1/rows?table=Products&limit=2" + (cursor is null ? "" : "&cursor=" + Uri.EscapeDataString(cursor));
            var page = await owner.Owner.GetFromJsonAsync<JsonElement>(url);
            seen.AddRange(Rows(page).Select(r => r.GetProperty("id").GetInt32()));
            pages++;
            cursor = page.GetProperty("nextCursor").GetString();
            if (!page.GetProperty("hasMore").GetBoolean()) break;
            Assert.True(pages < 10, "paging did not end");
        }

        Assert.Equal([1, 2, 3, 4, 5], seen.Order().ToArray());
        Assert.Equal(3, pages);

        // Asking again from the last cursor returns nothing new, and hands the same cursor back.
        var after = await owner.Owner.GetFromJsonAsync<JsonElement>("/owner/v1/rows?table=Products&cursor=" + Uri.EscapeDataString(cursor!));
        Assert.Empty(Rows(after));
        Assert.Equal(cursor, after.GetProperty("nextCursor").GetString());
    }

    [Fact]
    public async Task A_row_changed_after_the_cursor_is_returned_again()
    {
        var key = DataKey.Generate();
        var owner = await SeedOwnerAsync("rows3@shop.test", "Rows Three");
        await PushAsync(owner.Site, key.Cipher(), 0, ("Products", 1, "Old name"));
        var first = await owner.Owner.GetFromJsonAsync<JsonElement>("/owner/v1/rows?table=Products");
        var cursor = first.GetProperty("nextCursor").GetString()!;

        await Task.Delay(5); // arrival times must differ
        await PushAsync(owner.Site, key.Cipher(), 1, ("Products", 1, "New name"));
        var changed = await owner.Owner.GetFromJsonAsync<JsonElement>("/owner/v1/rows?table=Products&cursor=" + Uri.EscapeDataString(cursor));

        var row = Assert.Single(Rows(changed));
        Assert.Equal("New name", ((JsonElement)key.Cipher().Decrypt("Products", 1, row.GetProperty("payload").GetString()!)["Name"]!).GetString());
    }

    [Fact]
    public async Task Row_ids_let_the_phone_forget_what_the_shop_deleted()
    {
        var key = DataKey.Generate();
        var owner = await SeedOwnerAsync("rows4@shop.test", "Rows Four");
        await PushAsync(owner.Site, key.Cipher(), 0, ("Customers", 1, "A"), ("Customers", 2, "B"), ("Products", 9, "Other table"));
        var delete = new SyncBatch("", 3, 4, false, [new SyncChange("Customers", 1, true, null)]);
        Assert.Equal(HttpStatusCode.OK, (await _cloud.DeviceClient(owner.Site.Token).PostAsJsonAsync("api/sync/push", delete, SyncWire.Json)).StatusCode);

        var ids = await owner.Owner.GetFromJsonAsync<JsonElement>("/owner/v1/row-ids?table=Customers");

        Assert.Equal([2], ids.GetProperty("ids").EnumerateArray().Select(i => i.GetInt32()).ToArray());
    }

    [Fact]
    public async Task An_owner_only_ever_gets_their_own_shops_rows()
    {
        var key = DataKey.Generate();
        var alice = await SeedOwnerAsync("alice-rows@shop.test", "Alice Rows");
        var bob = await SeedOwnerAsync("bob-rows@shop.test", "Bob Rows");
        await PushAsync(alice.Site, key.Cipher(), 0, ("Products", 1, "Alices product"));

        var bobs = await bob.Owner.GetFromJsonAsync<JsonElement>("/owner/v1/rows?table=Products");
        var bobIds = await bob.Owner.GetFromJsonAsync<JsonElement>("/owner/v1/row-ids?table=Products");

        Assert.Empty(Rows(bobs));
        Assert.Empty(bobIds.GetProperty("ids").EnumerateArray());
    }

    [Fact]
    public async Task A_missing_table_or_a_made_up_cursor_is_a_clear_refusal_not_a_crash()
    {
        var owner = await SeedOwnerAsync("rows5@shop.test", "Rows Five");

        Assert.Equal(HttpStatusCode.BadRequest, (await owner.Owner.GetAsync("/owner/v1/rows")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.Owner.GetAsync("/owner/v1/rows?table=Products&cursor=garbage")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.Owner.GetAsync("/owner/v1/row-ids")).StatusCode);
    }

    [Fact]
    public async Task A_table_name_cannot_be_used_to_reach_other_data()
    {
        var owner = await SeedOwnerAsync("rows6@shop.test", "Rows Six");

        var page = await owner.Owner.GetAsync("/owner/v1/rows?table=" + Uri.EscapeDataString("Products' OR '1'='1"));

        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Empty(Rows(await page.Content.ReadFromJsonAsync<JsonElement>()));
    }

    [Fact]
    public async Task Reading_encrypted_rows_stops_when_the_plan_has_lapsed()
    {
        var owner = await SeedOwnerAsync("rows7@shop.test", "Rows Seven");
        Assert.Equal(HttpStatusCode.OK, (await owner.Owner.GetAsync("/owner/v1/rows?table=Products")).StatusCode);

        await _cloud.WithControlDbAsync(async db =>
        {
            var subscription = db.Subscriptions.First(s => s.ClientId == owner.Site.ClientId);
            subscription.ExpiresAt = _cloud.Clock.GetUtcNow().UtcDateTime.AddDays(-30);
            await db.SaveChangesAsync();
            return 0;
        });

        Assert.Equal(HttpStatusCode.PaymentRequired, (await owner.Owner.GetAsync("/owner/v1/rows?table=Products")).StatusCode);
    }
}
