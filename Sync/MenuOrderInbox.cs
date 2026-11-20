using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Sync;

/// <summary>One dish of an online order, as the cloud priced it. <see cref="PosProductId"/> is this shop's product, when the dish came from the POS.</summary>
public sealed record TillOrderLine(string Name, int Quantity, decimal UnitPrice, string Currency, int? PosProductId, string Total);

/// <summary>An order a guest sent from the online menu to this branch's till.</summary>
public sealed record TillOrder(
    int Id, int Number, string Status, string? Branch, string? OrderType, string Name, string? Phone, string? Place, string? Note,
    DateTime CreatedAtUtc, IReadOnlyList<TillOrderLine> Lines, string Total);

/// <summary>
/// Orders guests sent from the online menu to this shop's till (the owner chose "send to the POS"). They live in the cloud; the till asks for
/// the open ones when the cashier looks, and tells the cloud when one is taken, ready, done or turned down, so the guest's phone follows.
/// Nothing is stored here: with the shop offline there are no online orders to show anyway.
/// </summary>
public sealed class MenuOrderInbox(OnlineSettingsStore store, IHttpClientFactory httpFactory)
{
    private sealed record OrderList(IReadOnlyList<TillOrder> Orders);
    private sealed record Move(string Status);

    /// <summary>Whether this PC is connected to Online at all (otherwise there is nothing to ask).</summary>
    public bool Connected => store.Current.Enabled && store.Current.IsConfigured;

    private HttpClient Client()
    {
        var settings = store.Current;
        var http = httpFactory.CreateClient(OnlineSyncService.HttpClientName);
        http.BaseAddress = new Uri(settings.CloudUrl.TrimEnd('/') + "/");
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.DeviceToken);
        http.Timeout = TimeSpan.FromSeconds(15);
        return http;
    }

    /// <summary>The open orders for this branch, oldest first. Throws <see cref="HttpRequestException"/> when the cloud cannot be reached.</summary>
    public async Task<IReadOnlyList<TillOrder>> OpenAsync(CancellationToken ct)
    {
        if (!Connected) return [];
        using var http = Client();
        var list = await http.GetFromJsonAsync<OrderList>("api/sync/menu-orders", SyncWire.Json, ct);
        return list?.Orders ?? [];
    }

    /// <summary>Moves an order on (accepted, ready, done, rejected). False when the cloud does not know it (taken back, another branch's).</summary>
    public async Task<bool> MoveAsync(int id, string status, CancellationToken ct)
    {
        if (!Connected) return false;
        using var http = Client();
        using var response = await http.PutAsJsonAsync($"api/sync/menu-orders/{id}", new Move(status), SyncWire.Json, ct);
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest) return false;
        response.EnsureSuccessStatusCode();
        return true;
    }
}
