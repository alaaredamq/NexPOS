using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Cloud.Tests.Support;
using Sync;
using Xunit;

namespace Cloud.Tests;

/// <summary>
/// End-to-end encryption: the till encrypts every row with a key that never leaves it, and the cloud stores what it is given.
/// The point of these tests is the negative: the provider's side holds nothing readable.
/// </summary>
public class EncryptedSyncTests : IClassFixture<CloudFactory>
{
    private readonly CloudFactory _cloud;

    public EncryptedSyncTests(CloudFactory cloud) => _cloud = cloud;

    private static SyncPusher EncryptedPusher(TillDb till, ISyncTransport transport, DataKey key)
        => new(till.Schema, till.ConnectionString, transport, new SyncStatus(), TimeProvider.System, null, 500, key.Cipher());

    /// <summary>Everything the cloud holds for a site, as raw bytes: the database file and its write-ahead log.</summary>
    private static string RawBytes(string path)
    {
        var sb = new StringBuilder();
        foreach (var suffix in new[] { "", "-wal" })
        {
            if (!File.Exists(path + suffix)) continue;
            using var stream = new FileStream(path + suffix, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            sb.Append(Encoding.Latin1.GetString(ms.ToArray()));
        }
        return sb.ToString();
    }

    // ---- the key and its recovery code -----------------------------------------------------------------------------

    [Fact]
    public void The_recovery_code_gives_back_the_same_key_however_it_is_typed()
    {
        var key = DataKey.Generate();
        var code = key.ToRecoveryCode();

        Assert.Equal(56 + 13, code.Length); // 56 characters in 14 groups of four, 13 dashes
        Assert.Matches("^([A-Z2-7]{4}-){13}[A-Z2-7]{4}$", code);

        foreach (var typed in new[] { code, code.ToLowerInvariant(), code.Replace("-", ""), " " + code.Replace("-", " ") + "\n" })
        {
            Assert.True(DataKey.TryParseRecoveryCode(typed, out var back), typed);
            Assert.Equal(key.ToBase64(), back!.ToBase64());
        }
    }

    [Fact]
    public void A_mistyped_recovery_code_is_refused_not_turned_into_a_different_key()
    {
        var code = DataKey.Generate().ToRecoveryCode().Replace("-", "");

        // Change each position in turn: every single-character typo must be caught by the checksum or the alphabet.
        for (var i = 0; i < code.Length; i++)
        {
            var wrong = code[i] == 'A' ? 'B' : 'A';
            Assert.False(DataKey.TryParseRecoveryCode(code[..i] + wrong + code[(i + 1)..], out _), $"typo at {i} was accepted");
        }
        Assert.False(DataKey.TryParseRecoveryCode(code[..^1], out _));
        Assert.False(DataKey.TryParseRecoveryCode(code + "A", out _));
        Assert.False(DataKey.TryParseRecoveryCode("", out _));
        Assert.False(DataKey.TryParseRecoveryCode(null, out _));
        Assert.False(DataKey.TryParseRecoveryCode(new string('1', 56), out _)); // 1 is not in the alphabet
    }

    // ---- the phone's sealed changes (Flutter app) ----

    private const string PhoneRecoveryCode = "DLNE-EURX-WYBS-ECX5-TSMP-KPIZ-WQPA-3Y4Y-JS2J-4UYN-KN4P-LPPW-GIC7-JAXS";

    /// <summary>Made by the Flutter app (mobile/test/crypto), with a random nonce. The shop PC must open it, bound to its kind of change.</summary>
    private const string PhoneSealedStockChange =
        "e1.J9VOEJE3iNGkIEP9Km2ui94LKnF0Z3Z5Shd8CB6+rbB3GcQr9wd7ZMLonckJPQ3lmtOkn6Ttvn4RD2RdTlcLBApQJyZQg6Bb6nrhmbKuQMpnolCG2tykZHackyBom5sQUIJjIR7W6GY=";

    [Fact]
    public void A_change_sealed_by_the_phone_opens_on_the_shop_pc_and_only_as_the_kind_it_was_sealed_for()
    {
        Assert.True(DataKey.TryParseRecoveryCode(PhoneRecoveryCode, out var key));
        var payload = System.Text.Json.JsonSerializer.SerializeToElement(new Dictionary<string, string> { ["enc"] = PhoneSealedStockChange });

        var opened = CommandSeal.Open(key!.Cipher(), "stock.adjust", payload);

        Assert.NotNull(opened);
        Assert.Equal("phone-vector-key-1", opened!.Value.Key);
        Assert.Equal(42, opened.Value.Body.GetProperty("productId").GetInt32());
        Assert.Equal("add", opened.Value.Body.GetProperty("type").GetString());
        Assert.Equal(5, opened.Value.Body.GetProperty("quantity").GetInt32());
        Assert.Null(CommandSeal.Open(key.Cipher(), "product.update", payload)); // the same bytes, claimed to be another kind of change
        Assert.Null(CommandSeal.Open(DataKey.Generate().Cipher(), "stock.adjust", payload)); // another shop's key
    }

    [Fact]
    public void Two_keys_are_never_the_same()
        => Assert.NotEqual(DataKey.Generate().ToBase64(), DataKey.Generate().ToBase64());

    // ---- one row -----------------------------------------------------------------------------------------------------

    [Fact]
    public void A_row_decrypts_to_what_was_encrypted_including_bytes_and_nulls()
    {
        var cipher = DataKey.Generate().Cipher();
        var values = new Dictionary<string, object?>
        {
            ["Name"] = "Zaatar & Cheese",
            ["Price"] = 12.5,
            ["Stock"] = 7L,
            ["Note"] = null,
            ["Image"] = new SyncBlob(Convert.ToBase64String(new byte[] { 1, 2, 3 })),
        };

        var back = cipher.Decrypt("Products", 42, cipher.Encrypt("Products", 42, values));

        Assert.Equal("Zaatar & Cheese", ((JsonElement)back["Name"]!).GetString());
        Assert.Equal(12.5, ((JsonElement)back["Price"]!).GetDouble());
        Assert.Equal(7L, ((JsonElement)back["Stock"]!).GetInt64());
        Assert.Null(back["Note"]);
        Assert.Equal(Convert.ToBase64String(new byte[] { 1, 2, 3 }), ((JsonElement)back["Image"]!).GetProperty("$b64").GetString());
    }

    [Fact]
    public void The_same_row_encrypted_twice_looks_different()
    {
        var cipher = DataKey.Generate().Cipher();
        var values = new Dictionary<string, object?> { ["Name"] = "Same" };
        Assert.NotEqual(cipher.Encrypt("Products", 1, values), cipher.Encrypt("Products", 1, values));
    }

    [Fact]
    public void A_row_cannot_be_read_with_the_wrong_key_or_moved_or_altered()
    {
        var key = DataKey.Generate();
        var cipher = key.Cipher();
        var payload = cipher.Encrypt("Products", 5, new Dictionary<string, object?> { ["Name"] = "Cheese" });

        Assert.ThrowsAny<CryptographicException>(() => DataKey.Generate().Cipher().Decrypt("Products", 5, payload)); // wrong key
        Assert.ThrowsAny<CryptographicException>(() => cipher.Decrypt("Products", 6, payload));                      // moved to another row
        Assert.ThrowsAny<CryptographicException>(() => cipher.Decrypt("Customers", 5, payload));                     // moved to another table

        var bytes = Convert.FromBase64String(payload[RowCipher.Prefix.Length..]);
        bytes[^1] ^= 1; // flip one bit of the tag
        Assert.ThrowsAny<CryptographicException>(() => cipher.Decrypt("Products", 5, RowCipher.Prefix + Convert.ToBase64String(bytes)));
        Assert.ThrowsAny<CryptographicException>(() => cipher.Decrypt("Products", 5, "not encrypted data"));
    }

    // ---- through the cloud -------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_cloud_stores_the_shop_but_nothing_in_its_file_is_readable()
    {
        var site = await _cloud.SeedSiteAsync(clientName: "Secret Shop");
        using var till = new TillDb();
        till.EnableSync();
        await till.AddProductAsync("Zanzibar Truffle Oil 7731", "9990001");
        await till.AddCustomerAsync("Wafaa", "Qlayaat");
        var key = DataKey.Generate();

        var result = await EncryptedPusher(till, Transports.To(_cloud, site.Token), key).RunOnceAsync(default);
        Assert.True(result.Ok, result.Error);

        var path = _cloud.Mirrors.PathFor(site.SiteId);
        var raw = RawBytes(path);
        foreach (var secret in new[] { "Zanzibar Truffle Oil 7731", "9990001", "Wafaa", "Qlayaat", "wafaa@example.com" })
            Assert.DoesNotContain(secret, raw);

        // It is all there, though, and the owner's key opens it.
        Assert.True(SqlHelper.Scalar<long>(path, "SELECT COUNT(*) FROM \"__EncryptedRows\"") > 30, "the seeded shop and the new rows are stored");
        var payload = SqlHelper.Scalar<string>(path, "SELECT Payload FROM \"__EncryptedRows\" WHERE TableName = 'Products' LIMIT 1")!;
        var rowId = SqlHelper.Scalar<long>(path, "SELECT RowId FROM \"__EncryptedRows\" WHERE TableName = 'Products' LIMIT 1");
        var row = key.Cipher().Decrypt("Products", rowId, payload);
        Assert.Equal("Zanzibar Truffle Oil 7731", ((JsonElement)row["Name"]!).GetString());
    }

    [Fact]
    public async Task The_same_scan_does_find_readable_data_when_it_is_not_encrypted()
    {
        // Guards the test above: if reading raw bytes could not find a name, "not found" would prove nothing.
        var site = await _cloud.SeedSiteAsync(clientName: "Open Shop");
        using var till = new TillDb();
        till.EnableSync();
        await till.AddProductAsync("Zanzibar Truffle Oil 7731", "9990002");

        var result = await till.Pusher(Transports.To(_cloud, site.Token)).RunOnceAsync(default);

        Assert.True(result.Ok, result.Error);
        Assert.Contains("Zanzibar Truffle Oil 7731", RawBytes(_cloud.Mirrors.PathFor(site.SiteId)));
    }

    [Fact]
    public async Task Changes_and_deletes_follow_and_the_portal_still_gets_counts_per_table()
    {
        var site = await _cloud.SeedSiteAsync();
        using var till = new TillDb();
        till.EnableSync();
        var key = DataKey.Generate();
        var pusher = EncryptedPusher(till, Transports.To(_cloud, site.Token), key);
        Assert.True((await pusher.RunOnceAsync(default)).Ok);

        var before = _cloud.Mirrors.CountRows(site.SiteId, ["Customers"])["Customers"];
        var id = await till.AddCustomerAsync("Temporary");
        Assert.True((await pusher.RunOnceAsync(default)).Ok);
        Assert.Equal(before + 1, _cloud.Mirrors.CountRows(site.SiteId, ["Customers"])["Customers"]);

        await using (var context = till.CreateContext())
        {
            context.Customers.Remove(await context.Customers.FindAsync(id) ?? throw new InvalidOperationException());
            await context.SaveChangesAsync();
        }
        Assert.True((await pusher.RunOnceAsync(default)).Ok);
        Assert.Equal(before, _cloud.Mirrors.CountRows(site.SiteId, ["Customers"])["Customers"]);
    }

    [Fact]
    public async Task A_new_snapshot_replaces_the_old_copy_rather_than_adding_to_it()
    {
        var site = await _cloud.SeedSiteAsync();
        using var till = new TillDb();
        till.EnableSync();
        await till.AddCustomerAsync("Once");
        var pusher = EncryptedPusher(till, Transports.To(_cloud, site.Token), DataKey.Generate());
        Assert.True((await pusher.RunOnceAsync(default)).Ok);
        var first = SqlHelper.Scalar<long>(_cloud.Mirrors.PathFor(site.SiteId), "SELECT COUNT(*) FROM \"__EncryptedRows\"");

        using (var connection = till.OpenRaw()) SyncTriggers.Seed(connection, till.Schema); // what a "full resync" does
        Assert.True((await pusher.RunOnceAsync(default)).Ok);

        Assert.Equal(first, SqlHelper.Scalar<long>(_cloud.Mirrors.PathFor(site.SiteId), "SELECT COUNT(*) FROM \"__EncryptedRows\""));
    }

    [Fact]
    public async Task A_cloud_that_requires_encryption_refuses_readable_data_and_accepts_encrypted_data()
    {
        var site = await _cloud.SeedSiteAsync();
        using var strict = _cloud.WithWebHostBuilder(b => b.UseSetting("Cloud:RequireEncryption", "true"));
        var http = strict.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", site.Token);

        var readable = new SyncBatch("", 0, 1, true, [new SyncChange("Customers", 1, false, new Dictionary<string, object?> { ["FirstName"] = "Plain" })]);
        var refused = await http.PostAsJsonAsync("api/sync/push", readable, SyncWire.Json);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("end-to-end encrypted", await refused.Content.ReadAsStringAsync());

        var cipher = DataKey.Generate().Cipher();
        var encrypted = new SyncBatch("", 0, 1, true,
            [new SyncChange("Customers", 1, false, null, cipher.Encrypt("Customers", 1, new Dictionary<string, object?> { ["FirstName"] = "Hidden" }))]);
        Assert.Equal(HttpStatusCode.OK, (await http.PostAsJsonAsync("api/sync/push", encrypted, SyncWire.Json)).StatusCode);

        // A deletion carries nothing to hide, so it is always allowed.
        var delete = new SyncBatch("", 1, 2, false, [new SyncChange("Customers", 1, true, null)]);
        Assert.Equal(HttpStatusCode.OK, (await http.PostAsJsonAsync("api/sync/push", delete, SyncWire.Json)).StatusCode);
    }

    [Fact]
    public async Task A_payload_that_is_not_encrypted_data_is_rejected_whole()
    {
        var site = await _cloud.SeedSiteAsync();
        var http = _cloud.DeviceClient(site.Token);

        var bad = new SyncBatch("", 0, 1, true, [new SyncChange("Customers", 1, false, null, "just some text")]);

        Assert.Equal(HttpStatusCode.BadRequest, (await http.PostAsJsonAsync("api/sync/push", bad, SyncWire.Json)).StatusCode);
    }

}
