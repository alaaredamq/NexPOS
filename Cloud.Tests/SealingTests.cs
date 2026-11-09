using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Cloud.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Sync;
using Xunit;

namespace Cloud.Tests;

/// <summary>The shop locks what the cloud does not need to read (docs/online/PRIVACY-KEYS.md, step 1).</summary>
public class SealingTests : IClassFixture<CloudFactory>
{
    private readonly CloudFactory _cloud;

    public SealingTests(CloudFactory cloud) => _cloud = cloud;

    private string Latest => _cloud.Mirrors.LatestMigration;

    private static Dictionary<string, object?> Row(string table, long id, params (string Key, object? Value)[] values)
        => values.ToDictionary(v => v.Key, v => v.Value);

    // ---- the lock itself ----

    [Fact]
    public void A_sealed_row_opens_to_the_same_values()
    {
        var sealer = new RowSealer(RowSealer.NewKey());
        var blob = sealer.Seal("Customers", 7, new Dictionary<string, object?>
        {
            ["FirstName"] = "Zainab", ["Balance"] = "12.50", ["IsActive"] = 1L, ["Notes"] = null,
            ["Photo"] = new SyncBlob(Convert.ToBase64String([1, 2, 3])),
        });

        var opened = sealer.Open("Customers", 7, blob);

        Assert.Equal("Zainab", opened["FirstName"]!.ToString());
        Assert.Equal("12.50", opened["Balance"]!.ToString());
        Assert.Equal(1L, ((JsonElement)opened["IsActive"]!).GetInt64());
        Assert.Null(opened["Notes"]);
        Assert.Equal(Convert.ToBase64String([1, 2, 3]), SyncWire.ToDbValue(opened["Photo"]) is byte[] bytes ? Convert.ToBase64String(bytes) : null);
    }

    [Fact]
    public void Sealing_the_same_row_twice_gives_different_bytes_and_the_blob_hides_the_text()
    {
        var sealer = new RowSealer(RowSealer.NewKey());
        var values = new Dictionary<string, object?> { ["FirstName"] = "Zainab" };

        var a = sealer.Seal("Customers", 1, values);
        var b = sealer.Seal("Customers", 1, values);

        Assert.NotEqual(a, b);
        Assert.DoesNotContain("Zainab", Encoding.UTF8.GetString(Convert.FromBase64String(a)));
    }

    [Fact]
    public void A_row_does_not_open_with_the_wrong_key_or_when_moved_or_changed()
    {
        var sealer = new RowSealer(RowSealer.NewKey());
        var blob = sealer.Seal("Customers", 7, new Dictionary<string, object?> { ["FirstName"] = "Zainab" });

        Assert.ThrowsAny<CryptographicException>(() => new RowSealer(RowSealer.NewKey()).Open("Customers", 7, blob));
        Assert.ThrowsAny<CryptographicException>(() => sealer.Open("Customers", 8, blob));   // moved to another id
        Assert.ThrowsAny<CryptographicException>(() => sealer.Open("Suppliers", 7, blob));   // moved to another table

        var changed = Convert.FromBase64String(blob);
        changed[^20] ^= 0x01;                                                                 // one flipped bit in the ciphertext
        Assert.ThrowsAny<CryptographicException>(() => sealer.Open("Customers", 7, Convert.ToBase64String(changed)));

        var relabelled = Convert.FromBase64String(blob);
        relabelled[1] ^= 0x01;                                                                // claims another key id
        Assert.ThrowsAny<CryptographicException>(() => sealer.Open("Customers", 7, Convert.ToBase64String(relabelled)));

        Assert.ThrowsAny<CryptographicException>(() => sealer.Open("Customers", 7, "AAAA"));
    }

    [Fact]
    public void The_key_id_is_a_stable_fingerprint_and_keys_must_be_32_bytes()
    {
        var key = RowSealer.NewKey();
        Assert.Equal(new RowSealer(key).KeyId, new RowSealer(key).KeyId);
        Assert.NotEqual(new RowSealer(key).KeyId, new RowSealer(RowSealer.NewKey()).KeyId);
        Assert.Equal(16, new RowSealer(key).KeyId.Length);
        Assert.Throws<ArgumentException>(() => new RowSealer(new byte[16]));
    }

    [Fact]
    public void The_cloud_can_check_the_shape_of_a_sealed_value_without_a_key()
    {
        var sealer = new RowSealer(RowSealer.NewKey());
        var blob = sealer.Seal("Orders", 1, new Dictionary<string, object?> { ["Total"] = "1" });

        Assert.True(RowSealer.TryReadKeyId(blob, out var keyId));
        Assert.Equal(sealer.KeyId, keyId);
        Assert.False(RowSealer.TryReadKeyId("not base64!!", out _));
        Assert.False(RowSealer.TryReadKeyId("AAAA", out _));
        var wrongVersion = Convert.FromBase64String(blob);
        wrongVersion[0] = 9;
        Assert.False(RowSealer.TryReadKeyId(Convert.ToBase64String(wrongVersion), out _));
    }

    // ---- the key on disk ----

    private static string TempKeyPath() => Path.Combine(Path.GetTempPath(), "nexpos-key-" + Guid.NewGuid().ToString("N"), "online.key");

    [Fact]
    public void The_key_is_made_once_and_is_the_same_after_a_restart()
    {
        var path = TempKeyPath();
        var store = new SealKeyStore(path);
        Assert.Null(store.TryLoad());

        var first = store.LoadOrCreate();
        var again = store.LoadOrCreate();
        var afterRestart = new SealKeyStore(path).LoadOrCreate();

        Assert.True(File.Exists(path));
        Assert.Equal(first.KeyId, again.KeyId);
        Assert.Equal(first.KeyId, afterRestart.KeyId);
        Assert.Equal(first.KeyId, new SealKeyStore(path).TryLoad()!.KeyId);
    }

    [Fact]
    public void A_damaged_key_file_is_an_error_and_never_replaced_by_a_new_key()
    {
        var path = TempKeyPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        byte[] damaged = [9, 9, 9];
        File.WriteAllBytes(path, damaged);

        Assert.Throws<InvalidDataException>(() => new SealKeyStore(path).LoadOrCreate());
        Assert.Equal(damaged, File.ReadAllBytes(path));
    }

    [Fact]
    public void Sealing_is_off_until_someone_turns_it_on()
    {
        Assert.False(new OnlineSettings().SealData);
        var path = Path.Combine(Path.GetTempPath(), "nexpos-settings-" + Guid.NewGuid().ToString("N"), "online.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """{ "Online": { "Enabled": true, "CloudUrl": "https://c.example", "DeviceToken": "t" } }""");

        Assert.False(new OnlineSettingsStore(path, new OnlineSettings()).Current.SealData);
    }

    // ---- what is sealed ----

    [Fact]
    public void Every_synced_table_has_a_sealing_decision()
    {
        using var till = new TillDb();
        var synced = till.Schema.Tables.Select(t => t.Name).ToHashSet(StringComparer.Ordinal);

        Assert.Empty(SealPolicy.Readable.Intersect(SealPolicy.Sealed));
        Assert.Empty(synced.Where(t => !SealPolicy.Readable.Contains(t) && !SealPolicy.Sealed.Contains(t))
            .Select(t => $"{t}: add it to SealPolicy.Readable or SealPolicy.Sealed"));
        Assert.Empty(SealPolicy.Readable.Concat(SealPolicy.Sealed).Where(t => !synced.Contains(t))
            .Select(t => $"{t}: listed in SealPolicy but not a synced table"));

        foreach (var (table, columns) in SealPolicy.SealedColumns)
        {
            Assert.Contains(table, SealPolicy.Readable);
            var known = till.Schema.TryGetTable(table, out var t) ? t.Columns : [];
            Assert.All(columns, c => Assert.Contains(c, known));
        }
    }

    [Fact]
    public void Credentials_and_the_shops_own_access_control_are_not_synced_at_all()
    {
        using var till = new TillDb();
        Assert.DoesNotContain(till.Schema.Tables, t => SyncSchema.ExcludedTables.Contains(t.Name));
        Assert.Contains("TillDevices", SyncSchema.ExcludedTables);
        Assert.DoesNotContain("PasswordHash", till.Schema.TryGetTable("Users", out var users) ? users.Columns : []);
    }

    // ---- what the till sends ----

    private sealed class CapturingTransport : ISyncTransport
    {
        public List<SyncBatch> Batches { get; } = [];

        public Task<SyncAck> PushAsync(SyncBatch batch, CancellationToken ct)
        {
            Batches.Add(batch);
            return Task.FromResult(new SyncAck(batch.ThroughSeq, batch.Changes.Count, false));
        }
    }

    [Fact]
    public async Task A_sealing_till_sends_nothing_readable_that_the_policy_hides()
    {
        using var till = new TillDb();
        var customerId = await till.AddCustomerAsync("Zainab");
        var productId = await till.AddProductAsync("Olive oil 1L", "6001", costPrice: 7.77m);
        till.EnableSync();
        var capture = new CapturingTransport();

        var result = await till.Pusher(capture, sealer: new RowSealer(RowSealer.NewKey())).RunOnceAsync(default);

        Assert.True(result.Ok, result.Error);
        var changes = capture.Batches.SelectMany(b => b.Changes).ToList();
        Assert.All(capture.Batches, b => Assert.Equal(SyncFormat.Sealed, b.Format));

        // Fully sealed tables travel as locked bytes only.
        var customer = changes.Single(c => c.Table == "Customers" && c.Id == customerId);
        Assert.Null(customer.Values);
        Assert.NotNull(customer.Sealed);
        Assert.All(changes.Where(c => SealPolicy.IsFullySealed(c.Table)), c => { Assert.Null(c.Values); Assert.NotNull(c.Sealed); });

        // A readable table keeps its readable columns; only the cost is locked.
        var product = changes.Single(c => c.Table == "Products" && c.Id == productId);
        Assert.Equal("Olive oil 1L", product.Values!["Name"]);
        Assert.False(product.Values.ContainsKey("CostPrice"));
        Assert.NotNull(product.Sealed);

        // Nothing of the secrets anywhere in what would go over the wire.
        var wire = JsonSerializer.Serialize(capture.Batches, SyncWire.Json);
        Assert.DoesNotContain("Zainab", wire);
        Assert.DoesNotContain("zainab@example.com", wire);
        Assert.DoesNotContain("7.77", wire);
        Assert.Contains("Olive oil 1L", wire); // the check can see text when there is some
    }

    [Fact]
    public async Task Without_a_key_the_till_sends_everything_as_before()
    {
        using var till = new TillDb();
        await till.AddCustomerAsync("Zainab");
        till.EnableSync();
        var capture = new CapturingTransport();

        await till.Pusher(capture).RunOnceAsync(default);

        Assert.All(capture.Batches, b => Assert.Equal(SyncFormat.Plain, b.Format));
        Assert.All(capture.Batches.SelectMany(b => b.Changes), c => Assert.Null(c.Sealed));
        Assert.Contains("Zainab", JsonSerializer.Serialize(capture.Batches, SyncWire.Json));
    }

    // ---- what the cloud keeps ----

    private static string RawBytes(string path) => SqlHelper.RawText(path);

    private static string? SealedBlob(string mirror, string table, long id)
        => SqlHelper.Scalar<string>(mirror, $"SELECT Blob FROM SealedRows WHERE TableName = '{table}' AND Id = {id}");

    [Fact]
    public async Task The_cloud_stores_locked_rows_it_cannot_read_and_the_key_holder_can()
    {
        var site = await _cloud.SeedSiteAsync();
        using var till = new TillDb();
        var sealer = new RowSealer(RowSealer.NewKey());
        var customerId = await till.AddCustomerAsync("Zainab");
        var productId = await till.AddProductAsync("Olive oil 1L", "6001", costPrice: 7.77m);
        till.EnableSync();

        var result = await till.Pusher(Transports.To(_cloud, site.Token), sealer: sealer).RunOnceAsync(default);

        Assert.True(result.Ok, result.Error);
        Assert.Equal(0, result.Pending);
        var mirror = _cloud.Mirrors.PathFor(site.SiteId);

        // The raw file (and its write-ahead log) holds no trace of the customer or the cost.
        var raw = RawBytes(mirror);
        Assert.DoesNotContain("Zainab", raw);
        Assert.DoesNotContain("zainab@example.com", raw);
        Assert.DoesNotContain("7.77", raw);
        Assert.Contains("Olive oil 1L", raw); // readable on purpose, and proves the search can find text

        // Readable table: name readable, cost empty. Sealed table: no readable rows at all.
        Assert.Equal("Olive oil 1L", SqlHelper.Scalar<string>(mirror, $"SELECT Name FROM Products WHERE Id = {productId}"));
        Assert.Null(SqlHelper.Scalar<string>(mirror, $"SELECT CostPrice FROM Products WHERE Id = {productId}"));
        Assert.Equal(0L, SqlHelper.Scalar<long>(mirror, "SELECT COUNT(*) FROM Customers"));
        Assert.Equal(sealer.KeyId, SqlHelper.Scalar<string>(mirror, $"SELECT KeyId FROM SealedRows WHERE TableName = 'Customers' AND Id = {customerId}"));

        // The key holder opens them.
        Assert.Equal("Zainab", sealer.Open("Customers", customerId, SealedBlob(mirror, "Customers", customerId)!)["FirstName"]!.ToString());
        Assert.Equal("7.77", sealer.Open("Products", productId, SealedBlob(mirror, "Products", productId)!)["CostPrice"]!.ToString());
        Assert.ThrowsAny<CryptographicException>(() => new RowSealer(RowSealer.NewKey()).Open("Customers", customerId, SealedBlob(mirror, "Customers", customerId)!));
    }

    [Fact]
    public async Task The_portal_can_still_count_sealed_rows()
    {
        var site = await _cloud.SeedSiteAsync();
        using var till = new TillDb();
        await till.AddCustomerAsync("Zainab");
        await till.AddCustomerAsync("Omar");
        await till.AddProductAsync("Olive oil 1L", "6001");
        till.EnableSync();

        Assert.True((await till.Pusher(Transports.To(_cloud, site.Token), sealer: new RowSealer(RowSealer.NewKey())).RunOnceAsync(default)).Ok);

        var counts = _cloud.Mirrors.CountRows(site.SiteId, ["Customers", "Products", "Orders", "Users", "Receipts"]);
        foreach (var table in counts.Keys)
            Assert.Equal(SqlHelper.Scalar<long>(till.Path, $"SELECT COUNT(*) FROM \"{table}\""), counts[table]);
        Assert.True(counts["Customers"] >= 2);
        Assert.True(counts["Users"] > 0);
    }

    [Fact]
    public async Task Later_changes_replace_and_remove_the_locked_rows()
    {
        var site = await _cloud.SeedSiteAsync();
        using var till = new TillDb();
        var sealer = new RowSealer(RowSealer.NewKey());
        var kept = await till.AddCustomerAsync("Omar", city: "Beirut");
        var doomed = await till.AddCustomerAsync("Nour");
        till.EnableSync();
        var pusher = till.Pusher(Transports.To(_cloud, site.Token), sealer: sealer);
        Assert.True((await pusher.RunOnceAsync(default)).Ok);
        var mirror = _cloud.Mirrors.PathFor(site.SiteId);
        var before = _cloud.Mirrors.CountRows(site.SiteId, ["Customers"])["Customers"];

        await using (var context = till.CreateContext())
        {
            await context.Customers.Where(c => c.Id == kept).ExecuteUpdateAsync(s => s.SetProperty(c => c.City, "Saida"));
            await context.Customers.Where(c => c.Id == doomed).ExecuteDeleteAsync();
        }
        Assert.True((await pusher.RunOnceAsync(default)).Ok);

        Assert.Equal("Saida", sealer.Open("Customers", kept, SealedBlob(mirror, "Customers", kept)!)["City"]!.ToString());
        Assert.Null(SealedBlob(mirror, "Customers", doomed));
        Assert.Equal(before - 1, _cloud.Mirrors.CountRows(site.SiteId, ["Customers"])["Customers"]);
    }

    [Fact]
    public async Task A_full_resync_clears_the_locked_rows_too()
    {
        var site = await _cloud.SeedSiteAsync();
        using var till = new TillDb();
        await till.AddCustomerAsync("Zainab");
        till.EnableSync();
        Assert.True((await till.Pusher(Transports.To(_cloud, site.Token), sealer: new RowSealer(RowSealer.NewKey())).RunOnceAsync(default)).Ok);
        var mirror = _cloud.Mirrors.PathFor(site.SiteId);
        Assert.True(SqlHelper.Scalar<long>(mirror, "SELECT COUNT(*) FROM SealedRows") > 0);

        var response = await _cloud.DeviceClient(site.Token).PostAsJsonAsync("/api/sync/push",
            new SyncBatch(Latest, 0, 1, true, [], SyncFormat.Sealed), SyncWire.Json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0L, SqlHelper.Scalar<long>(mirror, "SELECT COUNT(*) FROM SealedRows"));
    }

    // ---- what the cloud refuses ----

    private async Task AssertRefusedAsync(SyncBatch batch)
    {
        var site = await _cloud.SeedSiteAsync();
        var response = await _cloud.DeviceClient(site.Token).PostAsJsonAsync("/api/sync/push", batch, SyncWire.Json);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(SyncErrorCodes.BadBatch, (await response.Content.ReadFromJsonAsync<SyncError>(SyncWire.Json))!.Code);
    }

    private static string AnyBlob(string table = "Customers", long id = 1)
        => new RowSealer(RowSealer.NewKey()).Seal(table, id, new Dictionary<string, object?> { ["x"] = "y" });

    [Fact]
    public Task An_unknown_format_is_refused_so_an_old_cloud_never_stores_rows_it_cannot_read()
        => AssertRefusedAsync(new SyncBatch(Latest, 0, 1, false, [], Format: 99));

    [Fact]
    public Task A_sealed_batch_that_sends_a_sealed_table_in_the_clear_is_refused()
        => AssertRefusedAsync(new SyncBatch(Latest, 0, 1, false,
            [new SyncChange("Customers", 1, false, Row("Customers", 1, ("FirstName", "Zainab")))], SyncFormat.Sealed));

    [Fact]
    public Task A_sealed_batch_that_sends_a_sealed_column_in_the_clear_is_refused()
        => AssertRefusedAsync(new SyncBatch(Latest, 0, 1, false,
            [new SyncChange("Products", 1, false, Row("Products", 1, ("Name", "Oil"), ("CostPrice", "7.77")), AnyBlob("Products"))], SyncFormat.Sealed));

    [Fact]
    public Task A_sealed_batch_that_leaves_out_the_locked_part_is_refused()
        => AssertRefusedAsync(new SyncBatch(Latest, 0, 1, false,
            [new SyncChange("Products", 1, false, Row("Products", 1, ("Name", "Oil")))], SyncFormat.Sealed));

    [Fact]
    public Task A_locked_value_in_a_batch_that_says_it_is_not_sealed_is_refused()
        => AssertRefusedAsync(new SyncBatch(Latest, 0, 1, false,
            [new SyncChange("Customers", 1, false, null, AnyBlob())], SyncFormat.Plain));

    [Fact]
    public Task A_malformed_locked_value_is_refused()
        => AssertRefusedAsync(new SyncBatch(Latest, 0, 1, false,
            [new SyncChange("Customers", 1, false, null, "not base64!!")], SyncFormat.Sealed));

    [Fact]
    public async Task A_well_formed_sealed_batch_is_accepted_and_stores_the_locked_row_only()
    {
        var site = await _cloud.SeedSiteAsync();
        var blob = AnyBlob("Customers", 5);

        var response = await _cloud.DeviceClient(site.Token).PostAsJsonAsync("/api/sync/push",
            new SyncBatch(Latest, 0, 1, false, [new SyncChange("Customers", 5, false, null, blob)], SyncFormat.Sealed), SyncWire.Json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var mirror = _cloud.Mirrors.PathFor(site.SiteId);
        Assert.Equal(blob, SealedBlob(mirror, "Customers", 5));
        Assert.Equal(0L, SqlHelper.Scalar<long>(mirror, "SELECT COUNT(*) FROM Customers WHERE Id = 5"));
    }
}
