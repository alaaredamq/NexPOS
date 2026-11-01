using System.Net;
using System.Text.Json;
using Application.IServices.Logging;
using Core.Auditing;
using Microsoft.Extensions.DependencyInjection;
using Tests.Api.Catalog;
using Tests.Api.Infrastructure;
using Web.Helpers;
using Xunit;

namespace Tests.Api.Users;

/// <summary>
/// Users, roles and account API (track A1): the permission matrix of the Razor <c>UsersController</c>, user CRUD with its uniqueness / role /
/// PIN rules, the role editor (grants, locked and in-use roles), the rule that an edit reaches people who are already signed in on their next
/// request, and the signed-in user's own PIN change (including the admin-set PIN that needs no current PIN).
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class UsersApiTests : CatalogApiTestBase
{
    public UsersApiTests(ApiFactory factory) : base(factory)
    {
    }

    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..14];

    private static HttpRequestMessage Put(string url, object body) => Json(HttpMethod.Put, url, body);

    private static async Task<int> CashierRoleIdAsync(HttpClient admin)
        => await RoleIdAsync(admin, "Cashier");

    private static async Task<int> RoleIdAsync(HttpClient admin, string name)
    {
        var roles = await Body(await admin.GetAsync("/api/v1/roles?pageSize=200"));
        return roles.GetProperty("items").EnumerateArray().Single(r => r.GetProperty("name").GetString() == name).GetProperty("id").GetInt32();
    }

    private static async Task<int> CreateRoleAsync(HttpClient admin, string name, params string[] permissions)
    {
        var response = await admin.SendAsync(Json(HttpMethod.Post, "/api/v1/roles", new { name, description = "test role", permissions }));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await Body(response)).GetProperty("role").GetProperty("id").GetInt32();
    }

    /// <summary>Creates a user and returns their id; the PIN is theirs to sign in with (and to change: the admin chose it).</summary>
    private static async Task<(int Id, string Username)> CreateUserAsync(HttpClient admin, int roleId, string pin = "4321", bool active = true, string? prefix = null)
    {
        var username = Unique(prefix ?? "usr");
        var response = await admin.SendAsync(Json(HttpMethod.Post, "/api/v1/users", new
        {
            username,
            email = $"{username}@example.com",
            firstName = "Test",
            lastName = "User",
            roleId,
            isActive = active,
            pin,
        }));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return ((await Body(response)).GetProperty("id").GetInt32(), username);
    }

    private async Task<HttpResponseMessage> SignInAsync(string username, string pin)
        => await Factory.CreateBrowser().SendAsync(Json(HttpMethod.Post, "/api/v1/auth/login", new { username, pin }));

    private async Task<HttpClient> SignedInAs(string username, string pin) => await SignedIn(username, pin);

    // ---- permissions -------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("/api/v1/users")]
    [InlineData("/api/v1/users/summary")]
    [InlineData("/api/v1/roles")]
    [InlineData("/api/v1/account/profile")]
    public async Task Without_a_cookie_everything_answers_401(string url)
    {
        var response = await Factory.CreateBrowser().GetAsync(url);

        await AssertProblem(response, HttpStatusCode.Unauthorized, "unauthenticated");
    }

    [Fact]
    public async Task A_cashier_cannot_see_or_change_users_and_roles_but_owns_their_account()
    {
        var cashier = await Cashier();
        var admin = await Admin();
        var (id, _) = await CreateUserAsync(admin, await CashierRoleIdAsync(admin));
        var roleId = await CashierRoleIdAsync(admin);

        foreach (var url in new[] { "/api/v1/users", "/api/v1/users/summary", $"/api/v1/users/{id}", "/api/v1/roles", $"/api/v1/roles/{roleId}" })
            await AssertProblem(await cashier.GetAsync(url), HttpStatusCode.Forbidden, "forbidden");

        await AssertProblem(await cashier.SendAsync(Json(HttpMethod.Post, "/api/v1/users", new { username = "x", email = "x@x.com", roleId, pin = "1234" })), HttpStatusCode.Forbidden, "forbidden");
        await AssertProblem(await cashier.SendAsync(Put($"/api/v1/users/{id}", new { email = "x@x.com", roleId, isActive = true })), HttpStatusCode.Forbidden, "forbidden");
        await AssertProblem(await cashier.SendAsync(Put($"/api/v1/users/{id}/active", new { isActive = false })), HttpStatusCode.Forbidden, "forbidden");
        await AssertProblem(await cashier.SendAsync(Json(HttpMethod.Post, $"/api/v1/users/{id}/reset-pin", new { newPin = "1111" })), HttpStatusCode.Forbidden, "forbidden");
        await AssertProblem(await cashier.SendAsync(Json(HttpMethod.Delete, $"/api/v1/users/{id}")), HttpStatusCode.Forbidden, "forbidden");
        await AssertProblem(await cashier.SendAsync(Json(HttpMethod.Post, "/api/v1/roles", new { name = "Nope" })), HttpStatusCode.Forbidden, "forbidden");
        await AssertProblem(await cashier.SendAsync(Put($"/api/v1/roles/{roleId}", new { name = "Nope" })), HttpStatusCode.Forbidden, "forbidden");
        await AssertProblem(await cashier.SendAsync(Json(HttpMethod.Delete, $"/api/v1/roles/{roleId}")), HttpStatusCode.Forbidden, "forbidden");

        var profile = await cashier.GetAsync("/api/v1/account/profile");
        Assert.Equal(HttpStatusCode.OK, profile.StatusCode);
        Assert.Equal("sara", (await Body(profile)).GetProperty("username").GetString());
    }

    [Fact]
    public async Task A_user_who_can_view_but_not_edit_gets_403_on_the_other_actions()
    {
        // Users.View only: the class-level permission of the Razor controller alone is not enough for the rest.
        var admin = await Admin();
        var roleId = await CreateRoleAsync(admin, Unique("Viewer"), PermissionHelper.Permissions.Users_View);
        var (target, _) = await CreateUserAsync(admin, roleId);
        var (_, viewerName) = await CreateUserAsync(admin, roleId);
        var viewer = await SignedInAs(viewerName, "4321");

        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync("/api/v1/users")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync("/api/v1/users/summary")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync("/api/v1/roles")).StatusCode);
        await AssertProblem(await viewer.GetAsync($"/api/v1/roles/{roleId}"), HttpStatusCode.Forbidden, "forbidden"); // grants need Users.Manage
        await AssertProblem(await viewer.SendAsync(Put($"/api/v1/users/{target}/active", new { isActive = false })), HttpStatusCode.Forbidden, "forbidden");
        await AssertProblem(await viewer.SendAsync(Json(HttpMethod.Delete, $"/api/v1/users/{target}")), HttpStatusCode.Forbidden, "forbidden");
    }

    [Fact]
    public async Task A_mutation_without_the_client_header_is_refused()
    {
        var admin = await Admin();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/account/change-pin")
        {
            Content = System.Net.Http.Json.JsonContent.Create(new { currentPin = "1234", newPin = "5678", confirmPin = "5678" }),
        };

        await AssertProblem(await admin.SendAsync(request), HttpStatusCode.Forbidden, "client_header_required");
    }

    // ---- users: list ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_list_pages_sorts_and_searches_by_name_email_username_and_role()
    {
        var admin = await Admin();
        var cashier = await CashierRoleIdAsync(admin);
        var tag = Unique("Srch");
        foreach (var first in new[] { "Bravo", "Alpha", "Charlie" })
        {
            var response = await admin.SendAsync(Json(HttpMethod.Post, "/api/v1/users", new
            {
                username = $"{tag}{first}", email = $"{tag}{first}@example.com", firstName = $"{tag}{first}", lastName = "Smith", roleId = cashier, pin = "4321",
            }));
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }

        var byName = await Body(await admin.GetAsync($"/api/v1/users?q={tag}&sort=name&dir=asc"));
        Assert.Equal(3, byName.GetProperty("total").GetInt32());
        Assert.Equal(new[] { $"{tag}Alpha", $"{tag}Bravo", $"{tag}Charlie" }, byName.GetProperty("items").EnumerateArray().Select(u => u.GetProperty("username").GetString()).ToArray());

        var page2 = await Body(await admin.GetAsync($"/api/v1/users?q={tag}&sort=name&dir=desc&page=2&pageSize=2"));
        Assert.Equal(3, page2.GetProperty("total").GetInt32());
        Assert.Equal(new[] { $"{tag}Alpha" }, page2.GetProperty("items").EnumerateArray().Select(u => u.GetProperty("username").GetString()).ToArray());

        Assert.Equal(1, (await Body(await admin.GetAsync($"/api/v1/users?q={tag}bravo%40example"))).GetProperty("total").GetInt32());
        Assert.Equal(1, (await Body(await admin.GetAsync($"/api/v1/users?q={tag}Charlie%20Smith"))).GetProperty("total").GetInt32());
        Assert.True((await Body(await admin.GetAsync("/api/v1/users?q=Cashier"))).GetProperty("total").GetInt32() >= 3); // role name
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/v1/users?pageSize=1000")).StatusCode);

        // Anything that is not a known column sorts by name instead of failing.
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/v1/users?sort=passwordHash")).StatusCode);
    }

    [Fact]
    public async Task The_list_never_carries_a_credential()
    {
        var admin = await Admin();

        var text = await (await admin.GetAsync("/api/v1/users?pageSize=200")).Content.ReadAsStringAsync();

        Assert.DoesNotContain("hash", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"pin\"", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_summary_counts_users_roles_active_users_and_whoever_can_manage_users()
    {
        var admin = await Admin();
        var before = await Body(await admin.GetAsync("/api/v1/users/summary"));
        var cashier = await CashierRoleIdAsync(admin);
        var manageRole = await CreateRoleAsync(admin, Unique("Mgr"), PermissionHelper.Permissions.Users_Manage);
        await CreateUserAsync(admin, cashier);
        await CreateUserAsync(admin, cashier, active: false);
        await CreateUserAsync(admin, manageRole);

        var after = await Body(await admin.GetAsync("/api/v1/users/summary"));

        Assert.Equal(before.GetProperty("totalUsers").GetInt32() + 3, after.GetProperty("totalUsers").GetInt32());
        Assert.Equal(before.GetProperty("activeUsers").GetInt32() + 2, after.GetProperty("activeUsers").GetInt32());
        Assert.Equal(before.GetProperty("totalRoles").GetInt32() + 1, after.GetProperty("totalRoles").GetInt32());
        Assert.Equal(before.GetProperty("adminUsers").GetInt32() + 1, after.GetProperty("adminUsers").GetInt32());
    }

    // ---- users: create / update ----------------------------------------------------------------------------------

    [Fact]
    public async Task A_created_user_signs_in_with_the_admins_pin_and_has_to_change_it()
    {
        var admin = await Admin();
        var (id, username) = await CreateUserAsync(admin, await CashierRoleIdAsync(admin), pin: "2468");

        var user = await Body(await admin.GetAsync($"/api/v1/users/{id}"));
        Assert.Equal("Test User", user.GetProperty("fullName").GetString());
        Assert.Equal("Cashier", user.GetProperty("roleName").GetString());
        Assert.True(user.GetProperty("mustChangePin").GetBoolean());
        Assert.Equal(JsonValueKind.Null, user.GetProperty("lastLoginAt").ValueKind);

        var login = await SignInAsync(username, "2468");
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.True((await Body(login)).GetProperty("user").GetProperty("mustChangePin").GetBoolean());
        Assert.NotEqual(HttpStatusCode.OK, (await SignInAsync(username, "1357")).StatusCode);
    }

    [Fact]
    public async Task Creating_a_user_refuses_a_taken_username_a_taken_email_an_unknown_role_and_a_bad_pin()
    {
        var admin = await Admin();
        var cashier = await CashierRoleIdAsync(admin);
        var (_, taken) = await CreateUserAsync(admin, cashier);

        async Task<JsonElement> Refused(object body)
        {
            var response = await admin.SendAsync(Json(HttpMethod.Post, "/api/v1/users", body));
            await AssertProblem(response, HttpStatusCode.BadRequest, "validation_failed");
            return (await Body(response)).GetProperty("errors");
        }

        var name = Unique("fresh");
        Assert.True((await Refused(new { username = taken, email = $"{name}@example.com", roleId = cashier, pin = "1234" })).TryGetProperty("username", out _));
        Assert.True((await Refused(new { username = name, email = $"{taken}@example.com", roleId = cashier, pin = "1234" })).TryGetProperty("email", out _));
        Assert.True((await Refused(new { username = name, email = $"{name}@example.com", roleId = 999999, pin = "1234" })).TryGetProperty("roleId", out _));
        Assert.True((await Refused(new { username = "   ", email = $"{name}@example.com", roleId = cashier, pin = "1234" })).TryGetProperty("username", out _));
        foreach (var pin in new[] { "123", "12345", "12a4", "" })
            Assert.True((await Refused(new { username = name, email = $"{name}@example.com", roleId = cashier, pin })).TryGetProperty("pin", out _), $"PIN '{pin}'");
        Assert.True((await Refused(new { username = name, email = "not-an-email", roleId = cashier, pin = "1234" })).TryGetProperty("email", out _));
    }

    [Fact]
    public async Task Editing_a_user_saves_the_fields_and_keeps_the_username_and_pin()
    {
        var admin = await Admin();
        var cashier = await CashierRoleIdAsync(admin);
        var manager = await RoleIdAsync(admin, "Manager");
        var (id, username) = await CreateUserAsync(admin, cashier);
        var email = $"{Unique("new")}@example.com";

        var response = await admin.SendAsync(Put($"/api/v1/users/{id}", new { email, firstName = "  Nadia ", lastName = "", roleId = manager, isActive = true }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var user = await Body(response);
        Assert.Equal(username, user.GetProperty("username").GetString());
        Assert.Equal(email, user.GetProperty("email").GetString());
        Assert.Equal("Nadia", user.GetProperty("firstName").GetString());
        Assert.Equal(JsonValueKind.Null, user.GetProperty("lastName").ValueKind);
        Assert.Equal("Manager", user.GetProperty("roleName").GetString());
        Assert.Equal(HttpStatusCode.OK, (await SignInAsync(username, "4321")).StatusCode);
    }

    [Fact]
    public async Task Editing_a_user_refuses_an_email_another_user_has_and_an_unknown_role_and_404s_for_nobody()
    {
        var admin = await Admin();
        var cashier = await CashierRoleIdAsync(admin);
        var (first, firstName) = await CreateUserAsync(admin, cashier);
        var (second, _) = await CreateUserAsync(admin, cashier);

        var clash = await admin.SendAsync(Put($"/api/v1/users/{second}", new { email = $"{firstName}@example.com", roleId = cashier, isActive = true }));
        await AssertProblem(clash, HttpStatusCode.BadRequest, "validation_failed");
        Assert.True((await Body(clash)).GetProperty("errors").TryGetProperty("email", out _));

        // Their own email is not a clash.
        Assert.Equal(HttpStatusCode.OK, (await admin.SendAsync(Put($"/api/v1/users/{first}", new { email = $"{firstName}@example.com", roleId = cashier, isActive = true }))).StatusCode);

        await AssertProblem(await admin.SendAsync(Put($"/api/v1/users/{first}", new { email = $"{firstName}@example.com", roleId = 999999, isActive = true })), HttpStatusCode.BadRequest, "validation_failed");
        await AssertProblem(await admin.SendAsync(Put("/api/v1/users/999999", new { email = "a@b.com", roleId = cashier, isActive = true })), HttpStatusCode.NotFound, "not_found");
        await AssertProblem(await admin.GetAsync("/api/v1/users/999999"), HttpStatusCode.NotFound, "not_found");
    }

    [Fact]
    public async Task Disabling_a_user_turns_their_open_session_away_on_the_next_request()
    {
        var admin = await Admin();
        var (id, username) = await CreateUserAsync(admin, await CashierRoleIdAsync(admin));
        var them = await SignedInAs(username, "4321");
        Assert.Equal(HttpStatusCode.OK, (await them.GetAsync("/api/v1/account/profile")).StatusCode);

        var disabled = await admin.SendAsync(Put($"/api/v1/users/{id}/active", new { isActive = false }));
        Assert.False((await Body(disabled)).GetProperty("isActive").GetBoolean());

        await AssertProblem(await them.GetAsync("/api/v1/account/profile"), HttpStatusCode.Unauthorized, "unauthenticated");
        Assert.NotEqual(HttpStatusCode.OK, (await SignInAsync(username, "4321")).StatusCode);

        Assert.True((await Body(await admin.SendAsync(Put($"/api/v1/users/{id}/active", new { isActive = true })))).GetProperty("isActive").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await SignInAsync(username, "4321")).StatusCode);
    }

    [Fact]
    public async Task Resetting_a_pin_replaces_it_and_makes_the_user_change_it_again()
    {
        var admin = await Admin();
        var (id, username) = await CreateUserAsync(admin, await CashierRoleIdAsync(admin), pin: "1111");
        var them = await SignedInAs(username, "1111");
        // They chose their own PIN already ...
        Assert.Equal(HttpStatusCode.NoContent, (await them.SendAsync(Json(HttpMethod.Post, "/api/v1/account/change-pin", new { newPin = "2222", confirmPin = "2222" }))).StatusCode);
        Assert.False((await Body(await admin.GetAsync($"/api/v1/users/{id}"))).GetProperty("mustChangePin").GetBoolean());

        // ... and the admin resets it.
        Assert.Equal(HttpStatusCode.NoContent, (await admin.SendAsync(Json(HttpMethod.Post, $"/api/v1/users/{id}/reset-pin", new { newPin = "3333" }))).StatusCode);

        Assert.NotEqual(HttpStatusCode.OK, (await SignInAsync(username, "2222")).StatusCode);
        var login = await SignInAsync(username, "3333");
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.True((await Body(login)).GetProperty("user").GetProperty("mustChangePin").GetBoolean());

        await AssertProblem(await admin.SendAsync(Json(HttpMethod.Post, $"/api/v1/users/{id}/reset-pin", new { newPin = "12" })), HttpStatusCode.BadRequest, "validation_failed");
        await AssertProblem(await admin.SendAsync(Json(HttpMethod.Post, "/api/v1/users/999999/reset-pin", new { newPin = "1234" })), HttpStatusCode.NotFound, "not_found");
    }

    // ---- users: delete -------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_user_without_history_can_be_deleted_and_is_gone()
    {
        var admin = await Admin();
        var (id, username) = await CreateUserAsync(admin, await CashierRoleIdAsync(admin));
        await SignedInAs(username, "4321"); // a sign-in leaves a session row behind; it goes with the user

        Assert.Equal(HttpStatusCode.NoContent, (await admin.SendAsync(Json(HttpMethod.Delete, $"/api/v1/users/{id}"))).StatusCode);

        await AssertProblem(await admin.GetAsync($"/api/v1/users/{id}"), HttpStatusCode.NotFound, "not_found");
        await AssertProblem(await admin.SendAsync(Json(HttpMethod.Delete, $"/api/v1/users/{id}")), HttpStatusCode.NotFound, "not_found");
        Assert.NotEqual(HttpStatusCode.OK, (await SignInAsync(username, "4321")).StatusCode);
    }

    [Fact]
    public async Task A_user_with_activity_on_record_cannot_be_deleted_only_deactivated()
    {
        var admin = await Admin();
        var admins = await RoleIdAsync(admin, "Admin");
        var (id, username) = await CreateUserAsync(admin, admins);
        var staff = await SignedInAs(username, "4321");

        // A ledger entry the user recorded points back at them.
        var customer = await staff.SendAsync(Json(HttpMethod.Post, "/api/v1/customers", new { firstName = "Hist", lastName = Unique("Ory") }));
        var customerId = (await Body(customer)).GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.Created, (await staff.SendAsync(Json(HttpMethod.Post, $"/api/v1/customers/{customerId}/debt-transactions", new { amount = 5, customerOwesMore = true }))).StatusCode);

        await AssertProblem(await admin.SendAsync(Json(HttpMethod.Delete, $"/api/v1/users/{id}")), HttpStatusCode.Conflict, "user_in_use");

        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/api/v1/users/{id}")).StatusCode);
        Assert.False((await Body(await admin.SendAsync(Put($"/api/v1/users/{id}/active", new { isActive = false })))).GetProperty("isActive").GetBoolean());
    }

    // ---- roles ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_role_list_shows_user_and_permission_counts_and_marks_the_locked_roles()
    {
        var admin = await Admin();

        var roles = (await Body(await admin.GetAsync("/api/v1/roles?pageSize=200"))).GetProperty("items").EnumerateArray().ToList();

        var superAdmin = roles.Single(r => r.GetProperty("name").GetString() == "SuperAdmin");
        Assert.True(superAdmin.GetProperty("isSuperAdmin").GetBoolean());
        Assert.True(superAdmin.GetProperty("isSystemRole").GetBoolean());
        Assert.Equal(PermissionHelper.Permissions.All.Length, superAdmin.GetProperty("permissionCount").GetInt32());

        var cashier = roles.Single(r => r.GetProperty("name").GetString() == "Cashier");
        Assert.False(cashier.GetProperty("isSuperAdmin").GetBoolean());
        Assert.True(cashier.GetProperty("userCount").GetInt32() >= 1);
        Assert.Equal(PermissionHelper.Permissions.Normalize(PermissionHelper.DefaultRolePermissions["Cashier"]).Count, cashier.GetProperty("permissionCount").GetInt32());

        var sorted = (await Body(await admin.GetAsync("/api/v1/roles?sort=permissionCount&dir=desc"))).GetProperty("items").EnumerateArray().Select(r => r.GetProperty("permissionCount").GetInt32()).ToList();
        Assert.Equal(sorted.OrderByDescending(x => x), sorted);
        Assert.Equal(1, (await Body(await admin.GetAsync("/api/v1/roles?q=supera"))).GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task A_role_is_created_with_exactly_its_known_permissions_and_read_back()
    {
        var admin = await Admin();
        var name = Unique("Shift");

        var response = await admin.SendAsync(Json(HttpMethod.Post, "/api/v1/roles", new
        {
            name = $"  {name}  ",
            description = " Closes the shop ",
            permissions = new[] { "Products.View", "Orders.View", "Products.View", "Not.APermission" },
        }));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await Body(response);
        var id = created.GetProperty("role").GetProperty("id").GetInt32();
        Assert.Equal(name, created.GetProperty("role").GetProperty("name").GetString());
        Assert.Equal("Closes the shop", created.GetProperty("role").GetProperty("description").GetString());
        Assert.Equal(new[] { "Products.View", "Orders.View" }.OrderBy(x => Array.IndexOf(PermissionHelper.Permissions.All, x)), created.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()!));

        var read = await Body(await admin.GetAsync($"/api/v1/roles/{id}"));
        Assert.Equal(2, read.GetProperty("role").GetProperty("permissionCount").GetInt32());
        Assert.Equal(0, read.GetProperty("role").GetProperty("userCount").GetInt32());
    }

    [Fact]
    public async Task A_role_name_must_be_present_short_enough_and_unique_without_regard_to_case()
    {
        var admin = await Admin();
        var name = Unique("Dup");
        await CreateRoleAsync(admin, name);

        foreach (var bad in new[] { name.ToUpperInvariant(), "   ", new string('x', 101) })
        {
            var response = await admin.SendAsync(Json(HttpMethod.Post, "/api/v1/roles", new { name = bad }));
            await AssertProblem(response, HttpStatusCode.BadRequest, "validation_failed");
            Assert.True((await Body(response)).GetProperty("errors").TryGetProperty("name", out _), $"name '{bad}'");
        }
    }

    [Fact]
    public async Task Updating_a_role_replaces_name_and_grants_and_the_super_admin_role_is_locked()
    {
        var admin = await Admin();
        var id = await CreateRoleAsync(admin, Unique("Edit"), "Products.View", "Orders.View");
        var renamed = Unique("Renamed");

        var response = await admin.SendAsync(Put($"/api/v1/roles/{id}", new { name = renamed, description = (string?)null, permissions = new[] { "Customers.View" } }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await Body(response);
        Assert.Equal(renamed, updated.GetProperty("role").GetProperty("name").GetString());
        Assert.Equal(new[] { "Customers.View" }, updated.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()!));

        // Keeping its own name is not a clash; another role's name is.
        Assert.Equal(HttpStatusCode.OK, (await admin.SendAsync(Put($"/api/v1/roles/{id}", new { name = renamed, permissions = Array.Empty<string>() }))).StatusCode);
        await AssertProblem(await admin.SendAsync(Put($"/api/v1/roles/{id}", new { name = "Cashier" })), HttpStatusCode.BadRequest, "validation_failed");

        var superAdmin = await RoleIdAsync(admin, "SuperAdmin");
        await AssertProblem(await admin.SendAsync(Put($"/api/v1/roles/{superAdmin}", new { name = "SuperAdmin", permissions = new[] { "Products.View" } })), HttpStatusCode.Conflict, "role_super_admin_locked");
        var all = await Body(await admin.GetAsync($"/api/v1/roles/{superAdmin}"));
        Assert.Equal(PermissionHelper.Permissions.All.Length, all.GetProperty("permissions").GetArrayLength());

        await AssertProblem(await admin.SendAsync(Put("/api/v1/roles/999999", new { name = "Ghost" })), HttpStatusCode.NotFound, "not_found");
        await AssertProblem(await admin.GetAsync("/api/v1/roles/999999"), HttpStatusCode.NotFound, "not_found");
    }

    [Fact]
    public async Task A_role_cannot_be_deleted_while_it_is_built_in_or_in_use()
    {
        var admin = await Admin();
        var cashier = await CashierRoleIdAsync(admin);
        // Only SuperAdmin ships as a built-in (system) role; the others are ordinary roles that cannot go while someone holds them.
        var superAdmin = await RoleIdAsync(admin, "SuperAdmin");
        await AssertProblem(await admin.SendAsync(Json(HttpMethod.Delete, $"/api/v1/roles/{superAdmin}")), HttpStatusCode.Conflict, "role_system");
        await AssertProblem(await admin.SendAsync(Json(HttpMethod.Delete, $"/api/v1/roles/{cashier}")), HttpStatusCode.Conflict, "role_in_use");

        var id = await CreateRoleAsync(admin, Unique("Temp"), "Products.View");
        var (userId, _) = await CreateUserAsync(admin, id);
        await AssertProblem(await admin.SendAsync(Json(HttpMethod.Delete, $"/api/v1/roles/{id}")), HttpStatusCode.Conflict, "role_in_use");

        // Move the user away and it goes.
        Assert.Equal(HttpStatusCode.OK, (await admin.SendAsync(Put($"/api/v1/users/{userId}", new { email = $"{Unique("m")}@example.com", roleId = cashier, isActive = true }))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.SendAsync(Json(HttpMethod.Delete, $"/api/v1/roles/{id}"))).StatusCode);
        await AssertProblem(await admin.GetAsync($"/api/v1/roles/{id}"), HttpStatusCode.NotFound, "not_found");
        await AssertProblem(await admin.SendAsync(Json(HttpMethod.Delete, $"/api/v1/roles/{id}")), HttpStatusCode.NotFound, "not_found");
    }

    // ---- permission changes reach signed-in users -----------------------------------------------------------------

    [Fact]
    public async Task A_role_edit_reaches_a_signed_in_user_on_their_next_request_without_signing_in_again()
    {
        var admin = await Admin();
        var roleId = await CreateRoleAsync(admin, Unique("Live"), PermissionHelper.Permissions.Users_View);
        var (_, username) = await CreateUserAsync(admin, roleId);
        var them = await SignedInAs(username, "4321");

        Assert.Equal(HttpStatusCode.OK, (await them.GetAsync("/api/v1/users")).StatusCode);
        await AssertProblem(await them.GetAsync("/api/v1/customers"), HttpStatusCode.Forbidden, "forbidden");
        Assert.DoesNotContain("Customers.View", await PermissionsOf(them));

        // Grant one permission, take another away.
        var edit = await admin.SendAsync(Put($"/api/v1/roles/{roleId}", new { name = "Live role", permissions = new[] { "Customers.View" } }));
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await them.GetAsync("/api/v1/customers")).StatusCode);
        await AssertProblem(await them.GetAsync("/api/v1/users"), HttpStatusCode.Forbidden, "forbidden");
        var now = await PermissionsOf(them);
        Assert.Contains("Customers.View", now);
        Assert.DoesNotContain("Users.View", now);
    }

    [Fact]
    public async Task Moving_a_user_to_another_role_changes_their_permissions_on_the_next_request()
    {
        var admin = await Admin();
        var viewRole = await CreateRoleAsync(admin, Unique("Seer"), PermissionHelper.Permissions.Customers_View);
        var (id, username) = await CreateUserAsync(admin, viewRole);
        var them = await SignedInAs(username, "4321");
        Assert.Equal(HttpStatusCode.OK, (await them.GetAsync("/api/v1/customers")).StatusCode);

        var none = await CreateRoleAsync(admin, Unique("Blind"));
        var move = await admin.SendAsync(Put($"/api/v1/users/{id}", new { email = $"{username}@example.com", roleId = none, isActive = true }));
        Assert.Equal(HttpStatusCode.OK, move.StatusCode);

        await AssertProblem(await them.GetAsync("/api/v1/customers"), HttpStatusCode.Forbidden, "forbidden");
        var session = await Body(await them.GetAsync("/api/v1/session"));
        Assert.Equal(0, session.GetProperty("permissions").GetArrayLength());
    }

    private static async Task<List<string>> PermissionsOf(HttpClient client)
        => (await Body(await client.GetAsync("/api/v1/session"))).GetProperty("permissions").EnumerateArray().Select(p => p.GetString()!).ToList();

    // ---- account: profile and PIN --------------------------------------------------------------------------------

    [Fact]
    public async Task The_profile_is_the_signed_in_users_own()
    {
        var admin = await Admin();

        var profile = await Body(await admin.GetAsync("/api/v1/account/profile"));

        Assert.Equal("layla", profile.GetProperty("username").GetString());
        Assert.Equal("Admin", profile.GetProperty("roleName").GetString());
        Assert.False(profile.GetProperty("mustChangePin").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(profile.GetProperty("email").GetString()));
    }

    [Fact]
    public async Task Changing_your_pin_needs_the_current_one_and_a_matching_new_one()
    {
        var admin = await Admin();
        var (_, username) = await CreateUserAsync(admin, await CashierRoleIdAsync(admin), pin: "1000");
        var them = await SignedInAs(username, "1000");
        // Take the admin-set status away first: from here on the current PIN is asked for.
        Assert.Equal(HttpStatusCode.NoContent, (await them.SendAsync(Json(HttpMethod.Post, "/api/v1/account/change-pin", new { newPin = "2000", confirmPin = "2000" }))).StatusCode);

        async Task<JsonElement> Refused(object body, string field)
        {
            var response = await them.SendAsync(Json(HttpMethod.Post, "/api/v1/account/change-pin", body));
            await AssertProblem(response, HttpStatusCode.BadRequest, "validation_failed");
            var errors = (await Body(response)).GetProperty("errors");
            Assert.True(errors.TryGetProperty(field, out _), $"expected an error on {field}: {errors}");
            return errors;
        }

        await Refused(new { currentPin = "9999", newPin = "3000", confirmPin = "3000" }, "currentPin");   // wrong current PIN
        await Refused(new { newPin = "3000", confirmPin = "3000" }, "currentPin");                          // missing current PIN
        await Refused(new { currentPin = "2000", newPin = "3000", confirmPin = "3001" }, "confirmPin");     // mismatch
        await Refused(new { currentPin = "2000", newPin = "2000", confirmPin = "2000" }, "newPin");         // unchanged
        await Refused(new { currentPin = "2000", newPin = "30", confirmPin = "30" }, "newPin");             // not 4 digits

        // None of the refusals changed anything.
        Assert.Equal(HttpStatusCode.OK, (await SignInAsync(username, "2000")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await them.SendAsync(Json(HttpMethod.Post, "/api/v1/account/change-pin", new { currentPin = "2000", newPin = "3000", confirmPin = "3000" }))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SignInAsync(username, "3000")).StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, (await SignInAsync(username, "2000")).StatusCode);
    }

    [Fact]
    public async Task An_admin_set_pin_is_changed_without_typing_it_again_but_cannot_be_kept()
    {
        var admin = await Admin();
        var (id, username) = await CreateUserAsync(admin, await CashierRoleIdAsync(admin), pin: "5555");
        var them = await SignedInAs(username, "5555");
        Assert.True((await Body(await them.GetAsync("/api/v1/account/profile"))).GetProperty("mustChangePin").GetBoolean());

        var keep = await them.SendAsync(Json(HttpMethod.Post, "/api/v1/account/change-pin", new { newPin = "5555", confirmPin = "5555" }));
        await AssertProblem(keep, HttpStatusCode.BadRequest, "validation_failed");
        Assert.True((await Body(keep)).GetProperty("errors").TryGetProperty("newPin", out _));
        Assert.True((await Body(await admin.GetAsync($"/api/v1/users/{id}"))).GetProperty("mustChangePin").GetBoolean());

        Assert.Equal(HttpStatusCode.NoContent, (await them.SendAsync(Json(HttpMethod.Post, "/api/v1/account/change-pin", new { newPin = "6666", confirmPin = "6666" }))).StatusCode);

        Assert.False((await Body(await them.GetAsync("/api/v1/session"))).GetProperty("user").GetProperty("mustChangePin").GetBoolean());
        Assert.False((await Body(await admin.GetAsync($"/api/v1/users/{id}"))).GetProperty("mustChangePin").GetBoolean());
        // The shortcut is gone once the PIN is the user's own: now the current PIN is needed.
        await AssertProblem(await them.SendAsync(Json(HttpMethod.Post, "/api/v1/account/change-pin", new { newPin = "7777", confirmPin = "7777" })), HttpStatusCode.BadRequest, "validation_failed");
    }

    [Fact]
    public async Task A_wrong_current_pin_is_written_to_the_audit_trail_as_a_warning()
    {
        var admin = await Admin();
        var (id, username) = await CreateUserAsync(admin, await CashierRoleIdAsync(admin), pin: "1212");
        var them = await SignedInAs(username, "1212");
        await them.SendAsync(Json(HttpMethod.Post, "/api/v1/account/change-pin", new { newPin = "3434", confirmPin = "3434" }));

        await them.SendAsync(Json(HttpMethod.Post, "/api/v1/account/change-pin", new { currentPin = "0000", newPin = "5656", confirmPin = "5656" }));

        using var scope = Factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IAuditTrail>().FlushAsync();
        var (failed, _) = await scope.ServiceProvider.GetRequiredService<IAuditLogService>()
            .SearchAsync(new AuditLogFilter { Action = AuditActions.PinChangeFailed, EntityId = id }, 0, 10);
        Assert.Single(failed);
        var (changed, _) = await scope.ServiceProvider.GetRequiredService<IAuditLogService>()
            .SearchAsync(new AuditLogFilter { Action = AuditActions.PinChanged, EntityId = id }, 0, 10);
        Assert.Equal(2, changed.Count()); // the admin's initial PIN and the user's own change; the PIN itself is never recorded
        Assert.DoesNotContain(changed, row => (row.AdditionalInfoJson ?? "").Contains("3434"));
    }
}
