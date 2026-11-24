using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cloud.Services;
using Cloud.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Cloud.Tests;

/// <summary>The portal screen where the provider creates the sign-in the shop owner uses on the phone.</summary>
public class PhoneLoginPortalTests : IClassFixture<CloudFactory>
{
    private readonly CloudFactory _cloud;

    public PhoneLoginPortalTests(CloudFactory cloud) => _cloud = cloud;

    private async Task<HttpClient> AdminAsync()
    {
        var c = _cloud.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = true });
        var r = await HtmlForms.PostAsync(c, "/admin/Account/Login", "/admin/Account/Login",
            ("Username", CloudFactory.AdminUser), ("Password", CloudFactory.AdminPassword));
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        return c;
    }

    private static Task<HttpResponseMessage> Post(HttpClient c, int clientId, string handlerAndQuery, params (string, string)[] fields)
        => HtmlForms.PostAsync(c, $"/admin/Clients/Details/{clientId}", $"/admin/Clients/Details/{clientId}?handler={handlerAndQuery}", fields);

    /// <summary>What the next page load shows once: the sign-in name and password to hand to the owner.</summary>
    private static async Task<(string Email, string Password)?> HandedOverAsync(HttpClient c, int clientId)
    {
        var html = await c.GetStringAsync($"/admin/Clients/Details/{clientId}");
        var m = Regex.Match(html, @"Sign in with: (?<e>[^\s<]+)\s*Password: (?<p>[A-Za-z0-9]+)");
        return m.Success ? (m.Groups["e"].Value, m.Groups["p"].Value) : null;
    }

    private async Task<HttpResponseMessage> PhoneLoginAsync(string email, string password)
        => await _cloud.CreateClient().PostAsJsonAsync("/owner/v1/auth/login", new { email, password });

    private async Task<HttpClient> SignedInPhoneAsync(string email, string password)
    {
        var phone = _cloud.CreateClient();
        var login = await phone.PostAsJsonAsync("/owner/v1/auth/login", new { email, password });
        var token = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
        phone.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return phone;
    }

    private Task<int> OwnerIdAsync(string email)
        => _cloud.WithControlDbAsync(db => db.OwnerUsers.Where(u => u.Email == email).Select(u => u.Id).SingleAsync());

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}@shop.example";

    [Fact]
    public async Task A_login_is_made_with_the_chosen_role_and_the_phone_is_told_what_it_may_do()
    {
        var site = await _cloud.SeedSiteAsync(clientName: "Roles Minimarket");
        var admin = await AdminAsync();
        var manager = Unique("sam");
        var staff = Unique("rita");
        var plain = Unique("owner");

        await Post(admin, site.ClientId, "AddOwner", ("Email", manager), ("DisplayName", "Sam"), ("Password", "manager-pass-1"), ("Role", "manager"));
        await Post(admin, site.ClientId, "AddOwner", ("Email", staff), ("DisplayName", "Rita"), ("Password", "staff-pass-001"), ("Role", "staff"));
        await Post(admin, site.ClientId, "AddOwner", ("Email", plain), ("DisplayName", "Owner"), ("Password", "owner-pass-001")); // no role chosen: the owner

        async Task<JsonElement> Me(string email, string password)
            => await (await SignedInPhoneAsync(email, password)).GetFromJsonAsync<JsonElement>("/owner/v1/me");

        Assert.Equal("manager", (await Me(manager, "manager-pass-1")).GetProperty("role").GetString());
        Assert.Equal("staff", (await Me(staff, "staff-pass-001")).GetProperty("role").GetString());
        Assert.Equal("owner", (await Me(plain, "owner-pass-001")).GetProperty("role").GetString());

        var html = await admin.GetStringAsync($"/admin/Clients/Details/{site.ClientId}");
        Assert.Contains("Manager", html);
        Assert.Contains("Staff", html);
        Assert.Contains("What can they do?", html);
    }

    [Fact]
    public async Task Changing_a_role_takes_effect_at_once_by_signing_that_phone_out()
    {
        var site = await _cloud.SeedSiteAsync(clientName: "Role Change Shop");
        var admin = await AdminAsync();
        var email = Unique("change");
        await Post(admin, site.ClientId, "AddOwner", ("Email", email), ("DisplayName", "Chris"), ("Password", "change-pass-01"), ("Role", "manager"));
        var phone = await SignedInPhoneAsync(email, "change-pass-01");
        Assert.Equal(HttpStatusCode.OK, (await phone.GetAsync("/owner/v1/me")).StatusCode);
        var ownerId = await OwnerIdAsync(email);

        var changed = await Post(admin, site.ClientId, $"SetRole&ownerId={ownerId}", ("role", "staff"));
        Assert.Equal(HttpStatusCode.Redirect, changed.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await phone.GetAsync("/owner/v1/me")).StatusCode);
        var again = await (await SignedInPhoneAsync(email, "change-pass-01")).GetFromJsonAsync<JsonElement>("/owner/v1/me");
        Assert.Equal("staff", again.GetProperty("role").GetString());

        await _cloud.WithControlDbAsync(async db =>
        {
            var history = await db.ClientEvents.Where(e => e.ClientId == site.ClientId).Select(e => e.Text).ToListAsync();
            Assert.Contains(history, t => t.Contains("role") && t.Contains(email) && t.Contains("staff"));
            return 0;
        });
    }

    [Fact]
    public async Task A_generated_password_is_shown_once_and_signs_the_owner_in_on_the_phone()
    {
        var site = await _cloud.SeedSiteAsync(clientName: "Hassan Minimarket");
        var admin = await AdminAsync();
        var email = Unique("hassan");

        var added = await Post(admin, site.ClientId, "AddOwner", ("Email", email), ("DisplayName", "Hassan"), ("Password", ""));
        Assert.Equal(HttpStatusCode.Redirect, added.StatusCode);

        var handed = await HandedOverAsync(admin, site.ClientId);
        Assert.NotNull(handed);
        Assert.Equal(email, handed!.Value.Email);
        Assert.True(handed.Value.Password.Length >= 12);
        Assert.Null(await HandedOverAsync(admin, site.ClientId)); // shown once

        var login = await PhoneLoginAsync(email, handed.Value.Password);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var body = await login.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Hassan", body.GetProperty("displayName").GetString());
        Assert.Equal("Hassan Minimarket", body.GetProperty("clientName").GetString());

        // Only a hash is stored, and the history says who got a login but never the password.
        var password = handed.Value.Password;
        await _cloud.WithControlDbAsync(async db =>
        {
            var user = await db.OwnerUsers.SingleAsync(u => u.Email == email);
            Assert.DoesNotContain(password, user.PasswordHash);
            Assert.StartsWith("v1.", user.PasswordHash);
            var events = await db.ClientEvents.Where(e => e.ClientId == site.ClientId).Select(e => e.Text).ToListAsync();
            Assert.Contains(events, t => t.Contains(email));
            Assert.DoesNotContain(events, t => t.Contains(password));
            return 0;
        });
    }

    [Fact]
    public async Task A_password_the_provider_types_is_kept_and_the_email_is_not_case_sensitive()
    {
        var site = await _cloud.SeedSiteAsync();
        var admin = await AdminAsync();
        var email = Unique("typed");

        await Post(admin, site.ClientId, "AddOwner", ("Email", email.ToUpperInvariant()), ("DisplayName", ""), ("Password", "Sunrise-2026"));

        Assert.Equal(HttpStatusCode.OK, (await PhoneLoginAsync(email, "Sunrise-2026")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PhoneLoginAsync(email, "sunrise-2026")).StatusCode);
    }

    [Theory]
    [InlineData("not-an-email@", "longenough1", "Enter an email, a phone number")]
    [InlineData("12345", "longenough1", "Enter an email, a phone number")]
    [InlineData("al", "longenough1", "Enter an email, a phone number")]
    [InlineData("has space", "longenough1", "Enter an email, a phone number")]
    [InlineData("", "longenough1", "Enter an email, a phone number")]
    [InlineData("ok@shop.example", "short", "at least 8")]
    public async Task Bad_input_is_refused_with_a_reason_and_creates_nothing(string email, string password, string reason)
    {
        var site = await _cloud.SeedSiteAsync();
        var admin = await AdminAsync();

        var response = await Post(admin, site.ClientId, "AddOwner", ("Email", email), ("DisplayName", ""), ("Password", password));
        var after = await admin.GetAsync(response.Headers.Location!.OriginalString);

        Assert.Contains(reason, await after.Content.ReadAsStringAsync());
        Assert.Equal(0, await _cloud.WithControlDbAsync(db => db.OwnerUsers.CountAsync(u => u.ClientId == site.ClientId)));
    }

    [Fact]
    public async Task One_email_cannot_have_logins_on_two_clients()
    {
        var first = await _cloud.SeedSiteAsync(clientName: "Shop One");
        var second = await _cloud.SeedSiteAsync(clientName: "Shop Two");
        var admin = await AdminAsync();
        var email = Unique("shared");

        await Post(admin, first.ClientId, "AddOwner", ("Email", email), ("DisplayName", ""), ("Password", "first-password-1"));
        var dup = await Post(admin, second.ClientId, "AddOwner", ("Email", email), ("DisplayName", ""), ("Password", "second-password-2"));

        Assert.Contains("already has a phone login", await (await admin.GetAsync(dup.Headers.Location!.OriginalString)).Content.ReadAsStringAsync());
        Assert.Equal(0, await _cloud.WithControlDbAsync(db => db.OwnerUsers.CountAsync(u => u.ClientId == second.ClientId)));
        Assert.Equal(HttpStatusCode.OK, (await PhoneLoginAsync(email, "first-password-1")).StatusCode);
    }

    [Fact]
    public async Task A_login_can_be_a_username_instead_of_an_email()
    {
        var site = await _cloud.SeedSiteAsync();
        var admin = await AdminAsync();
        var username = $"Hassan.K-{Random.Shared.Next(100000, 999999)}";

        await Post(admin, site.ClientId, "AddOwner", ("Email", "  " + username + " "), ("DisplayName", ""), ("Password", "username-pass-1"));

        Assert.Equal(username.ToLowerInvariant(), (await HandedOverAsync(admin, site.ClientId))?.Email);
        var login = await PhoneLoginAsync(username.ToUpperInvariant(), "username-pass-1");
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Equal(username.ToLowerInvariant(), (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("displayName").GetString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await PhoneLoginAsync(username, "wrong-pass-1")).StatusCode);
    }

    [Fact]
    public async Task A_login_can_be_a_phone_number_typed_with_or_without_the_country_code()
    {
        var first = await _cloud.SeedSiteAsync(clientName: "Phone Shop One");
        var second = await _cloud.SeedSiteAsync(clientName: "Phone Shop Two");
        var admin = await AdminAsync();
        var line = Random.Shared.Next(1_000_000, 9_999_999).ToString();  // seven digits, like 3 123456 after +961
        var local = "0" + line;                                            // how it is written at home: 03 123456

        await Post(admin, first.ClientId, "AddOwner", ("Email", $"{local[..2]} {local[2..5]} {local[5..]}"), ("DisplayName", "Rami"), ("Password", "phone-pass-01"));
        Assert.Equal(local, (await HandedOverAsync(admin, first.ClientId))?.Email);  // stored as digits only

        foreach (var typed in new[] { local, $"+961 {line}", $"00961{line}", $"961-{line}", $"({local[..2]}) {local[2..]}" })
            Assert.True((await PhoneLoginAsync(typed, "phone-pass-01")).StatusCode == HttpStatusCode.OK, $"\"{typed}\" should sign in");
        Assert.Equal(HttpStatusCode.Unauthorized, (await PhoneLoginAsync(local[..^1], "phone-pass-01")).StatusCode);  // one digit short

        // The same line written another way is still the same login, so a second shop cannot take it.
        var dup = await Post(admin, second.ClientId, "AddOwner", ("Email", "+961" + line), ("DisplayName", ""), ("Password", "phone-pass-02"));
        Assert.Contains("That phone number already has a phone login", await (await admin.GetAsync(dup.Headers.Location!.OriginalString)).Content.ReadAsStringAsync());
        Assert.Equal(0, await _cloud.WithControlDbAsync(db => db.OwnerUsers.CountAsync(u => u.ClientId == second.ClientId)));
    }

    [Theory]
    [InlineData("Owner@Shop.Example", "owner@shop.example", OwnerSignIn.Kind.Email)]
    [InlineData("+961 3 123 456", "9613123456", OwnerSignIn.Kind.Phone)]
    [InlineData("00961-3-123456", "9613123456", OwnerSignIn.Kind.Phone)]
    [InlineData("(03) 123.456", "03123456", OwnerSignIn.Kind.Phone)]
    [InlineData(" Hassan_K ", "hassan_k", OwnerSignIn.Kind.Username)]
    [InlineData("shop2026", "shop2026", OwnerSignIn.Kind.Username)]
    public void Sign_in_names_are_stored_in_one_form(string typed, string stored, OwnerSignIn.Kind kind)
        => Assert.Equal((stored, kind), OwnerSignIn.Normalize(typed));

    [Theory]
    [InlineData("")]
    [InlineData("a@")]
    [InlineData("123456")]            // too short for a phone, and a username needs a letter
    [InlineData("1234567890123456")]  // longer than any phone number
    [InlineData("ab")]
    [InlineData("hassan k")]
    [InlineData("حسن")]
    [InlineData("3+123456789")]
    public void Anything_else_is_refused(string typed) => Assert.Null(OwnerSignIn.Normalize(typed));

    [Theory]
    [InlineData("03123456", "9613123456", true)]
    [InlineData("3123456", "03123456", true)]
    [InlineData("70123456", "96170123456", true)]
    [InlineData("03123456", "03123457", false)]
    [InlineData("0123456", "96170123456", false)]  // under seven digits once the 0 goes: too little to match on
    public void Phone_numbers_match_with_or_without_country_code(string a, string b, bool same)
        => Assert.Equal(same, OwnerSignIn.SamePhone(a, b));

    [Fact]
    public async Task Resetting_makes_a_new_password_and_signs_the_phone_out()
    {
        var site = await _cloud.SeedSiteAsync();
        var admin = await AdminAsync();
        var email = Unique("reset");
        await Post(admin, site.ClientId, "AddOwner", ("Email", email), ("DisplayName", ""), ("Password", "old-password-1"));
        var ownerId = await OwnerIdAsync(email);
        var phone = await SignedInPhoneAsync(email, "old-password-1");
        Assert.Equal(HttpStatusCode.OK, (await phone.GetAsync("/owner/v1/me")).StatusCode);

        await Post(admin, site.ClientId, "ResetOwner&ownerId=" + ownerId);

        var handed = await HandedOverAsync(admin, site.ClientId);
        Assert.NotNull(handed);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PhoneLoginAsync(email, "old-password-1")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PhoneLoginAsync(email, handed!.Value.Password)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await phone.GetAsync("/owner/v1/me")).StatusCode); // the phone that was signed in is out
    }

    [Fact]
    public async Task Disabling_blocks_the_login_and_signs_the_phone_out_and_enabling_restores_it()
    {
        var site = await _cloud.SeedSiteAsync();
        var admin = await AdminAsync();
        var email = Unique("toggle");
        await Post(admin, site.ClientId, "AddOwner", ("Email", email), ("DisplayName", ""), ("Password", "toggle-password-1"));
        var ownerId = await OwnerIdAsync(email);
        var phone = await SignedInPhoneAsync(email, "toggle-password-1");

        await Post(admin, site.ClientId, "ToggleOwner&ownerId=" + ownerId);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PhoneLoginAsync(email, "toggle-password-1")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await phone.GetAsync("/owner/v1/me")).StatusCode);

        await Post(admin, site.ClientId, "ToggleOwner&ownerId=" + ownerId);
        Assert.Equal(HttpStatusCode.OK, (await PhoneLoginAsync(email, "toggle-password-1")).StatusCode);
    }

    [Fact]
    public async Task A_login_can_only_be_changed_through_its_own_client()
    {
        var mine = await _cloud.SeedSiteAsync(clientName: "Mine");
        var theirs = await _cloud.SeedSiteAsync(clientName: "Theirs");
        var admin = await AdminAsync();
        var email = Unique("theirs");
        await Post(admin, theirs.ClientId, "AddOwner", ("Email", email), ("DisplayName", ""), ("Password", "theirs-password-1"));
        var ownerId = await OwnerIdAsync(email);

        // Naming another client's login under this client's page must not reach it.
        Assert.Equal(HttpStatusCode.NotFound, (await Post(admin, mine.ClientId, "ResetOwner&ownerId=" + ownerId)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Post(admin, mine.ClientId, "ToggleOwner&ownerId=" + ownerId)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PhoneLoginAsync(email, "theirs-password-1")).StatusCode);
    }

    [Fact]
    public async Task The_phone_login_screen_needs_the_admin_sign_in()
    {
        var site = await _cloud.SeedSiteAsync();
        var anonymous = _cloud.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = true });

        var response = await anonymous.PostAsync($"/admin/Clients/Details/{site.ClientId}?handler=AddOwner",
            new FormUrlEncodedContent([new("Email", "x@y.example")]));

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, await _cloud.WithControlDbAsync(db => db.OwnerUsers.CountAsync(u => u.ClientId == site.ClientId)));
    }

    [Fact]
    public async Task The_hand_over_tells_the_owner_about_the_recovery_code_in_both_languages()
    {
        var site = await _cloud.SeedSiteAsync(clientName: "Recovery Minimarket");
        var admin = await AdminAsync();
        await Post(admin, site.ClientId, "AddOwner", ("Email", Unique("rc")), ("DisplayName", "Rima"), ("Password", ""));

        var html = await admin.GetStringAsync($"/admin/Clients/Details/{site.ClientId}");

        Assert.Contains("recovery code", html);
        Assert.Contains("Settings &gt; Online &gt; Show recovery code", html);
        // The WhatsApp text carries the same step, in English and Arabic.
        var whatsapp = Uri.UnescapeDataString(Regex.Match(html, @"https://wa\.me/\?text=[^""]+").Value.Replace("&amp;", "&"));
        Assert.Contains("Show recovery code", whatsapp);
        Assert.Contains("رمز الاسترداد", whatsapp);
    }

    [Fact]
    public async Task A_shop_that_sends_encrypted_data_is_labelled_so_on_its_till_page()
    {
        var site = await _cloud.SeedSiteAsync(clientName: "Encrypted Minimarket");
        var admin = await AdminAsync();
        var plain = await admin.GetStringAsync($"/admin/Sites/Details/{site.SiteId}");
        Assert.DoesNotContain("Encrypted by the shop", plain);

        var cipher = Sync.DataKey.Generate().Cipher();
        var batch = new Sync.SyncBatch("", 0, 1, true,
            [new Sync.SyncChange("Customers", 1, false, null, cipher.Encrypt("Customers", 1, new Dictionary<string, object?> { ["FirstName"] = "Hidden" }))]);
        var push = await _cloud.DeviceClient(site.Token).PostAsJsonAsync("api/sync/push", batch, Sync.SyncWire.Json);
        Assert.Equal(HttpStatusCode.OK, push.StatusCode);

        var html = await admin.GetStringAsync($"/admin/Sites/Details/{site.SiteId}");

        Assert.Contains("Encrypted by the shop", html);
        Assert.Contains("nobody can recover this data", html);
        Assert.DoesNotContain("Hidden", html);
    }
}
