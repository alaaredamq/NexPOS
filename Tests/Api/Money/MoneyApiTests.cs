using System.Net;
using System.Text.Json;
using Application.IServices.Logging;
using Core.Auditing;
using Microsoft.Extensions.DependencyInjection;
using Tests.Api.Catalog;
using Tests.Api.Infrastructure;
using Xunit;

namespace Tests.Api.Money;

/// <summary>
/// Money configuration API (track A3): currencies and their rates, exchange-rate pairs, taxes, discounts, payment methods and code lists.
/// The database is shared by every API test, so each test works on rows it created and puts back anything global it moved
/// (the default currency, discount and payment method).
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class MoneyApiTests : CatalogApiTestBase
{
    public MoneyApiTests(ApiFactory factory) : base(factory)
    {
    }

    private static string Unique(string prefix, int length = 14) => $"{prefix}{Guid.NewGuid():N}"[..length];

    private static HttpRequestMessage Put(string url, object body) => Json(HttpMethod.Put, url, body);

    private static HttpRequestMessage Post(string url, object? body = null) => Json(HttpMethod.Post, url, body ?? new { });

    private static HttpRequestMessage Delete(string url) => Json(HttpMethod.Delete, url);

    private static async Task<JsonElement> Created(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await Body(response);
    }

    private static async Task<JsonElement> Ok(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await Body(response);
    }

    private static async Task<JsonElement> ErrorsOf(HttpResponseMessage response, string field)
    {
        await AssertProblem(response, HttpStatusCode.BadRequest, "validation_failed");
        var errors = (await Body(response)).GetProperty("errors");
        Assert.True(errors.TryGetProperty(field, out var messages), $"expected an error on '{field}', got {errors}");
        return messages;
    }

    private static int Id(JsonElement e) => e.GetProperty("id").GetInt32();

    // ---- currencies -------------------------------------------------------------------------------------------

    private static object CurrencyBody(string code, double rate = 2, bool isDefault = false, bool isActive = true, string? name = null)
        => new { code, name = name ?? $"Test {code}", notation = "¤", rateToUsd = rate, isDefault, isActive };

    private static async Task<JsonElement> CurrenciesAsync(HttpClient admin)
        => (await Ok(await admin.GetAsync("/api/v1/currencies?pageSize=200"))).GetProperty("items");

    private static async Task<JsonElement> NewCurrencyAsync(HttpClient admin, double rate = 2, string? code = null)
        => await Created(await admin.SendAsync(Post("/api/v1/currencies", CurrencyBody(code ?? Unique("T", 8), rate))));

    private static async Task<int> DefaultCurrencyIdAsync(HttpClient admin)
        => Id((await CurrenciesAsync(admin)).EnumerateArray().Single(c => c.GetProperty("isDefault").GetBoolean()));

    private static async Task<int> UsdIdAsync(HttpClient admin)
        => Id((await CurrenciesAsync(admin)).EnumerateArray().Single(c => c.GetProperty("code").GetString() == "USD"));

    [Fact]
    public async Task Currencies_are_listed_with_exactly_one_default_and_can_be_searched_and_sorted()
    {
        var admin = await Admin();
        var created = await NewCurrencyAsync(admin, 7);
        var code = created.GetProperty("code").GetString()!;

        var all = (await CurrenciesAsync(admin)).EnumerateArray().ToList();
        Assert.Single(all, c => c.GetProperty("isDefault").GetBoolean());
        Assert.Contains(all, c => c.GetProperty("code").GetString() == "USD" && c.GetProperty("rateToUsd").GetDecimal() == 1m);

        var found = await Ok(await admin.GetAsync($"/api/v1/currencies?q={code[..5].ToLowerInvariant()}"));
        Assert.Equal(code, Assert.Single(found.GetProperty("items").EnumerateArray()).GetProperty("code").GetString());

        var byRate = (await Ok(await admin.GetAsync("/api/v1/currencies?sort=rateToUsd&dir=desc&pageSize=200"))).GetProperty("items").EnumerateArray()
            .Select(c => c.GetProperty("rateToUsd").GetDecimal()).ToList();
        Assert.Equal(byRate.OrderByDescending(r => r), byRate);
    }

    [Fact]
    public async Task A_currency_is_saved_with_a_trimmed_upper_case_code_and_bad_input_is_refused_on_the_field()
    {
        var admin = await Admin();
        var code = Unique("t", 5);

        var created = await Created(await admin.SendAsync(Post("/api/v1/currencies", CurrencyBody($"  {code}  ", 3.5))));

        Assert.Equal(code.ToUpperInvariant(), created.GetProperty("code").GetString());
        Assert.Equal(3.5m, created.GetProperty("rateToUsd").GetDecimal());
        Assert.False(created.GetProperty("isDefault").GetBoolean());

        // The code is unique ignoring case; a zero rate would divide by zero in the till.
        await ErrorsOf(await admin.SendAsync(Post("/api/v1/currencies", CurrencyBody(code.ToLowerInvariant()))), "code");
        await ErrorsOf(await admin.SendAsync(Post("/api/v1/currencies", CurrencyBody(Unique("Z", 8), rate: 0))), "rateToUsd");
        await ErrorsOf(await admin.SendAsync(Post("/api/v1/currencies", new { code = "", name = "x", notation = "x", rateToUsd = 1, isDefault = false, isActive = true })), "code");
        await ErrorsOf(await admin.SendAsync(Post("/api/v1/currencies", CurrencyBody(Unique("Y", 8), isDefault: true, isActive: false))), "isActive");

        // Editing keeps the id, may keep its own code, but not take another currency's.
        var id = Id(created);
        var edited = await Ok(await admin.SendAsync(Put($"/api/v1/currencies/{id}", CurrencyBody(code, 4, name: "Renamed"))));
        Assert.Equal("Renamed", edited.GetProperty("name").GetString());
        Assert.Equal(4m, edited.GetProperty("rateToUsd").GetDecimal());
        await ErrorsOf(await admin.SendAsync(Put($"/api/v1/currencies/{id}", CurrencyBody("USD"))), "code");
        Assert.Equal(HttpStatusCode.NotFound, (await admin.SendAsync(Put("/api/v1/currencies/999999", CurrencyBody(Unique("N", 8))))).StatusCode);
    }

    [Fact]
    public async Task The_default_currency_moves_but_cannot_be_unset_deactivated_or_deleted()
    {
        var admin = await Admin();
        var original = await DefaultCurrencyIdAsync(admin);
        var other = await NewCurrencyAsync(admin);
        var otherId = Id(other);
        var inactive = Id(await Created(await admin.SendAsync(Post("/api/v1/currencies", CurrencyBody(Unique("I", 8), isActive: false)))));

        try
        {
            // Not offered to an inactive currency.
            await AssertProblem(await admin.SendAsync(Put($"/api/v1/currencies/{inactive}/default", new { })), HttpStatusCode.Conflict, "currency_inactive");

            var moved = await Ok(await admin.SendAsync(Put($"/api/v1/currencies/{otherId}/default", new { })));
            Assert.True(moved.GetProperty("isDefault").GetBoolean());
            Assert.Equal(otherId, await DefaultCurrencyIdAsync(admin));
            Assert.Single((await CurrenciesAsync(admin)).EnumerateArray(), c => c.GetProperty("isDefault").GetBoolean());

            // The default stays the default: pick another one instead.
            await AssertProblem(await admin.SendAsync(Put($"/api/v1/currencies/{otherId}", CurrencyBody(other.GetProperty("code").GetString()!, 2, isDefault: false))),
                HttpStatusCode.Conflict, "currency_default_required");
            await ErrorsOf(await admin.SendAsync(Put($"/api/v1/currencies/{otherId}", CurrencyBody(other.GetProperty("code").GetString()!, 2, isDefault: true, isActive: false))), "isActive");
            await AssertProblem(await admin.SendAsync(Delete($"/api/v1/currencies/{otherId}")), HttpStatusCode.Conflict, "currency_is_default");

            // Creating a currency as the default also takes the flag from the previous one.
            var third = await Created(await admin.SendAsync(Post("/api/v1/currencies", CurrencyBody(Unique("D", 8), isDefault: true))));
            Assert.Equal(Id(third), await DefaultCurrencyIdAsync(admin));
            Assert.Single((await CurrenciesAsync(admin)).EnumerateArray(), c => c.GetProperty("isDefault").GetBoolean());
        }
        finally
        {
            await admin.SendAsync(Put($"/api/v1/currencies/{original}/default", new { }));
        }

        Assert.Equal(original, await DefaultCurrencyIdAsync(admin));
    }

    [Fact]
    public async Task A_currency_with_a_rate_pointing_at_it_cannot_be_deleted_but_an_unused_one_can()
    {
        var admin = await Admin();
        var from = Id(await NewCurrencyAsync(admin));
        var to = Id(await NewCurrencyAsync(admin));
        var rate = await Created(await admin.SendAsync(Post("/api/v1/exchange-rates", new { fromCurrencyId = from, toCurrencyId = to, rate = 3 })));

        await AssertProblem(await admin.SendAsync(Delete($"/api/v1/currencies/{from}")), HttpStatusCode.Conflict, "currency_in_use");
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/api/v1/currencies/{from}")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.SendAsync(Delete($"/api/v1/exchange-rates/{Id(rate)}"))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.SendAsync(Delete($"/api/v1/currencies/{from}"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/v1/currencies/{from}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.SendAsync(Delete($"/api/v1/currencies/{from}"))).StatusCode);
    }

    [Fact]
    public async Task A_changed_rate_reaches_the_till_and_the_converter_rounds_like_the_till()
    {
        var admin = await Admin();
        var usd = await UsdIdAsync(admin);
        var created = await NewCurrencyAsync(admin, 1.5);
        var id = Id(created);

        async Task<decimal> ConvertAsync(decimal amount, int from, int to)
            => (await Ok(await admin.SendAsync(Post("/api/v1/currencies/convert", new { amount, fromCurrencyId = from, toCurrencyId = to })))).GetProperty("converted").GetDecimal();

        // Through USD, 2 decimals, banker's rounding (Math.Round): 0.015 -> 0.02 and 0.045 -> 0.04, not away from zero.
        Assert.Equal(0.02m, await ConvertAsync(0.01m, usd, id));
        Assert.Equal(0.04m, await ConvertAsync(0.03m, usd, id));
        Assert.Equal(15m, await ConvertAsync(10m, usd, id));
        Assert.Equal(2m, await ConvertAsync(3m, id, usd));
        Assert.Equal(7m, await ConvertAsync(7m, id, id));
        // Between two non-USD currencies it is still "to USD, then out".
        var other = Id(await NewCurrencyAsync(admin, 4));
        Assert.Equal(8m, await ConvertAsync(3m, id, other));

        var result = await Ok(await admin.SendAsync(Post("/api/v1/currencies/convert", new { amount = 3m, fromCurrencyId = id, toCurrencyId = usd })));
        Assert.Equal(created.GetProperty("code").GetString(), result.GetProperty("fromCode").GetString());
        Assert.Equal("USD", result.GetProperty("toCode").GetString());
        await ErrorsOf(await admin.SendAsync(Post("/api/v1/currencies/convert", new { amount = 1, fromCurrencyId = 999999, toCurrencyId = usd })), "fromCurrencyId");

        // Edit the rate: the till's currency list and the converter both use the new figure straight away.
        await Ok(await admin.SendAsync(Put($"/api/v1/currencies/{id}", CurrencyBody(created.GetProperty("code").GetString()!, 2))));
        var till = (await Ok(await admin.GetAsync("/api/v1/pos/currencies"))).EnumerateArray().ToList();
        Assert.Equal(2m, till.Single(c => c.GetProperty("id").GetInt32() == id).GetProperty("rateToUsd").GetDecimal());
        Assert.Equal(20m, await ConvertAsync(10m, usd, id));
    }

    [Fact]
    public async Task A_rate_change_is_written_to_the_audit_trail()
    {
        var admin = await Admin();
        var created = await NewCurrencyAsync(admin, 2);
        var id = Id(created);
        await Ok(await admin.SendAsync(Put($"/api/v1/currencies/{id}", CurrencyBody(created.GetProperty("code").GetString()!, 5))));

        using var scope = Factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IAuditTrail>().FlushAsync();
        var (rows, _) = await scope.ServiceProvider.GetRequiredService<IAuditLogService>()
            .SearchAsync(new AuditLogFilter { EntityType = "Currency", EntityId = id, Action = AuditActions.Update }, 0, 10);

        Assert.NotEmpty(rows);
    }

    // ---- exchange rates ---------------------------------------------------------------------------------------

    [Fact]
    public async Task An_exchange_rate_is_one_per_pair_between_two_different_currencies()
    {
        var admin = await Admin();
        var a = await NewCurrencyAsync(admin);
        var b = await NewCurrencyAsync(admin);
        var (idA, idB) = (Id(a), Id(b));

        var created = await Created(await admin.SendAsync(Post("/api/v1/exchange-rates", new { fromCurrencyId = idA, toCurrencyId = idB, rate = 1.25 })));
        Assert.Equal(a.GetProperty("code").GetString(), created.GetProperty("fromCurrencyCode").GetString());
        Assert.Equal(b.GetProperty("code").GetString(), created.GetProperty("toCurrencyCode").GetString());
        Assert.Equal(1.25m, created.GetProperty("rate").GetDecimal());
        Assert.Equal(JsonValueKind.Null, created.GetProperty("supplierId").ValueKind);

        await ErrorsOf(await admin.SendAsync(Post("/api/v1/exchange-rates", new { fromCurrencyId = idA, toCurrencyId = idA, rate = 1 })), "toCurrencyId");
        await ErrorsOf(await admin.SendAsync(Post("/api/v1/exchange-rates", new { fromCurrencyId = idA, toCurrencyId = idB, rate = 9 })), "toCurrencyId");
        await ErrorsOf(await admin.SendAsync(Post("/api/v1/exchange-rates", new { fromCurrencyId = idA, toCurrencyId = idB, rate = 0 })), "rate");
        await ErrorsOf(await admin.SendAsync(Post("/api/v1/exchange-rates", new { fromCurrencyId = 999999, toCurrencyId = idB, rate = 1 })), "fromCurrencyId");
        await ErrorsOf(await admin.SendAsync(Post("/api/v1/exchange-rates", new { fromCurrencyId = idA, toCurrencyId = idB, rate = 1, supplierId = 999999 })), "supplierId");

        // The reverse direction is its own row; editing a rate to its own pair is fine.
        var reverse = await Created(await admin.SendAsync(Post("/api/v1/exchange-rates", new { fromCurrencyId = idB, toCurrencyId = idA, rate = 0.8 })));
        var edited = await Ok(await admin.SendAsync(Put($"/api/v1/exchange-rates/{Id(created)}", new { fromCurrencyId = idA, toCurrencyId = idB, rate = 1.5 })));
        Assert.Equal(1.5m, edited.GetProperty("rate").GetDecimal());
        await ErrorsOf(await admin.SendAsync(Put($"/api/v1/exchange-rates/{Id(reverse)}", new { fromCurrencyId = idA, toCurrencyId = idB, rate = 1 })), "toCurrencyId");

        var listed = (await Ok(await admin.GetAsync($"/api/v1/exchange-rates?q={a.GetProperty("code").GetString()}&sort=rate&dir=desc"))).GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(new[] { Id(created), Id(reverse) }, listed.Select(Id).ToArray());

        Assert.Equal(HttpStatusCode.NoContent, (await admin.SendAsync(Delete($"/api/v1/exchange-rates/{Id(created)}"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/v1/exchange-rates/{Id(created)}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.SendAsync(Put("/api/v1/exchange-rates/999999", new { fromCurrencyId = idA, toCurrencyId = idB, rate = 1 }))).StatusCode);
    }

    [Fact]
    public async Task A_supplier_can_have_its_own_rate_next_to_the_store_wide_one()
    {
        var admin = await Admin();
        var a = Id(await NewCurrencyAsync(admin));
        var b = Id(await NewCurrencyAsync(admin));
        var supplier = await Created(await admin.SendAsync(Post("/api/v1/suppliers", new { name = Unique("Sup"), currencyId = a, isActive = true })));
        var supplierId = Id(supplier);

        var storeWide = await Created(await admin.SendAsync(Post("/api/v1/exchange-rates", new { fromCurrencyId = a, toCurrencyId = b, rate = 2 })));
        var forSupplier = await Created(await admin.SendAsync(Post("/api/v1/exchange-rates", new { fromCurrencyId = a, toCurrencyId = b, rate = 2.2, supplierId })));
        Assert.Equal(supplier.GetProperty("name").GetString(), forSupplier.GetProperty("supplierName").GetString());
        await ErrorsOf(await admin.SendAsync(Post("/api/v1/exchange-rates", new { fromCurrencyId = a, toCurrencyId = b, rate = 3, supplierId })), "toCurrencyId");

        var only = (await Ok(await admin.GetAsync($"/api/v1/exchange-rates?supplierId={supplierId}"))).GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(new[] { Id(forSupplier) }, only.Select(Id).ToArray());
        Assert.NotEqual(Id(storeWide), Id(forSupplier));

        var options = (await Ok(await admin.GetAsync("/api/v1/exchange-rates/suppliers"))).EnumerateArray().ToList();
        Assert.Contains(options, o => Id(o) == supplierId);
    }

    // ---- taxes ------------------------------------------------------------------------------------------------

    private static object TaxBody(string name, double rate = 10, bool isDefault = false, bool isActive = true, int type = 0, int? categoryId = null,
        string? from = null, string? to = null)
        => new
        {
            name, rate, type, isDefault, isActive, isInclusive = false, applyToAllProducts = true,
            categoryId, effectiveFrom = from, effectiveTo = to, taxCode = (string?)null, description = (string?)null,
        };

    private static async Task<JsonElement> NewTaxAsync(HttpClient admin, string? name = null, double rate = 10, bool isDefault = false)
        => await Created(await admin.SendAsync(Post("/api/v1/taxes", TaxBody(name ?? Unique("Tax"), rate, isDefault))));

    private static async Task<List<JsonElement>> TaxesAsync(HttpClient admin)
        => (await Ok(await admin.GetAsync("/api/v1/taxes?pageSize=200"))).GetProperty("items").EnumerateArray().ToList();

    [Fact]
    public async Task Tax_rates_are_created_edited_searched_and_deleted_with_their_input_checked()
    {
        var admin = await Admin();
        var name = Unique("Vat");

        var created = await Created(await admin.SendAsync(Post("/api/v1/taxes", TaxBody(name, 11.5, type: 2, from: "2026-01-01T00:00:00Z", to: "2026-12-31T00:00:00Z"))));
        var id = Id(created);
        Assert.Equal(11.5m, created.GetProperty("rate").GetDecimal());
        Assert.Equal(2, created.GetProperty("type").GetInt32());
        Assert.False(created.GetProperty("isDefault").GetBoolean());

        await ErrorsOf(await admin.SendAsync(Post("/api/v1/taxes", TaxBody(Unique("Bad"), 101))), "rate");
        await ErrorsOf(await admin.SendAsync(Post("/api/v1/taxes", TaxBody(Unique("Bad"), -1))), "rate");
        await ErrorsOf(await admin.SendAsync(Post("/api/v1/taxes", TaxBody(Unique("Bad"), type: 9))), "type");
        await ErrorsOf(await admin.SendAsync(Post("/api/v1/taxes", TaxBody(Unique("Bad"), categoryId: 999999))), "categoryId");
        await ErrorsOf(await admin.SendAsync(Post("/api/v1/taxes", TaxBody(Unique("Bad"), from: "2026-06-01T00:00:00Z", to: "2026-01-01T00:00:00Z"))), "effectiveTo");
        await ErrorsOf(await admin.SendAsync(Post("/api/v1/taxes", TaxBody(""))), "name");

        var edited = await Ok(await admin.SendAsync(Put($"/api/v1/taxes/{id}", TaxBody(name + "x", 12, isActive: false))));
        Assert.Equal(name + "x", edited.GetProperty("name").GetString());
        Assert.Equal(12m, edited.GetProperty("rate").GetDecimal());
        Assert.False(edited.GetProperty("isActive").GetBoolean());
        Assert.Equal(12m, (await Ok(await admin.GetAsync($"/api/v1/taxes/{id}"))).GetProperty("rate").GetDecimal());

        var found = (await Ok(await admin.GetAsync($"/api/v1/taxes?q={name[..8]}"))).GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(id, Id(Assert.Single(found)));

        Assert.Equal(HttpStatusCode.NotFound, (await admin.SendAsync(Put("/api/v1/taxes/999999", TaxBody("x")))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.SendAsync(Delete($"/api/v1/taxes/{id}"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/v1/taxes/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.SendAsync(Delete($"/api/v1/taxes/{id}"))).StatusCode);
    }

    [Fact]
    public async Task A_tax_can_be_limited_to_a_category_and_the_category_name_comes_back()
    {
        var admin = await Admin();
        var categories = (await Ok(await admin.GetAsync("/api/v1/taxes/categories"))).EnumerateArray().ToList();
        if (categories.Count == 0)
        {
            var made = await Created(await admin.SendAsync(Post("/api/v1/categories", new { name = Unique("Cat"), isActive = true })));
            categories = (await Ok(await admin.GetAsync("/api/v1/taxes/categories"))).EnumerateArray().ToList();
            Assert.Contains(categories, c => Id(c) == Id(made));
        }

        var category = categories[0];
        var created = await Created(await admin.SendAsync(Post("/api/v1/taxes", TaxBody(Unique("Cat"), categoryId: Id(category)))));

        Assert.Equal(Id(category), created.GetProperty("categoryId").GetInt32());
        Assert.Equal(category.GetProperty("name").GetString(), created.GetProperty("categoryName").GetString());
        var sorted = await Ok(await admin.GetAsync("/api/v1/taxes?sort=category&dir=desc&pageSize=200"));
        Assert.Contains(sorted.GetProperty("items").EnumerateArray(), t => Id(t) == Id(created));
    }

    [Fact]
    public async Task Only_one_tax_is_the_default_and_deactivating_ends_it()
    {
        var admin = await Admin();
        var first = await NewTaxAsync(admin, isDefault: true);
        var second = await NewTaxAsync(admin, isDefault: true);
        var defaults = (await TaxesAsync(admin)).Where(t => t.GetProperty("isDefault").GetBoolean()).Select(Id).ToList();
        Assert.Equal(new[] { Id(second) }, defaults);

        // "Make default" is its own action (Tax.Manage) and moves the flag.
        var moved = await Ok(await admin.SendAsync(Post($"/api/v1/taxes/{Id(first)}/default")));
        Assert.True(moved.GetProperty("isDefault").GetBoolean());
        Assert.Equal(new[] { Id(first) }, (await TaxesAsync(admin)).Where(t => t.GetProperty("isDefault").GetBoolean()).Select(Id).ToArray());

        var off = await Ok(await admin.SendAsync(Post($"/api/v1/taxes/{Id(second)}/deactivate")));
        Assert.False(off.GetProperty("isActive").GetBoolean());
        Assert.NotEqual(JsonValueKind.Null, off.GetProperty("effectiveTo").ValueKind);

        Assert.Equal(HttpStatusCode.NotFound, (await admin.SendAsync(Post("/api/v1/taxes/999999/default"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.SendAsync(Post("/api/v1/taxes/999999/deactivate"))).StatusCode);
    }

    [Fact]
    public async Task The_calculator_adds_the_chosen_tax_and_nothing_for_a_tax_that_is_not_in_force()
    {
        var admin = await Admin();
        var ten = await NewTaxAsync(admin, rate: 10);
        var ended = Id(await Created(await admin.SendAsync(Post("/api/v1/taxes", TaxBody(Unique("Old"), 50, from: "2020-01-01T00:00:00Z", to: "2020-12-31T00:00:00Z")))));
        var off = Id(await Created(await admin.SendAsync(Post("/api/v1/taxes", TaxBody(Unique("Off"), 50, isActive: false)))));

        var result = await Ok(await admin.SendAsync(Post("/api/v1/taxes/calculate", new { baseAmount = 200, taxId = Id(ten) })));
        Assert.Equal(200m, result.GetProperty("baseAmount").GetDecimal());
        Assert.Equal(20m, result.GetProperty("taxAmount").GetDecimal());
        Assert.Equal(220m, result.GetProperty("totalAmount").GetDecimal());

        foreach (var id in new[] { ended, off })
        {
            var none = await Ok(await admin.SendAsync(Post("/api/v1/taxes/calculate", new { baseAmount = 200, taxId = id })));
            Assert.Equal(0m, none.GetProperty("taxAmount").GetDecimal());
            Assert.Equal(200m, none.GetProperty("totalAmount").GetDecimal());
        }

        await ErrorsOf(await admin.SendAsync(Post("/api/v1/taxes/calculate", new { baseAmount = 100, taxId = 999999 })), "taxId");
        await ErrorsOf(await admin.SendAsync(Post("/api/v1/taxes/calculate", new { baseAmount = -1, taxId = Id(ten) })), "baseAmount");
    }

    // ---- discounts --------------------------------------------------------------------------------------------

    private static async Task<List<JsonElement>> DiscountsAsync(HttpClient admin)
        => (await Ok(await admin.GetAsync("/api/v1/discounts?pageSize=200"))).GetProperty("items").EnumerateArray().ToList();

    [Fact]
    public async Task Discounts_are_percentages_with_one_default_and_the_till_sees_changes_at_once()
    {
        var admin = await Admin();
        var original = (await DiscountsAsync(admin)).Where(d => d.GetProperty("isDefault").GetBoolean()).Select(Id).ToList();
        var name = Unique("Disc");

        try
        {
            var created = await Created(await admin.SendAsync(Post("/api/v1/discounts", new { name, rate = 12.5, isDefault = true })));
            var id = Id(created);
            Assert.Equal(12.5m, created.GetProperty("rate").GetDecimal());
            Assert.Equal(new[] { id }, (await DiscountsAsync(admin)).Where(d => d.GetProperty("isDefault").GetBoolean()).Select(Id).ToArray());

            // The till reads the same list through a cache; the change shows without a restart.
            var till = (await Ok(await admin.GetAsync("/api/v1/pos/discounts"))).EnumerateArray().ToList();
            Assert.Equal(12.5m, till.Single(d => Id(d) == id).GetProperty("rate").GetDecimal());

            var edited = await Ok(await admin.SendAsync(Put($"/api/v1/discounts/{id}", new { name = name + "x", rate = 5, isDefault = false })));
            Assert.Equal(5m, edited.GetProperty("rate").GetDecimal());
            Assert.False(edited.GetProperty("isDefault").GetBoolean());
            till = (await Ok(await admin.GetAsync("/api/v1/pos/discounts"))).EnumerateArray().ToList();
            Assert.Equal(5m, till.Single(d => Id(d) == id).GetProperty("rate").GetDecimal());

            await ErrorsOf(await admin.SendAsync(Post("/api/v1/discounts", new { name = Unique("Bad"), rate = 100.01, isDefault = false })), "rate");
            await ErrorsOf(await admin.SendAsync(Post("/api/v1/discounts", new { name = Unique("Bad"), rate = -1, isDefault = false })), "rate");
            await ErrorsOf(await admin.SendAsync(Post("/api/v1/discounts", new { name = "", rate = 1, isDefault = false })), "name");

            var found = (await Ok(await admin.GetAsync($"/api/v1/discounts?q={name[..8]}&sort=rate"))).GetProperty("items").EnumerateArray().ToList();
            Assert.Equal(id, Id(Assert.Single(found)));

            Assert.Equal(HttpStatusCode.NoContent, (await admin.SendAsync(Delete($"/api/v1/discounts/{id}"))).StatusCode);
            Assert.DoesNotContain((await Ok(await admin.GetAsync("/api/v1/pos/discounts"))).EnumerateArray(), d => Id(d) == id);
            Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/v1/discounts/{id}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await admin.SendAsync(Put($"/api/v1/discounts/{id}", new { name = "x", rate = 1, isDefault = false }))).StatusCode);
        }
        finally
        {
            // Put the default back where it was (the created discount's default flag took it away from the seeded one).
            foreach (var id in original)
            {
                var row = (await DiscountsAsync(admin)).First(d => Id(d) == id);
                await admin.SendAsync(Put($"/api/v1/discounts/{id}", new { name = row.GetProperty("name").GetString(), rate = row.GetProperty("rate").GetDecimal(), isDefault = true }));
            }
        }
    }

    // ---- payment methods --------------------------------------------------------------------------------------

    private static async Task<List<JsonElement>> MethodsAsync(HttpClient admin)
        => (await Ok(await admin.GetAsync("/api/v1/payment-methods?pageSize=200"))).GetProperty("items").EnumerateArray().ToList();

    [Fact]
    public async Task Payment_methods_have_unique_names_one_default_and_flag_which_ones_give_change()
    {
        var admin = await Admin();
        var original = (await MethodsAsync(admin)).Where(m => m.GetProperty("isDefault").GetBoolean()).Select(Id).ToList();
        var name = Unique("Wire");

        try
        {
            var created = await Created(await admin.SendAsync(Post("/api/v1/payment-methods", new { name = $"  {name}  ", isDefault = true })));
            var id = Id(created);
            Assert.Equal(name, created.GetProperty("name").GetString());
            Assert.False(created.GetProperty("givesChange").GetBoolean());
            Assert.False(created.GetProperty("isSystem").GetBoolean());
            Assert.Equal(new[] { id }, (await MethodsAsync(admin)).Where(m => m.GetProperty("isDefault").GetBoolean()).Select(Id).ToArray());

            await ErrorsOf(await admin.SendAsync(Post("/api/v1/payment-methods", new { name = name.ToUpperInvariant(), isDefault = false })), "name");
            await ErrorsOf(await admin.SendAsync(Post("/api/v1/payment-methods", new { name = "", isDefault = false })), "name");

            // A name with "cash" in it is what makes the till work out change.
            var cash = await Created(await admin.SendAsync(Post("/api/v1/payment-methods", new { name = Unique("Petty cash"), isDefault = false })));
            Assert.True(cash.GetProperty("givesChange").GetBoolean());
            var till = (await Ok(await admin.GetAsync("/api/v1/pos/payment-methods"))).EnumerateArray().ToList();
            Assert.True(till.Single(m => Id(m) == Id(cash)).GetProperty("requiresChange").GetBoolean());
            Assert.False(till.Single(m => Id(m) == id).GetProperty("requiresChange").GetBoolean());

            var edited = await Ok(await admin.SendAsync(Put($"/api/v1/payment-methods/{id}", new { name = name + "2", isDefault = false })));
            Assert.Equal(name + "2", edited.GetProperty("name").GetString());
            await ErrorsOf(await admin.SendAsync(Put($"/api/v1/payment-methods/{id}", new { name = cash.GetProperty("name").GetString(), isDefault = false })), "name");

            Assert.Equal(HttpStatusCode.NoContent, (await admin.SendAsync(Delete($"/api/v1/payment-methods/{id}"))).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await admin.SendAsync(Delete($"/api/v1/payment-methods/{Id(cash)}"))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await admin.SendAsync(Delete($"/api/v1/payment-methods/{id}"))).StatusCode);
            Assert.DoesNotContain((await Ok(await admin.GetAsync("/api/v1/pos/payment-methods"))).EnumerateArray(), m => Id(m) == id);
        }
        finally
        {
            foreach (var id in original)
            {
                var row = (await MethodsAsync(admin)).First(m => Id(m) == id);
                await admin.SendAsync(Put($"/api/v1/payment-methods/{id}", new { name = row.GetProperty("name").GetString(), isDefault = true }));
            }
        }
    }

    [Fact]
    public async Task Customer_Debt_is_a_system_method_that_cannot_be_renamed_deleted_or_recreated()
    {
        var admin = await Admin();
        var debt = (await MethodsAsync(admin)).Single(m => m.GetProperty("name").GetString() == "Customer Debt");
        var id = Id(debt);
        Assert.True(debt.GetProperty("isSystem").GetBoolean());

        await AssertProblem(await admin.SendAsync(Put($"/api/v1/payment-methods/{id}", new { name = "Account", isDefault = false })), HttpStatusCode.Conflict, "payment_method_system");
        await AssertProblem(await admin.SendAsync(Put($"/api/v1/payment-methods/{id}", new { name = "Customer Debt", isDefault = true })), HttpStatusCode.Conflict, "payment_method_system");
        await AssertProblem(await admin.SendAsync(Delete($"/api/v1/payment-methods/{id}")), HttpStatusCode.Conflict, "payment_method_system");
        await ErrorsOf(await admin.SendAsync(Post("/api/v1/payment-methods", new { name = "customer debt", isDefault = false })), "name");

        // Another method cannot take its name either, so the till's lookup by name stays unambiguous.
        var other = await Created(await admin.SendAsync(Post("/api/v1/payment-methods", new { name = Unique("Mine"), isDefault = false })));
        await ErrorsOf(await admin.SendAsync(Put($"/api/v1/payment-methods/{Id(other)}", new { name = "CUSTOMER DEBT", isDefault = false })), "name");

        // The cashier is never offered it as a tender; the admin screen still lists it.
        var till = (await Ok(await admin.GetAsync("/api/v1/pos/payment-methods"))).EnumerateArray().ToList();
        Assert.DoesNotContain(till, m => m.GetProperty("name").GetString() == "Customer Debt");
        Assert.Contains(await MethodsAsync(admin), m => Id(m) == id);
    }

    // ---- code lists -------------------------------------------------------------------------------------------

    private static object ListBody(string key, string? name = null, int order = 5, bool isActive = true)
        => new { key, name = name ?? $"List {key}", description = "test", isActive, allowMultipleSelection = false, displayOrder = order };

    private static object ValueBody(string value, int order = 1, bool isActive = true)
        => new { value, displayText = $"Text {value}", description = (string?)null, isActive, displayOrder = order, metadata = (string?)null };

    [Fact]
    public async Task A_code_list_holds_unique_values_and_takes_them_with_it_when_deleted()
    {
        var admin = await Admin();
        var key = Unique("list_");

        var list = await Created(await admin.SendAsync(Post("/api/v1/code-lists", ListBody(key))));
        var id = Id(list);
        Assert.Equal(0, list.GetProperty("valueCount").GetInt32());
        await ErrorsOf(await admin.SendAsync(Post("/api/v1/code-lists", ListBody(key.ToUpperInvariant()))), "key");

        var second = await Created(await admin.SendAsync(Post($"/api/v1/code-lists/{id}/values", ValueBody("beta", order: 2))));
        var first = await Created(await admin.SendAsync(Post($"/api/v1/code-lists/{id}/values", ValueBody("alpha", order: 1))));
        await ErrorsOf(await admin.SendAsync(Post($"/api/v1/code-lists/{id}/values", ValueBody("ALPHA"))), "value");
        Assert.Equal(HttpStatusCode.NotFound, (await admin.SendAsync(Post("/api/v1/code-lists/999999/values", ValueBody("x")))).StatusCode);

        var details = await Ok(await admin.GetAsync($"/api/v1/code-lists/{id}"));
        Assert.Equal(2, details.GetProperty("list").GetProperty("valueCount").GetInt32());
        Assert.Equal(new[] { "alpha", "beta" }, details.GetProperty("values").EnumerateArray().Select(v => v.GetProperty("value").GetString()).ToArray());
        Assert.Equal(2, (await Ok(await admin.GetAsync($"/api/v1/code-lists?q={key}"))).GetProperty("items")[0].GetProperty("valueCount").GetInt32());

        var edited = await Ok(await admin.SendAsync(Put($"/api/v1/code-lists/{id}/values/{Id(second)}", ValueBody("gamma", order: 3, isActive: false))));
        Assert.Equal("gamma", edited.GetProperty("value").GetString());
        Assert.False(edited.GetProperty("isActive").GetBoolean());
        await ErrorsOf(await admin.SendAsync(Put($"/api/v1/code-lists/{id}/values/{Id(second)}", ValueBody("alpha"))), "value");
        Assert.Equal(HttpStatusCode.NotFound, (await admin.SendAsync(Put($"/api/v1/code-lists/{id}/values/999999", ValueBody("zzz")))).StatusCode);
        // A value is looked up inside its own list only.
        var otherList = Id(await Created(await admin.SendAsync(Post("/api/v1/code-lists", ListBody(Unique("other_"))))));
        Assert.Equal(HttpStatusCode.NotFound, (await admin.SendAsync(Put($"/api/v1/code-lists/{otherList}/values/{Id(first)}", ValueBody("alpha")))).StatusCode);

        var renamed = await Ok(await admin.SendAsync(Put($"/api/v1/code-lists/{id}", ListBody(key, "Renamed", order: 9, isActive: false))));
        Assert.Equal("Renamed", renamed.GetProperty("name").GetString());
        Assert.False(renamed.GetProperty("isActive").GetBoolean());
        Assert.Equal(2, renamed.GetProperty("valueCount").GetInt32());
        await ErrorsOf(await admin.SendAsync(Put($"/api/v1/code-lists/{otherList}", ListBody(key))), "key");

        Assert.Equal(HttpStatusCode.NoContent, (await admin.SendAsync(Delete($"/api/v1/code-lists/{id}/values/{Id(first)}"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.SendAsync(Delete($"/api/v1/code-lists/{id}/values/{Id(first)}"))).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.SendAsync(Delete($"/api/v1/code-lists/{id}"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/v1/code-lists/{id}")).StatusCode);
        // The key is free again, which only works if the values went with the list.
        await Created(await admin.SendAsync(Post("/api/v1/code-lists", ListBody(key))));
    }

    // ---- permissions ------------------------------------------------------------------------------------------

    private static async Task<int> CreateRoleAsync(HttpClient admin, params string[] permissions)
    {
        var response = await admin.SendAsync(Post("/api/v1/roles", new { name = Unique("role"), description = "test role", permissions }));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await Body(response)).GetProperty("role").GetProperty("id").GetInt32();
    }

    /// <summary>A user whose role holds exactly <paramref name="permissions"/>.</summary>
    private async Task<HttpClient> UserWith(params string[] permissions)
    {
        var admin = await Admin();
        var roleId = await CreateRoleAsync(admin, permissions);
        var username = Unique("usr");
        var response = await admin.SendAsync(Post("/api/v1/users", new { username, email = $"{username}@example.com", firstName = "T", lastName = "U", roleId, isActive = true, pin = "4321" }));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await SignedIn(username, "4321");
    }

    [Theory]
    [InlineData("/api/v1/currencies")]
    [InlineData("/api/v1/exchange-rates")]
    [InlineData("/api/v1/taxes")]
    [InlineData("/api/v1/discounts")]
    [InlineData("/api/v1/payment-methods")]
    [InlineData("/api/v1/code-lists")]
    public async Task Without_a_cookie_everything_answers_401(string url)
    {
        await AssertProblem(await Factory.CreateBrowser().GetAsync(url), HttpStatusCode.Unauthorized, "unauthenticated");
    }

    [Theory]
    [InlineData("/api/v1/currencies")]
    [InlineData("/api/v1/currencies/1")]
    [InlineData("/api/v1/exchange-rates")]
    [InlineData("/api/v1/exchange-rates/suppliers")]
    [InlineData("/api/v1/taxes")]
    [InlineData("/api/v1/taxes/categories")]
    [InlineData("/api/v1/discounts")]
    [InlineData("/api/v1/payment-methods")]
    [InlineData("/api/v1/code-lists")]
    public async Task A_cashier_may_read_none_of_it(string url)
    {
        await AssertProblem(await (await Cashier()).GetAsync(url), HttpStatusCode.Forbidden, "forbidden");
    }

    [Fact]
    public async Task A_view_only_user_reads_currencies_and_rates_but_changes_nothing()
    {
        var viewer = await UserWith("Currency.View");
        var admin = await Admin();
        var id = Id(await NewCurrencyAsync(admin));

        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync("/api/v1/currencies")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync("/api/v1/exchange-rates")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await viewer.SendAsync(Post("/api/v1/currencies/convert", new { amount = 1, fromCurrencyId = id, toCurrencyId = id }))).StatusCode);

        // The Razor page let a view-only user create, edit and delete currencies; the API asks for the matching permission.
        foreach (var request in new[]
                 {
                     Post("/api/v1/currencies", CurrencyBody(Unique("V", 8))),
                     Put($"/api/v1/currencies/{id}", CurrencyBody(Unique("V", 8))),
                     Put($"/api/v1/currencies/{id}/default", new { }),
                     Delete($"/api/v1/currencies/{id}"),
                     Post("/api/v1/exchange-rates", new { fromCurrencyId = id, toCurrencyId = id, rate = 1 }),
                     Put("/api/v1/exchange-rates/1", new { fromCurrencyId = id, toCurrencyId = id, rate = 1 }),
                     Delete("/api/v1/exchange-rates/1"),
                 })
        {
            await AssertProblem(await viewer.SendAsync(request), HttpStatusCode.Forbidden, "forbidden");
        }

        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/api/v1/currencies/{id}")).StatusCode);
    }

    [Fact]
    public async Task Each_currency_write_needs_its_own_permission()
    {
        var admin = await Admin();
        var id = Id(await NewCurrencyAsync(admin));
        var creator = await UserWith("Currency.View", "Currency.Create");
        var editor = await UserWith("Currency.View", "Currency.Edit");
        var deleter = await UserWith("Currency.View", "Currency.Delete");

        Assert.Equal(HttpStatusCode.Created, (await creator.SendAsync(Post("/api/v1/currencies", CurrencyBody(Unique("C", 8))))).StatusCode);
        await AssertProblem(await creator.SendAsync(Put($"/api/v1/currencies/{id}", CurrencyBody(Unique("C", 8)))), HttpStatusCode.Forbidden, "forbidden");
        await AssertProblem(await creator.SendAsync(Delete($"/api/v1/currencies/{id}")), HttpStatusCode.Forbidden, "forbidden");

        Assert.Equal(HttpStatusCode.OK, (await editor.SendAsync(Put($"/api/v1/currencies/{id}", CurrencyBody(Unique("E", 8))))).StatusCode);
        await AssertProblem(await editor.SendAsync(Post("/api/v1/currencies", CurrencyBody(Unique("E", 8)))), HttpStatusCode.Forbidden, "forbidden");

        await AssertProblem(await deleter.SendAsync(Put($"/api/v1/currencies/{id}", CurrencyBody(Unique("D", 8)))), HttpStatusCode.Forbidden, "forbidden");
        Assert.Equal(HttpStatusCode.NoContent, (await deleter.SendAsync(Delete($"/api/v1/currencies/{id}"))).StatusCode);
    }

    [Fact]
    public async Task Tax_actions_follow_the_razor_permissions_create_edit_delete_and_manage()
    {
        var admin = await Admin();
        var tax = Id(await NewTaxAsync(admin));
        var editor = await UserWith("Tax.View", "Tax.Edit");
        var manager = await UserWith("Tax.View", "Tax.Manage");

        Assert.Equal(HttpStatusCode.OK, (await editor.GetAsync("/api/v1/taxes")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await editor.GetAsync("/api/v1/taxes/categories")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await editor.SendAsync(Put($"/api/v1/taxes/{tax}", TaxBody(Unique("Tax"))))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await editor.SendAsync(Post("/api/v1/taxes/calculate", new { baseAmount = 10, taxId = tax }))).StatusCode);
        foreach (var request in new[]
                 {
                     Post("/api/v1/taxes", TaxBody(Unique("Tax"))),
                     Delete($"/api/v1/taxes/{tax}"),
                     Post($"/api/v1/taxes/{tax}/default"),
                     Post($"/api/v1/taxes/{tax}/deactivate"),
                 })
        {
            await AssertProblem(await editor.SendAsync(request), HttpStatusCode.Forbidden, "forbidden");
        }

        Assert.Equal(HttpStatusCode.OK, (await manager.SendAsync(Post($"/api/v1/taxes/{tax}/deactivate"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await manager.SendAsync(Post($"/api/v1/taxes/{tax}/default"))).StatusCode);
        await AssertProblem(await manager.SendAsync(Put($"/api/v1/taxes/{tax}", TaxBody(Unique("Tax")))), HttpStatusCode.Forbidden, "forbidden");
    }

    [Fact]
    public async Task Discounts_payment_methods_and_code_lists_change_only_with_their_manage_permission()
    {
        var admin = await Admin();
        var discount = Id(await Created(await admin.SendAsync(Post("/api/v1/discounts", new { name = Unique("D"), rate = 1, isDefault = false }))));
        var method = Id(await Created(await admin.SendAsync(Post("/api/v1/payment-methods", new { name = Unique("M"), isDefault = false }))));
        var list = Id(await Created(await admin.SendAsync(Post("/api/v1/code-lists", ListBody(Unique("p_"))))));

        var viewer = await UserWith("Discounts.View", "PaymentMethods.View", "Settings.View");
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync("/api/v1/discounts")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync("/api/v1/payment-methods")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync($"/api/v1/code-lists/{list}")).StatusCode);
        foreach (var request in new[]
                 {
                     Post("/api/v1/discounts", new { name = Unique("D"), rate = 1, isDefault = false }),
                     Put($"/api/v1/discounts/{discount}", new { name = Unique("D"), rate = 1, isDefault = false }),
                     Delete($"/api/v1/discounts/{discount}"),
                     Post("/api/v1/payment-methods", new { name = Unique("M"), isDefault = false }),
                     Put($"/api/v1/payment-methods/{method}", new { name = Unique("M"), isDefault = false }),
                     Delete($"/api/v1/payment-methods/{method}"),
                     Post("/api/v1/code-lists", ListBody(Unique("p_"))),
                     Put($"/api/v1/code-lists/{list}", ListBody(Unique("p_"))),
                     Delete($"/api/v1/code-lists/{list}"),
                     Post($"/api/v1/code-lists/{list}/values", ValueBody("v")),
                 })
        {
            await AssertProblem(await viewer.SendAsync(request), HttpStatusCode.Forbidden, "forbidden");
        }

        var discounts = await UserWith("Discounts.View", "Discounts.Manage");
        var methods = await UserWith("PaymentMethods.View", "PaymentMethods.Manage");
        var lists = await UserWith("Settings.View", "Settings.Manage");
        Assert.Equal(HttpStatusCode.OK, (await discounts.SendAsync(Put($"/api/v1/discounts/{discount}", new { name = Unique("D"), rate = 2, isDefault = false }))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await methods.SendAsync(Put($"/api/v1/payment-methods/{method}", new { name = Unique("M"), isDefault = false }))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await lists.SendAsync(Post($"/api/v1/code-lists/{list}/values", ValueBody("v")))).StatusCode);
    }
}
