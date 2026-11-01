using System.Net;
using System.Text.Json;
using Tests.Api.Catalog;
using Tests.Api.Infrastructure;
using Web.Helpers;
using Xunit;

namespace Tests.Api.Backups;

/// <summary>
/// Backup API (track A4): the permission matrix of the Razor <c>BackupController</c>, the snapshot / download / restore round trip on the real
/// test database, delete and bulk calls, the schedules, the audit entries the shared service writes, and the licence trap (the test host has no
/// licence, so selling is stopped - a shop in that state must still be able to take and download backups).
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class BackupsApiTests : CatalogApiTestBase
{
    public BackupsApiTests(ApiFactory factory) : base(factory)
    {
    }

    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..14];

    private static async Task<JsonElement> CreateBackupAsync(HttpClient client, string? name = null)
    {
        var response = await client.SendAsync(Json(HttpMethod.Post, "/api/v1/backups", new { name = name ?? Unique("bk"), description = "api test" }));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await Body(response);
    }

    private static async Task<JsonElement> ListAsync(HttpClient client, string query = "")
    {
        var response = await client.GetAsync("/api/v1/backups?pageSize=200" + query);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await Body(response);
    }

    private static async Task<List<string>> NamesAsync(HttpClient client)
        => (await ListAsync(client)).GetProperty("items").EnumerateArray().Select(b => b.GetProperty("name").GetString()!).ToList();

    private static async Task<string> BackupFolderAsync(HttpClient client)
        => (await Body(await client.GetAsync("/api/v1/backups/summary"))).GetProperty("backupDirectory").GetString()!;

    private async Task<int> RoleWithAsync(HttpClient admin, params string[] permissions)
    {
        var response = await admin.SendAsync(Json(HttpMethod.Post, "/api/v1/roles", new { name = Unique("bkrole"), description = "test role", permissions }));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await Body(response)).GetProperty("role").GetProperty("id").GetInt32();
    }

    /// <summary>A signed-in client whose role holds exactly the given permissions.</summary>
    private async Task<HttpClient> UserWithAsync(params string[] permissions)
    {
        var admin = await Admin();
        var roleId = await RoleWithAsync(admin, permissions);
        var username = Unique("bku");
        var created = await admin.SendAsync(Json(HttpMethod.Post, "/api/v1/users", new
        {
            username,
            email = $"{username}@example.com",
            firstName = "Test",
            lastName = "User",
            roleId,
            isActive = true,
            pin = "4321",
        }));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return await SignedIn(username, "4321");
    }

    private static async Task<int> AuditCountAsync(HttpClient admin, string action)
        => (await Body(await admin.GetAsync($"/api/v1/audit-logs?action={action}&pageSize=1"))).GetProperty("total").GetInt32();

    // ---- permissions -------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("/api/v1/backups")]
    [InlineData("/api/v1/backups/summary")]
    [InlineData("/api/v1/backups/1")]
    [InlineData("/api/v1/backups/1/download")]
    [InlineData("/api/v1/backup-schedules")]
    public async Task Without_a_cookie_everything_answers_401(string url)
    {
        await AssertProblem(await Factory.CreateBrowser().GetAsync(url), HttpStatusCode.Unauthorized, "unauthenticated");
    }

    [Fact]
    public async Task A_cashier_cannot_see_or_change_backups()
    {
        var cashier = await Cashier();

        foreach (var url in new[] { "/api/v1/backups", "/api/v1/backups/summary", "/api/v1/backups/1", "/api/v1/backups/1/download", "/api/v1/backup-schedules" })
            await AssertProblem(await cashier.GetAsync(url), HttpStatusCode.Forbidden, "forbidden");

        var writes = new (HttpMethod Method, string Url, object? Body)[]
        {
            (HttpMethod.Post, "/api/v1/backups", new { name = "x" }),
            (HttpMethod.Post, "/api/v1/backups/1/restore", new { }),
            (HttpMethod.Post, "/api/v1/backups/1/validate", null),
            (HttpMethod.Delete, "/api/v1/backups/1", null),
            (HttpMethod.Post, "/api/v1/backups/bulk-delete", new { ids = new[] { 1 } }),
            (HttpMethod.Post, "/api/v1/backups/bulk-validate", new { ids = new[] { 1 } }),
            (HttpMethod.Post, "/api/v1/backups/cleanup", new { daysToKeep = 30 }),
            (HttpMethod.Post, "/api/v1/backups/test-connection", null),
            (HttpMethod.Post, "/api/v1/backup-schedules", new { name = "x", frequency = "Daily", time = "02:00" }),
            (HttpMethod.Put, "/api/v1/backup-schedules/1", new { name = "x", frequency = "Daily", time = "02:00" }),
            (HttpMethod.Put, "/api/v1/backup-schedules/1/active", new { isActive = false }),
            (HttpMethod.Delete, "/api/v1/backup-schedules/1", null),
            (HttpMethod.Post, "/api/v1/backup-schedules/1/run", null),
        };
        foreach (var (method, url, body) in writes)
            await AssertProblem(await cashier.SendAsync(Json(method, url, body)), HttpStatusCode.Forbidden, "forbidden");
    }

    [Fact]
    public async Task Each_action_needs_the_permission_the_razor_controller_named()
    {
        var viewer = await UserWithAsync(PermissionHelper.Permissions.Backup_View);
        var creator = await UserWithAsync(PermissionHelper.Permissions.Backup_View, PermissionHelper.Permissions.Backup_Create);
        var restorer = await UserWithAsync(PermissionHelper.Permissions.Backup_View, PermissionHelper.Permissions.Backup_Restore);
        var manager = await UserWithAsync(PermissionHelper.Permissions.Backup_View, PermissionHelper.Permissions.Backup_Manage);
        var admin = await Admin();
        var backup = await CreateBackupAsync(admin);
        var id = backup.GetProperty("id").GetInt32();

        // View: read everything, change nothing.
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync("/api/v1/backups")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync($"/api/v1/backups/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync($"/api/v1/backups/{id}/download")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync("/api/v1/backup-schedules")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await viewer.SendAsync(Json(HttpMethod.Post, "/api/v1/backups/test-connection"))).StatusCode);
        await AssertProblem(await viewer.SendAsync(Json(HttpMethod.Post, "/api/v1/backups", new { name = "nope" })), HttpStatusCode.Forbidden, "forbidden");
        await AssertProblem(await viewer.SendAsync(Json(HttpMethod.Post, $"/api/v1/backups/{id}/restore", new { })), HttpStatusCode.Forbidden, "forbidden");
        await AssertProblem(await viewer.SendAsync(Json(HttpMethod.Post, $"/api/v1/backups/{id}/validate")), HttpStatusCode.Forbidden, "forbidden");
        await AssertProblem(await viewer.SendAsync(Json(HttpMethod.Delete, $"/api/v1/backups/{id}")), HttpStatusCode.Forbidden, "forbidden");
        await AssertProblem(await viewer.SendAsync(Json(HttpMethod.Post, "/api/v1/backup-schedules", new { name = "x", frequency = "Daily", time = "02:00" })), HttpStatusCode.Forbidden, "forbidden");

        // Create takes backups (and runs a schedule now) but cannot restore or delete.
        Assert.Equal(HttpStatusCode.Created, (await creator.SendAsync(Json(HttpMethod.Post, "/api/v1/backups", new { name = Unique("mine") }))).StatusCode);
        await AssertProblem(await creator.SendAsync(Json(HttpMethod.Post, $"/api/v1/backups/{id}/restore", new { })), HttpStatusCode.Forbidden, "forbidden");
        await AssertProblem(await creator.SendAsync(Json(HttpMethod.Delete, $"/api/v1/backups/{id}")), HttpStatusCode.Forbidden, "forbidden");

        // Restore alone cannot take or delete one.
        await AssertProblem(await restorer.SendAsync(Json(HttpMethod.Post, "/api/v1/backups", new { name = "nope" })), HttpStatusCode.Forbidden, "forbidden");
        await AssertProblem(await restorer.SendAsync(Json(HttpMethod.Delete, $"/api/v1/backups/{id}")), HttpStatusCode.Forbidden, "forbidden");

        // Manage validates and deletes but cannot take or restore.
        Assert.Equal(HttpStatusCode.OK, (await manager.SendAsync(Json(HttpMethod.Post, $"/api/v1/backups/{id}/validate"))).StatusCode);
        await AssertProblem(await manager.SendAsync(Json(HttpMethod.Post, "/api/v1/backups", new { name = "nope" })), HttpStatusCode.Forbidden, "forbidden");
        await AssertProblem(await manager.SendAsync(Json(HttpMethod.Post, $"/api/v1/backups/{id}/restore", new { })), HttpStatusCode.Forbidden, "forbidden");
        Assert.Equal(HttpStatusCode.NoContent, (await manager.SendAsync(Json(HttpMethod.Delete, $"/api/v1/backups/{id}"))).StatusCode);
    }

    [Fact]
    public async Task Without_the_view_permission_the_other_backup_permissions_do_not_help()
    {
        // The Razor controller's class-level Backup.View applies to every action.
        var onlyCreate = await UserWithAsync(PermissionHelper.Permissions.Backup_Create);

        await AssertProblem(await onlyCreate.SendAsync(Json(HttpMethod.Post, "/api/v1/backups", new { name = "nope" })), HttpStatusCode.Forbidden, "forbidden");
        await AssertProblem(await onlyCreate.GetAsync("/api/v1/backups"), HttpStatusCode.Forbidden, "forbidden");
    }

    // ---- create / list / download ------------------------------------------------------------------------------

    [Fact]
    public async Task A_backup_is_a_real_sqlite_snapshot_that_can_be_listed_and_downloaded()
    {
        var admin = await Admin();
        var name = Unique("snap");

        var created = await CreateBackupAsync(admin, name);

        Assert.Equal(name, created.GetProperty("name").GetString());
        Assert.Equal("Completed", created.GetProperty("status").GetString());
        Assert.True(created.GetProperty("fileSizeBytes").GetInt64() > 0);
        Assert.EndsWith(".bak", created.GetProperty("fileName").GetString());
        Assert.False(created.TryGetProperty("filePath", out _), "the server's folder layout is not part of the contract");
        Assert.True(created.GetProperty("createdAt").GetDateTime() <= DateTime.UtcNow.AddMinutes(1));

        var id = created.GetProperty("id").GetInt32();
        Assert.Contains(name, await NamesAsync(admin));
        Assert.Equal(name, (await Body(await admin.GetAsync($"/api/v1/backups/{id}"))).GetProperty("name").GetString());

        var download = await admin.GetAsync($"/api/v1/backups/{id}/download");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("application/octet-stream", download.Content.Headers.ContentType?.MediaType);
        Assert.Equal(created.GetProperty("fileName").GetString(), download.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        var bytes = await download.Content.ReadAsByteArrayAsync();
        Assert.Equal(created.GetProperty("fileSizeBytes").GetInt64(), bytes.Length);
        Assert.Equal("SQLite format 3", System.Text.Encoding.ASCII.GetString(bytes, 0, 15));
    }

    [Fact]
    public async Task Two_backups_in_the_same_second_get_two_files()
    {
        // The file name carries the time to the second; the second snapshot used to overwrite the first one's file.
        var admin = await Admin();
        var created = new List<JsonElement>();
        for (var i = 0; i < 4; i++) created.Add(await CreateBackupAsync(admin));

        var files = created.Select(c => c.GetProperty("fileName").GetString()!).ToList();

        Assert.Equal(files.Count, files.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        var folder = await BackupFolderAsync(admin);
        Assert.All(files, f => Assert.True(File.Exists(Path.Combine(folder, f)), f));
    }

    [Fact]
    public async Task A_blank_name_gets_a_generated_one_and_every_backup_is_full_and_uncompressed()
    {
        var admin = await Admin();

        var response = await admin.SendAsync(Json(HttpMethod.Post, "/api/v1/backups", new { }));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.StartsWith("Backup_", (await Body(response)).GetProperty("name").GetString());
    }

    [Fact]
    public async Task The_summary_counts_backups_and_says_where_they_live()
    {
        var admin = await Admin();
        var before = await Body(await admin.GetAsync("/api/v1/backups/summary"));
        await CreateBackupAsync(admin);

        var after = await Body(await admin.GetAsync("/api/v1/backups/summary"));

        Assert.Equal(before.GetProperty("totalBackups").GetInt32() + 1, after.GetProperty("totalBackups").GetInt32());
        Assert.Equal(before.GetProperty("successfulBackups").GetInt32() + 1, after.GetProperty("successfulBackups").GetInt32());
        Assert.True(after.GetProperty("totalSizeBytes").GetInt64() > before.GetProperty("totalSizeBytes").GetInt64());
        Assert.Equal("Completed", after.GetProperty("lastBackupStatus").GetString());
        Assert.NotNull(after.GetProperty("lastBackupAt").GetString());
        Assert.True(Directory.Exists(after.GetProperty("backupDirectory").GetString()));
        Assert.True(after.GetProperty("availableDiskBytes").GetInt64() > 0);
        Assert.True(after.GetProperty("databaseSizeBytes").GetInt64() > 0);
    }

    [Fact]
    public async Task The_list_pages_sorts_searches_and_filters_by_status()
    {
        var admin = await Admin();
        var tag = Unique("lst");
        foreach (var suffix in new[] { "b", "a", "c" }) await CreateBackupAsync(admin, $"{tag}-{suffix}");

        var searched = await ListAsync(admin, $"&q={tag}&sort=name&dir=asc");
        Assert.Equal(new[] { $"{tag}-a", $"{tag}-b", $"{tag}-c" }, searched.GetProperty("items").EnumerateArray().Select(b => b.GetProperty("name").GetString()));
        Assert.Equal(3, searched.GetProperty("total").GetInt32());

        var newest = await ListAsync(admin, $"&q={tag}");
        Assert.Equal($"{tag}-c", newest.GetProperty("items")[0].GetProperty("name").GetString()); // newest first by default

        var paged = await Body(await admin.GetAsync($"/api/v1/backups?q={tag}&sort=name&dir=asc&page=2&pageSize=2"));
        Assert.Equal(3, paged.GetProperty("total").GetInt32());
        Assert.Equal(new[] { $"{tag}-c" }, paged.GetProperty("items").EnumerateArray().Select(b => b.GetProperty("name").GetString()));

        Assert.Equal(0, (await ListAsync(admin, $"&q={tag}&status=Failed")).GetProperty("total").GetInt32());
        Assert.Equal(3, (await ListAsync(admin, $"&q={tag}&status=completed")).GetProperty("total").GetInt32());
        await AssertProblem(await admin.GetAsync("/api/v1/backups?status=Maybe"), HttpStatusCode.BadRequest, "validation_failed");
    }

    [Fact]
    public async Task An_unknown_backup_is_a_404_everywhere()
    {
        var admin = await Admin();

        await AssertProblem(await admin.GetAsync("/api/v1/backups/999999"), HttpStatusCode.NotFound, "not_found");
        await AssertProblem(await admin.SendAsync(Json(HttpMethod.Post, "/api/v1/backups/999999/validate")), HttpStatusCode.NotFound, "not_found");
        await AssertProblem(await admin.SendAsync(Json(HttpMethod.Post, "/api/v1/backups/999999/restore", new { })), HttpStatusCode.NotFound, "not_found");
        await AssertProblem(await admin.SendAsync(Json(HttpMethod.Delete, "/api/v1/backups/999999")), HttpStatusCode.NotFound, "not_found");
        await AssertProblem(await admin.GetAsync("/api/v1/backups/999999/download"), HttpStatusCode.NotFound, "backup_file_missing");
    }

    // ---- validate / delete / bulk ------------------------------------------------------------------------------

    [Fact]
    public async Task Validate_reports_the_checksum_and_a_missing_file()
    {
        var admin = await Admin();
        var created = await CreateBackupAsync(admin);
        var id = created.GetProperty("id").GetInt32();

        var good = await Body(await admin.SendAsync(Json(HttpMethod.Post, $"/api/v1/backups/{id}/validate")));
        Assert.True(good.GetProperty("isValid").GetBoolean());
        Assert.False(string.IsNullOrEmpty(good.GetProperty("checksum").GetString()));
        Assert.Equal(created.GetProperty("fileSizeBytes").GetInt64(), good.GetProperty("fileSizeBytes").GetInt64());

        File.Delete(Path.Combine(await BackupFolderAsync(admin), created.GetProperty("fileName").GetString()!));

        var bad = await Body(await admin.SendAsync(Json(HttpMethod.Post, $"/api/v1/backups/{id}/validate")));
        Assert.False(bad.GetProperty("isValid").GetBoolean());
        Assert.NotEmpty(bad.GetProperty("errors").EnumerateArray());
        await AssertProblem(await admin.GetAsync($"/api/v1/backups/{id}/download"), HttpStatusCode.NotFound, "backup_file_missing");
    }

    [Fact]
    public async Task Delete_removes_the_file_and_the_record_and_is_audited()
    {
        var admin = await Admin();
        var created = await CreateBackupAsync(admin);
        var id = created.GetProperty("id").GetInt32();
        var file = Path.Combine(await BackupFolderAsync(admin), created.GetProperty("fileName").GetString()!);
        Assert.True(File.Exists(file));
        var audited = await AuditCountAsync(admin, "BackupDeleted");

        var response = await admin.SendAsync(Json(HttpMethod.Delete, $"/api/v1/backups/{id}"));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.False(File.Exists(file));
        Assert.False(File.Exists(file + ".meta.json"));
        await AssertProblem(await admin.GetAsync($"/api/v1/backups/{id}"), HttpStatusCode.NotFound, "not_found");
        Assert.Equal(audited + 1, await AuditCountAsync(admin, "BackupDeleted"));
    }

    [Fact]
    public async Task Bulk_delete_removes_the_known_ids_and_skips_the_rest()
    {
        var admin = await Admin();
        var ids = new List<int>();
        for (var i = 0; i < 3; i++) ids.Add((await CreateBackupAsync(admin)).GetProperty("id").GetInt32());

        var response = await admin.SendAsync(Json(HttpMethod.Post, "/api/v1/backups/bulk-delete", new { ids = ids.Append(999999).Append(ids[0]).ToArray() }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, (await Body(response)).GetProperty("deleted").GetInt32());
        foreach (var id in ids) Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/v1/backups/{id}")).StatusCode);

        await AssertProblem(await admin.SendAsync(Json(HttpMethod.Post, "/api/v1/backups/bulk-delete", new { ids = Array.Empty<int>() })), HttpStatusCode.BadRequest, "validation_failed");
    }

    [Fact]
    public async Task Bulk_validate_says_per_backup_and_overall()
    {
        var admin = await Admin();
        var good = await CreateBackupAsync(admin);
        var broken = await CreateBackupAsync(admin);
        File.Delete(Path.Combine(await BackupFolderAsync(admin), broken.GetProperty("fileName").GetString()!));
        var goodId = good.GetProperty("id").GetInt32();
        var brokenId = broken.GetProperty("id").GetInt32();

        var onlyGood = await Body(await admin.SendAsync(Json(HttpMethod.Post, "/api/v1/backups/bulk-validate", new { ids = new[] { goodId } })));
        Assert.True(onlyGood.GetProperty("allValid").GetBoolean(), onlyGood.ToString());

        var both = await Body(await admin.SendAsync(Json(HttpMethod.Post, "/api/v1/backups/bulk-validate", new { ids = new[] { goodId, brokenId } })));
        Assert.False(both.GetProperty("allValid").GetBoolean());
        var results = both.GetProperty("results").EnumerateArray().ToDictionary(r => r.GetProperty("id").GetInt32(), r => r.GetProperty("isValid").GetBoolean());
        Assert.True(results[goodId]);
        Assert.False(results[brokenId]);
    }

    [Fact]
    public async Task Cleanup_keeps_recent_backups_and_refuses_a_nonsense_age()
    {
        var admin = await Admin();
        var created = await CreateBackupAsync(admin);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.SendAsync(Json(HttpMethod.Post, "/api/v1/backups/cleanup", new { daysToKeep = 30 }))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/api/v1/backups/{created.GetProperty("id").GetInt32()}")).StatusCode);

        foreach (var days in new[] { 0, -5, 100000 })
            await AssertProblem(await admin.SendAsync(Json(HttpMethod.Post, "/api/v1/backups/cleanup", new { daysToKeep = days })), HttpStatusCode.BadRequest, "validation_failed");
    }

    [Fact]
    public async Task Test_connection_reports_both_directions()
    {
        var admin = await Admin();

        var body = await Body(await admin.SendAsync(Json(HttpMethod.Post, "/api/v1/backups/test-connection")));

        Assert.True(body.GetProperty("backupConnection").GetBoolean());
        Assert.True(body.GetProperty("restoreConnection").GetBoolean());
    }

    // ---- restore -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Restore_puts_the_data_back_as_it_was_and_keeps_the_newer_backups_listed()
    {
        var admin = await Admin();
        var kept = Unique("keep");
        Assert.Equal(HttpStatusCode.Created, (await admin.SendAsync(Json(HttpMethod.Post, "/api/v1/categories", new { name = kept }))).StatusCode);
        var snapshot = await CreateBackupAsync(admin);

        var lost = Unique("lost");
        Assert.Equal(HttpStatusCode.Created, (await admin.SendAsync(Json(HttpMethod.Post, "/api/v1/categories", new { name = lost }))).StatusCode);
        var later = await CreateBackupAsync(admin, Unique("later"));
        var restored = await AuditCountAsync(admin, "BackupRestored");

        var response = await admin.SendAsync(Json(HttpMethod.Post, $"/api/v1/backups/{snapshot.GetProperty("id").GetInt32()}/restore", new { createBackupBeforeRestore = false }));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var categories = await Body(await admin.GetAsync("/api/v1/categories?pageSize=200"));
        var names = categories.GetProperty("items").EnumerateArray().Select(c => c.GetProperty("name").GetString()).ToList();
        Assert.Contains(kept, names);
        Assert.DoesNotContain(lost, names);

        // The Backups table was rolled back with the rest of the database; the list is rebuilt from the files on disk.
        Assert.Contains(later.GetProperty("name").GetString(), await NamesAsync(admin));
        // The snapshot was taken while its own record still said "InProgress", and that record came back with the table:
        // it must not stay in progress (the file is there and whole), or it could not be downloaded again.
        var restoredFrom = await Body(await admin.GetAsync($"/api/v1/backups/{snapshot.GetProperty("id").GetInt32()}"));
        Assert.Equal("Completed", restoredFrom.GetProperty("status").GetString());
        Assert.True(restoredFrom.GetProperty("fileSizeBytes").GetInt64() > 0);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/api/v1/backups/{snapshot.GetProperty("id").GetInt32()}/download")).StatusCode);
        // The audit trail is a separate database: it was not rolled back and records the restore.
        Assert.Equal(restored + 1, await AuditCountAsync(admin, "BackupRestored"));
    }

    [Fact]
    public async Task Restore_takes_a_backup_of_the_current_data_first_unless_told_not_to()
    {
        var admin = await Admin();
        var snapshot = await CreateBackupAsync(admin);
        var id = snapshot.GetProperty("id").GetInt32();
        var before = (await ListAsync(admin)).GetProperty("total").GetInt32();

        Assert.Equal(HttpStatusCode.NoContent, (await admin.SendAsync(Json(HttpMethod.Post, $"/api/v1/backups/{id}/restore", new { createBackupBeforeRestore = false }))).StatusCode);
        Assert.Equal(before, (await ListAsync(admin)).GetProperty("total").GetInt32());

        Assert.Equal(HttpStatusCode.NoContent, (await admin.SendAsync(Json(HttpMethod.Post, $"/api/v1/backups/{id}/restore", new { }))).StatusCode);
        Assert.Equal(before + 1, (await ListAsync(admin)).GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Restoring_a_backup_whose_file_is_gone_is_a_409_and_changes_nothing()
    {
        var admin = await Admin();
        var created = await CreateBackupAsync(admin);
        File.Delete(Path.Combine(await BackupFolderAsync(admin), created.GetProperty("fileName").GetString()!));
        var failures = await AuditCountAsync(admin, "BackupRestoreFailed");

        var response = await admin.SendAsync(Json(HttpMethod.Post, $"/api/v1/backups/{created.GetProperty("id").GetInt32()}/restore", new { createBackupBeforeRestore = false }));

        await AssertProblem(response, HttpStatusCode.Conflict, "restore_failed");
        Assert.Equal(failures + 1, await AuditCountAsync(admin, "BackupRestoreFailed"));
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/v1/backups/summary")).StatusCode);
    }

    [Fact]
    public async Task A_file_that_is_not_a_backup_of_this_database_is_refused()
    {
        var admin = await Admin();
        var created = await CreateBackupAsync(admin);
        var file = Path.Combine(await BackupFolderAsync(admin), created.GetProperty("fileName").GetString()!);
        await File.WriteAllTextAsync(file, "this is not a database");
        File.Delete(file + ".meta.json"); // without the sidecar the file header decides

        var response = await admin.SendAsync(Json(HttpMethod.Post, $"/api/v1/backups/{created.GetProperty("id").GetInt32()}/restore", new { createBackupBeforeRestore = false }));

        await AssertProblem(response, HttpStatusCode.Conflict, "restore_failed");
    }

    // ---- the licence trap --------------------------------------------------------------------------------------

    [Fact]
    public async Task Backups_keep_working_while_the_licence_has_stopped_selling()
    {
        var admin = await Admin();
        var session = await Body(await admin.GetAsync("/api/v1/session"));
        Assert.False(session.GetProperty("license").GetProperty("allowsSales").GetBoolean()); // the test host has no licence file

        var created = await CreateBackupAsync(admin);
        var id = created.GetProperty("id").GetInt32();

        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/v1/backups")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/api/v1/backups/{id}/download")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.SendAsync(Json(HttpMethod.Post, $"/api/v1/backups/{id}/restore", new { createBackupBeforeRestore = false }))).StatusCode);
    }

    // ---- schedules ---------------------------------------------------------------------------------------------

    private static object Schedule(string name, string frequency = "Daily", string time = "02:00", int? dayOfWeek = null, int? dayOfMonth = null, int? retentionDays = null, bool isActive = true)
        => new { name, description = "from a test", frequency, time, dayOfWeek, dayOfMonth, retentionDays, isActive };

    private static async Task<JsonElement> CreateScheduleAsync(HttpClient client, object body)
    {
        var response = await client.SendAsync(Json(HttpMethod.Post, "/api/v1/backup-schedules", body));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await Body(response);
    }

    [Fact]
    public async Task A_daily_schedule_gets_its_next_run_and_shows_in_the_list_and_the_summary()
    {
        var admin = await Admin();
        var name = Unique("daily");

        var created = await CreateScheduleAsync(admin, Schedule(name, retentionDays: 14));

        Assert.Equal(name, created.GetProperty("name").GetString());
        Assert.Equal("Daily", created.GetProperty("frequency").GetString());
        Assert.Equal("02:00", created.GetProperty("time").GetString());
        Assert.Equal(14, created.GetProperty("retentionDays").GetInt32());
        Assert.True(created.GetProperty("isActive").GetBoolean());
        Assert.True(created.GetProperty("nextRunAt").GetDateTime() > DateTime.UtcNow.AddMinutes(-1));
        Assert.True(created.GetProperty("nextRunAt").GetDateTime() <= DateTime.UtcNow.AddHours(25));
        Assert.Equal(JsonValueKind.Null, created.GetProperty("lastRunAt").ValueKind);

        var list = await Body(await admin.GetAsync("/api/v1/backup-schedules"));
        Assert.Contains(name, list.EnumerateArray().Select(s => s.GetProperty("name").GetString()));
        var summary = await Body(await admin.GetAsync("/api/v1/backups/summary"));
        Assert.True(summary.GetProperty("scheduledCount").GetInt32() >= 1);
        Assert.NotNull(summary.GetProperty("nextScheduledAt").GetString());
    }

    [Fact]
    public async Task Weekly_and_monthly_schedules_keep_only_the_day_that_belongs_to_them()
    {
        var admin = await Admin();

        var weekly = await CreateScheduleAsync(admin, Schedule(Unique("wk"), "weekly", "23:30", dayOfWeek: 3, dayOfMonth: 9));
        Assert.Equal("Weekly", weekly.GetProperty("frequency").GetString());
        Assert.Equal(3, weekly.GetProperty("dayOfWeek").GetInt32());
        Assert.Equal(JsonValueKind.Null, weekly.GetProperty("dayOfMonth").ValueKind);
        Assert.Equal("23:30", weekly.GetProperty("time").GetString());

        var monthly = await CreateScheduleAsync(admin, Schedule(Unique("mo"), "Monthly", dayOfWeek: 2, dayOfMonth: 15));
        Assert.Equal(15, monthly.GetProperty("dayOfMonth").GetInt32());
        Assert.Equal(JsonValueKind.Null, monthly.GetProperty("dayOfWeek").ValueKind);
    }

    [Fact]
    public async Task A_schedule_that_cannot_run_is_refused_with_the_field()
    {
        var admin = await Admin();

        async Task<JsonElement> Refused(object body)
        {
            var response = await admin.SendAsync(Json(HttpMethod.Post, "/api/v1/backup-schedules", body));
            await AssertProblem(response, HttpStatusCode.BadRequest, "validation_failed");
            return (await Body(response)).GetProperty("errors");
        }

        Assert.True((await Refused(Schedule(Unique("x"), "Weekly"))).TryGetProperty("dayOfWeek", out _));
        Assert.True((await Refused(Schedule(Unique("x"), "Monthly"))).TryGetProperty("dayOfMonth", out _));
        Assert.True((await Refused(Schedule(Unique("x"), "Hourly"))).TryGetProperty("frequency", out _));
        Assert.True((await Refused(Schedule(Unique("x"), time: "25:00"))).TryGetProperty("time", out _));
        Assert.True((await Refused(Schedule(Unique("x"), time: "2:00"))).TryGetProperty("time", out _));
        Assert.True((await Refused(Schedule(Unique("x"), "Monthly", dayOfMonth: 31))).TryGetProperty("dayOfMonth", out _));
        Assert.True((await Refused(Schedule(Unique("x"), "Weekly", dayOfWeek: 7))).TryGetProperty("dayOfWeek", out _));
        Assert.True((await Refused(Schedule(Unique("x"), retentionDays: 0))).TryGetProperty("retentionDays", out _));
        Assert.True((await Refused(Schedule(""))).TryGetProperty("name", out _));
    }

    [Fact]
    public async Task A_schedule_can_be_changed_paused_resumed_and_deleted()
    {
        var admin = await Admin();
        var created = await CreateScheduleAsync(admin, Schedule(Unique("edit")));
        var id = created.GetProperty("id").GetInt32();

        var renamed = Unique("renamed");
        var updated = await admin.SendAsync(Json(HttpMethod.Put, $"/api/v1/backup-schedules/{id}", Schedule(renamed, "Weekly", "04:15", dayOfWeek: 6, retentionDays: 7)));
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var body = await Body(updated);
        Assert.Equal(renamed, body.GetProperty("name").GetString());
        Assert.Equal("Weekly", body.GetProperty("frequency").GetString());
        Assert.Equal("04:15", body.GetProperty("time").GetString());
        Assert.Equal(7, body.GetProperty("retentionDays").GetInt32());

        var paused = await Body(await admin.SendAsync(Json(HttpMethod.Put, $"/api/v1/backup-schedules/{id}/active", new { isActive = false })));
        Assert.False(paused.GetProperty("isActive").GetBoolean());
        Assert.Equal(JsonValueKind.Null, paused.GetProperty("nextRunAt").ValueKind);

        var resumed = await Body(await admin.SendAsync(Json(HttpMethod.Put, $"/api/v1/backup-schedules/{id}/active", new { isActive = true })));
        Assert.True(resumed.GetProperty("isActive").GetBoolean());
        Assert.NotEqual(JsonValueKind.Null, resumed.GetProperty("nextRunAt").ValueKind);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.SendAsync(Json(HttpMethod.Delete, $"/api/v1/backup-schedules/{id}"))).StatusCode);
        await AssertProblem(await admin.SendAsync(Json(HttpMethod.Delete, $"/api/v1/backup-schedules/{id}")), HttpStatusCode.NotFound, "not_found");
        await AssertProblem(await admin.SendAsync(Json(HttpMethod.Put, $"/api/v1/backup-schedules/{id}", Schedule("x"))), HttpStatusCode.NotFound, "not_found");
        await AssertProblem(await admin.SendAsync(Json(HttpMethod.Put, $"/api/v1/backup-schedules/{id}/active", new { isActive = true })), HttpStatusCode.NotFound, "not_found");
        await AssertProblem(await admin.SendAsync(Json(HttpMethod.Post, $"/api/v1/backup-schedules/{id}/run")), HttpStatusCode.NotFound, "not_found");
    }

    [Fact]
    public async Task Run_now_takes_a_backup_named_after_the_schedule_and_records_the_run()
    {
        var admin = await Admin();
        var name = Unique("runme");
        var created = await CreateScheduleAsync(admin, Schedule(name));
        var id = created.GetProperty("id").GetInt32();
        var before = (await ListAsync(admin)).GetProperty("total").GetInt32();

        var response = await admin.SendAsync(Json(HttpMethod.Post, $"/api/v1/backup-schedules/{id}/run"));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(before + 1, (await ListAsync(admin)).GetProperty("total").GetInt32());
        Assert.Contains((await NamesAsync(admin)), n => n.StartsWith($"Manual_{name}_"));
        var schedule = (await Body(await admin.GetAsync("/api/v1/backup-schedules"))).EnumerateArray().Single(s => s.GetProperty("id").GetInt32() == id);
        Assert.NotEqual(JsonValueKind.Null, schedule.GetProperty("lastRunAt").ValueKind);
    }

    [Fact]
    public async Task Creating_a_backup_is_audited_like_the_razor_page_did()
    {
        var admin = await Admin();
        var audited = await AuditCountAsync(admin, "BackupCreated");

        await CreateBackupAsync(admin);

        Assert.Equal(audited + 1, await AuditCountAsync(admin, "BackupCreated"));
    }
}
