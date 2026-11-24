using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Cloud.Data;
using Cloud.Services;
using Cloud.Tests.Support;
using Xunit;

namespace Cloud.Tests;

/// <summary>
/// What a menu says about the shop besides its dishes: its look (colours, cover), where it is, when it is open, how to reach it,
/// and pictures for its sections.
/// </summary>
public class OnlineMenuProfileTests : IClassFixture<CloudFactory>
{
    private const string Password = "pass-1234-ok";
    private readonly CloudFactory _cloud;

    public OnlineMenuProfileTests(CloudFactory cloud) => _cloud = cloud;

    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8DwHwAFBQIAX8jx0gAAAABJRU5ErkJggg==");

    private async Task<HttpClient> OwnerAsync(string stem)
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
        return http;
    }

    private static async Task<JsonElement> Ok(Task<HttpResponseMessage> call)
    {
        var response = await call;
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private static readonly string[] AlwaysOpen = Enumerable.Repeat("00:00-00:00", 7).ToArray();

    [Fact]
    public async Task The_shop_puts_its_colours_cover_contacts_and_hours_on_the_menu()
    {
        var owner = await OwnerAsync("profile-a");
        await Ok(owner.PostAsJsonAsync("/menu/v1/editor", new { title = "Sumac & Ember" }));
        var state = await Ok(owner.PutAsJsonAsync("/menu/v1/settings", new
        {
            isPublished = true, theme = "ember", address = "Hamra Street, Beirut", phone = "+961 1 234 567", whatsApp = "+961 70 123 456",
            instagram = "@sumac.ember", facebook = "https://facebook.com/sumacember", website = "sumac.example", hours = AlwaysOpen,
        }));
        state = await Ok(owner.PutAsync("/menu/v1/cover", new ByteArrayContent(Png)));
        var m = state.GetProperty("menu");
        Assert.Equal("ember", m.GetProperty("theme").GetString());
        Assert.Matches("^/m/img/[0-9a-f]{32}\\.webp$", m.GetProperty("coverUrl").GetString());
        Assert.Equal(7, m.GetProperty("hours").GetArrayLength());
        await Ok(owner.PostAsJsonAsync("/menu/v1/items", new { name = "Grilled halloumi", price = 6 }));

        // Razor writes "+" and "&" in attributes as entities; the browser reads them back, so compare what it reads.
        var html = WebUtility.HtmlDecode(await _cloud.CreateClient().GetStringAsync("/m/" + m.GetProperty("slug").GetString()));
        Assert.Contains("data-theme=\"ember\"", html);
        Assert.Contains("family=Barlow", html); // the theme's heading face
        Assert.Contains("class=\"cover\" src=\"/m/img/", html);
        Assert.Contains("href=\"tel:+9611234567\"", html);
        Assert.Contains("https://wa.me/96170123456", html);
        Assert.Contains("data-wa=\"96170123456\"", html); // the order can be sent there
        Assert.Contains("https://instagram.com/sumac.ember", html);
        Assert.Contains("https://facebook.com/sumacember", html);
        Assert.Contains("https://sumac.example/", html);
        Assert.Contains("https://www.google.com/maps/search/?api=1&query=Hamra%20Street%2C%20Beirut", html);
        Assert.Contains("Open now", html);
        Assert.Contains("Find us", html);
    }

    [Fact]
    public async Task Contact_details_that_cannot_be_links_are_refused_and_empty_ones_clear()
    {
        var owner = await OwnerAsync("profile-b");
        await Ok(owner.PostAsJsonAsync("/menu/v1/editor", new { title = "Shop B" }));
        foreach (var bad in new object[]
        {
            new { mapUrl = "javascript:alert(1)" }, new { phone = "call me" }, new { instagram = "<b>" }, new { website = "not a site" },
            new { theme = "neon" }, new { hours = new[] { "9-5", "", "", "", "", "", "" } }, new { hours = new[] { "09:00-17:00" } },
        })
            Assert.Equal(HttpStatusCode.BadRequest, (await owner.PutAsJsonAsync("/menu/v1/settings", bad)).StatusCode);

        await Ok(owner.PutAsJsonAsync("/menu/v1/settings", new { phone = "+961 1 234 567", hours = AlwaysOpen }));
        var state = await Ok(owner.PutAsJsonAsync("/menu/v1/settings", new { phone = "", hours = new[] { "", "", "", "", "", "", "" } }));
        Assert.Equal(JsonValueKind.Null, state.GetProperty("menu").GetProperty("phone").ValueKind);
        Assert.Equal(JsonValueKind.Null, state.GetProperty("menu").GetProperty("hours").ValueKind);
    }

    [Fact]
    public async Task A_section_gets_a_picture_and_a_line_under_its_heading()
    {
        var owner = await OwnerAsync("profile-c");
        await Ok(owner.PostAsJsonAsync("/menu/v1/editor", new { title = "Shop C" }));
        var made = await owner.PostAsJsonAsync("/menu/v1/sections", new { name = "Breakfast", description = "Served until noon" });
        made.EnsureSuccessStatusCode();
        var id = int.Parse(made.Headers.GetValues("X-Created-Id").Single());
        var state = await Ok(owner.PutAsync($"/menu/v1/sections/{id}/image", new ByteArrayContent(Png)));
        var section = state.GetProperty("sections").EnumerateArray().Single();
        Assert.Equal("Served until noon", section.GetProperty("description").GetString());
        var pic = section.GetProperty("imageUrl").GetString()!;
        await Ok(owner.PostAsJsonAsync("/menu/v1/items", new { name = "Manakish", price = 2, sectionId = id }));
        state = await Ok(owner.PutAsJsonAsync("/menu/v1/settings", new { isPublished = true }));

        var html = await _cloud.CreateClient().GetStringAsync("/m/" + state.GetProperty("menu").GetProperty("slug").GetString());
        Assert.Contains("class=\"sec has-pic\"", html);
        Assert.Contains($"class=\"sp\" src=\"{pic}\"", html);
        Assert.Contains("Served until noon", html);

        // Or as a small logo before the name instead of a banner.
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PutAsJsonAsync($"/menu/v1/sections/{id}", new { pictureAs = "poster" })).StatusCode);
        state = await Ok(owner.PutAsJsonAsync($"/menu/v1/sections/{id}", new { pictureAs = "icon" }));
        Assert.Equal("icon", state.GetProperty("sections").EnumerateArray().Single().GetProperty("pictureAs").GetString());
        html = await _cloud.CreateClient().GetStringAsync("/m/" + state.GetProperty("menu").GetProperty("slug").GetString());
        Assert.Contains("class=\"sec has-icon\"", html);
        Assert.Contains($"class=\"si\" src=\"{pic}\"", html);

        // Taking the picture off deletes the file.
        await Ok(owner.DeleteAsync($"/menu/v1/sections/{id}/image"));
        Assert.Equal(HttpStatusCode.NotFound, (await _cloud.CreateClient().GetAsync(pic)).StatusCode);
    }

    [Theory]
    [InlineData("2026-11-16 10:00", true, "until 23:00")]   // Monday, inside 09:00-23:00
    [InlineData("2026-11-16 07:30", false, "opens 09:00")]  // Monday, before opening
    [InlineData("2026-11-21 01:30", true, "until 02:00")]   // Saturday 01:30: still Friday night's 18:00-02:00
    [InlineData("2026-11-21 03:00", false, "opens Sunday")] // Saturday is closed: next is Sunday 10:00
    public void Hours_say_whether_the_shop_is_open_and_when_it_opens_next(string at, bool open, string expect)
    {
        // Sunday 10:00-16:00; Monday to Thursday 09:00-23:00; Friday 18:00-02:00; Saturday closed.
        const string hours = "10:00-16:00;09:00-23:00;09:00-23:00;09:00-23:00;09:00-23:00;18:00-02:00;";
        var status = MenuHours.At(hours, DateTime.Parse(at, System.Globalization.CultureInfo.InvariantCulture))!;
        Assert.Equal(open, status.Open);
        var text = status.Open ? $"until {status.Until:HH\\:mm}" : status.OpensOn == DayOfWeek.Sunday ? "opens Sunday" : $"opens {status.Opens:HH\\:mm}";
        Assert.Equal(expect, text);
    }

    [Theory]
    [InlineData("2026-11-16 10:00", true, "until 12:00")]   // Monday morning
    [InlineData("2026-11-16 12:30", false, "opens 13:00")]  // the lunch break
    [InlineData("2026-11-16 16:30", false, "opens 18:00")]  // between afternoon and evening
    [InlineData("2026-11-16 20:00", true, "until 21:00")]   // the evening
    [InlineData("2026-11-16 21:30", false, "opens 09:00")]  // after closing: tomorrow morning
    public void A_day_can_open_several_times(string at, bool open, string expect)
    {
        // Every day 09:00-12:00, 13:00-16:00 and 18:00-21:00, typed out of order.
        var day = "18:00-21:00, 09:00-12:00,13:00-16:00";
        var (hours, error) = MenuHours.Normalize([day, day, day, day, day, day, day]);
        Assert.Null(error);
        Assert.StartsWith("09:00-12:00,13:00-16:00,18:00-21:00;", hours);
        var status = MenuHours.At(hours, DateTime.Parse(at, System.Globalization.CultureInfo.InvariantCulture))!;
        Assert.Equal(open, status.Open);
        Assert.Equal(expect, status.Open ? $"until {status.Until:HH\\:mm}" : $"opens {status.Opens:HH\\:mm}");
    }

    [Theory]
    [InlineData("09:00-13:00,12:00-16:00")]                         // the second opens before the first closes
    [InlineData("18:00-02:00,20:00-23:00")]                         // a range past midnight must be the last
    [InlineData("08:00-09:00,10:00-11:00,12:00-13:00,14:00-15:00,16:00-17:00")] // more than four
    public void Overlapping_or_too_many_opening_times_are_refused(string monday)
    {
        var (hours, error) = MenuHours.Normalize(["", monday, "", "", "", "", ""]);
        Assert.Null(hours);
        Assert.NotNull(error);
    }
}
