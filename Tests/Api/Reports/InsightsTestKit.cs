using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Tests.Api.Infrastructure;
using Xunit;
using static Tests.Api.Pos.PosTestKit;

namespace Tests.Api.Reports;

/// <summary>Makes real sales through the till for the dashboard and report tests (the same path the shop uses), on the licence-enabled host.</summary>
internal static class InsightsTestKit
{
    public const int Usd = 1, Cash = 1, Card = 2;

    internal sealed record Sale(int OrderId, int? ReceiptId, decimal Total);

    /// <summary>The first piece-sold product (ids 37-60 are not used by the POS tests) with room for a few more sales.</summary>
    private static async Task<int> PieceProduct(HttpClient browser)
    {
        for (var id = 37; id <= 60; id++)
        {
            var response = await browser.GetAsync($"/api/v1/pos/products/{id}");
            if (response.StatusCode != HttpStatusCode.OK) continue;
            var product = await Body(response);
            if (product.GetProperty("unitKind").GetString() == "count" && !product.GetProperty("isPack").GetBoolean() && product.GetProperty("stockQuantity").GetDecimal() >= 12)
                return id;
        }

        throw new InvalidOperationException("The demo database has no piece product with enough stock between ids 37 and 60.");
    }

    public static async Task<Sale> Sell(HttpClient browser, int quantity = 2, int paymentMethodId = Cash)
    {
        var product = await PieceProduct(browser);
        var cart = Cart(Usd, [Line(product, quantity)]);
        var total = (await Price(browser, cart)).GetProperty("total").GetDecimal();
        var response = await browser.SendAsync(Json(HttpMethod.Post, "/api/v1/pos/sales/checkout", Checkout(cart, total, paymentMethodId)));
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, text);
        var sale = JsonDocument.Parse(text).RootElement;
        return new Sale(
            sale.GetProperty("orderId").GetInt32(),
            sale.GetProperty("receiptId").ValueKind == JsonValueKind.Null ? null : sale.GetProperty("receiptId").GetInt32(),
            sale.GetProperty("total").GetDecimal());
    }

    public static async Task<JsonElement> Ok(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{(int)response.StatusCode}: {text}");
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    public static string Today => DateTime.Now.ToString("yyyy-MM-dd");

    public static WebApplicationFactory<Program> Host(ApiFactory factory) => Selling(factory);
}
