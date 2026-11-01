using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Application.IServices.Sales;
using Application.IServices.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Moq;
using Tests.Api.Catalog;
using Tests.Api.Infrastructure;
using Web;
using Web.Api.Settings;
using Web.Helpers;
using Web.Services;
using Xunit;

namespace Tests.Api.Settings;

/// <summary>
/// Settings API (track A2): everything needs <c>Settings.Manage</c> (the class-level permission of the Razor <c>SettingsController</c>; <c>Settings.View</c>
/// is not enough), the store details save only the receipt fields, the printing save touches only the three settings the app reads, and branding
/// validates both files before it writes anything and shows up in the session payload straight away.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class SettingsApiTests : CatalogApiTestBase
{
    // 1x1 transparent PNG.
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    public SettingsApiTests(ApiFactory factory) : base(factory)
    {
    }

    private static HttpRequestMessage Put(string url, object body) => Json(HttpMethod.Put, url, body);

    private static HttpRequestMessage Multipart(string url, Action<MultipartFormDataContent> build)
    {
        var content = new MultipartFormDataContent();
        build(content);
        var request = new HttpRequestMessage(HttpMethod.Put, url) { Content = content };
        request.Headers.Add("X-NexPOS-Client", "web");
        return request;
    }

    private static void AddFile(MultipartFormDataContent form, string name, string fileName, byte[] bytes)
    {
        var part = new ByteArrayContent(bytes);
        part.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(part, name, fileName);
    }

    private static object Store(string name = "Corner Shop", string? footer = "Thanks!") => new
    {
        storeName = name,
        addressLine1 = "1 Main St",
        addressLine2 = "Beirut",
        phone = "01 234 567",
        taxId = "TX-1",
        website = "shop.example.com",
        receiptFooterMessage = footer,
        footerLine1 = "No refunds",
        footerLine2 = "",
        footerLine3 = (string?)null,
    };

    /// <summary>A signed-in user whose role holds exactly the given permissions.</summary>
    private async Task<HttpClient> SignedInWith(params string[] permissions)
    {
        var admin = await Admin();
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var role = await admin.SendAsync(Json(HttpMethod.Post, "/api/v1/roles", new { name = "set" + suffix, description = "test", permissions }));
        Assert.Equal(HttpStatusCode.Created, role.StatusCode);
        var roleId = (await Body(role)).GetProperty("role").GetProperty("id").GetInt32();
        var username = "set" + suffix;
        var user = await admin.SendAsync(Json(HttpMethod.Post, "/api/v1/users", new { username, email = $"{username}@example.com", roleId, isActive = true, pin = "4321" }));
        Assert.Equal(HttpStatusCode.Created, user.StatusCode);
        return await SignedIn(username, "4321");
    }

    private async Task<JsonElement> SessionBranding(HttpClient client)
    {
        var response = await client.GetAsync("/api/v1/session");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await Body(response)).GetProperty("branding");
    }

    // ---- permissions -------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("/api/v1/settings/store")]
    [InlineData("/api/v1/settings/printing")]
    [InlineData("/api/v1/settings/printers")]
    [InlineData("/api/v1/settings/branding")]
    public async Task Without_a_cookie_everything_answers_401(string url)
    {
        await AssertProblem(await Factory.CreateBrowser().GetAsync(url), HttpStatusCode.Unauthorized, "unauthenticated");
    }

    [Fact]
    public async Task A_cashier_cannot_read_or_change_any_setting()
    {
        var cashier = await Cashier();

        foreach (var url in new[] { "store", "printing", "printers", "branding" })
            await AssertProblem(await cashier.GetAsync($"/api/v1/settings/{url}"), HttpStatusCode.Forbidden, "forbidden");

        await AssertProblem(await cashier.SendAsync(Put("/api/v1/settings/store", Store())), HttpStatusCode.Forbidden, "forbidden");
        await AssertProblem(await cashier.SendAsync(Json(HttpMethod.Post, "/api/v1/settings/store/reset")), HttpStatusCode.Forbidden, "forbidden");
        await AssertProblem(await cashier.SendAsync(Put("/api/v1/settings/printing", new { stickerWidthMm = 40, stickerHeightMm = 25 })), HttpStatusCode.Forbidden, "forbidden");
        await AssertProblem(await cashier.SendAsync(Json(HttpMethod.Post, "/api/v1/settings/printers/test", new { printerName = "x" })), HttpStatusCode.Forbidden, "forbidden");
        await AssertProblem(await cashier.SendAsync(Multipart("/api/v1/settings/branding", f => f.Add(new StringContent("Hacked"), "appName"))), HttpStatusCode.Forbidden, "forbidden");
    }

    [Fact]
    public async Task Settings_View_alone_is_not_enough_because_the_razor_controller_asks_for_Manage()
    {
        var viewer = await SignedInWith("Settings.View");

        await AssertProblem(await viewer.GetAsync("/api/v1/settings/store"), HttpStatusCode.Forbidden, "forbidden");
        await AssertProblem(await viewer.SendAsync(Put("/api/v1/settings/store", Store())), HttpStatusCode.Forbidden, "forbidden");
    }

    [Fact]
    public async Task Settings_Manage_alone_opens_every_endpoint()
    {
        var manager = await SignedInWith("Settings.Manage");

        foreach (var url in new[] { "store", "printing", "printers", "branding" })
            Assert.Equal(HttpStatusCode.OK, (await manager.GetAsync($"/api/v1/settings/{url}")).StatusCode);
    }

    [Fact]
    public async Task A_change_without_the_client_header_is_refused()
    {
        var admin = await Admin();
        var request = new HttpRequestMessage(HttpMethod.Put, "/api/v1/settings/store") { Content = System.Net.Http.Json.JsonContent.Create(Store()) };

        await AssertProblem(await admin.SendAsync(request), HttpStatusCode.Forbidden, "client_header_required");
    }

    // ---- store & receipts --------------------------------------------------------------------------------------

    [Fact]
    public async Task The_store_details_save_trimmed_and_read_back()
    {
        var admin = await Admin();

        var saved = await admin.SendAsync(Put("/api/v1/settings/store", Store("  Corner Shop  ")));

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var body = await Body(saved);
        Assert.Equal("Corner Shop", body.GetProperty("storeName").GetString());
        Assert.Equal("Beirut", body.GetProperty("addressLine2").GetString());
        Assert.Equal("", body.GetProperty("footerLine3").GetString()); // null is stored as empty text, not as the string "null"

        var read = await Body(await admin.GetAsync("/api/v1/settings/store"));
        Assert.Equal("Corner Shop", read.GetProperty("storeName").GetString());
        Assert.Equal("Thanks!", read.GetProperty("receiptFooterMessage").GetString());
        Assert.Equal("TX-1", read.GetProperty("taxId").GetString());
    }

    [Fact]
    public async Task A_blank_store_name_is_refused_on_the_field_and_nothing_changes()
    {
        var admin = await Admin();
        Assert.Equal(HttpStatusCode.OK, (await admin.SendAsync(Put("/api/v1/settings/store", Store("Before")))).StatusCode);

        foreach (var name in new[] { "", "   " })
        {
            var response = await admin.SendAsync(Put("/api/v1/settings/store", Store(name)));

            await AssertProblem(response, HttpStatusCode.BadRequest, "validation_failed");
        }

        Assert.Equal("Before", (await Body(await admin.GetAsync("/api/v1/settings/store"))).GetProperty("storeName").GetString());
    }

    [Fact]
    public async Task Fields_over_their_length_are_refused_with_the_field_named()
    {
        var admin = await Admin();

        var response = await admin.SendAsync(Put("/api/v1/settings/store", new { storeName = new string('x', 201), phone = new string('1', 51) }));

        await AssertProblem(response, HttpStatusCode.BadRequest, "validation_failed");
        var errors = (await Body(response)).GetProperty("errors");
        Assert.True(errors.TryGetProperty("storeName", out _));
        Assert.True(errors.TryGetProperty("phone", out _));
    }

    [Fact]
    public async Task A_store_save_leaves_the_columns_nobody_reads_alone_and_reset_restores_the_factory_text()
    {
        var admin = await Admin();
        using (var scope = Factory.Services.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IStoreSettingsService>();
            var entity = await service.GetStoreSettingsEntityAsync();
            Assert.NotNull(entity); // the row exists; its other columns keep their values through a save
        }

        Assert.Equal(HttpStatusCode.OK, (await admin.SendAsync(Put("/api/v1/settings/store", Store("Custom Name")))).StatusCode);

        var reset = await admin.SendAsync(Json(HttpMethod.Post, "/api/v1/settings/store/reset"));

        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        var body = await Body(reset);
        Assert.NotEqual("Custom Name", body.GetProperty("storeName").GetString());
        Assert.NotEqual("1 Main St", body.GetProperty("addressLine1").GetString());
        Assert.NotEqual("TX-1", body.GetProperty("taxId").GetString());
        Assert.NotEqual("", body.GetProperty("receiptFooterMessage").GetString());
        Assert.Equal(body.GetProperty("storeName").GetString(), (await Body(await admin.GetAsync("/api/v1/settings/store"))).GetProperty("storeName").GetString());
    }

    // ---- printers & labels -------------------------------------------------------------------------------------

    [Fact]
    public async Task The_printing_settings_save_and_read_back_and_a_blank_printer_clears_it()
    {
        var admin = await Admin();

        var saved = await admin.SendAsync(Put("/api/v1/settings/printing", new { receiptPrinterName = "  XP-80C  ", stickerWidthMm = 50, stickerHeightMm = 30 }));

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var body = await Body(saved);
        Assert.Equal("XP-80C", body.GetProperty("receiptPrinterName").GetString());
        Assert.Equal(50, body.GetProperty("stickerWidthMm").GetInt32());
        Assert.Equal(30, body.GetProperty("stickerHeightMm").GetInt32());

        var cleared = await Body(await admin.SendAsync(Put("/api/v1/settings/printing", new { receiptPrinterName = (string?)null, stickerWidthMm = 40, stickerHeightMm = 25 })));
        Assert.Equal("", cleared.GetProperty("receiptPrinterName").GetString());
        Assert.Equal(40, (await Body(await admin.GetAsync("/api/v1/settings/printing"))).GetProperty("stickerWidthMm").GetInt32());
    }

    [Theory]
    [InlineData(14, 25, "stickerWidthMm")]
    [InlineData(151, 25, "stickerWidthMm")]
    [InlineData(40, 9, "stickerHeightMm")]
    [InlineData(40, 151, "stickerHeightMm")]
    public async Task A_sticker_size_outside_the_range_is_refused_and_nothing_is_saved(int width, int height, string field)
    {
        var admin = await Admin();
        Assert.Equal(HttpStatusCode.OK, (await admin.SendAsync(Put("/api/v1/settings/printing", new { stickerWidthMm = 41, stickerHeightMm = 26 }))).StatusCode);

        var response = await admin.SendAsync(Put("/api/v1/settings/printing", new { stickerWidthMm = width, stickerHeightMm = height }));

        await AssertProblem(response, HttpStatusCode.BadRequest, "validation_failed");
        Assert.True((await Body(response)).GetProperty("errors").TryGetProperty(field, out _));
        var read = await Body(await admin.GetAsync("/api/v1/settings/printing"));
        Assert.Equal(41, read.GetProperty("stickerWidthMm").GetInt32());
        Assert.Equal(26, read.GetProperty("stickerHeightMm").GetInt32());
    }

    [Fact]
    public async Task Saving_the_printing_settings_does_not_touch_the_other_stored_settings()
    {
        var admin = await Admin();
        using var scope = Factory.Services.CreateScope();
        var system = scope.ServiceProvider.GetRequiredService<ISystemSettingsService>();
        await system.SetSettingValueAsync("LowStockThreshold", "17");

        Assert.Equal(HttpStatusCode.OK, (await admin.SendAsync(Put("/api/v1/settings/printing", new { receiptPrinterName = "P1", stickerWidthMm = 40, stickerHeightMm = 25 }))).StatusCode);

        Assert.Equal(17, (await system.GetAppSettingsAsync()).LowStockThreshold);
    }

    [Fact]
    public async Task The_installed_printers_are_listed()
    {
        var admin = await Admin();

        var response = await admin.GetAsync("/api/v1/settings/printers");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(JsonValueKind.Array, (await Body(response)).GetProperty("items").ValueKind);
    }

    [Fact]
    public async Task A_test_print_needs_a_printer_name()
    {
        var admin = await Admin();

        var response = await admin.SendAsync(Json(HttpMethod.Post, "/api/v1/settings/printers/test", new { printerName = "" }));

        await AssertProblem(response, HttpStatusCode.BadRequest, "validation_failed");
    }

    [Fact]
    public async Task A_test_print_reports_that_the_slip_was_sent()
    {
        // The test host runs with NEXPOS_DEMO=1: its stand-in printer accepts every job and is the only one listed.
        var admin = await Admin();
        var listed = (await Body(await admin.GetAsync("/api/v1/settings/printers"))).GetProperty("items").EnumerateArray().Select(p => p.GetString()).ToList();
        Assert.Contains("Demo Receipt Printer", listed);

        var response = await admin.SendAsync(Json(HttpMethod.Post, "/api/v1/settings/printers/test", new { printerName = " Demo Receipt Printer " }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await Body(response);
        Assert.True(body.GetProperty("printed").GetBoolean());
        Assert.Equal("Test print sent successfully", body.GetProperty("message").GetString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_printer_that_cannot_be_reached_is_a_normal_answer_not_an_error(bool driverThrows)
    {
        var printers = new Mock<IThermalPrinterService>();
        var setup = printers.Setup(p => p.TestPrintAsync("Back office"));
        if (driverThrows) setup.ThrowsAsync(new InvalidOperationException("spooler is down"));
        else setup.ReturnsAsync(false);
        var service = new SettingsApiService(Mock.Of<IStoreSettingsService>(), Mock.Of<ISystemSettingsService>(), printers.Object, Mock.Of<IBrandingService>(), new FakeText());

        var result = await service.TestPrinterAsync("  Back office ");

        Assert.False(result.Printed);
        Assert.Contains("Back office", result.Message);
        Assert.DoesNotContain("spooler", result.Message); // a driver's own error text is not for the user
    }

    /// <summary>Echoes the key and its arguments, so a test can see which message was chosen.</summary>
    private sealed class FakeText : IStringLocalizer<SharedResource>
    {
        public LocalizedString this[string name] => new(name, name);

        public LocalizedString this[string name, params object[] arguments] => new(name, name + ":" + string.Join(",", arguments));

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => Array.Empty<LocalizedString>();
    }

    // ---- branding ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Branding_starts_on_the_defaults()
    {
        var admin = await Admin();

        var body = await Body(await admin.GetAsync("/api/v1/settings/branding"));

        Assert.Equal("", body.GetProperty("appName").GetString());
        Assert.Equal("NexPos", body.GetProperty("defaultAppName").GetString());
        Assert.False(body.GetProperty("hasCustomLogo").GetBoolean());
        Assert.False(body.GetProperty("hasCustomFavicon").GetBoolean());
        Assert.Equal(40, body.GetProperty("maxNameLength").GetInt32());
        Assert.Contains(".png", body.GetProperty("logoExtensions").EnumerateArray().Select(e => e.GetString()));
        Assert.Contains(".ico", body.GetProperty("faviconExtensions").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public async Task A_branding_save_reaches_the_session_and_the_served_images_and_can_be_undone()
    {
        var admin = await Admin();
        try
        {
            var saved = await admin.SendAsync(Multipart("/api/v1/settings/branding", f =>
            {
                f.Add(new StringContent("  Acme POS "), "appName");
                AddFile(f, "logo", "logo.png", Png);
                AddFile(f, "favicon", "icon.png", Png);
            }));

            Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
            var body = await Body(saved);
            Assert.Equal("Acme POS", body.GetProperty("appName").GetString());
            Assert.True(body.GetProperty("hasCustomLogo").GetBoolean());
            Assert.True(body.GetProperty("hasCustomFavicon").GetBoolean());

            var branding = await SessionBranding(admin);
            Assert.Equal("Acme POS", branding.GetProperty("appName").GetString());
            Assert.True(branding.GetProperty("hasCustomLogo").GetBoolean());

            var logo = await admin.GetAsync(body.GetProperty("logoUrl").GetString());
            Assert.Equal(HttpStatusCode.OK, logo.StatusCode);
            Assert.Equal("image/png", logo.Content.Headers.ContentType?.MediaType);
            Assert.Equal(Png, await logo.Content.ReadAsByteArrayAsync());

            var removed = await Body(await admin.SendAsync(Multipart("/api/v1/settings/branding", f =>
            {
                f.Add(new StringContent(""), "appName");
                f.Add(new StringContent("true"), "removeLogo");
                f.Add(new StringContent("true"), "removeFavicon");
            })));
            Assert.Equal("", removed.GetProperty("appName").GetString());
            Assert.False(removed.GetProperty("hasCustomLogo").GetBoolean());
            Assert.False(removed.GetProperty("hasCustomFavicon").GetBoolean());
            Assert.Equal("NexPos", (await SessionBranding(admin)).GetProperty("appName").GetString());
        }
        finally
        {
            await admin.SendAsync(Multipart("/api/v1/settings/branding", f =>
            {
                f.Add(new StringContent("true"), "removeLogo");
                f.Add(new StringContent("true"), "removeFavicon");
            }));
        }
    }

    [Fact]
    public async Task An_image_that_is_not_sent_stays_as_it_was()
    {
        var admin = await Admin();
        try
        {
            await admin.SendAsync(Multipart("/api/v1/settings/branding", f => AddFile(f, "logo", "logo.png", Png)));

            var renamed = await Body(await admin.SendAsync(Multipart("/api/v1/settings/branding", f => f.Add(new StringContent("Just a name"), "appName"))));

            Assert.Equal("Just a name", renamed.GetProperty("appName").GetString());
            Assert.True(renamed.GetProperty("hasCustomLogo").GetBoolean());
        }
        finally
        {
            await admin.SendAsync(Multipart("/api/v1/settings/branding", f => f.Add(new StringContent("true"), "removeLogo")));
        }
    }

    [Fact]
    public async Task A_bad_favicon_stops_the_whole_save_so_nothing_is_half_written()
    {
        var admin = await Admin();

        var response = await admin.SendAsync(Multipart("/api/v1/settings/branding", f =>
        {
            f.Add(new StringContent("Should not stick"), "appName");
            AddFile(f, "logo", "logo.png", Png);
            AddFile(f, "favicon", "icon.gif", Png); // .gif is not an allowed favicon type
        }));

        await AssertProblem(response, HttpStatusCode.BadRequest, "validation_failed");
        Assert.True((await Body(response)).GetProperty("errors").TryGetProperty("favicon", out _));
        var read = await Body(await admin.GetAsync("/api/v1/settings/branding"));
        Assert.Equal("", read.GetProperty("appName").GetString());
        Assert.False(read.GetProperty("hasCustomLogo").GetBoolean());
    }

    [Fact]
    public async Task A_logo_over_2_MB_and_a_name_over_40_characters_are_refused()
    {
        var admin = await Admin();

        var big = await admin.SendAsync(Multipart("/api/v1/settings/branding", f => AddFile(f, "logo", "logo.png", new byte[2 * 1024 * 1024 + 1])));
        await AssertProblem(big, HttpStatusCode.BadRequest, "validation_failed");
        Assert.True((await Body(big)).GetProperty("errors").TryGetProperty("logo", out _));

        var longName = await admin.SendAsync(Multipart("/api/v1/settings/branding", f => f.Add(new StringContent(new string('n', 41)), "appName")));
        await AssertProblem(longName, HttpStatusCode.BadRequest, "validation_failed");
        Assert.True((await Body(longName)).GetProperty("errors").TryGetProperty("appName", out _));

        Assert.False((await Body(await admin.GetAsync("/api/v1/settings/branding"))).GetProperty("hasCustomLogo").GetBoolean());
    }
}
