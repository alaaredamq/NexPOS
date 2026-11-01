using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Application.DTOs.Licensing;
using Application.IServices.Licensing;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tests.Api.Infrastructure;
using Xunit;

namespace Tests.Api.Pos;

/// <summary>Shared plumbing of the POS API tests: a host whose licence allows selling, signing in, request builders, problem assertions.</summary>
internal static class PosTestKit
{
    public const string Problem = "application/problem+json";

    private static readonly ConditionalWeakTable<ApiFactory, WebApplicationFactory<Program>> SellingHosts = new();

    /// <summary>
    /// The same app and database as <see cref="ApiFactory"/> but with a licence that allows selling (the shared host has none, so every sale is refused).
    /// One per run: starting a host is slow. The product cache lives in the host, so POS tests that sell use this host for their reads too.
    /// </summary>
    public static WebApplicationFactory<Program> Selling(ApiFactory factory)
        => SellingHosts.GetValue(factory, f => f.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ILicenseService>();
            services.AddSingleton<ILicenseService, SellingLicense>();
        })));

    private sealed class SellingLicense : ILicenseService
    {
        public LicenseState GetState() => new()
        {
            Status = LicenseStatus.Active,
            MachineId = "TEST-TEST-TEST-TEST",
            BuildDate = DateOnly.FromDateTime(DateTime.Today),
        };

        public LicenseActivationResult Activate(string licenseKeyText) => throw new NotSupportedException();

        public void EnsureSalesAllowed() { }
    }

    public static HttpRequestMessage Json(HttpMethod method, string url, object? body = null, bool clientHeader = true, string? language = null)
    {
        var request = new HttpRequestMessage(method, url);
        if (body is not null) request.Content = JsonContent.Create(body);
        if (clientHeader) request.Headers.Add("X-NexPOS-Client", "web");
        if (language is not null) request.Headers.Add("Accept-Language", language);
        return request;
    }

    public static async Task<JsonElement> Body(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    /// <summary>A browser signed in as <paramref name="user"/>; also returns the user's id.</summary>
    public static async Task<(HttpClient Browser, int UserId)> SignedIn(WebApplicationFactory<Program> host, string user, string pin)
    {
        var browser = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
        var response = await browser.SendAsync(Json(HttpMethod.Post, "/api/v1/auth/login", new { username = user, pin }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (browser, (await Body(response)).GetProperty("user").GetProperty("id").GetInt32());
    }

    public static async Task<(HttpClient Browser, int UserId)> Cashier(WebApplicationFactory<Program> host)
        => await SignedIn(host, ApiFactory.CashierUser, ApiFactory.CashierPin);

    public static async Task AssertProblem(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == status, $"expected {(int)status} {code}, got {(int)response.StatusCode}: {text}");
        Assert.Equal(Problem, response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(code, JsonDocument.Parse(text).RootElement.GetProperty("code").GetString());
    }

    // ---- request bodies ----------------------------------------------------------------------------------------------

    public static object Line(int productId, int quantity = 1, decimal? weightOrVolume = null, int? priceTierId = null, decimal? customPrice = null)
        => new { productId, quantity, weightOrVolume, priceTierId, customPrice };

    public static object Cart(int currencyId, object[] lines, decimal? discountAmount = null, decimal? discountPercentage = null, string? discountLabel = null)
        => new { currencyId, lines, discountAmount, discountPercentage, discountLabel };

    public static object Checkout(object cart, decimal totalAmount, int paymentMethodId = 1, int? customerId = null, bool addToDebt = false,
        string? key = null, bool allowInsufficientStock = false, bool requireOpenDrawer = false, bool printReceipt = false, string? notes = null)
        => new
        {
            idempotencyKey = key ?? Guid.NewGuid().ToString("N"),
            cart,
            customerId,
            paymentMethodId,
            addToDebt,
            totalAmount,
            notes,
            printReceipt,
            allowInsufficientStock,
            requireOpenDrawer,
        };

    // ---- reads ---------------------------------------------------------------------------------------------------------

    public static async Task<decimal> StockOf(HttpClient browser, int productId)
        => (await Body(await browser.GetAsync($"/api/v1/pos/products/{productId}"))).GetProperty("stockQuantity").GetDecimal();

    public static async Task<decimal> BalanceOf(HttpClient browser, string name)
    {
        var page = await Body(await browser.GetAsync($"/api/v1/pos/customers?q={Uri.EscapeDataString(name)}"));
        return page.GetProperty("items")[0].GetProperty("balance").GetDecimal();
    }

    public static async Task<JsonElement> Price(HttpClient browser, object cart)
    {
        var response = await browser.SendAsync(Json(HttpMethod.Post, "/api/v1/pos/price", cart));
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await Body(response);
    }
}
