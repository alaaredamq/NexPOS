using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Application.Services.Sales.Whish;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Tests.Api.Infrastructure;
using Xunit;
using static Tests.Api.Pos.PosTestKit;

namespace Tests.Api.Wallets;

/// <summary>
/// Online Whish payments (step 2) against a pretend Whish that only answers what each test decides: the owner saves the keys (the secret
/// is never sent back), the till sends a payment request, waits until Whish says paid, then checks out with it. The paid request pays for
/// that sale only, lands in the wallet already ticked, and its Whish number is the receipt's reference.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class WhishOnlineApiTests
{
    private const int Usd = 1;
    private const int Product = 48; // a $3.20 piece product of the test seed

    private static readonly ConditionalWeakTable<ApiFactory, WebApplicationFactory<Program>> Hosts = new();

    private readonly WebApplicationFactory<Program> _host;

    public WhishOnlineApiTests(ApiFactory factory) =>
        _host = Hosts.GetValue(factory, f => Selling(f).WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IWhishClient>();
            // Never pays by itself: each test settles its requests.
            services.AddSingleton(new FakeWhishClient(Options.Create(new WhishOptions { FakePaysAfterSeconds = -1 })));
            services.AddSingleton<IWhishClient>(sp => sp.GetRequiredService<FakeWhishClient>());
        })));

    private FakeWhishClient Whish => _host.Services.GetRequiredService<FakeWhishClient>();

    private async Task<HttpClient> Admin() => (await SignedIn(_host, ApiFactory.AdminUser, ApiFactory.AdminPin)).Browser;

    /// <summary>The Whish wallet with online payments on (Sandbox), set up by whichever test came first.</summary>
    private static async Task<JsonElement> OnlineWallet(HttpClient admin)
    {
        var wallets = await Body(await admin.GetAsync("/api/v1/wallets"));
        var wallet = wallets.EnumerateArray().FirstOrDefault(w => w.GetProperty("provider").GetString() == "Whish");
        if (wallet.ValueKind == JsonValueKind.Undefined)
        {
            var created = await admin.SendAsync(Json(HttpMethod.Post, "/api/v1/wallets/whish", new { accountNumber = "70 123 456" }));
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            wallet = await Body(created);
        }

        var id = wallet.GetProperty("id").GetInt32();
        var saved = await admin.SendAsync(Json(HttpMethod.Put, $"/api/v1/wallets/{id}/online",
            new { mode = "Sandbox", channel = "test-channel", secret = "s3cr3t-value", websiteUrl = "https://shop.example" }));
        Assert.True(saved.StatusCode == HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        return await Body(saved);
    }

    private static async Task<JsonElement> Request(HttpClient browser, int walletId, decimal amount, string? key = null)
    {
        var response = await browser.SendAsync(Json(HttpMethod.Post, "/api/v1/pos/whish/requests",
            new { walletId, amount, currencyId = Usd, idempotencyKey = key ?? Guid.NewGuid().ToString("N") }));
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await Body(response);
    }

    private static async Task<JsonElement> Poll(HttpClient browser, int id)
    {
        await Task.Delay(2100); // Whish is asked at most every 2 s per request
        return await Body(await browser.GetAsync($"/api/v1/pos/whish/requests/{id}"));
    }

    private static HttpRequestMessage Checkout(object cart, decimal total, int methodId, int? requestId, string? typedReference = null) =>
        Json(HttpMethod.Post, "/api/v1/pos/sales/checkout", new
        {
            idempotencyKey = Guid.NewGuid().ToString("N"),
            cart,
            customerId = (int?)null,
            paymentMethodId = methodId,
            addToDebt = false,
            totalAmount = total,
            printReceipt = false,
            allowInsufficientStock = true,
            requireOpenDrawer = false,
            paymentReference = typedReference,
            walletPaymentRequestId = requestId,
        });

    [Fact]
    public async Task The_secret_is_kept_but_never_sent_back_and_the_till_is_told_online_payments_work()
    {
        var admin = await Admin();
        var wallet = await OnlineWallet(admin);
        var online = wallet.GetProperty("online");
        Assert.Equal("Sandbox", online.GetProperty("mode").GetString());
        Assert.True(online.GetProperty("hasSecret").GetBoolean());
        Assert.True(online.GetProperty("ready").GetBoolean());
        Assert.DoesNotContain("s3cr3t-value", wallet.GetRawText());

        // An empty secret keeps the stored one.
        var id = wallet.GetProperty("id").GetInt32();
        var again = await Body(await admin.SendAsync(Json(HttpMethod.Put, $"/api/v1/wallets/{id}/online",
            new { mode = "Sandbox", channel = "test-channel", secret = "", websiteUrl = "https://shop.example" })));
        Assert.True(again.GetProperty("online").GetProperty("ready").GetBoolean());
        Assert.DoesNotContain("s3cr3t-value", (await (await admin.GetAsync("/api/v1/wallets")).Content.ReadAsStringAsync()));

        // Turning it on without the keys is refused field by field.
        var missing = await admin.SendAsync(Json(HttpMethod.Put, $"/api/v1/wallets/{id}/online", new { mode = "Live", channel = "", websiteUrl = "x" }));
        await AssertProblem(missing, HttpStatusCode.BadRequest, "validation_failed");

        var methods = await Body(await admin.GetAsync("/api/v1/pos/payment-methods"));
        var whish = methods.EnumerateArray().Single(m => m.GetProperty("walletProvider").GetString() == "Whish");
        Assert.True(whish.GetProperty("onlinePayments").GetBoolean());
        Assert.Equal(id, whish.GetProperty("walletId").GetInt32());
        Assert.False(methods.EnumerateArray().Single(m => m.GetProperty("name").GetString() == "Cash").GetProperty("onlinePayments").GetBoolean());
    }

    [Fact]
    public async Task A_paid_request_completes_one_sale_lands_ticked_and_prints_its_Whish_number()
    {
        var admin = await Admin();
        var wallet = await OnlineWallet(admin);
        var walletId = wallet.GetProperty("id").GetInt32();
        var methodId = wallet.GetProperty("paymentMethodId").GetInt32();
        var cart = Cart(Usd, [Line(Product, 1)]);
        var total = (await Price(admin, cart)).GetProperty("total").GetDecimal();

        var key = Guid.NewGuid().ToString("N");
        var request = await Request(admin, walletId, total, key);
        Assert.Equal("Pending", request.GetProperty("status").GetString());
        Assert.StartsWith("https://", request.GetProperty("collectUrl").GetString());
        var id = request.GetProperty("id").GetInt32();
        var externalId = request.GetProperty("externalId").GetString()!;

        // The till shows the link as a QR code it draws itself.
        var qr = await admin.GetAsync($"/api/v1/pos/whish/requests/{id}/qr.svg");
        Assert.Equal(HttpStatusCode.OK, qr.StatusCode);
        Assert.Equal("image/svg+xml", qr.Content.Headers.ContentType?.MediaType);
        Assert.Contains("<svg", await qr.Content.ReadAsStringAsync());
        await AssertProblem(await admin.GetAsync("/api/v1/pos/whish/requests/999999/qr.svg"), HttpStatusCode.NotFound, "whish_request_not_found");

        // A retry with the same key gets the same request back, not a second one.
        Assert.Equal(id, (await Request(admin, walletId, total, key)).GetProperty("id").GetInt32());

        // Not paid yet: checkout refuses.
        await AssertProblem(await admin.SendAsync(Checkout(cart, total, methodId, id)), HttpStatusCode.Conflict, "whish_not_paid");

        Whish.Settle(long.Parse(externalId), paid: true);
        var paid = await Poll(admin, id);
        Assert.Equal("Paid", paid.GetProperty("status").GetString());
        Assert.Equal("96170000000", paid.GetProperty("payerPhone").GetString());

        // The amount must match the sale.
        await AssertProblem(await admin.SendAsync(Checkout(cart, total + 1, methodId, id)), HttpStatusCode.Conflict, "whish_payment_mismatch");

        var sold = await admin.SendAsync(Checkout(cart, total, methodId, id, typedReference: "ignored"));
        Assert.True(sold.StatusCode == HttpStatusCode.OK, await sold.Content.ReadAsStringAsync());
        var sale = await Body(sold);

        var receipt = await Body(await admin.GetAsync($"/api/v1/receipts/{sale.GetProperty("receiptId").GetInt32()}"));
        Assert.Contains(externalId, receipt.GetRawText());
        Assert.DoesNotContain("ignored", receipt.GetRawText());

        var rows = await Body(await admin.GetAsync($"/api/v1/wallets/{walletId}/transactions?q={externalId}"));
        var row = Assert.Single(rows.GetProperty("items").EnumerateArray());
        Assert.Equal("Sale", row.GetProperty("type").GetString());
        Assert.True(row.GetProperty("isChecked").GetBoolean());
        Assert.Contains("96170000000", row.GetProperty("notes").GetString());

        // Used once only.
        await AssertProblem(await admin.SendAsync(Checkout(cart, total, methodId, id)), HttpStatusCode.Conflict, "whish_already_used");
        Assert.Equal(sale.GetProperty("orderId").GetInt32(), (await Poll(admin, id)).GetProperty("orderId").GetInt32());
    }

    [Fact]
    public async Task A_refused_payment_fails_and_one_paid_after_cancelling_shows_as_paid_without_a_sale()
    {
        var admin = await Admin();
        var walletId = (await OnlineWallet(admin)).GetProperty("id").GetInt32();

        var refused = await Request(admin, walletId, 4m);
        Whish.Settle(long.Parse(refused.GetProperty("externalId").GetString()!), paid: false);
        var failed = await Poll(admin, refused.GetProperty("id").GetInt32());
        Assert.Equal("Failed", failed.GetProperty("status").GetString());

        var late = await Request(admin, walletId, 6.5m);
        var lateId = late.GetProperty("id").GetInt32();
        var cancelled = await Body(await admin.SendAsync(Json(HttpMethod.Post, $"/api/v1/pos/whish/requests/{lateId}/cancel")));
        Assert.Equal("Cancelled", cancelled.GetProperty("status").GetString());

        Whish.Settle(long.Parse(late.GetProperty("externalId").GetString()!), paid: true);
        Assert.Equal("Paid", (await Poll(admin, lateId)).GetProperty("status").GetString());

        var wallet = await Body(await admin.GetAsync($"/api/v1/wallets/{walletId}"));
        var unused = wallet.GetProperty("online").GetProperty("paidWithoutSale").EnumerateArray().ToList();
        Assert.Contains(unused, p => p.GetProperty("id").GetInt32() == lateId && p.GetProperty("amount").GetDecimal() == 6.5m);
    }

    [Fact]
    public async Task Whish_only_takes_dollars_and_pounds_and_a_cashier_may_send_requests()
    {
        var admin = await Admin();
        var walletId = (await OnlineWallet(admin)).GetProperty("id").GetInt32();

        var currencies = await Body(await admin.GetAsync("/api/v1/pos/currencies"));
        var other = currencies.EnumerateArray().FirstOrDefault(c => c.GetProperty("code").GetString() is not ("USD" or "LBP"));
        if (other.ValueKind != JsonValueKind.Undefined)
        {
            var response = await admin.SendAsync(Json(HttpMethod.Post, "/api/v1/pos/whish/requests",
                new { walletId, amount = 5m, currencyId = other.GetProperty("id").GetInt32(), idempotencyKey = Guid.NewGuid().ToString("N") }));
            await AssertProblem(response, HttpStatusCode.BadRequest, "whish_currency_not_accepted");
        }

        var (cashier, _) = await Cashier(_host);
        Assert.Equal("Pending", (await Request(cashier, walletId, 2m)).GetProperty("status").GetString());
        await AssertProblem(await cashier.SendAsync(Json(HttpMethod.Put, $"/api/v1/wallets/{walletId}/online", new { mode = "Off" })),
            HttpStatusCode.Forbidden, "forbidden");
    }
}
