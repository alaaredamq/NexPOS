using System.Text.Json;
using Cloud.Tests.Support;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sync;
using Xunit;

namespace Cloud.Tests;

/// <summary>
/// The till-side wiring exactly as Web registers it (settings store, connection service, background loop, HTTP), talking to the
/// real cloud app in memory. These are the "owner pastes a link and it works" scenarios.
/// </summary>
public class OnlineSyncServiceTests : IClassFixture<CloudFactory>
{
    private readonly CloudFactory _cloud;

    public OnlineSyncServiceTests(CloudFactory cloud) => _cloud = cloud;

    /// <summary>Sends requests for "localhost" to the in-memory cloud and fails every other host like a dead network would.</summary>
    private sealed class RoutingHandler(HttpMessageHandler cloud) : DelegatingHandler(cloud)
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => request.RequestUri!.Host == "localhost"
                ? base.SendAsync(request, ct)
                : throw new HttpRequestException("No such host is known.");
    }

    /// <summary>A till with its own database and settings file, running the background service.</summary>
    private sealed class TillRig : IAsyncDisposable
    {
        public required TillDb Till { get; init; }
        public required string SettingsPath { get; init; }
        public required ServiceProvider Provider { get; init; }
        public required OnlineSyncService Service { get; init; }
        public IOnlineConnection Connection => Provider.GetRequiredService<IOnlineConnection>();
        public OnlineSettingsStore Store => Provider.GetRequiredService<OnlineSettingsStore>();
        public SyncStatus Status => Provider.GetRequiredService<SyncStatus>();
        private readonly string? _previousDbPath = Environment.GetEnvironmentVariable(AppDataPathProvider.DatabasePathEnvVar);

        public static async Task<TillRig> StartAsync(CloudFactory cloud, TillDb? till = null, string? settingsPath = null,
            Action<OnlineSettingsStore>? preset = null, int intervalSeconds = 5, bool sealOnConnect = false)
        {
            till ??= new TillDb();
            settingsPath ??= Path.Combine(Path.GetTempPath(), "nexpos-rig-" + Guid.NewGuid().ToString("N"), "online.json");

            // The service reads the database location the way the real app does.
            Environment.SetEnvironmentVariable(AppDataPathProvider.DatabasePathEnvVar, till.Path);

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<ErpDbContext>(o => o.UseSqlite(till.ConnectionString));
            services.AddOnlineSync(new Microsoft.Extensions.Configuration.ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Online:IntervalSeconds"] = intervalSeconds.ToString(),
                    // Real tills seal every new connection; most scenarios here are about the sync itself, so the rig opts out.
                    ["Online:SealOnConnect"] = sealOnConnect.ToString(),
                }).Build(), settingsPath);
            services.AddHttpClient(OnlineSyncService.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => new RoutingHandler(cloud.Server.CreateHandler()));
            var provider = services.BuildServiceProvider();

            var store = provider.GetRequiredService<OnlineSettingsStore>();
            preset?.Invoke(store);

            var service = provider.GetServices<IHostedService>().OfType<OnlineSyncService>().Single();
            await service.StartAsync(default);
            return new TillRig { Till = till, SettingsPath = settingsPath, Provider = provider, Service = service };
        }

        public async ValueTask DisposeAsync()
        {
            await Service.StopAsync(default);
            await Provider.DisposeAsync();
            Environment.SetEnvironmentVariable(AppDataPathProvider.DatabasePathEnvVar, _previousDbPath);
        }

        public long TriggerCount() => SqlHelper.Scalar<long>(Till.Path, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'trigger' AND name LIKE '__sync_%'");
    }

    private static async Task<bool> WaitForAsync(Func<bool> condition, int seconds = 20)
    {
        // A mirror file appears before its schema is finished; reading it mid-creation is "not yet", not a failure.
        bool Check()
        {
            try { return condition(); }
            catch (Microsoft.Data.Sqlite.SqliteException) { return false; }
        }

        var until = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < until)
        {
            if (Check()) return true;
            await Task.Delay(100);
        }
        return Check();
    }

    private bool InMirror(SeededSite site, string firstName)
        => _cloud.Mirrors.Exists(site.SiteId)
           && SqlHelper.Scalar<long>(_cloud.Mirrors.PathFor(site.SiteId), $"SELECT COUNT(*) FROM Customers WHERE FirstName = '{firstName}'") == 1;

    [Fact]
    public async Task Pasting_the_link_starts_syncing_at_once_without_a_restart()
    {
        var site = await _cloud.SeedSiteAsync(clientName: "Link Shop");
        await using var rig = await TillRig.StartAsync(_cloud);
        await rig.Till.AddCustomerAsync("Before");
        Assert.False(rig.Connection.GetStatus().Enabled);
        Assert.Equal(0, rig.TriggerCount());

        var status = await rig.Connection.ConnectAsync(await _cloud.IssueLinkAsync(site.SiteId), default);

        Assert.True(status.Enabled);
        Assert.Equal("Main till", status.SiteName);
        Assert.Equal("Link Shop", status.ClientName);
        Assert.True(await WaitForAsync(() => InMirror(site, "Before")), "the existing shop should be uploaded");

        await rig.Till.AddCustomerAsync("After");
        Assert.True(await WaitForAsync(() => InMirror(site, "After")), "later changes should follow");
        Assert.True(await WaitForAsync(() => rig.Connection.GetStatus().LastSuccessUtc is not null));
    }

    [Fact]
    public async Task A_shop_with_sealing_on_uploads_locked_rows_with_the_key_beside_the_settings()
    {
        var site = await _cloud.SeedSiteAsync();
        await using var rig = await TillRig.StartAsync(_cloud, preset: s => s.Save(s.Current with { SealData = true, SealConfirmedAtUtc = DateTime.UtcNow }));
        await rig.Till.AddCustomerAsync("Zainab");
        await rig.Till.AddProductAsync("Olive oil 1L", "6001");
        rig.Provider.GetRequiredService<SealKeyStore>().LoadOrCreate(); // the key the owner kept the slip for

        await rig.Connection.ConnectAsync(await _cloud.IssueLinkAsync(site.SiteId), default);

        Assert.True(await WaitForAsync(() => rig.Connection.GetStatus().LastSuccessUtc is not null), "the first upload should succeed");
        var mirror = _cloud.Mirrors.PathFor(site.SiteId);
        Assert.Equal(0L, SqlHelper.Scalar<long>(mirror, "SELECT COUNT(*) FROM Customers"));
        Assert.Equal(1L, SqlHelper.Scalar<long>(mirror, "SELECT COUNT(*) FROM Products WHERE Name = 'Olive oil 1L'"));
        Assert.True(SqlHelper.Scalar<long>(mirror, "SELECT COUNT(*) FROM SealedRows WHERE TableName = 'Customers'") >= 1);
        Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(rig.SettingsPath)!, "online.key")));
    }

    [Fact]
    public async Task A_shop_with_sealing_on_and_an_unreadable_key_uploads_nothing_instead_of_falling_back_to_readable()
    {
        var site = await _cloud.SeedSiteAsync();
        var folder = Path.Combine(Path.GetTempPath(), "nexpos-rig-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        byte[] damaged = [9, 9, 9];
        File.WriteAllBytes(Path.Combine(folder, "online.key"), damaged);

        await using var rig = await TillRig.StartAsync(_cloud, settingsPath: Path.Combine(folder, "online.json"),
            preset: s => s.Save(s.Current with { SealData = true, SealConfirmedAtUtc = DateTime.UtcNow }));
        await rig.Till.AddCustomerAsync("Zainab");

        await rig.Connection.ConnectAsync(await _cloud.IssueLinkAsync(site.SiteId), default);

        Assert.True(await WaitForAsync(() => rig.Status.Snapshot().LastErrorCode == "seal_key"), "the key problem should be reported");
        Assert.Equal(damaged, File.ReadAllBytes(Path.Combine(folder, "online.key")));
        if (_cloud.Mirrors.Exists(site.SiteId))
        {
            var mirror = _cloud.Mirrors.PathFor(site.SiteId);
            Assert.Equal(0L, SqlHelper.Scalar<long>(mirror, "SELECT COUNT(*) FROM Customers"));
            Assert.Equal(0L, SqlHelper.Scalar<long>(mirror, "SELECT COUNT(*) FROM SealedRows"));
        }
    }

    [Fact]
    public async Task A_new_connection_uploads_nothing_until_the_owner_keeps_the_slip_and_then_uploads_sealed()
    {
        var site = await _cloud.SeedSiteAsync();
        await using var rig = await TillRig.StartAsync(_cloud, sealOnConnect: true);
        await rig.Till.AddCustomerAsync("Zainab");
        await rig.Till.AddProductAsync("Olive oil 1L", "6001");

        await rig.Connection.ConnectAsync(await _cloud.IssueLinkAsync(site.SiteId), default);

        Assert.Equal(SealingStates.NeedsSlip, rig.Connection.GetStatus().SealingState);
        await Task.Delay(1500); // plenty of loop turns: it must stay quiet
        Assert.Null(rig.Connection.GetStatus().LastSuccessUtc);
        Assert.False(_cloud.Mirrors.Exists(site.SiteId) && SqlHelper.Scalar<long>(_cloud.Mirrors.PathFor(site.SiteId), "SELECT COUNT(*) FROM Products WHERE Name = 'Olive oil 1L'") > 0,
            "nothing may leave the shop before the slip is kept");

        var sealing = rig.Provider.GetRequiredService<IOnlineSealing>();
        var slip = sealing.GetSlip();
        sealing.Confirm();

        Assert.True(await WaitForAsync(() => rig.Connection.GetStatus().LastSuccessUtc is not null), "the first upload should follow the confirmation");
        var mirror = _cloud.Mirrors.PathFor(site.SiteId);
        Assert.Equal(1L, SqlHelper.Scalar<long>(mirror, "SELECT COUNT(*) FROM Products WHERE Name = 'Olive oil 1L'"));
        Assert.True(SqlHelper.Scalar<long>(mirror, "SELECT COUNT(*) FROM SealedRows WHERE TableName = 'Customers'") >= 1);
        Assert.DoesNotContain("Zainab", SqlHelper.RawText(mirror));

        var status = rig.Connection.GetStatus();
        Assert.Equal(SealingStates.On, status.SealingState);
        Assert.Equal(slip.KeyId, status.SealKeyId);
    }

    [Fact]
    public async Task Turning_privacy_on_for_a_shop_that_uploaded_readable_replaces_the_cloud_copy_and_leaves_no_trace()
    {
        var site = await _cloud.SeedSiteAsync();
        await using var rig = await TillRig.StartAsync(_cloud);          // a shop connected before sealing existed
        await rig.Till.AddCustomerAsync("Zainab");
        await rig.Connection.ConnectAsync(await _cloud.IssueLinkAsync(site.SiteId), default);
        Assert.True(await WaitForAsync(() => InMirror(site, "Zainab")), "it starts readable");
        var mirror = _cloud.Mirrors.PathFor(site.SiteId);
        Assert.Equal(SealingStates.Off, rig.Connection.GetStatus().SealingState);

        var sealing = rig.Provider.GetRequiredService<IOnlineSealing>();
        sealing.GetSlip();
        sealing.Confirm();

        Assert.True(await WaitForAsync(() => SqlHelper.Scalar<long>(mirror, "SELECT COUNT(*) FROM SealedRows WHERE TableName = 'Customers'") >= 1),
            "the whole shop should be uploaded again, sealed");
        Assert.True(await WaitForAsync(() => SqlHelper.Scalar<long>(mirror, "SELECT COUNT(*) FROM Customers WHERE FirstName = 'Zainab'") == 0),
            "the readable copy is wiped");
        Assert.DoesNotContain("Zainab", SqlHelper.RawText(mirror)); // not in free pages, not in the old log
        Assert.Equal(SealingStates.On, rig.Connection.GetStatus().SealingState);
    }

    [Fact]
    public async Task A_confirmed_shop_whose_key_is_missing_uploads_nothing_until_the_key_is_restored()
    {
        var site = await _cloud.SeedSiteAsync();
        await using var rig = await TillRig.StartAsync(_cloud, preset: s => s.Save(s.Current with { SealData = true, SealConfirmedAtUtc = DateTime.UtcNow }));
        await rig.Till.AddCustomerAsync("Zainab");
        var keys = rig.Provider.GetRequiredService<SealKeyStore>();
        Assert.False(keys.Exists); // a restore on a new PC: the settings came along, the key did not

        await rig.Connection.ConnectAsync(await _cloud.IssueLinkAsync(site.SiteId), default);

        Assert.True(await WaitForAsync(() => rig.Status.Snapshot().LastErrorCode == "seal_key_missing"));
        Assert.Equal(SealingStates.KeyMissing, rig.Connection.GetStatus().SealingState);
        Assert.False(keys.Exists, "a new key must never be made behind the owner's back");
        Assert.False(_cloud.Mirrors.Exists(site.SiteId) && SqlHelper.Scalar<long>(_cloud.Mirrors.PathFor(site.SiteId), "SELECT COUNT(*) FROM SealedRows") > 0);

        var slipKey = RowSealer.NewKey();
        Assert.Equal(RestoreOutcome.Restored, rig.Provider.GetRequiredService<IOnlineSealing>().RestoreKey(RecoveryCode.Format(slipKey)));

        Assert.True(await WaitForAsync(() => rig.Connection.GetStatus().LastSuccessUtc is not null), "uploading resumes once the key is back");
        var mirror = _cloud.Mirrors.PathFor(site.SiteId);
        Assert.Equal(new RowSealer(slipKey).KeyId, SqlHelper.Scalar<string>(mirror, "SELECT KeyId FROM SealedRows WHERE TableName = 'Customers' LIMIT 1"));
        Assert.Equal(SealingStates.On, rig.Connection.GetStatus().SealingState);
    }

    [Fact]
    public async Task The_connection_is_remembered_across_a_restart()
    {
        var site = await _cloud.SeedSiteAsync();
        using var till = new TillDb();
        string settingsPath;

        await using (var first = await TillRig.StartAsync(_cloud, till))
        {
            settingsPath = first.SettingsPath;
            await first.Connection.ConnectAsync(await _cloud.IssueLinkAsync(site.SiteId), default);
            Assert.True(await WaitForAsync(() => first.Connection.GetStatus().LastSuccessUtc is not null));
        }

        await using var second = await TillRig.StartAsync(_cloud, till, settingsPath);
        Assert.True(second.Connection.GetStatus().Enabled, "no need to paste the link again");

        await till.AddCustomerAsync("AfterRestart");
        Assert.True(await WaitForAsync(() => InMirror(site, "AfterRestart")));
    }

    [Fact]
    public async Task Disconnecting_stops_the_sync_and_removes_the_capture_from_the_database()
    {
        var site = await _cloud.SeedSiteAsync();
        await using var rig = await TillRig.StartAsync(_cloud);
        await rig.Connection.ConnectAsync(await _cloud.IssueLinkAsync(site.SiteId), default);
        Assert.True(await WaitForAsync(() => rig.Connection.GetStatus().LastSuccessUtc is not null));
        Assert.True(rig.TriggerCount() > 0);

        var status = await rig.Connection.DisconnectAsync(default);

        Assert.False(status.Enabled);
        Assert.Null(status.SiteName);
        Assert.True(await WaitForAsync(() => rig.TriggerCount() == 0), "triggers should be removed");
        Assert.Equal(0L, SqlHelper.Scalar<long>(rig.Till.Path, "SELECT COUNT(*) FROM \"__SyncLog\""));
        Assert.False(new OnlineSettingsStore(rig.SettingsPath, new OnlineSettings()).Current.IsConfigured, "the credential is forgotten");

        await rig.Till.AddCustomerAsync("WhileOff");
        await Task.Delay(1500);
        Assert.False(InMirror(site, "WhileOff"));
    }

    [Fact]
    public async Task Reconnecting_uploads_what_changed_while_disconnected()
    {
        var site = await _cloud.SeedSiteAsync();
        await using var rig = await TillRig.StartAsync(_cloud);
        await rig.Connection.ConnectAsync(await _cloud.IssueLinkAsync(site.SiteId), default);
        Assert.True(await WaitForAsync(() => rig.Connection.GetStatus().LastSuccessUtc is not null));
        await rig.Connection.DisconnectAsync(default);
        Assert.True(await WaitForAsync(() => rig.TriggerCount() == 0));

        await rig.Till.AddCustomerAsync("Meanwhile");
        await rig.Connection.ConnectAsync(await _cloud.IssueLinkAsync(site.SiteId), default);

        Assert.True(await WaitForAsync(() => InMirror(site, "Meanwhile")), "a reconnect starts from a full snapshot");
    }

    [Fact]
    public async Task Sync_now_does_not_wait_for_the_interval()
    {
        var site = await _cloud.SeedSiteAsync();
        await using var rig = await TillRig.StartAsync(_cloud, intervalSeconds: 3600);
        await rig.Connection.ConnectAsync(await _cloud.IssueLinkAsync(site.SiteId), default);
        Assert.True(await WaitForAsync(() => rig.Connection.GetStatus().LastSuccessUtc is not null));

        await rig.Till.AddCustomerAsync("Pressed");
        rig.Connection.SyncNow();

        Assert.True(await WaitForAsync(() => InMirror(site, "Pressed"), seconds: 10));
    }

    [Fact]
    public async Task The_status_shows_the_plan_and_never_the_token()
    {
        var site = await _cloud.SeedSiteAsync();
        await using var rig = await TillRig.StartAsync(_cloud);
        await rig.Connection.ConnectAsync(await _cloud.IssueLinkAsync(site.SiteId), default);

        Assert.True(await WaitForAsync(() => rig.Connection.GetStatus().SubscriptionStatus == "Active"));
        var json = JsonSerializer.Serialize(rig.Connection.GetStatus());
        Assert.DoesNotContain(rig.Store.Current.DeviceToken, json);
        Assert.False(string.IsNullOrEmpty(rig.Store.Current.DeviceToken));
    }

    [Fact]
    public async Task Without_a_subscription_the_till_says_so_and_keeps_its_changes()
    {
        var site = await _cloud.SeedSiteAsync(subscriptionMonths: 0);
        await using var rig = await TillRig.StartAsync(_cloud);
        await rig.Till.AddCustomerAsync("Queued");
        await rig.Connection.ConnectAsync(await _cloud.IssueLinkAsync(site.SiteId), default);

        Assert.True(await WaitForAsync(() => rig.Connection.GetStatus().LastErrorCode == SyncErrorCodes.SubscriptionInactive));
        var status = rig.Connection.GetStatus();
        Assert.True(status.PendingChanges > 0);
        Assert.True(await WaitForAsync(() => rig.Connection.GetStatus().SubscriptionStatus == "None"));
        Assert.True(status.Enabled, "still connected: it resumes as soon as a subscription exists");
    }

    [Fact]
    public async Task A_credential_the_provider_revoked_is_reported_not_retried_loudly()
    {
        var site = await _cloud.SeedSiteAsync();
        await using var rig = await TillRig.StartAsync(_cloud);
        await rig.Connection.ConnectAsync(await _cloud.IssueLinkAsync(site.SiteId), default);
        Assert.True(await WaitForAsync(() => rig.Connection.GetStatus().LastSuccessUtc is not null));

        await _cloud.WithControlDbAsync(async db =>
        {
            foreach (var t in db.DeviceTokens.Where(t => t.SiteId == site.SiteId)) t.RevokedAt = DateTime.UtcNow;
            return await db.SaveChangesAsync();
        });
        await rig.Till.AddCustomerAsync("Orphan");
        rig.Connection.SyncNow();

        Assert.True(await WaitForAsync(() => rig.Connection.GetStatus().LastErrorCode == SyncErrorCodes.Unauthorized));
        Assert.False(InMirror(site, "Orphan"));
    }

    [Fact]
    public async Task A_till_that_is_not_enrolled_adds_nothing_to_its_database_and_makes_no_request()
    {
        using var till = new TillDb();
        var before = SqlHelper.Scalar<long>(till.Path, "SELECT COUNT(*) FROM sqlite_master");

        await using (var rig = await TillRig.StartAsync(_cloud, till))
        {
            await Task.Delay(500);
            Assert.False(rig.Connection.GetStatus().Enabled);
            Assert.False(File.Exists(rig.SettingsPath), "nothing is written until the owner connects");
        }

        Assert.Equal(before, SqlHelper.Scalar<long>(till.Path, "SELECT COUNT(*) FROM sqlite_master"));
    }

    [Fact]
    public async Task A_hand_written_settings_file_still_connects_the_old_way()
    {
        var site = await _cloud.SeedSiteAsync();
        await using var rig = await TillRig.StartAsync(_cloud, preset: store => store.Save(new OnlineSettings
        {
            Enabled = true, CloudUrl = "http://localhost", DeviceToken = site.Token,
        }));
        await rig.Till.AddCustomerAsync("Manual");

        Assert.True(await WaitForAsync(() => InMirror(site, "Manual")));
    }

    [Fact]
    public async Task Enabled_without_a_url_or_token_stays_off_instead_of_failing()
    {
        await using var rig = await TillRig.StartAsync(_cloud, preset: store => store.Save(new OnlineSettings { Enabled = true }));
        await Task.Delay(500);
        Assert.False(rig.Connection.GetStatus().Enabled);
        Assert.Equal(0, rig.TriggerCount());
    }

    [Theory]
    [InlineData("not a link at all", "enroll_link_invalid")]
    [InlineData("http://cloud.example.com/enroll/AAAA-AAAA-AAAA", "cloud_insecure_url")]
    [InlineData("http://localhost/enroll/AAAA-AAAA-AAAA", "enroll_code_invalid")]          // reaches the cloud, code unknown
    [InlineData("https://cloud.example.com/enroll/AAAA-AAAA-AAAA", "cloud_unreachable")]   // host that does not answer
    public async Task A_refused_link_says_why_and_changes_nothing(string link, string expectedCode)
    {
        using var factory = new CloudFactory(); // its own throttle, so repeated bad codes here cannot affect other tests
        await using var rig = await TillRig.StartAsync(factory);

        var failure = await Assert.ThrowsAsync<OnlineConnectException>(() => rig.Connection.ConnectAsync(link, default));

        Assert.Equal(expectedCode, failure.Code);
        Assert.False(rig.Connection.GetStatus().Enabled);
        Assert.False(File.Exists(rig.SettingsPath));
        Assert.Equal(0, rig.TriggerCount());
    }

    [Fact]
    public async Task A_link_that_was_already_used_is_refused_with_its_own_code()
    {
        var site = await _cloud.SeedSiteAsync();
        var link = await _cloud.IssueLinkAsync(site.SiteId);
        await using var first = await TillRig.StartAsync(_cloud);
        await first.Connection.ConnectAsync(link, default);

        await using var second = await TillRig.StartAsync(_cloud);
        var failure = await Assert.ThrowsAsync<OnlineConnectException>(() => second.Connection.ConnectAsync(link, default));

        Assert.Equal(SyncErrorCodes.EnrollCodeUsed, failure.Code);
        Assert.False(second.Connection.GetStatus().Enabled);
    }

    [Fact]
    public async Task The_whole_whatsapp_message_can_be_pasted()
    {
        var site = await _cloud.SeedSiteAsync();
        var link = await _cloud.IssueLinkAsync(site.SiteId);
        await using var rig = await TillRig.StartAsync(_cloud);

        var status = await rig.Connection.ConnectAsync($"NexPOS Online: open NexPOS > Settings > Online, paste this link.\nلتفعيل NexPOS Online\n{link}\n", default);

        Assert.True(status.Enabled);
    }
}
