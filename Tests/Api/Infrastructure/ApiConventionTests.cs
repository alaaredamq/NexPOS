using Xunit;
using System.Reflection;
using Application.Exceptions;
using Application.DTOs.Licensing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Net.Http.Headers;
using Web.Api.Infrastructure;
using Web.Attributes;
using Web.Helpers;
using Web.Middlewares;

namespace Tests.Api.Infrastructure;

/// <summary>Rules every module of the API must keep, checked by reflection over the whole Web assembly, plus the pure helpers.</summary>
public sealed class ApiConventionTests
{
    private static readonly Assembly WebAssembly = typeof(ApiControllerBase).Assembly;

    private static IEnumerable<Type> ApiControllers()
        => WebAssembly.GetTypes().Where(t => !t.IsAbstract && typeof(ApiControllerBase).IsAssignableFrom(t));

    private static IEnumerable<MethodInfo> Actions(Type controller)
        => controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any());

    [Fact]
    public void Every_api_controller_routes_under_api_v1_with_kebab_case()
    {
        foreach (var controller in ApiControllers())
        {
            var templates = controller.GetCustomAttributes<RouteAttribute>(inherit: false).Select(r => r.Template).ToList();

            Assert.True(templates.Count > 0, $"{controller.FullName} has no [Route(\"api/v1/...\")]");
            Assert.All(templates, t =>
            {
                Assert.StartsWith("api/v1/", t);
                Assert.Equal(t.ToLowerInvariant(), t);
                Assert.DoesNotContain("[controller]", t);
            });
        }
    }

    [Fact]
    public void Every_api_action_says_who_may_call_it()
    {
        // A forgotten permission attribute is a hole, not a default: pick [RequirePermission...], [RequireRole...],
        // [AllowAnonymous] or [AnySignedInUser] on the action or the class.
        static bool Declares(ICustomAttributeProvider member) => member.GetCustomAttributes(inherit: true).Any(a =>
            a is AllowAnonymousAttribute or AnySignedInUserAttribute
              or RequirePermissionAttribute or RequireAnyPermissionAttribute or RequireAllPermissionsAttribute
              or RequireRoleAttribute or RequireAnyRoleAttribute or RequireAllRolesAttribute);

        var offenders = ApiControllers()
            .SelectMany(c => Actions(c).Where(a => !Declares(a) && !Declares(c)).Select(a => $"{c.Name}.{a.Name}"))
            .ToList();

        Assert.True(offenders.Count == 0, "Actions with no access declaration: " + string.Join(", ", offenders));
    }

    [Fact]
    public void Api_controllers_never_touch_the_database_context()
    {
        // Web/Requirements/rules.md: controller -> service -> repository.
        var offenders = WebAssembly.GetTypes()
            .Where(t => t.Namespace?.StartsWith("Web.Api", StringComparison.Ordinal) == true)
            .Where(t => t.GetConstructors().SelectMany(c => c.GetParameters())
                .Any(p => p.ParameterType == typeof(global::Infrastructure.Data.ErpDbContext)))
            .Select(t => t.FullName)
            .ToList();

        Assert.True(offenders.Count == 0, "Take a service, not the DbContext: " + string.Join(", ", offenders));
    }

    [Fact]
    public void Api_controllers_are_named_Api_dot_name_for_the_audit_trail()
    {
        var model = new ControllerModel(typeof(FakeApiController).GetTypeInfo(), Array.Empty<object>()) { ControllerName = "Fake" };
        var legacy = new ControllerModel(typeof(Web.Controllers.AuthController).GetTypeInfo(), Array.Empty<object>()) { ControllerName = "Auth" };
        var convention = new ApiControllerNameConvention();

        convention.Apply(model);
        convention.Apply(model); // idempotent
        convention.Apply(legacy);

        Assert.Equal("Api.Fake", model.ControllerName);
        Assert.Equal("Auth", legacy.ControllerName);
    }

    // ---- helpers ---------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("Pin", "pin")]
    [InlineData("Items[0].UnitPrice", "items[0].unitPrice")]
    [InlineData("$.pin", "pin")]
    [InlineData("$", "")]
    [InlineData("", "")]
    [InlineData("already", "already")]
    public void Field_names_become_camelCase(string key, string expected) => Assert.Equal(expected, ApiModelState.CamelCase(key));

    [Theory]
    [InlineData("ar-LB,ar;q=0.9,en;q=0.8", "ar")]
    [InlineData("en-US,en;q=0.9,ar;q=0.8", "en")]
    [InlineData("fr,en;q=0.5", "en")]
    [InlineData("fr-FR,de;q=0.8", "")]
    [InlineData("ar;q=0,en", "en")]
    [InlineData("en;q=0.4,ar;q=0.6", "ar")]
    [InlineData("", "")]
    public void Accept_Language_resolves_to_a_supported_language_or_nothing(string header, string expected)
    {
        var context = new DefaultHttpContext();
        if (header.Length > 0) context.Request.Headers[HeaderNames.AcceptLanguage] = header;

        Assert.Equal(expected, ApiLanguage.FromAcceptLanguage(context.Request));
    }

    [Theory]
    [InlineData("/api/v1/session", true, true)]
    [InlineData("/api/v1", true, true)]
    [InlineData("/api/carts", true, false)]
    [InlineData("/api/v10/x", true, false)]
    [InlineData("/en/Orders", false, false)]
    [InlineData("/apiary", false, false)]
    public void Only_the_versioned_api_is_treated_as_the_new_api(string path, bool isApi, bool isV1)
    {
        var request = new DefaultHttpContext { Request = { Path = path } }.Request;

        Assert.Equal(isApi, ApiPaths.IsApi(request));
        Assert.Equal(isV1, ApiPaths.IsV1(request));
    }

    [Fact]
    public void Unknown_codes_fall_back_to_the_message_of_their_status()
    {
        Assert.Equal("Api_Conflict", ApiCodes.MessageKey("drawer_not_open", 409));
        Assert.Equal("Api_NotFound", ApiCodes.MessageKey("product_gone", 404));
        Assert.Equal("Api_BadRequest", ApiCodes.MessageKey("whatever", 400));
        Assert.Equal("Api_Forbidden", ApiCodes.MessageKey(ApiCodes.Forbidden, 403));
    }

    [Fact]
    public void Exceptions_map_to_status_and_code_and_hide_detail_outside_development()
    {
        var licence = ApiExceptionMapper.Map(new LicenseRequiredException(LicenseStatus.Expired), includeDetail: false);
        var crash = ApiExceptionMapper.Map(new InvalidOperationException("connection string is X"), includeDetail: false);
        var devCrash = ApiExceptionMapper.Map(new InvalidOperationException("boom"), includeDetail: true);
        var tooBig = ApiExceptionMapper.Map(new BadHttpRequestException("too big", StatusCodes.Status413PayloadTooLarge), includeDetail: false);

        Assert.Equal((403, ApiCodes.LicenseSalesStopped), (licence.Status, licence.Code));
        Assert.False(licence.Unexpected);
        Assert.Equal((500, ApiCodes.InternalError), (crash.Status, crash.Code));
        Assert.True(crash.Unexpected);
        Assert.Null(crash.Detail);
        Assert.Equal("boom", devCrash.Detail);
        Assert.Equal((413, ApiCodes.PayloadTooLarge), (tooBig.Status, tooBig.Code));
    }

    // ---- SPA migration (nav links) ---------------------------------------------------------------------------------

    [Theory]
    [InlineData("POS", "/app/pos")]
    [InlineData("Orders", "/app/orders")]
    [InlineData("CashDrawer", "/app/cash-drawer")]
    [InlineData("AuditLogs", "/app/audit-logs")]
    [InlineData("ProductDisplayOrder", "/app/product-display-order")]
    [InlineData("License", "/app/license")]
    public void Every_page_has_a_planned_kebab_case_spa_path(string controller, string expected)
        => Assert.Equal(expected, SpaMigration.PlannedPath(controller));

    [Fact]
    public void Planned_spa_paths_are_unique_across_the_nav_catalog()
    {
        var paths = NavCatalog.All.Select(p => SpaMigration.PlannedPath(p.Controller)).ToList();

        Assert.Equal(paths.Count, paths.Distinct().Count());
    }

    [Fact]
    public void A_page_that_is_not_migrated_links_to_its_razor_page_in_the_current_language()
    {
        // Whichever page is still waiting for its track: this test must not need editing every time a module flips its line.
        var waiting = NavCatalog.All.First(p => !SpaMigration.IsMigrated(p.Controller)).Controller;
        Assert.Equal($"/ar/{waiting}", SpaMigration.HrefFor(waiting, "ar"));
        Assert.Equal("/app/orders", SpaMigration.HrefFor("Orders", "ar"));
    }

    [Fact]
    public void Every_migrated_path_lives_under_app()
        => Assert.All(SpaMigration.Migrated.Values, p => Assert.StartsWith("/app/", p));

    [Fact]
    public void Every_nav_page_is_in_a_section_the_session_knows()
        => Assert.All(NavCatalog.All, page => Assert.Contains(page.Section, NavCatalog.Sections));

    // ---- generated permissions.ts ----------------------------------------------------------------------------------

    [Fact]
    public void The_generated_permissions_module_lists_every_permission_key_once()
    {
        var text = PermissionsTypeScript.Render();

        Assert.Contains("Users_View: 'Users.View',", text);
        Assert.Contains("export type Permission =", text);
        foreach (var key in PermissionHelper.Permissions.All) Assert.Contains($"'{key}'", text);
        Assert.Equal(text, PermissionsTypeScript.Render()); // deterministic: the file is committed
    }
}
