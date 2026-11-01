using System.Globalization;
using System.Text.Json;
using Core.Discounts;
using Core.Entities.Sales;
using Web.Api.Pos;
using Xunit;

namespace Tests.Api.Pos;

/// <summary>
/// The arithmetic of a till price against <c>docs/react-migration/golden/pos-pricing.json</c> - the file P3's Vitest also reads, so the browser's preview
/// and the server cannot drift apart.
/// </summary>
public sealed class PosPricingGoldenTests
{
    private static readonly JsonElement Golden = Load();

    private static JsonElement Load()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "REACT_MIGRATION.md"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return JsonDocument.Parse(File.ReadAllText(Path.Combine(dir!.FullName, "docs", "react-migration", "golden", "pos-pricing.json"))).RootElement.Clone();
    }

    private static decimal D(JsonElement e, string name) => decimal.Parse(e.GetProperty(name).GetString()!, CultureInfo.InvariantCulture);

    private static decimal? Dn(JsonElement e, string name)
        => e.GetProperty(name).ValueKind == JsonValueKind.Null ? null : D(e, name);

    private static Currency Cur(int id, string rate)
        => new("c" + id, float.Parse(rate, CultureInfo.InvariantCulture), false, true, "C" + id, "c") { Id = id };

    public static TheoryData<string> Names(string section)
    {
        var data = new TheoryData<string>();
        foreach (var c in Golden.GetProperty(section).EnumerateArray()) data.Add(c.GetProperty("name").GetString()!);
        return data;
    }

    public static TheoryData<string> UnitPriceCases() => Names("unitPrice");
    public static TheoryData<string> LineTotalCases() => Names("lineTotal");
    public static TheoryData<string> DiscountCases() => Names("discount");

    private static JsonElement Case(string section, string name)
        => Golden.GetProperty(section).EnumerateArray().First(c => c.GetProperty("name").GetString() == name);

    [Theory, MemberData(nameof(UnitPriceCases))]
    public void Unit_price_in_the_cart_currency(string name)
    {
        var c = Case("unitPrice", name);
        var same = c.GetProperty("sameCurrency").GetBoolean();
        var from = Cur(1, c.GetProperty("fromRate").GetString()!);
        var to = same ? from : Cur(2, c.GetProperty("toRate").GetString()!);

        Assert.Equal(D(c, "expected"), PosPricingMath.ToCartCurrency(D(c, "price"), from, to));
    }

    [Theory, MemberData(nameof(LineTotalCases))]
    public void Line_total(string name)
    {
        var c = Case("lineTotal", name);
        Assert.Equal(D(c, "expected"), PosPricingMath.LineTotal(D(c, "unitPrice"), c.GetProperty("quantity").GetInt32(), Dn(c, "weightOrVolume")));
    }

    [Theory, MemberData(nameof(DiscountCases))]
    public void Discount_and_total(string name)
    {
        var c = Case("discount", name);
        var subtotal = D(c, "subtotal");
        var discount = DiscountBreakdown.From(subtotal, Dn(c, "amount"), Dn(c, "percentage"));

        Assert.Equal(D(c, "expectedAmount"), discount.Amount);
        Assert.Equal(D(c, "expectedTotal"), PosPricingMath.Total(subtotal, discount.Amount));
    }
}
