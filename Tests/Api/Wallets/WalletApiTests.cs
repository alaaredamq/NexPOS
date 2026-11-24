using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Tests.Api.Infrastructure;
using Xunit;
using static Tests.Api.Pos.PosTestKit;

namespace Tests.Api.Wallets;

/// <summary>
/// The Whish wallet (step 1, no Whish API yet): setting it up makes the "Whish" payment method, a Whish sale puts its money in the wallet
/// with the transfer number, a void takes it back out, the cash drawer does not expect Whish money, and the owner records movements by hand
/// and ticks rows off. A cashier takes Whish payments but cannot open the wallet.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class WalletApiTests
{
    private const string Base = "/api/v1/wallets";
    private const int Usd = 1;
    private const int Product = 48; // a $3.20 piece product of the test seed

    private readonly WebApplicationFactory<Program> _host;

    public WalletApiTests(ApiFactory factory) => _host = Selling(factory);

    private async Task<HttpClient> Admin() => (await SignedIn(_host, ApiFactory.AdminUser, ApiFactory.AdminPin)).Browser;

    /// <summary>The shop's Whish wallet, set up by whichever test of the run came first.</summary>
    private static async Task<JsonElement> Whish(HttpClient admin)
    {
        var all = await Body(await admin.GetAsync(Base));
        foreach (var wallet in all.EnumerateArray())
            if (wallet.GetProperty("provider").GetString() == "Whish") return wallet;

        var created = await admin.SendAsync(Json(HttpMethod.Post, $"{Base}/whish", new { accountNumber = "70 123 456" }));
        Assert.True(created.StatusCode == HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        return await Body(created);
    }

    private static async Task<JsonElement> Wallet(HttpClient admin, int id) => await Body(await admin.GetAsync($"{Base}/{id}"));

    private static decimal UsdBalance(JsonElement wallet) =>
        wallet.GetProperty("balances").EnumerateArray().First(b => b.GetProperty("currencyId").GetInt32() == Usd).GetProperty("balance").GetDecimal();

    private static int Unchecked(JsonElement wallet) =>
        wallet.GetProperty("balances").EnumerateArray().First(b => b.GetProperty("currencyId").GetInt32() == Usd).GetProperty("uncheckedCount").GetInt32();

    private static async Task<JsonElement> Rows(HttpClient admin, int walletId, string query = "")
    {
        var response = await admin.GetAsync($"{Base}/{walletId}/transactions?pageSize=50{query}");
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await Body(response);
    }

    /// <summary>Sells one product with <paramref name="methodId"/>; returns the sale.</summary>
    private static async Task<JsonElement> Sell(HttpClient browser, int methodId, string? reference)
    {
        var cart = Cart(Usd, [Line(Product, 1)]);
        var total = (await Price(browser, cart)).GetProperty("total").GetDecimal();
        var body = new
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
            paymentReference = reference,
        };
        var response = await browser.SendAsync(Json(HttpMethod.Post, "/api/v1/pos/sales/checkout", body));
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await Body(response);
    }

    [Fact]
    public async Task Setting_up_Whish_adds_a_payment_method_the_till_asks_a_reference_for()
    {
        var admin = await Admin();
        var wallet = await Whish(admin);
        Assert.Equal("Whish", wallet.GetProperty("paymentMethodName").GetString());

        var methods = await Body(await admin.GetAsync("/api/v1/pos/payment-methods"));
        var whish = methods.EnumerateArray().Single(m => m.GetProperty("id").GetInt32() == wallet.GetProperty("paymentMethodId").GetInt32());
        Assert.True(whish.GetProperty("asksReference").GetBoolean());
        Assert.Equal("Whish", whish.GetProperty("walletProvider").GetString());
        Assert.False(whish.GetProperty("requiresChange").GetBoolean());
        var cash = methods.EnumerateArray().Single(m => m.GetProperty("name").GetString() == "Cash");
        Assert.False(cash.GetProperty("asksReference").GetBoolean());

        // Only one Whish wallet.
        await AssertProblem(await admin.SendAsync(Json(HttpMethod.Post, $"{Base}/whish", new { })), HttpStatusCode.Conflict, "wallet_exists");
    }

    [Fact]
    public async Task A_Whish_sale_lands_in_the_wallet_and_its_void_takes_it_back_out()
    {
        var admin = await Admin();
        var wallet = await Whish(admin);
        var id = wallet.GetProperty("id").GetInt32();
        var methodId = wallet.GetProperty("paymentMethodId").GetInt32();
        var before = UsdBalance(await Wallet(admin, id));

        var reference = $"WH-{Guid.NewGuid():N}"[..14];
        var sale = await Sell(admin, methodId, reference);
        var total = sale.GetProperty("total").GetDecimal();
        Assert.Equal(before + total, UsdBalance(await Wallet(admin, id)));

        var found = await Rows(admin, id, $"&q={reference}");
        var row = Assert.Single(found.GetProperty("items").EnumerateArray());
        Assert.Equal("Sale", row.GetProperty("type").GetString());
        Assert.Equal(total, row.GetProperty("amount").GetDecimal());
        Assert.Equal(reference, row.GetProperty("reference").GetString());
        Assert.Equal(sale.GetProperty("receiptNumber").GetString(), row.GetProperty("receiptNumber").GetString());
        Assert.False(row.GetProperty("isChecked").GetBoolean());

        // The receipt carries the Whish number instead of the till's own id.
        var receipt = await Body(await admin.GetAsync($"/api/v1/receipts/{sale.GetProperty("receiptId").GetInt32()}"));
        Assert.Contains(reference, receipt.GetRawText());

        var voided = await admin.SendAsync(Json(HttpMethod.Post, $"/api/v1/receipts/{sale.GetProperty("receiptId").GetInt32()}/void", new { reason = "wrong item" }));
        Assert.True(voided.IsSuccessStatusCode, await voided.Content.ReadAsStringAsync());
        Assert.Equal(before, UsdBalance(await Wallet(admin, id)));

        var both = (await Rows(admin, id, $"&q={reference}")).GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(2, both.Count);
        var reversal = both.Single(r => r.GetProperty("type").GetString() == "SaleReversal");
        Assert.Equal(-total, reversal.GetProperty("amount").GetDecimal());
    }

    [Fact]
    public async Task The_cash_drawer_does_not_expect_Whish_money()
    {
        var admin = await Admin();
        var wallet = await Whish(admin);
        var current = await Body(await admin.GetAsync("/api/v1/cash-drawer/current"));
        int? openedHere = null;
        if (!current.GetProperty("status").GetProperty("isOpen").GetBoolean())
        {
            var opened = await admin.SendAsync(Json(HttpMethod.Post, "/api/v1/cash-drawer/open", new { openingCash = 0 }));
            Assert.Equal(HttpStatusCode.Created, opened.StatusCode);
            openedHere = (await Body(opened)).GetProperty("id").GetInt32();
        }

        try
        {
            static decimal InDrawer(JsonElement current) => current.GetProperty("status").GetProperty("cashInDrawer").GetDecimal();
            var before = InDrawer(await Body(await admin.GetAsync("/api/v1/cash-drawer/current")));

            await Sell(admin, wallet.GetProperty("paymentMethodId").GetInt32(), null);
            Assert.Equal(before, InDrawer(await Body(await admin.GetAsync("/api/v1/cash-drawer/current"))));

            // A cash sale still does.
            var cash = await Sell(admin, 1, null);
            Assert.Equal(before + cash.GetProperty("total").GetDecimal(), InDrawer(await Body(await admin.GetAsync("/api/v1/cash-drawer/current"))));
        }
        finally
        {
            // Other tests expect the admin to start without an open drawer.
            if (openedHere is { } id)
                await admin.SendAsync(Json(HttpMethod.Post, $"/api/v1/cash-drawer/sessions/{id}/close", new { actualCash = 0 }));
        }
    }

    [Fact]
    public async Task Money_moved_by_hand_changes_the_balance_and_rows_can_be_ticked_off()
    {
        var admin = await Admin();
        var id = (await Whish(admin)).GetProperty("id").GetInt32();
        var before = await Wallet(admin, id);

        var withdrawal = await admin.SendAsync(Json(HttpMethod.Post, $"{Base}/{id}/transactions",
            new { type = "Withdrawal", amount = 12.5m, currencyId = Usd, notes = "Cashed out at the agent" }));
        Assert.True(withdrawal.StatusCode == HttpStatusCode.Created, await withdrawal.Content.ReadAsStringAsync());
        var row = await Body(withdrawal);
        Assert.Equal(-12.5m, row.GetProperty("amount").GetDecimal());

        var correction = await admin.SendAsync(Json(HttpMethod.Post, $"{Base}/{id}/transactions",
            new { type = "Adjustment", amount = 2m, moneyIn = true, currencyId = Usd }));
        Assert.Equal(HttpStatusCode.Created, correction.StatusCode);

        var after = await Wallet(admin, id);
        Assert.Equal(UsdBalance(before) - 10.5m, UsdBalance(after));
        Assert.Equal(Unchecked(before) + 2, Unchecked(after));

        // Sales only come from checkout; zero and unknown types are refused.
        foreach (var bad in new object[]
                 {
                     new { type = "Sale", amount = 5m, currencyId = Usd },
                     new { type = "Nonsense", amount = 5m, currencyId = Usd },
                     new { type = "Deposit", amount = 5m, currencyId = 999 },
                 })
            await AssertProblem(await admin.SendAsync(Json(HttpMethod.Post, $"{Base}/{id}/transactions", bad)), HttpStatusCode.BadRequest, "validation_failed");

        var check = await admin.SendAsync(Json(HttpMethod.Post, $"{Base}/{id}/transactions/check", new { ids = new[] { row.GetProperty("id").GetInt32() }, @checked = true }));
        Assert.Equal(1, (await Body(check)).GetProperty("changed").GetInt32());
        Assert.Equal(Unchecked(after) - 1, Unchecked(await Wallet(admin, id)));

        var ticked = await Rows(admin, id, "&status=checked");
        Assert.Contains(ticked.GetProperty("items").EnumerateArray(), r => r.GetProperty("id").GetInt32() == row.GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task A_cashier_sells_with_Whish_but_cannot_open_the_wallet()
    {
        var admin = await Admin();
        var wallet = await Whish(admin);
        var (cashier, _) = await Cashier(_host);

        await AssertProblem(await cashier.GetAsync(Base), HttpStatusCode.Forbidden, "forbidden");
        await AssertProblem(await cashier.SendAsync(Json(HttpMethod.Post, $"{Base}/{wallet.GetProperty("id").GetInt32()}/transactions",
            new { type = "Deposit", amount = 1m, currencyId = Usd })), HttpStatusCode.Forbidden, "forbidden");

        var sale = await Sell(cashier, wallet.GetProperty("paymentMethodId").GetInt32(), "CASHIER-1");
        Assert.Equal("Whish", sale.GetProperty("paymentMethod").GetString());
    }
}
