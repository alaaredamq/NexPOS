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

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var connectionString = AppDataPathProvider.GetConnectionString();
            SyncSchema schema;
            using (var scope = services.CreateScope())
                schema = SyncSchema.FromModel(scope.ServiceProvider.GetRequiredService<ErpDbContext>().Model);

            bool? installed = null;
            var lastPlanCheck = DateTime.MinValue;
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
                        using var http = httpFactory.CreateClient(HttpClientName);
                        http.BaseAddress = new Uri(settings.CloudUrl.TrimEnd('/') + "/");
                        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.DeviceToken);

                        var pusher = new SyncPusher(schema, connectionString, new HttpSyncTransport(http), status, time, null,
                            Math.Clamp(settings.BatchSize, 1, 2000));
                        var result = await pusher.RunOnceAsync(stoppingToken);

                        // Back off while failing so a down cloud costs nothing; snap back on the first success.
                        failureDelay = result.Ok ? TimeSpan.Zero : TimeSpan.FromSeconds(Math.Min(300, Math.Max(10, failureDelay.TotalSeconds * 2)));
                        wait = result.Ok ? interval : failureDelay;

                        var now = time.GetUtcNow().UtcDateTime;
                        if (now - lastPlanCheck > PlanCheckEvery)
                        {
                            await RefreshPlanAsync(http, stoppingToken);
                            lastPlanCheck = now;
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

        private void Prepare(string connectionString, SyncSchema schema, bool active)
        {
            using var connection = new SqliteConnection(connectionString);
            connection.Open();
            if (active) SyncTriggers.Install(connection, schema);
            else SyncTriggers.Uninstall(connection, schema);
        }

        /// <summary>Learns the subscription state (and the site's name) for the Online screen. Failures are not interesting: the push already reports them.</summary>
        private async Task RefreshPlanAsync(HttpClient http, CancellationToken ct)
        {
            try
            {
                var ping = await http.GetFromJsonAsync<SyncPing>("api/sync/ping", SyncWire.Json, ct);
                if (ping is not null) status.SetPlan(ping.SubscriptionStatus, ping.ExpiresAtUtc);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or NotSupportedException)
            {
                if (ct.IsCancellationRequested) throw;
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
            services.AddSingleton<SyncStatus>();
            services.AddSingleton<OnlineSignal>();
            services.TryAddSingleton(TimeProvider.System);
            services.AddHttpClient(OnlineSyncService.HttpClientName);
            services.AddSingleton<OnlineConnection>();
            services.AddSingleton<IOnlineConnection>(sp => sp.GetRequiredService<OnlineConnection>());
            services.AddHostedService<OnlineSyncService>();
            return services;
        }
    }
}
