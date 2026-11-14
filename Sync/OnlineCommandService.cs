using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Sync;

/// <summary>
/// Asks the cloud for changes the owner made from the phone and applies them. It holds one request open ("long poll"), so a
/// change typed on the phone reaches the shop in a moment instead of at the next regular check-in. Like the push loop it never
/// reaches the cashier: a failure just waits and tries again, and selling does not depend on it.
/// </summary>
public sealed class OnlineCommandService(
    IServiceProvider services,
    OnlineSettingsStore store,
    CommandLedger ledger,
    IHttpClientFactory httpFactory,
    ILogger<OnlineCommandService> logger) : BackgroundService
{
    private static readonly TimeSpan IdleCheck = TimeSpan.FromSeconds(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var failureDelay = TimeSpan.Zero;
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = TimeSpan.Zero;
            try
            {
                var settings = store.Current;
                if (!settings.Enabled || !settings.IsConfigured)
                {
                    delay = IdleCheck;
                }
                else
                {
                    var ran = await PollOnceAsync(stoppingToken);
                    failureDelay = TimeSpan.Zero;
                    // A short wait setting (tests, or a cloud that answers at once) must not turn into a hot loop.
                    if (ran == 0 && settings.CommandWaitSeconds < 5) delay = TimeSpan.FromSeconds(2);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                if (ex is not (HttpRequestException or TaskCanceledException or CommandTransportException))
                    logger.LogError(ex, "Online command loop failed unexpectedly.");
                failureDelay = TimeSpan.FromSeconds(Math.Min(300, Math.Max(10, failureDelay.TotalSeconds * 2)));
                delay = failureDelay;
            }

            if (delay > TimeSpan.Zero)
            {
                try { await Task.Delay(delay, stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    /// <summary>Fetches whatever is waiting (holding the request open up to the configured wait), applies it, reports the outcomes. Returns how many commands ran.</summary>
    public async Task<int> PollOnceAsync(CancellationToken ct)
    {
        var settings = store.Current;
        if (!settings.Enabled || !settings.IsConfigured) return 0;

        var wait = Math.Clamp(settings.CommandWaitSeconds, 0, 55);
        using var http = httpFactory.CreateClient(OnlineSyncService.HttpClientName);
        http.BaseAddress = new Uri(settings.CloudUrl.TrimEnd('/') + "/");
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.DeviceToken);
        http.Timeout = TimeSpan.FromSeconds(wait + 20);

        using var response = await http.GetAsync($"api/sync/commands?wait={wait}", ct);
        if (response.StatusCode == HttpStatusCode.NoContent) return 0;
        if (!response.IsSuccessStatusCode)
            throw new CommandTransportException($"The cloud answered {(int)response.StatusCode}.");

        var batch = await response.Content.ReadFromJsonAsync<CommandBatchResponse>(SyncWire.Json, ct);
        if (batch is null || batch.Commands.Count == 0) return 0;

        foreach (var command in batch.Commands)
        {
            var result = await RunAsync(command, ct);
            await ReportAsync(http, command.Id, result, ct);
        }
        return batch.Commands.Count;
    }

    private async Task<CommandResult> RunAsync(CommandEnvelope command, CancellationToken ct)
    {
        // Already run before (the answer never reached the cloud): repeat the answer, do not repeat the change.
        if (ledger.TryGet(command.Id, out var known)) return known;

        CommandResult result;
        try
        {
            using var scope = services.CreateScope();
            var handler = scope.ServiceProvider.GetServices<IOnlineCommandHandler>()
                .FirstOrDefault(h => string.Equals(h.Type, command.Type, StringComparison.Ordinal));
            result = handler is null
                ? CommandResult.Reject(CommandErrorCodes.UnknownType,
                    "This version of NexPOS on the shop PC does not know this kind of change. Update NexPOS and try again.")
                : await handler.HandleAsync(command.Payload, new CommandContext(command.Id, command.RequestedBy), ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (JsonException)
        {
            result = CommandResult.Reject(CommandErrorCodes.BadPayload, "The request was not in a form the shop PC understands.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Applying online command {Id} ({Type}) failed.", command.Id, command.Type);
            result = CommandResult.Reject(CommandErrorCodes.ServerError, "The shop PC could not apply this change. Nothing was saved.");
        }

        ledger.Record(command.Id, result);
        return result;
    }

    private static async Task ReportAsync(HttpClient http, Guid id, CommandResult result, CancellationToken ct)
    {
        // A lost report is safe: the cloud re-delivers after a while and the ledger answers without re-applying.
        using var response = await http.PostAsJsonAsync($"api/sync/commands/{id}/result", result, SyncWire.Json, ct);
        if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NotFound)
            throw new CommandTransportException($"The cloud answered {(int)response.StatusCode} to a result.");
    }
}

public sealed class CommandTransportException(string message) : Exception(message);
