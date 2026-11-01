using Xunit;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Tests.Api.Infrastructure;

/// <summary>
/// The 0A "done when": the right 401 / 403 / 200 JSON with and without a cookie and a permission, problem details with a
/// stable code, CSRF header, language from Accept-Language, and the sign-in / session endpoints - through the real pipeline.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class ApiPipelineTests
{
    private const string Problem = "application/problem+json";
    private readonly ApiFactory _factory;

    public ApiPipelineTests(ApiFactory factory) => _factory = factory;

    // ---- helpers -----------------------------------------------------------------------------------------------

    private static HttpRequestMessage Json(HttpMethod method, string url, object? body = null, bool clientHeader = true, string? language = null)
    {
        var request = new HttpRequestMessage(method, url);
        if (body is not null) request.Content = JsonContent.Create(body);
        if (clientHeader) request.Headers.Add("X-NexPOS-Client", "web");
        if (language is not null) request.Headers.Add("Accept-Language", language);
        return request;
    }

    private static async Task<JsonElement> Body(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private async Task<HttpClient> SignedIn(string user, string pin, string? language = null)
    {
        var browser = _factory.CreateBrowser();
        var response = await browser.SendAsync(Json(HttpMethod.Post, "/api/v1/auth/login", new { username = user, pin }, language: language));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return browser;
    }

    private static async Task AssertProblem(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(Problem, response.Content.Headers.ContentType?.MediaType);
        Assert.Null(response.Headers.Location); // never a redirect to an HTML page

        var body = await Body(response);
        Assert.Equal((int)status, body.GetProperty("status").GetInt32());
        Assert.Equal(code, body.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("title").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("traceId").GetString()));
    }

    // ---- 401 / 403 / 200 ----------------------------------------------------------------------------------------

    [Fact]
    public async Task Without_a_cookie_a_protected_endpoint_answers_401_json_not_a_redirect()
    {
        var response = await _factory.CreateBrowser().GetAsync("/api/v1/fake/signed-in");

        await AssertProblem(response, HttpStatusCode.Unauthorized, "unauthenticated");
    }

    [Fact]
    public async Task Without_a_cookie_a_permission_protected_endpoint_answers_401_json()
    {
        var response = await _factory.CreateBrowser().GetAsync("/api/v1/fake/users-only");

        await AssertProblem(response, HttpStatusCode.Unauthorized, "unauthenticated");
    }

    [Fact]
    public async Task A_public_endpoint_needs_no_cookie()
    {
        var response = await _factory.CreateBrowser().GetAsync("/api/v1/fake/open");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_user_without_the_permission_gets_403_json_and_an_admin_gets_200()
    {
        var cashier = await SignedIn(ApiFactory.CashierUser, ApiFactory.CashierPin);
        var admin = await SignedIn(ApiFactory.AdminUser, ApiFactory.AdminPin);

        await AssertProblem(await cashier.GetAsync("/api/v1/fake/users-only"), HttpStatusCode.Forbidden, "forbidden");
        Assert.Equal(HttpStatusCode.OK, (await cashier.GetAsync("/api/v1/fake/signed-in")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/v1/fake/users-only")).StatusCode);
    }

    [Fact]
    public async Task Razor_pages_still_redirect_to_the_login_page()
    {
        var response = await _factory.CreateBrowser().GetAsync("/en/Dashboard");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location?.ToString());
    }

    // ---- unknown routes and methods ------------------------------------------------------------------------------

    [Fact]
    public async Task An_unknown_api_route_answers_404_problem_json()
    {
        var response = await _factory.CreateBrowser().GetAsync("/api/v1/does-not-exist");

        await AssertProblem(response, HttpStatusCode.NotFound, "not_found");
    }

    [Fact]
    public async Task A_wrong_method_answers_405_problem_json()
    {
        var response = await _factory.CreateBrowser().SendAsync(Json(HttpMethod.Post, "/api/v1/session"));

        await AssertProblem(response, HttpStatusCode.MethodNotAllowed, "method_not_allowed");
    }

    [Fact]
    public async Task NotFound_from_an_action_carries_a_code_and_a_localized_title()
    {
        var admin = await SignedIn(ApiFactory.AdminUser, ApiFactory.AdminPin);

        var response = await admin.SendAsync(Json(HttpMethod.Get, "/api/v1/fake/missing", language: "ar"));

        await AssertProblem(response, HttpStatusCode.NotFound, "not_found");
        Assert.Equal("لم يتم العثور على العنصر المطلوب.", (await Body(response)).GetProperty("title").GetString());
    }

    // ---- CSRF header ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_mutating_request_without_the_client_header_is_refused()
    {
        var admin = await SignedIn(ApiFactory.AdminUser, ApiFactory.AdminPin);

        var response = await admin.SendAsync(Json(HttpMethod.Post, "/api/v1/fake/echo", new { name = "x" }, clientHeader: false));

        await AssertProblem(response, HttpStatusCode.Forbidden, "client_header_required");
    }

    [Fact]
    public async Task The_login_itself_needs_the_client_header()
    {
        var response = await _factory.CreateBrowser().SendAsync(
            Json(HttpMethod.Post, "/api/v1/auth/login", new { username = ApiFactory.AdminUser, pin = ApiFactory.AdminPin }, clientHeader: false));

        await AssertProblem(response, HttpStatusCode.Forbidden, "client_header_required");
    }

    [Fact]
    public async Task A_wrong_header_value_is_refused_and_reads_never_need_the_header()
    {
        var admin = await SignedIn(ApiFactory.AdminUser, ApiFactory.AdminPin);
        var wrong = Json(HttpMethod.Post, "/api/v1/fake/echo", new { name = "x" }, clientHeader: false);
        wrong.Headers.Add("X-NexPOS-Client", "other");

        await AssertProblem(await admin.SendAsync(wrong), HttpStatusCode.Forbidden, "client_header_required");
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/v1/fake/signed-in")).StatusCode);
    }

    // ---- validation ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_bad_body_answers_400_with_camelCase_field_errors()
    {
        var admin = await SignedIn(ApiFactory.AdminUser, ApiFactory.AdminPin);

        var response = await admin.SendAsync(Json(HttpMethod.Post, "/api/v1/fake/echo",
            new { name = "", quantity = 50, lines = new[] { new { unitPrice = 0 } } }));

        await AssertProblem(response, HttpStatusCode.BadRequest, "validation_failed");
        var errors = (await Body(response)).GetProperty("errors");
        Assert.True(errors.TryGetProperty("name", out _), "field names are camelCase, like the request");
        Assert.True(errors.TryGetProperty("quantity", out _));
        Assert.True(errors.TryGetProperty("lines[0].unitPrice", out _));
    }

    [Fact]
    public async Task An_unreadable_body_answers_400_validation_failed_without_leaking_parser_text()
    {
        var admin = await SignedIn(ApiFactory.AdminUser, ApiFactory.AdminPin);
        var request = Json(HttpMethod.Post, "/api/v1/fake/echo");
        request.Content = new StringContent("{ not json", System.Text.Encoding.UTF8, "application/json");

        var response = await admin.SendAsync(request);

        await AssertProblem(response, HttpStatusCode.BadRequest, "validation_failed");
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(!text.Contains("LineNumber"), text);
    }

    [Fact]
    public async Task A_value_of_the_wrong_type_lands_on_its_field_with_a_plain_message()
    {
        var admin = await SignedIn(ApiFactory.AdminUser, ApiFactory.AdminPin);
        var request = Json(HttpMethod.Post, "/api/v1/fake/echo");
        request.Content = new StringContent("{\"name\":\"x\",\"quantity\":\"many\"}", System.Text.Encoding.UTF8, "application/json");

        var response = await admin.SendAsync(request);

        await AssertProblem(response, HttpStatusCode.BadRequest, "validation_failed");
        var text = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("System.", text);
        Assert.Equal("This value is not valid.", JsonDocument.Parse(text).RootElement.GetProperty("errors").GetProperty("quantity")[0].GetString());
    }

    [Fact]
    public async Task A_missing_body_answers_400_not_500()
    {
        var admin = await SignedIn(ApiFactory.AdminUser, ApiFactory.AdminPin);

        var request = Json(HttpMethod.Post, "/api/v1/fake/echo");
        request.Content = new StringContent("", System.Text.Encoding.UTF8, "application/json");

        var response = await admin.SendAsync(request);

        await AssertProblem(response, HttpStatusCode.BadRequest, "validation_failed");
    }

    [Fact]
    public async Task A_rule_the_attributes_cannot_express_can_still_name_its_field()
    {
        var admin = await SignedIn(ApiFactory.AdminUser, ApiFactory.AdminPin);

        var response = await admin.GetAsync("/api/v1/fake/field");

        await AssertProblem(response, HttpStatusCode.BadRequest, "validation_failed");
        Assert.Equal("Must not be negative.", (await Body(response)).GetProperty("errors").GetProperty("openingCash")[0].GetString());
    }

    [Fact]
    public async Task A_domain_conflict_carries_its_own_stable_code()
    {
        var admin = await SignedIn(ApiFactory.AdminUser, ApiFactory.AdminPin);

        var response = await admin.GetAsync("/api/v1/fake/drawer");

        await AssertProblem(response, HttpStatusCode.Conflict, "drawer_not_open");
        Assert.Equal("Open the drawer first.", (await Body(response)).GetProperty("detail").GetString());
    }

    // ---- paging --------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Lists_return_items_total_page_and_pageSize()
    {
        var admin = await SignedIn(ApiFactory.AdminUser, ApiFactory.AdminPin);

        var body = await Body(await admin.GetAsync("/api/v1/fake/paged?page=3&pageSize=10"));

        Assert.Equal(95, body.GetProperty("total").GetInt32());
        Assert.Equal(3, body.GetProperty("page").GetInt32());
        Assert.Equal(10, body.GetProperty("pageSize").GetInt32());
        Assert.Equal(Enumerable.Range(21, 10), body.GetProperty("items").EnumerateArray().Select(i => i.GetInt32()));
    }

    [Fact]
    public async Task Paging_defaults_apply_and_the_last_page_is_short()
    {
        var admin = await SignedIn(ApiFactory.AdminUser, ApiFactory.AdminPin);

        var defaults = await Body(await admin.GetAsync("/api/v1/fake/paged"));
        var last = await Body(await admin.GetAsync("/api/v1/fake/paged?page=4&pageSize=25"));

        Assert.Equal(25, defaults.GetProperty("pageSize").GetInt32());
        Assert.Equal(25, defaults.GetProperty("items").GetArrayLength());
        Assert.Equal(20, last.GetProperty("items").GetArrayLength());
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=201")]
    [InlineData("dir=sideways")]
    public async Task Bad_paging_parameters_answer_400(string query)
    {
        var admin = await SignedIn(ApiFactory.AdminUser, ApiFactory.AdminPin);

        await AssertProblem(await admin.GetAsync("/api/v1/fake/paged?" + query), HttpStatusCode.BadRequest, "validation_failed");
    }

    // ---- unexpected errors and licence ---------------------------------------------------------------------------

    [Fact]
    public async Task An_unhandled_exception_is_a_500_with_a_trace_id_that_matches_the_request_id_header()
    {
        var admin = await SignedIn(ApiFactory.AdminUser, ApiFactory.AdminPin);

        var response = await admin.GetAsync("/api/v1/fake/throws");

        await AssertProblem(response, HttpStatusCode.InternalServerError, "internal_error");
        var traceId = (await Body(response)).GetProperty("traceId").GetString();
        Assert.Equal(traceId, response.Headers.GetValues("X-Request-Id").Single());
    }

    [Fact]
    public async Task A_license_exception_from_a_service_is_403_license_sales_stopped()
    {
        var admin = await SignedIn(ApiFactory.AdminUser, ApiFactory.AdminPin);

        await AssertProblem(await admin.GetAsync("/api/v1/fake/license-exception"), HttpStatusCode.Forbidden, "license_sales_stopped");
    }

    [Fact]
    public async Task While_sales_are_stopped_a_sales_endpoint_is_403_but_everything_else_still_works()
    {
        var admin = await SignedIn(ApiFactory.AdminUser, ApiFactory.AdminPin);

        await AssertProblem(await admin.GetAsync("/api/v1/fake/sales-only"), HttpStatusCode.Forbidden, "license_sales_stopped");
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/v1/session")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/v1/fake/signed-in")).StatusCode);
    }

    // ---- language ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Accept_Language_picks_the_language_of_errors()
    {
        var client = _factory.CreateBrowser();

        var arabic = await Body(await client.SendAsync(Json(HttpMethod.Get, "/api/v1/fake/signed-in", language: "ar-LB,ar;q=0.9,en;q=0.5")));
        var english = await Body(await client.SendAsync(Json(HttpMethod.Get, "/api/v1/fake/signed-in", language: "en-US,en;q=0.9")));

        Assert.Equal("انتهت جلستك. يرجى تسجيل الدخول مرة أخرى.", arabic.GetProperty("title").GetString());
        Assert.Equal("Your session has ended. Please sign in again.", english.GetProperty("title").GetString());
    }

    [Fact]
    public async Task An_api_call_does_not_change_the_language_cookie_of_the_razor_pages()
    {
        var response = await _factory.CreateBrowser().SendAsync(Json(HttpMethod.Get, "/api/v1/fake/open", language: "ar"));

        Assert.False(response.Headers.TryGetValues("Set-Cookie", out var cookies) && cookies.Any(c => c.Contains(".AspNetCore.Culture")));
    }

    [Fact]
    public async Task The_request_language_reaches_the_actions()
    {
        var admin = await SignedIn(ApiFactory.AdminUser, ApiFactory.AdminPin);

        var body = await Body(await admin.SendAsync(Json(HttpMethod.Get, "/api/v1/fake/language", language: "ar")));

        Assert.Equal("ar", body.GetProperty("language").GetString());
    }

    // ---- sign in / out and the session ---------------------------------------------------------------------------

    [Fact]
    public async Task Signing_in_returns_the_session_and_the_cookie_works_on_the_next_request()
    {
        var browser = _factory.CreateBrowser();

        var login = await browser.SendAsync(Json(HttpMethod.Post, "/api/v1/auth/login",
            new { username = ApiFactory.AdminUser, pin = ApiFactory.AdminPin }));
        var session = await browser.GetAsync("/api/v1/session");

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Contains(login.Headers.GetValues("Set-Cookie"), c => c.StartsWith(".AspNetCore.Cookies=", StringComparison.Ordinal) && c.Contains("httponly", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(HttpStatusCode.OK, session.StatusCode);

        var fromLogin = await Body(login);
        var fromSession = await Body(session);
        Assert.Equal(ApiFactory.AdminUser, fromLogin.GetProperty("user").GetProperty("username").GetString());
        Assert.Equal(fromSession.GetProperty("user").GetProperty("id").GetInt32(), fromLogin.GetProperty("user").GetProperty("id").GetInt32());
        Assert.Equal(fromSession.GetProperty("permissions").GetRawText(), fromLogin.GetProperty("permissions").GetRawText());
    }

    [Fact]
    public async Task The_session_carries_user_role_permissions_nav_license_branding_and_language()
    {
        var admin = await SignedIn(ApiFactory.AdminUser, ApiFactory.AdminPin);

        var session = await Body(await admin.GetAsync("/api/v1/session"));

        Assert.Equal("Layla Haddad", session.GetProperty("user").GetProperty("fullName").GetString());
        Assert.Equal("LH", session.GetProperty("user").GetProperty("initials").GetString());
        Assert.False(string.IsNullOrEmpty(session.GetProperty("role").GetProperty("name").GetString()));
        var permissions = session.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()).ToList();
        Assert.Contains("Users.View", permissions);
        Assert.Equal(permissions.OrderBy(p => p, StringComparer.Ordinal), permissions);

        var items = session.GetProperty("nav").EnumerateArray().SelectMany(s => s.GetProperty("items").EnumerateArray()).ToList();
        var page = items.Single(i => i.GetProperty("key").GetString() == "POS");  // a page no track has migrated yet
        Assert.Equal("/app/pos", page.GetProperty("spaPath").GetString());
        Assert.Equal("/en/POS", page.GetProperty("legacyPath").GetString());
        Assert.Equal("/en/POS", page.GetProperty("href").GetString());   // not migrated yet: a plain link to the Razor page
        Assert.False(page.GetProperty("migrated").GetBoolean());
        Assert.Equal("/app/cash-drawer", items.Single(i => i.GetProperty("key").GetString() == "CashDrawer").GetProperty("spaPath").GetString());

        Assert.False(session.GetProperty("license").GetProperty("allowsSales").GetBoolean()); // no licence file in the test host
        // The banner text is composed on the server (LicenseMessageHelper) so the Razor and React banners cannot drift apart.
        Assert.False(string.IsNullOrWhiteSpace(session.GetProperty("license").GetProperty("title").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(session.GetProperty("license").GetProperty("message").GetString()));
        Assert.Equal("danger", session.GetProperty("license").GetProperty("severity").GetString());
        Assert.False(string.IsNullOrEmpty(session.GetProperty("branding").GetProperty("appName").GetString()));
        Assert.StartsWith("/branding/logo", session.GetProperty("branding").GetProperty("logoUrl").GetString());
        Assert.Equal("en", session.GetProperty("language").GetString());
        Assert.Equal("ltr", session.GetProperty("direction").GetString());
        Assert.True(session.GetProperty("unreadNotifications").GetInt32() >= 0);
    }

    [Fact]
    public async Task The_navigation_is_filtered_by_permission()
    {
        var cashier = await SignedIn(ApiFactory.CashierUser, ApiFactory.CashierPin);

        var session = await Body(await cashier.GetAsync("/api/v1/session"));

        var keys = session.GetProperty("nav").EnumerateArray()
            .SelectMany(s => s.GetProperty("items").EnumerateArray()).Select(i => i.GetProperty("key").GetString()).ToList();
        Assert.Contains("POS", keys);
        Assert.Contains("Products", keys);
        Assert.DoesNotContain("Users", keys);
        Assert.DoesNotContain("Backup", keys);
        Assert.DoesNotContain("AuditLogs", keys);
        Assert.DoesNotContain("Users.View", session.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()));
    }

    [Fact]
    public async Task The_session_follows_Accept_Language_in_text_and_direction()
    {
        var admin = await SignedIn(ApiFactory.AdminUser, ApiFactory.AdminPin);

        var session = await Body(await admin.SendAsync(Json(HttpMethod.Get, "/api/v1/session", language: "ar")));

        Assert.Equal("ar", session.GetProperty("language").GetString());
        Assert.Equal("rtl", session.GetProperty("direction").GetString());
        var users = session.GetProperty("nav").EnumerateArray()
            .SelectMany(s => s.GetProperty("items").EnumerateArray()).Single(i => i.GetProperty("key").GetString() == "Users");
        Assert.Equal("/ar/Users", users.GetProperty("legacyPath").GetString());
        Assert.NotEqual(users.GetProperty("labelKey").GetString(), users.GetProperty("label").GetString());
    }

    [Fact]
    public async Task Wrong_credentials_answer_401_and_never_say_which_half_was_wrong()
    {
        var browser = _factory.CreateBrowser();

        var wrongPin = await browser.SendAsync(Json(HttpMethod.Post, "/api/v1/auth/login", new { username = ApiFactory.AdminUser, pin = "9999" }));
        var unknown = await browser.SendAsync(Json(HttpMethod.Post, "/api/v1/auth/login", new { username = "nobody", pin = "9999" }));

        await AssertProblem(wrongPin, HttpStatusCode.Unauthorized, "invalid_credentials");
        await AssertProblem(unknown, HttpStatusCode.Unauthorized, "invalid_credentials");
        Assert.Equal((await Body(wrongPin)).GetProperty("detail").GetString(), (await Body(unknown)).GetProperty("detail").GetString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync("/api/v1/session")).StatusCode); // no cookie was issued
    }

    [Fact]
    public async Task A_malformed_pin_is_a_validation_error_before_any_lookup()
    {
        var response = await _factory.CreateBrowser().SendAsync(
            Json(HttpMethod.Post, "/api/v1/auth/login", new { username = ApiFactory.AdminUser, pin = "12" }));

        await AssertProblem(response, HttpStatusCode.BadRequest, "validation_failed");
        Assert.True((await Body(response)).GetProperty("errors").TryGetProperty("pin", out _));
    }

    [Fact]
    public async Task Signing_out_ends_the_session_and_is_harmless_when_repeated()
    {
        var admin = await SignedIn(ApiFactory.AdminUser, ApiFactory.AdminPin);

        var first = await admin.SendAsync(Json(HttpMethod.Post, "/api/v1/auth/logout"));
        var after = await admin.GetAsync("/api/v1/session");
        var second = await admin.SendAsync(Json(HttpMethod.Post, "/api/v1/auth/logout"));

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        await AssertProblem(after, HttpStatusCode.Unauthorized, "unauthenticated");
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);
    }

    [Fact]
    public async Task The_sign_in_screen_can_list_who_may_sign_in_without_a_cookie()
    {
        var response = await _factory.CreateBrowser().GetAsync("/api/v1/auth/sign-in-options");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await Body(response);
        var users = body.GetProperty("users").EnumerateArray().Select(u => u.GetProperty("username").GetString()).ToList();
        Assert.Contains(ApiFactory.AdminUser, users);
        Assert.Contains(ApiFactory.CashierUser, users);
        Assert.DoesNotContain("pinHash", body.GetRawText(), StringComparison.OrdinalIgnoreCase);
        // The login screen draws the app name and logo before anyone is signed in.
        Assert.False(string.IsNullOrEmpty(body.GetProperty("branding").GetProperty("appName").GetString()));
        Assert.StartsWith("/branding/logo", body.GetProperty("branding").GetProperty("markUrl").GetString());
    }

    [Fact]
    public void The_login_body_is_redacted_before_the_audit_trail_stores_it()
    {
        // AuditRequestMiddleware stores the redacted JSON body of API posts; the field names of LoginRequest must stay covered.
        var stored = Core.Auditing.AuditRedactor.RedactJson("{\"username\":\"layla\",\"pin\":\"4321\",\"rememberMe\":true}");

        Assert.NotNull(stored);
        Assert.DoesNotContain("4321", stored);
        Assert.Contains("layla", stored);
    }

    // ---- swagger -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Swagger_lists_auth_and_session_and_only_the_versioned_api()
    {
        var response = await _factory.CreateBrowser().GetAsync("/swagger/v1/swagger.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var paths = (await Body(response)).GetProperty("paths").EnumerateObject().Select(p => p.Name).ToList();
        Assert.Contains("/api/v1/auth/login", paths);
        Assert.Contains("/api/v1/auth/logout", paths);
        Assert.Contains("/api/v1/auth/sign-in-options", paths);
        Assert.Contains("/api/v1/session", paths);
        Assert.All(paths, p => Assert.StartsWith("/api/v1/", p));
    }

    [Fact]
    public async Task The_contract_marks_required_fields_and_documents_the_error_envelope()
    {
        var swagger = await Body(await _factory.CreateBrowser().GetAsync("/swagger/v1/swagger.json"));

        var schemas = swagger.GetProperty("components").GetProperty("schemas");
        var session = schemas.GetProperty("SessionDto");
        Assert.Contains("permissions", session.GetProperty("required").EnumerateArray().Select(r => r.GetString()));
        Assert.False(session.GetProperty("properties").GetProperty("permissions").TryGetProperty("nullable", out var nullable) && nullable.GetBoolean());

        var problem = schemas.GetProperty("ApiProblemDetails").GetProperty("properties");
        Assert.True(problem.TryGetProperty("code", out _));
        Assert.True(problem.TryGetProperty("traceId", out _));
        Assert.True(problem.TryGetProperty("errors", out _));

        var login = swagger.GetProperty("paths").GetProperty("/api/v1/auth/login").GetProperty("post");
        Assert.False(login.GetProperty("responses").TryGetProperty("403", out _), "a public action does not declare 401/403 from the base class");
        Assert.Single(login.GetProperty("requestBody").GetProperty("content").EnumerateObject());
    }
}
