using System.Net;
using System.Text.Json;
using Core.Auditing;
using Core.Entities.Logging;
using Microsoft.Extensions.DependencyInjection;
using Tests.Api.Catalog;
using Tests.Api.Infrastructure;
using Xunit;

namespace Tests.Api.AuditLogs;

/// <summary>
/// Audit log API (track I3): who may read it, newest-first paging, every filter, the details payload, the CSV export (and that the
/// export is itself audited), and that nothing can be written through it. Rows are seeded straight into the trail under a unique
/// action name so the tests do not depend on what other tests, or the sign-ins, wrote.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class AuditLogsApiTests : CatalogApiTestBase
{
    public AuditLogsApiTests(ApiFactory factory) : base(factory)
    {
    }

    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..16];

    private static readonly DateTime Base = new(2026, 3, 10, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>Writes entries through the real trail and waits until they are in the audit database.</summary>
    private async Task SeedAsync(params AuditLog[] entries)
    {
        using var scope = Factory.Services.CreateScope();
        var trail = scope.ServiceProvider.GetRequiredService<IAuditTrail>();
        foreach (var entry in entries) trail.Record(entry);
        await trail.FlushAsync();
    }

    private static AuditLog Entry(string action, int minute = 0, Action<AuditLog>? tweak = null)
    {
        var entry = new AuditLog
        {
            // Set explicitly so the rows are not replaced by the ambient request context (user, id, address).
            UserId = 9001,
            Username = "seed-user",
            UserRole = "Tester",
            Category = AuditCategory.Business,
            Action = action,
            EntityType = "SeedThing",
            Description = $"Seeded {action}",
            IpAddress = "10.1.1.1",
            UserAgent = "xunit",
            CorrelationId = $"corr-{action}"
        };
        entry.CreatedAt = Base.AddMinutes(minute);
        tweak?.Invoke(entry);
        return entry;
    }

    private static string Url(string action, string extra = "") => $"/api/v1/audit-logs?action={action}{extra}";

    private static async Task<JsonElement> ListAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await Body(response);
    }

    private static int[] Minutes(JsonElement list)
        => list.GetProperty("items").EnumerateArray().Select(i => (int)(DateTime.Parse(i.GetProperty("time").GetString()!).ToUniversalTime() - Base).TotalMinutes).ToArray();

    // ---- permissions -------------------------------------------------------------------------------------------

    [Fact]
    public async Task Without_a_cookie_the_list_answers_401()
    {
        var response = await Factory.CreateBrowser().GetAsync("/api/v1/audit-logs");

        await AssertProblem(response, HttpStatusCode.Unauthorized, "unauthenticated");
    }

    [Fact]
    public async Task A_cashier_cannot_read_any_part_of_the_log()
    {
        var cashier = await Cashier();

        foreach (var url in new[] { "/api/v1/audit-logs", "/api/v1/audit-logs/summary", "/api/v1/audit-logs/options", "/api/v1/audit-logs/1", "/api/v1/audit-logs/export" })
            await AssertProblem(await cashier.GetAsync(url), HttpStatusCode.Forbidden, "forbidden");
    }

    [Fact]
    public async Task The_log_is_read_only_over_the_api()
    {
        var admin = await Admin();

        foreach (var method in new[] { HttpMethod.Post, HttpMethod.Put, HttpMethod.Delete, HttpMethod.Patch })
        {
            var response = await admin.SendAsync(Json(method, "/api/v1/audit-logs/1", new { description = "rewritten" }));
            Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        }

        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await admin.SendAsync(Json(HttpMethod.Post, "/api/v1/audit-logs", new { action = "Forged" }))).StatusCode);
    }

    // ---- list --------------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_list_is_newest_first_and_dir_asc_reverses_it()
    {
        var action = Unique("Order");
        await SeedAsync(Entry(action, 0), Entry(action, 5), Entry(action, 2));
        var admin = await Admin();

        var newest = await ListAsync(admin, Url(action));
        var oldest = await ListAsync(admin, Url(action, "&dir=asc&sort=time"));

        Assert.Equal(3, newest.GetProperty("total").GetInt32());
        Assert.Equal(new[] { 5, 2, 0 }, Minutes(newest));
        Assert.Equal(new[] { 0, 2, 5 }, Minutes(oldest));
    }

    [Fact]
    public async Task The_list_pages_and_reports_the_total()
    {
        var action = Unique("Paged");
        await SeedAsync(Enumerable.Range(0, 7).Select(i => Entry(action, i)).ToArray());
        var admin = await Admin();

        var second = await ListAsync(admin, Url(action, "&page=2&pageSize=3"));

        Assert.Equal(7, second.GetProperty("total").GetInt32());
        Assert.Equal(2, second.GetProperty("page").GetInt32());
        Assert.Equal(new[] { 3, 2, 1 }, Minutes(second));
        Assert.Equal(1, (await ListAsync(admin, Url(action, "&page=3&pageSize=3"))).GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task A_row_carries_who_what_where_and_the_time_in_utc()
    {
        var action = Unique("Shape");
        await SeedAsync(Entry(action, 0, e =>
        {
            e.EntityId = 42;
            e.EntityLabel = "Milk 1L";
            e.Outcome = AuditOutcome.Failure;
            e.Severity = AuditLogSeverity.Error;
            e.StatusCode = 500;
            e.DurationMs = 120;
        }));
        var admin = await Admin();

        var item = (await ListAsync(admin, Url(action))).GetProperty("items")[0];

        Assert.Equal("2026-03-10T12:00:00Z", item.GetProperty("time").GetString());
        Assert.Equal("seed-user", item.GetProperty("user").GetString());
        Assert.Equal("Tester", item.GetProperty("role").GetString());
        Assert.Equal(9001, item.GetProperty("userId").GetInt32());
        Assert.Equal("Business", item.GetProperty("category").GetString());
        Assert.Equal("Other", item.GetProperty("area").GetString());
        Assert.Equal("SeedThing", item.GetProperty("entityType").GetString());
        Assert.Equal(42, item.GetProperty("entityId").GetInt32());
        Assert.Equal("Milk 1L", item.GetProperty("label").GetString());
        Assert.Equal("Failure", item.GetProperty("outcome").GetString());
        Assert.Equal("Error", item.GetProperty("severity").GetString());
        Assert.Equal(500, item.GetProperty("statusCode").GetInt32());
        Assert.Equal(120, item.GetProperty("durationMs").GetInt32());
    }

    [Fact]
    public async Task Every_filter_narrows_the_list()
    {
        var action = Unique("Filter");
        await SeedAsync(
            Entry(action, 0, e => { e.Category = AuditCategory.Security; e.Outcome = AuditOutcome.Denied; e.Severity = AuditLogSeverity.Warning; e.UserId = 7001; e.Username = "ann"; }),
            Entry(action, 10, e => { e.Category = AuditCategory.Data; e.EntityType = "Product"; e.EntityId = 55; e.Severity = AuditLogSeverity.Info; }),
            Entry(action, 20, e => { e.Category = AuditCategory.Diagnostic; e.Outcome = AuditOutcome.Failure; e.Severity = AuditLogSeverity.Critical; e.UserId = null; e.Username = "System"; }));
        var admin = await Admin();

        async Task<int> Count(string extra) => (await ListAsync(admin, Url(action, extra))).GetProperty("total").GetInt32();

        Assert.Equal(3, await Count(""));
        Assert.Equal(1, await Count("&category=security"));
        Assert.Equal(1, await Count("&outcome=Denied"));
        Assert.Equal(2, await Count("&minSeverity=Warning"));
        Assert.Equal(1, await Count("&minSeverity=Critical"));
        Assert.Equal(1, await Count("&userId=7001"));
        Assert.Equal(1, await Count("&username=System"));
        Assert.Equal(1, await Count("&entityType=Product&entityId=55"));
        Assert.Equal(1, await Count("&correlationId=corr-" + action + "&entityId=55"));
        Assert.Equal(2, await Count($"&from={Uri.EscapeDataString(Base.AddMinutes(10).ToString("O"))}"));
        Assert.Equal(1, await Count($"&from={Uri.EscapeDataString(Base.AddMinutes(5).ToString("O"))}&to={Uri.EscapeDataString(Base.AddMinutes(15).ToString("O"))}"));
        Assert.Equal(0, await Count("&category=Security&outcome=Failure"));
    }

    [Fact]
    public async Task Free_text_matches_the_description_the_label_and_the_user()
    {
        var action = Unique("Text");
        var token = Unique("zz");
        await SeedAsync(
            Entry(action, 0, e => e.Description = $"Opened the {token} drawer"),
            Entry(action, 1, e => e.EntityLabel = $"Shelf {token}"),
            Entry(action, 2));
        var admin = await Admin();

        var list = await ListAsync(admin, Url(action, $"&q={token}"));

        Assert.Equal(2, list.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task An_unknown_category_or_sort_is_a_400_not_an_unfiltered_list()
    {
        var admin = await Admin();

        var category = await admin.GetAsync("/api/v1/audit-logs?category=Banana");
        await AssertProblem(category, HttpStatusCode.BadRequest, "validation_failed");
        Assert.True((await Body(category)).GetProperty("errors").TryGetProperty("category", out _));

        var area = await admin.GetAsync("/api/v1/audit-logs?area=Kitchen");
        await AssertProblem(area, HttpStatusCode.BadRequest, "validation_failed");
        Assert.True((await Body(area)).GetProperty("errors").TryGetProperty("area", out _));

        await AssertProblem(await admin.GetAsync("/api/v1/audit-logs?outcome=Maybe"), HttpStatusCode.BadRequest, "validation_failed");
        await AssertProblem(await admin.GetAsync("/api/v1/audit-logs?minSeverity=Loud"), HttpStatusCode.BadRequest, "validation_failed");
        await AssertProblem(await admin.GetAsync("/api/v1/audit-logs?sort=description"), HttpStatusCode.BadRequest, "validation_failed");
        await AssertProblem(await admin.GetAsync("/api/v1/audit-logs?from=yesterday"), HttpStatusCode.BadRequest, "validation_failed");
    }

    // ---- summary and options -----------------------------------------------------------------------------------

    [Fact]
    public async Task The_summary_counts_the_log()
    {
        var admin = await Admin();

        var summary = await Body(await admin.GetAsync("/api/v1/audit-logs/summary"));

        Assert.True(summary.GetProperty("total").GetInt32() > 0);
        Assert.True(summary.GetProperty("last24Hours").GetInt32() >= 0);
        Assert.True(summary.GetProperty("failures24Hours").GetInt32() >= 0);
        Assert.True(summary.GetProperty("security24Hours").GetInt32() >= 0);
        Assert.EndsWith("Z", summary.GetProperty("oldestUtc").GetString());
    }

    [Fact]
    public async Task The_options_list_the_users_record_types_and_actions_in_the_log()
    {
        var action = Unique("Option");
        var type = Unique("Kind");
        await SeedAsync(Entry(action, 0, e => { e.EntityType = type; e.UserId = null; e.Username = "ghost-" + action; }));
        var admin = await Admin();

        // The filter options are cached for a minute; the factory's host is shared, so ask until the new values show up or give up.
        JsonElement options = default;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            options = await Body(await admin.GetAsync("/api/v1/audit-logs/options"));
            if (options.GetProperty("actions").EnumerateArray().Any(a => a.GetString() == action)) break;
            await Task.Delay(50);
        }

        Assert.NotEqual(JsonValueKind.Undefined, options.ValueKind);
        Assert.True(options.GetProperty("users").GetArrayLength() > 0);
        Assert.True(options.GetProperty("entityTypes").GetArrayLength() > 0);
        Assert.True(options.GetProperty("actions").GetArrayLength() > 0);
    }

    // ---- details -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Details_has_the_changes_the_extra_json_and_the_request_group()
    {
        var action = Unique("Detail");
        await SeedAsync(
            Entry(action, 0, e =>
            {
                e.Category = AuditCategory.Data;
                e.EntityId = 3;
                e.ChangedFields = "Name,Price";
                e.OldValuesJson = "{\"Name\":\"Milk\",\"Price\":1.5}";
                e.NewValuesJson = "{\"Name\":\"Milk 1L\",\"Price\":1.75}";
                e.AdditionalInfoJson = "{\"reason\":\"repricing\"}";
                e.MachineName = "TILL-07";
                e.AppVersion = "9.9.9";
                e.Source = "Web";
                e.HttpMethod = "PUT";
                e.RequestPath = "/api/v1/products/3";
                e.StatusCode = 200;
            }),
            Entry(action, 0, e => e.Category = AuditCategory.Request));
        var admin = await Admin();
        var id = (await ListAsync(admin, Url(action, "&category=Data"))).GetProperty("items")[0].GetProperty("id").GetInt32();

        var details = await Body(await admin.GetAsync($"/api/v1/audit-logs/{id}"));

        Assert.Equal(id, details.GetProperty("entry").GetProperty("id").GetInt32());
        var changes = details.GetProperty("changes").EnumerateArray().ToList();
        Assert.Equal(new[] { "Name", "Price" }, changes.Select(c => c.GetProperty("field").GetString()));
        Assert.Equal("Milk", changes[0].GetProperty("oldValue").GetString());
        Assert.Equal("Milk 1L", changes[0].GetProperty("newValue").GetString());
        Assert.Equal("1.5", changes[1].GetProperty("oldValue").GetString());
        Assert.Equal("1.75", changes[1].GetProperty("newValue").GetString());
        Assert.Contains("\"reason\": \"repricing\"", details.GetProperty("additional").GetString());
        Assert.Equal("10.1.1.1", details.GetProperty("ipAddress").GetString());
        Assert.Equal("TILL-07", details.GetProperty("machine").GetString());
        Assert.Equal("9.9.9", details.GetProperty("appVersion").GetString());
        Assert.Equal("PUT", details.GetProperty("httpMethod").GetString());
        Assert.Equal("/api/v1/products/3", details.GetProperty("path").GetString());
        Assert.Equal($"corr-{action}", details.GetProperty("correlationId").GetString());
        Assert.Equal(2, details.GetProperty("rowsInSameRequest").GetInt32());
    }

    [Fact]
    public async Task Details_of_an_entry_without_changes_has_an_empty_list_and_no_extra()
    {
        var action = Unique("Plain");
        await SeedAsync(Entry(action, 0, e => e.CorrelationId = null));
        var admin = await Admin();
        var id = (await ListAsync(admin, Url(action))).GetProperty("items")[0].GetProperty("id").GetInt32();

        var details = await Body(await admin.GetAsync($"/api/v1/audit-logs/{id}"));

        Assert.Equal(0, details.GetProperty("changes").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, details.GetProperty("additional").ValueKind);
        Assert.Equal(1, details.GetProperty("rowsInSameRequest").GetInt32());
    }

    [Fact]
    public async Task An_unknown_entry_is_a_404()
    {
        var admin = await Admin();

        await AssertProblem(await admin.GetAsync("/api/v1/audit-logs/99999999"), HttpStatusCode.NotFound, "not_found");
    }

    // ---- export ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_export_is_a_csv_of_the_filtered_rows_and_is_itself_audited()
    {
        var action = Unique("Export");
        await SeedAsync(Entry(action, 0), Entry(action, 1, e => e.Description = "Needs, \"quoting\""));
        var admin = await Admin();

        var response = await admin.GetAsync($"/api/v1/audit-logs/export?action={action}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        Assert.StartsWith("attachment", response.Content.Headers.ContentDisposition?.ToString());
        Assert.Contains(".csv", response.Content.Headers.ContentDisposition?.ToString());
        var csv = await response.Content.ReadAsStringAsync();
        Assert.Equal(2, csv.Split('\n').Count(line => line.Contains(action)));
        Assert.Contains("\"Needs, \"\"quoting\"\"\"", csv);

        using var scope = Factory.Services.CreateScope();
        var trail = scope.ServiceProvider.GetRequiredService<IAuditTrail>();
        await trail.FlushAsync();
        var (rows, _) = await scope.ServiceProvider.GetRequiredService<Application.IServices.Logging.IAuditLogService>()
            .SearchAsync(new AuditLogFilter { Action = AuditActions.AuditExported, Search = "(2 rows)" }, 0, 5);
        Assert.NotEmpty(rows);
    }

    [Fact]
    public async Task The_export_with_a_bad_filter_is_a_400_and_writes_no_file()
    {
        var admin = await Admin();

        var response = await admin.GetAsync("/api/v1/audit-logs/export?outcome=Maybe");

        await AssertProblem(response, HttpStatusCode.BadRequest, "validation_failed");
    }
}
