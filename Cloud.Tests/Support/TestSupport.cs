using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using Cloud.Data;
using Cloud.Services;
using Core.Entities.Products;
using Core.Entities.Users;
using Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sync;

namespace Cloud.Tests.Support;

public sealed class TestClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>The cloud app on an in-memory test server, with its own data folder and a clock the test controls.</summary>
public sealed class CloudFactory : WebApplicationFactory<Program>
{
    public const string AdminUser = "owner";
    public const string AdminPassword = "correct-horse-battery";

    public string DataRoot { get; } = Path.Combine(Path.GetTempPath(), "nexpos-cloud-tests-" + Guid.NewGuid().ToString("N"));
    public TestClock Clock { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Cloud:DataRoot", DataRoot);
        builder.UseSetting("Cloud:AdminUser", AdminUser);
        builder.UseSetting("Cloud:AdminPassword", AdminPassword);
        builder.ConfigureTestServices(services => services.AddSingleton<TimeProvider>(Clock));
    }

    public MirrorStore Mirrors => Services.GetRequiredService<MirrorStore>();

    /// <summary>Creates client + subscription + site + token directly in the control database.</summary>
    public async Task<SeededSite> SeedSiteAsync(int subscriptionMonths = 1, string clientName = "Corner Shop")
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var now = Clock.GetUtcNow().UtcDateTime;

        var client = new Client { Name = clientName, CreatedAt = now };
        if (subscriptionMonths > 0)
            client.Subscription = new Subscription { StartsAt = now, ExpiresAt = now.AddMonths(subscriptionMonths) };

        var (token, hash, prefix) = DeviceTokens.Generate();
        var site = new Site { Id = Guid.NewGuid(), Name = "Main till", CreatedAt = now };
        site.Tokens.Add(new DeviceToken { TokenHash = hash, Prefix = prefix, CreatedAt = now });
        client.Sites.Add(site);
        db.Clients.Add(client);
        await db.SaveChangesAsync();
        return new SeededSite(client.Id, site.Id, token);
    }

    /// <summary>What the portal would hand the owner: a live connection link for the installation (loopback http, as the test server is).</summary>
    public async Task<string> IssueLinkAsync(Guid siteId)
    {
        using var scope = Services.CreateScope();
        var issued = await scope.ServiceProvider.GetRequiredService<EnrollmentService>().IssueAsync(siteId);
        return $"http://localhost/enroll/{issued.Display}";
    }

    public async Task<T> WithControlDbAsync<T>(Func<ControlDbContext, Task<T>> action)
    {
        using var scope = Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<ControlDbContext>());
    }

    public HttpClient DeviceClient(string token)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        SqliteConnection.ClearAllPools();
        try { if (Directory.Exists(DataRoot)) Directory.Delete(DataRoot, recursive: true); } catch (IOException) { /* temp folder; the OS cleans it */ }
    }
}

public sealed record SeededSite(int ClientId, Guid SiteId, string Token);

/// <summary>A real till database (file, fully migrated, with the app's own seed data) plus the sync plumbing around it.</summary>
public sealed class TillDb : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"nexpos-till-{Guid.NewGuid():N}.db");
    public string ConnectionString => $"Data Source={Path}";
    public SyncSchema Schema { get; }

    public TillDb()
    {
        using var context = CreateContext();
        context.Database.Migrate();
        Schema = SyncSchema.FromModel(context.Model);
    }

    public ErpDbContext CreateContext()
        => new(new DbContextOptionsBuilder<ErpDbContext>().UseSqlite(ConnectionString).Options);

    public SqliteConnection OpenRaw()
    {
        var connection = new SqliteConnection(ConnectionString);
        connection.Open();
        return connection;
    }

    public void EnableSync()
    {
        using var connection = OpenRaw();
        SyncTriggers.Install(connection, Schema);
    }

    public SyncPusher Pusher(ISyncTransport transport, SyncStatus? status = null, int batchSize = 500, RowSealer? sealer = null)
        => new(Schema, ConnectionString, transport, status ?? new SyncStatus(), TimeProvider.System, null, batchSize, sealer);

    public async Task<int> AddCustomerAsync(string first, string city = "Beirut")
    {
        await using var context = CreateContext();
        var customer = new Customer(first, "Test", $"{first.ToLowerInvariant()}@example.com") { City = city };
        context.Customers.Add(customer);
        await context.SaveChangesAsync();
        return customer.Id;
    }

    public async Task<int> AddProductAsync(string name, string barcode, decimal? costPrice = null)
    {
        await using var context = CreateContext();
        var product = new Product
        {
            Name = name,
            Barcode = barcode,
            CostPrice = costPrice,
            SupplierId = await context.Suppliers.Select(s => s.Id).FirstAsync(),
            CategoryId = await context.ProductCategories.Select(c => c.Id).FirstAsync(),
            CurrencyId = await context.Currencies.Select(c => c.Id).FirstAsync(),
        };
        context.Products.Add(product);
        await context.SaveChangesAsync();
        return product.Id;
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm" })
            try { File.Delete(Path + suffix); } catch (IOException) { /* temp file */ }
    }
}

public static class SqlHelper
{
    public static T? Scalar<T>(string path, string sql, bool readOnly = true)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWrite,
        }.ToString());
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        var value = cmd.ExecuteScalar();
        return value is null or DBNull ? default : (T)Convert.ChangeType(value, typeof(T));
    }

    /// <summary>
    /// Every byte of a SQLite file and its write-ahead log as text, for proving a value is nowhere in it, not even in free pages
    /// or old log frames. Reads while the database is open.
    /// </summary>
    public static string RawText(string path)
    {
        var all = new System.Text.StringBuilder();
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            if (!File.Exists(path + suffix)) continue;
            using var stream = File.Open(path + suffix, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            all.Append(System.Text.Encoding.Latin1.GetString(memory.ToArray()));
        }
        return all.ToString();
    }

    public static void Exec(string path, string sql)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}

/// <summary>Sends batches to the in-memory cloud with the device token, the same way a till does over HTTP.</summary>
public static class Transports
{
    public static HttpSyncTransport To(CloudFactory factory, string token) => new(factory.DeviceClient(token));
}

/// <summary>Fails (before or after the cloud has applied the batch) to prove a dropped connection never loses or duplicates data.</summary>
public sealed class FlakyTransport(ISyncTransport inner, bool failAfterDelivery) : ISyncTransport
{
    public int FailuresLeft { get; set; } = 1;
    public int Calls { get; private set; }

    public async Task<SyncAck> PushAsync(SyncBatch batch, CancellationToken ct)
    {
        Calls++;
        if (FailuresLeft > 0 && !failAfterDelivery)
        {
            FailuresLeft--;
            throw new SyncTransportException("network", "simulated outage", retryable: true);
        }
        var ack = await inner.PushAsync(batch, ct);
        if (FailuresLeft > 0)
        {
            FailuresLeft--;
            throw new SyncTransportException("network", "simulated: answer lost", retryable: true);
        }
        return ack;
    }
}

public static class HtmlForms
{
    private static readonly Regex Token = new("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"", RegexOptions.Compiled);

    /// <summary>GETs a page that holds a form to harvest its antiforgery token, then posts the fields to <paramref name="postUrl"/>.</summary>
    public static async Task<HttpResponseMessage> PostAsync(
        HttpClient client, string pageUrl, string postUrl, params (string Key, string Value)[] fields)
    {
        var html = await client.GetStringAsync(pageUrl);
        var match = Token.Match(html);
        if (!match.Success) throw new InvalidOperationException($"No antiforgery token on {pageUrl}");
        var form = fields.Select(f => new KeyValuePair<string, string>(f.Key, f.Value))
            .Append(new KeyValuePair<string, string>("__RequestVerificationToken", match.Groups[1].Value));
        return await client.PostAsync(postUrl, new FormUrlEncodedContent(form));
    }
}
