using System.Net.Http.Headers;
using System.Net.Http.Json;
using Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sync;

namespace Sync
{
    /// <summary>
    /// Keeps the till's change log flowing to the cloud. Starts after the host has migrated the database (migrations run
    /// in Program.cs before the host runs). It re-reads the settings every cycle, so connecting or disconnecting from the
    /// Online screen takes effect at once, with no restart: connecting installs the capture triggers and uploads the shop;
    /// disconnecting removes them.
    /// A failure never reaches the cashier: selling does not depend on this loop, and a paused sync just queues changes
    /// locally until the cloud is reachable and the subscription is active.
    /// </summary>
    public sealed class OnlineSyncService(
        IServiceProvider services,
        OnlineSettingsStore store,
        SyncStatus status,
        OnlineSignal signal,
        IHttpClientFactory httpFactory,
        TimeProvider time,
        ILogger<OnlineSyncService> logger) : BackgroundService
    {
        public const string HttpClientName = "NexPOS.Online";
        private static readonly TimeSpan PlanCheckEvery = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan IdleCheck = TimeSpan.FromMinutes(1);
        private static readonly TimeSpan MenuCheckEvery = TimeSpan.FromMinutes(1);

        // From the last check-in: whether the owner linked this branch to the online menu, and which product list the cloud holds for it.
        private bool _menuLinked;
        private string? _menuHash;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var connectionString = AppDataPathProvider.GetConnectionString();
            SyncSchema schema;
            using (var scope = services.CreateScope())
                schema = SyncSchema.FromModel(scope.ServiceProvider.GetRequiredService<ErpDbContext>().Model);

            bool? installed = null;
            var lastPlanCheck = DateTime.MinValue;
            var lastMenuCheck = DateTime.MinValue;
            var failureDelay = TimeSpan.Zero;

            while (!stoppingToken.IsCancellationRequested)
            {
                var settings = store.Current;
                var active = settings.Enabled && settings.IsConfigured;
                var interval = TimeSpan.FromSeconds(Math.Clamp(settings.IntervalSeconds, 5, 3600));
                var wait = interval;

                try
                {
                    if (active != installed)
                    {
                        Prepare(connectionString, schema, active);
                        installed = active;
                        lastPlanCheck = DateTime.MinValue;
                        failureDelay = TimeSpan.Zero;
                    }
                    status.Enabled = active;

                    if (!active)
                    {
                        wait = IdleCheck;
                    }
                    else
                    {
                        settings = EnsureDataKey(connectionString, schema, settings);
                        using var http = httpFactory.CreateClient(HttpClientName);
                        http.BaseAddress = new Uri(settings.CloudUrl.TrimEnd('/') + "/");
                        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.DeviceToken);

                        var pusher = new SyncPusher(schema, connectionString, new HttpSyncTransport(http), status, time, null,
                            Math.Clamp(settings.BatchSize, 1, 2000), DataKey.FromBase64(settings.DataKeyBase64).Cipher());
                        var result = await pusher.RunOnceAsync(stoppingToken);

                        // Back off while failing so a down cloud costs nothing; snap back on the first success.
                        failureDelay = result.Ok ? TimeSpan.Zero : TimeSpan.FromSeconds(Math.Min(300, Math.Max(10, failureDelay.TotalSeconds * 2)));
                        wait = result.Ok ? interval : failureDelay;

                        var now = time.GetUtcNow().UtcDateTime;
                        // The online-menu screen asks for a fresh look right away (it was just linked, or its sections changed).
                        var nudged = status.TakeMenuNudge();
                        if (nudged || now - lastPlanCheck > PlanCheckEvery)
                        {
                            await RefreshPlanAsync(http, stoppingToken);
                            lastPlanCheck = now;
                        }

                        if (_menuLinked && result.Ok && (nudged || now - lastMenuCheck > MenuCheckEvery))
                        {
                            await PublishMenuAsync(http, connectionString, stoppingToken);
                            lastMenuCheck = now;
                        }
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Online sync run failed unexpectedly.");
                    failureDelay = TimeSpan.FromSeconds(Math.Min(300, Math.Max(10, failureDelay.TotalSeconds * 2)));
                    wait = failureDelay;
                }

                try { await signal.WaitAsync(wait, stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }

        /// <summary>
        /// A till connected before end-to-end encryption existed has no key yet. Make one and re-send the whole shop encrypted: the first
        /// batch of a snapshot wipes the cloud copy, so the old readable rows are replaced rather than left behind.
        /// </summary>
        private OnlineSettings EnsureDataKey(string connectionString, SyncSchema schema, OnlineSettings settings)
        {
            if (!string.IsNullOrEmpty(settings.DataKeyBase64)) return settings;

            var updated = settings with { DataKeyBase64 = DataKey.Generate().ToBase64() };
            store.Save(updated);
            using var connection = new SqliteConnection(connectionString);
            connection.Open();
            SyncTriggers.EnsureTables(connection);
            SyncTriggers.Seed(connection, schema);
            logger.LogInformation("Created the shop's encryption key and queued a full encrypted snapshot.");
            return updated;
        }

        private void Prepare(string connectionString, SyncSchema schema, bool active)
        {
            using var connection = new SqliteConnection(connectionString);
            connection.Open();
            if (active) SyncTriggers.Install(connection, schema);
            else SyncTriggers.Uninstall(connection, schema);
        }

        /// <summary>
        /// Learns the subscription state (and the site's name) for the Online screen, and hands the cloud the shop's signed licence key so it knows the plan.
        /// Failures are not interesting: the push already reports them.
        /// </summary>
        private async Task RefreshPlanAsync(HttpClient http, CancellationToken ct)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, "api/sync/ping");
                if (services.GetService<IShopLicenseSource>()?.LicenseKey is { Length: > 0 } key) request.Headers.TryAddWithoutValidation(SyncHeaders.License, key);
                using var response = await http.SendAsync(request, ct);
                response.EnsureSuccessStatusCode();
                var ping = await response.Content.ReadFromJsonAsync<SyncPing>(SyncWire.Json, ct);
                if (ping is not null)
                {
                    status.SetPlan(ping.SubscriptionStatus, ping.ExpiresAtUtc);
                    _menuLinked = ping.MenuLinked;
                    _menuHash = ping.MenuCatalogHash;
                    status.SetMenu(ping.MenuLinked, ping.MenuUrl);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or NotSupportedException)
            {
                if (ct.IsCancellationRequested) throw;
            }
        }

        /// <summary>Keeps the online menu in step with the POS products (see <see cref="MenuCatalog"/>). A failure waits for the next minute.</summary>
        private async Task PublishMenuAsync(HttpClient http, string connectionString, CancellationToken ct)
        {
            try
            {
                if (await MenuCatalog.PublishIfChangedAsync(http, connectionString, _menuHash, ct) is { } sent)
                {
                    if (sent != _menuHash) status.MenuSent(time.GetUtcNow().UtcDateTime);
                    _menuHash = sent;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or SqliteException)
            {
                if (ct.IsCancellationRequested) throw;
                logger.LogWarning(ex, "Could not update the online menu; will try again.");
            }
        }
    }
}

// In the DI namespace by convention, so `services.AddOnlineSync(...)` is found without an extra using.
namespace Microsoft.Extensions.DependencyInjection
{
    public static class OnlineSyncServiceCollectionExtensions
    {
        /// <param name="settingsFilePath">Where the Online screen keeps its settings (see <see cref="AppDataPathProvider.GetOnlineSettingsFilePath"/>).</param>
        public static IServiceCollection AddOnlineSync(this IServiceCollection services, IConfiguration configuration, string settingsFilePath)
        {
            var defaults = configuration.GetSection(OnlineSettings.SectionName).Get<OnlineSettings>() ?? new OnlineSettings();
            services.AddSingleton(new OnlineSettingsStore(settingsFilePath, defaults));
            services.AddSingleton(OnlineServer.From(configuration[OnlineServer.ConfigKey]));
            services.AddSingleton<SyncStatus>();
            services.AddSingleton<OnlineSignal>();
            services.TryAddSingleton(TimeProvider.System);
            services.AddHttpClient(OnlineSyncService.HttpClientName);
            services.AddSingleton<OnlineConnection>();
            services.AddSingleton<IOnlineConnection>(sp => sp.GetRequiredService<OnlineConnection>());
            services.AddSingleton<MenuOrderInbox>();
            services.AddSingleton(new CommandLedger(
                Path.Combine(Path.GetDirectoryName(Path.GetFullPath(settingsFilePath))!, "online-commands.json")));
            services.AddHostedService<OnlineSyncService>();
            services.AddHostedService<OnlineCommandService>();
            return services;
        }
    }
}
