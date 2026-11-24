using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Cloud.Data;
using Cloud.Services;
using Cloud.Tests.Support;
using Common.Licensing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Sync;
using Xunit;

namespace Cloud.Tests;

/// <summary>Licence keys in the cloud: issuing online, recording keys from elsewhere, linking them, and POS-only clients.</summary>
public class LicenseTests
{
    private const string Api = "/api/admin/v1";
    private static readonly string Machine = MachineId.FromComponents(["cpu-a", "board-a", "disk-a", "bios-a"]).Format();
    private static readonly string OtherMachine = MachineId.FromComponents(["cpu-b", "board-b", "disk-b", "bios-b"]).Format();

    private static async Task<HttpClient> ApiAsync(CloudFactory cloud)
    {
        var c = cloud.CreateClient(new() { AllowAutoRedirect = false });
        var login = await c.PostAsJsonAsync($"{Api}/auth/login", new { username = CloudFactory.AdminUser, password = CloudFactory.AdminPassword });
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString());
        return c;
    }

    private static async Task<HttpClient> PortalAsync(CloudFactory cloud)
    {
        var c = cloud.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = true });
        await HtmlForms.PostAsync(c, "/admin/Account/Login", "/admin/Account/Login",
            ("Username", CloudFactory.AdminUser), ("Password", CloudFactory.AdminPassword));
        return c;
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage r, HttpStatusCode expected)
    {
        var body = await r.Content.ReadAsStringAsync();
        Assert.True(r.StatusCode == expected, $"Expected {expected}, got {r.StatusCode}: {body}");
        return body.Length == 0 ? default : JsonDocument.Parse(body).RootElement.Clone();
    }

    private static string Sign(CloudFactory cloud, string machine, LicenseKind kind, DateOnly issued, DateOnly until, uint serial, string customer,
        LicensePlan plan = LicensePlan.Pro, int seats = 1)
    {
        MachineId.TryParse(machine, out var id);
        return LicenseCodec.Encode(new LicensePayload(kind, id, issued, until, serial, customer, seats, plan), cloud.LicenseIssuer);
    }

    [Fact]
    public async Task A_key_is_issued_online_linked_to_its_client_branch_and_payment()
    {
        using var cloud = new CloudFactory();
        var seeded = await cloud.SeedSiteAsync(clientName: "Key Market");
        await cloud.WithControlDbAsync(async db =>
        {
            var site = await db.Sites.FirstAsync(s => s.Id == seeded.SiteId);
            site.MachineId = Machine;
            return await db.SaveChangesAsync();
        });
        var app = await ApiAsync(cloud);

        var signer = await JsonAsync(await app.GetAsync($"{Api}/licenses/signer"), HttpStatusCode.OK);
        Assert.True(signer.GetProperty("ready").GetBoolean());

        // The passphrase is needed every time, and a wrong one signs nothing.
        var wrong = await app.PostAsJsonAsync($"{Api}/licenses/issue",
            new { clientId = seeded.ClientId, machineId = Machine, kind = "full", plan = "pro", passphrase = "not it" });
        Assert.Equal("wrong_passphrase", (await JsonAsync(wrong, HttpStatusCode.BadRequest)).GetProperty("code").GetString());
        Assert.Equal(0, await cloud.WithControlDbAsync(db => db.Licenses.CountAsync()));

        var basicTwo = await app.PostAsJsonAsync($"{Api}/licenses/issue",
            new { clientId = seeded.ClientId, machineId = Machine, kind = "full", plan = "basic", seats = 2, passphrase = CloudFactory.SigningPassphrase });
        Assert.Equal("bad_seats", (await JsonAsync(basicTwo, HttpStatusCode.BadRequest)).GetProperty("code").GetString());
        var noPlan = await app.PostAsJsonAsync($"{Api}/licenses/issue",
            new { clientId = seeded.ClientId, machineId = Machine, kind = "full", passphrase = CloudFactory.SigningPassphrase });
        Assert.Equal("bad_plan", (await JsonAsync(noPlan, HttpStatusCode.BadRequest)).GetProperty("code").GetString());

        var issued = await JsonAsync(await app.PostAsJsonAsync($"{Api}/licenses/issue", new
        {
            clientId = seeded.ClientId, machineId = Machine.ToLowerInvariant(), kind = "provisional", plan = "pro", seats = 3, days = 45,
            paymentAmount = 200, paymentMethod = "Whish", passphrase = CloudFactory.SigningPassphrase,
        }), HttpStatusCode.Created);
        var license = issued.GetProperty("license");
        var key = license.GetProperty("key").GetString()!;

        // A real key the shop PC accepts: right PC, plan, PCs and end date, and the client's name inside.
        var decoded = LicenseCodec.Decode(key, Convert.FromBase64String(LicenseCodec.ExportPublicKeyBase64(cloud.LicenseIssuer)));
        Assert.True(decoded.Success);
        Assert.Equal(Machine, decoded.Payload!.Machine.Format());
        Assert.Equal(LicenseKind.Provisional, decoded.Payload.Kind);
        Assert.Equal(3, decoded.Payload.Seats);
        Assert.Equal("Key Market", decoded.Payload.Customer);
        Assert.Equal(new DateOnly(2026, 11, 5).AddDays(45), decoded.Payload.ValidUntil);

        // Linked to the client and the branch with that Machine ID, with its payment in Finance.
        Assert.Equal(seeded.ClientId, license.GetProperty("clientId").GetInt32());
        Assert.Equal(seeded.SiteId, license.GetProperty("siteId").GetGuid());
        Assert.Equal("Cloud", license.GetProperty("source").GetString());
        var payment = await cloud.WithControlDbAsync(db => db.Payments.AsNoTracking().SingleAsync());
        Assert.Equal(200, payment.Amount);
        Assert.Equal("POS licence", payment.Purpose);
        Assert.Equal(payment.Id, license.GetProperty("paymentId").GetInt32());

        // Keys signed here stay on record.
        var id = license.GetProperty("id").GetInt32();
        Assert.Equal("issued_here", (await JsonAsync(await app.DeleteAsync($"{Api}/licenses/{id}"), HttpStatusCode.BadRequest)).GetProperty("code").GetString());

        // A renewal replaces it; the old one is then "Superseded" and drops out of the default list.
        cloud.Clock.Now = cloud.Clock.Now.AddDays(1);
        await JsonAsync(await app.PostAsJsonAsync($"{Api}/licenses/issue", new
        {
            clientId = seeded.ClientId, machineId = Machine, kind = "full", plan = "pro", seats = 3, passphrase = CloudFactory.SigningPassphrase,
        }), HttpStatusCode.Created);
        var current = await JsonAsync(await app.GetAsync($"{Api}/clients/{seeded.ClientId}/licenses"), HttpStatusCode.OK);
        Assert.Contains(current.EnumerateArray(), l => l.GetProperty("id").GetInt32() == id && l.GetProperty("status").GetString() == "Superseded");
        Assert.Single((await JsonAsync(await app.GetAsync($"{Api}/licenses"), HttpStatusCode.OK)).EnumerateArray());

        // The client's history says what was signed.
        var events = await JsonAsync(await app.GetAsync($"{Api}/clients/{seeded.ClientId}/events"), HttpStatusCode.OK);
        Assert.Contains(events.EnumerateArray(), e => e.GetProperty("text").GetString()!.StartsWith("Licence key issued: Pro · Provisional · 3 PCs"));
    }

    [Fact]
    public async Task Issuing_is_off_without_a_protected_signing_key()
    {
        using var cloud = new CloudFactory();
        var app = await ApiAsync(cloud);
        // The signer reads the file at each issue: swap in the same key without its passphrase.
        File.WriteAllText(cloud.SigningKeyPath, cloud.LicenseIssuer.ExportPkcs8PrivateKeyPem());
        var state = await JsonAsync(await app.GetAsync($"{Api}/licenses/signer"), HttpStatusCode.OK);
        Assert.Equal("NotProtected", state.GetProperty("state").GetString());

        var refused = await app.PostAsJsonAsync($"{Api}/licenses/issue",
            new { machineId = Machine, customer = "Anyone", kind = "trial", plan = "basic", passphrase = "x" });
        Assert.Equal("signer_notprotected", (await JsonAsync(refused, HttpStatusCode.Conflict)).GetProperty("code").GetString());

        File.Delete(cloud.SigningKeyPath);
        Assert.Equal("Missing", (await JsonAsync(await app.GetAsync($"{Api}/licenses/signer"), HttpStatusCode.OK)).GetProperty("state").GetString());
    }

    [Fact]
    public async Task A_signing_key_the_app_does_not_trust_is_refused()
    {
        using var cloud = new CloudFactory();
        using var stranger = LicenseCodec.CreatePrivateKey();
        var app = await ApiAsync(cloud);
        File.WriteAllText(cloud.SigningKeyPath, stranger.ExportEncryptedPkcs8PrivateKeyPem("pw",
            new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 1000)));
        var refused = await app.PostAsJsonAsync($"{Api}/licenses/issue",
            new { machineId = Machine, customer = "Anyone", kind = "trial", plan = "basic", passphrase = "pw" });
        Assert.Equal("key_mismatch", (await JsonAsync(refused, HttpStatusCode.Conflict)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Keys_from_the_desktop_keygen_and_issued_csv_are_recorded_and_linked()
    {
        using var cloud = new CloudFactory();
        var seeded = await cloud.SeedSiteAsync(clientName: "Csv Shop");
        var app = await ApiAsync(cloud);

        // The keygen reports each key it signs: recorded once, matched to the client by the exact name in the key.
        var key = Sign(cloud, OtherMachine, LicenseKind.Full, new(2026, 6, 1), new(2027, 6, 1), 0xABCDEF01, "Csv Shop");
        var first = await JsonAsync(await app.PostAsJsonAsync($"{Api}/licenses/record", new { key, source = "Keygen" }), HttpStatusCode.Created);
        Assert.Equal(seeded.ClientId, first.GetProperty("clientId").GetInt32());
        Assert.Equal("Keygen", first.GetProperty("source").GetString());
        await JsonAsync(await app.PostAsJsonAsync($"{Api}/licenses/record", new { key, source = "Keygen" }), HttpStatusCode.OK);

        // An edited key fails its signature.
        var forged = key[..^3] + (key[^3] == 'A' ? "B" : "A") + key[^2..];
        Assert.Equal("bad_key", (await JsonAsync(await app.PostAsJsonAsync($"{Api}/licenses/record", new { key = forged }), HttpStatusCode.BadRequest)).GetProperty("code").GetString());

        // issued.csv: a key for the same PC links to the same client; an unknown name waits unlinked; the keygen's key is already here.
        var csv = new StringBuilder("﻿issued_on,customer,kind,valid_until,machine_id,serial,seats,plan\r\n")
            .Append($"2026-06-01,\"Csv Shop\",Full,2027-06-01,{OtherMachine},ABCDEF01,1,Pro\r\n")
            .Append($"2025-01-10,\"Csv Shop, old name\",Trial,2025-01-24,{OtherMachine},00000010\r\n")
            .Append($"2026-02-01,\"Nobody We Know\",Provisional,2026-03-18,{Machine},00000011,1,Basic\r\n")
            .Append("garbage line\r\n").ToString();
        var import = await JsonAsync(await app.PostAsync($"{Api}/licenses/import", new StringContent(csv, Encoding.UTF8, "text/csv")), HttpStatusCode.OK);
        Assert.Equal(2, import.GetProperty("added").GetInt32());
        Assert.Equal(1, import.GetProperty("alreadyKnown").GetInt32());
        Assert.Equal(1, import.GetProperty("linked").GetInt32());
        Assert.Equal(1, import.GetProperty("problems").GetArrayLength());

        var unassigned = await JsonAsync(await app.GetAsync($"{Api}/licenses?show=unassigned"), HttpStatusCode.OK);
        var stray = unassigned.EnumerateArray().Single();
        Assert.Equal("Nobody We Know", stray.GetProperty("customer").GetString());
        Assert.Equal("Basic", stray.GetProperty("plan").GetString());

        // Link it by hand, with a note; then unlink it again with an explicit null.
        var linked = await JsonAsync(await app.PutAsJsonAsync($"{Api}/licenses/{stray.GetProperty("id").GetInt32()}", new { clientId = seeded.ClientId, note = "Second shop" }), HttpStatusCode.OK);
        Assert.Equal(seeded.ClientId, linked.GetProperty("clientId").GetInt32());
        Assert.Equal("Second shop", linked.GetProperty("note").GetString());
        var unlinked = await JsonAsync(await app.PutAsync($"{Api}/licenses/{stray.GetProperty("id").GetInt32()}",
            new StringContent("{\"clientId\":null}", Encoding.UTF8, "application/json")), HttpStatusCode.OK);
        Assert.Equal(JsonValueKind.Null, unlinked.GetProperty("clientId").ValueKind);
        Assert.Equal("Second shop", unlinked.GetProperty("note").GetString());

        // Imported records can be removed (the key keeps working at the shop regardless).
        Assert.Equal(HttpStatusCode.NoContent, (await app.DeleteAsync($"{Api}/licenses/{stray.GetProperty("id").GetInt32()}")).StatusCode);
    }

    [Fact]
    public async Task A_key_shown_by_a_connected_shop_PC_is_recorded_against_that_branch()
    {
        using var cloud = new CloudFactory();
        var seeded = await cloud.SeedSiteAsync(clientName: "Till Shop");
        var till = cloud.DeviceClient(seeded.Token);
        var key = cloud.IssueLicense(LicensePlan.Basic, LicenseKind.Provisional, until: new DateOnly(2026, 11, 12));
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/sync/ping");
        request.Headers.TryAddWithoutValidation(SyncHeaders.License, key);
        Assert.Equal(HttpStatusCode.OK, (await till.SendAsync(request)).StatusCode);

        var recorded = await cloud.WithControlDbAsync(db => db.Licenses.AsNoTracking().SingleAsync());
        Assert.Equal(seeded.ClientId, recorded.ClientId);
        Assert.Equal(seeded.SiteId, recorded.SiteId);
        Assert.Equal("Till", recorded.Source);
        Assert.Null(recorded.KeyText);            // the key itself is not copied off the shop PC
        Assert.Equal("Till Shop", recorded.Customer);

        // It ends within two weeks: the overview says so, and Finance lists it as a renewal.
        var app = await ApiAsync(cloud);
        var overview = await JsonAsync(await app.GetAsync($"{Api}/overview"), HttpStatusCode.OK);
        Assert.Contains(overview.GetProperty("attention").EnumerateArray(), a => a.GetProperty("reason").GetString()!.StartsWith("Provisional licence ends 2026-11-12"));
        var finance = await JsonAsync(await app.GetAsync($"{Api}/finance/summary"), HttpStatusCode.OK);
        Assert.Contains(finance.GetProperty("renewals").EnumerateArray(), r => r.GetProperty("what").GetString() == "Provisional licence ends");

        // Checking in again does not add it twice.
        using var again = new HttpRequestMessage(HttpMethod.Get, "/api/sync/ping");
        again.Headers.TryAddWithoutValidation(SyncHeaders.License, key);
        await till.SendAsync(again);
        Assert.Equal(1, await cloud.WithControlDbAsync(db => db.Licenses.CountAsync()));

        // Removing the branch keeps the record, unlinked from the branch.
        var c = await PortalAsync(cloud);
        await HtmlForms.PostAsync(c, $"/admin/Sites/Details/{seeded.SiteId}", $"/admin/Sites/Details/{seeded.SiteId}?handler=Remove");
        var kept = await cloud.WithControlDbAsync(db => db.Licenses.AsNoTracking().SingleAsync());
        Assert.Null(kept.SiteId);
        Assert.Equal(seeded.ClientId, kept.ClientId);
    }

    [Fact]
    public async Task A_POS_only_client_has_no_branch_and_is_not_asked_to_connect()
    {
        using var cloud = new CloudFactory();
        var c = await PortalAsync(cloud);
        var created = await HtmlForms.PostAsync(c, "/admin/Clients/Create", "/admin/Clients/Create",
            ("Name", "Offline Grocer"), ("Type", "pos"), ("Months", "3"));
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        var url = created.Headers.Location!.OriginalString;
        Assert.Matches(@"^/admin/Clients/Details/\d+#licences$", url);
        var id = int.Parse(url["/admin/Clients/Details/".Length..url.IndexOf('#')]);

        var client = await cloud.WithControlDbAsync(db => db.Clients.Include(x => x.Sites).Include(x => x.Subscription).AsNoTracking().SingleAsync(x => x.Id == id));
        Assert.False(client.HasOnline);
        Assert.Empty(client.Sites);
        Assert.Null(client.Subscription); // POS only buys no Online months

        var page = await c.GetStringAsync($"/admin/Clients/Details/{id}");
        Assert.Contains("POS only", page);
        Assert.Contains("Issue a key", page);
        var list = await c.GetStringAsync("/admin");
        Assert.DoesNotContain("No branch added yet", list);
        Assert.Contains("POS only", list);

        // Later they buy Online: a branch and its link appear, ready to send.
        var on = await HtmlForms.PostAsync(c, $"/admin/Clients/Details/{id}", $"/admin/Clients/Details/{id}?handler=Online&on=true");
        Assert.Matches(@"^/admin/Sites/Details/[0-9a-f-]{36}$", on.Headers.Location!.OriginalString);
        Assert.Contains("/enroll/", await c.GetStringAsync(on.Headers.Location!.OriginalString));

        // The API makes one too.
        var app = await ApiAsync(cloud);
        var made = await JsonAsync(await app.PostAsJsonAsync($"{Api}/clients", new { name = "Api Offline", online = false }), HttpStatusCode.Created);
        Assert.Equal("pos", made.GetProperty("client").GetProperty("type").GetString());
        Assert.Equal(JsonValueKind.Null, made.GetProperty("link").ValueKind);
    }

    [Fact]
    public async Task The_portal_issues_a_key_and_shows_it_ready_to_send()
    {
        using var cloud = new CloudFactory();
        var seeded = await cloud.SeedSiteAsync(clientName: "Portal Key Shop");
        var c = await PortalAsync(cloud);

        var bad = await HtmlForms.PostAsync(c, "/admin/Licenses", "/admin/Licenses?handler=Issue",
            ("Issue.ClientId", seeded.ClientId.ToString()), ("Issue.MachineId", Machine), ("Issue.Plan", "pro"), ("Issue.Kind", "full"),
            ("Issue.Seats", "1"), ("passphrase", "wrong"));
        Assert.Equal(HttpStatusCode.OK, bad.StatusCode);
        Assert.Contains("Wrong passphrase", await bad.Content.ReadAsStringAsync());

        var ok = await HtmlForms.PostAsync(c, "/admin/Licenses", "/admin/Licenses?handler=Issue",
            ("Issue.ClientId", seeded.ClientId.ToString()), ("Issue.MachineId", Machine), ("Issue.Plan", "basic"), ("Issue.Kind", "trial"),
            ("Issue.Seats", "1"), ("passphrase", CloudFactory.SigningPassphrase));
        Assert.Equal(HttpStatusCode.Redirect, ok.StatusCode);
        var page = await c.GetStringAsync("/admin/Licenses");
        Assert.Contains("Key issued: send it to the shop", page);
        Assert.Matches(@"NXL1-[A-Z2-7-]+", page);
        Assert.Contains("https://wa.me/?text=", page);

        // Shown once as "new"; it stays in the list with a Copy button.
        var again = await c.GetStringAsync("/admin/Licenses");
        Assert.DoesNotContain("Key issued: send it to the shop", again);
        Assert.Contains("Copy key", again);
        Assert.Contains("Copy key", await c.GetStringAsync($"/admin/Clients/Details/{seeded.ClientId}"));
    }
}
