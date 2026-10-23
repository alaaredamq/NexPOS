using System.Text.Json.Nodes;
using Core.Auditing;
using Xunit;

namespace Tests.Auditing;

public class AuditRedactorTests
{
    [Theory]
    [InlineData("PinHash")]
    [InlineData("PasswordHash")]
    [InlineData("Pin")]
    [InlineData("pin")]
    [InlineData("NewPin")]
    [InlineData("ConfirmPin")]
    [InlineData("CurrentPin")]
    [InlineData("user_pin")]
    [InlineData("password")]
    [InlineData("NewPassword")]
    [InlineData("SessionToken")]
    [InlineData("RefreshToken")]
    [InlineData("__RequestVerificationToken")]
    [InlineData("ApiKey")]
    [InlineData("Jwt:SecretKey")]
    [InlineData("Authorization")]
    [InlineData("Cookie")]
    public void IsSensitiveName_TrueForSecrets(string name)
        => Assert.True(AuditRedactor.IsSensitiveName(name), name);

    [Theory]
    [InlineData("MustChangePin")]
    [InlineData("MustChangePassword")]
    [InlineData("HasPin")]
    [InlineData("IsActive")]
    [InlineData("Name")]
    [InlineData("Description")]
    [InlineData("Shipping")]
    [InlineData("Spinning")]
    [InlineData("Email")]
    [InlineData("Phone")]
    [InlineData("StockQuantity")]
    [InlineData("SellingPrice")]
    [InlineData("")]
    [InlineData(null)]
    public void IsSensitiveName_FalseForOrdinaryFieldsAndFlags(string? name)
        => Assert.False(AuditRedactor.IsSensitiveName(name), name);

    [Fact]
    public void RedactJson_HidesSecretsAtAnyDepth_AndKeepsTheRest()
    {
        const string body = """
            { "username": "bob", "pin": "1234",
              "customer": { "name": "Ann", "Password": "hunter2", "tags": [ { "apiKey": "abc" }, "x" ] },
              "items": [ { "productId": 5, "quantity": 2 } ] }
            """;

        var redacted = AuditRedactor.RedactJson(body)!;
        var node = JsonNode.Parse(redacted)!;

        Assert.DoesNotContain("1234", redacted);
        Assert.DoesNotContain("hunter2", redacted);
        Assert.DoesNotContain("abc", redacted);
        Assert.Equal(AuditRedactor.Redacted, (string?)node["pin"]);
        Assert.Equal(AuditRedactor.Redacted, (string?)node["customer"]!["Password"]);
        Assert.Equal("bob", (string?)node["username"]);
        Assert.Equal("Ann", (string?)node["customer"]!["name"]);
        Assert.Equal(5, (int?)node["items"]![0]!["productId"]);
    }

    [Fact]
    public void RedactJson_ReturnsNull_ForInvalidJson()
        => Assert.Null(AuditRedactor.RedactJson("not json {"));

    [Fact]
    public void RedactJson_CutsVeryLongStrings()
    {
        var redacted = AuditRedactor.RedactJson($$"""{ "note": "{{new string('x', 5000)}}" }""")!;

        Assert.True(redacted.Length < 1000);
        Assert.Contains("chars]", redacted);
    }

    [Fact]
    public void SerializeDetails_RedactsKeysOfAnonymousObjects_AndSkipsNulls()
    {
        var json = AuditRedactor.SerializeDetails(new { rememberMe = true, pin = "9999", reason = (string?)null })!;

        Assert.DoesNotContain("9999", json);
        Assert.Contains("rememberMe", json);
        Assert.DoesNotContain("reason", json);
    }

    [Fact]
    public void SerializeDetails_NullIn_NullOut() => Assert.Null(AuditRedactor.SerializeDetails(null));

    [Fact]
    public void Cap_KeepsValidJson_WhenTooLong()
    {
        var capped = AuditRedactor.Cap(new string('a', AuditRedactor.MaxJsonLength + 10))!;

        Assert.NotNull(JsonNode.Parse(capped)); // still parseable
        Assert.True(capped.Length < AuditRedactor.MaxJsonLength);
    }

    [Fact]
    public void RedactPairs_HidesSecretFields()
    {
        var pairs = AuditRedactor.RedactPairs(
        [
            new KeyValuePair<string, string?>("Username", "bob"),
            new KeyValuePair<string, string?>("Pin", "1234"),
            new KeyValuePair<string, string?>("__RequestVerificationToken", "tok")
        ]);

        Assert.Equal("bob", pairs["Username"]);
        Assert.Equal(AuditRedactor.Redacted, pairs["Pin"]);
        Assert.Equal(AuditRedactor.Redacted, pairs["__RequestVerificationToken"]);
    }
}

public class AuditRequestRulesTests
{
    private static readonly string[] Excluded = ["/branding", "/Notifications/GetUnreadCount", "/POS/SearchProduct"];

    [Theory]
    [InlineData("/Notifications/GetUnreadCount", true)]
    [InlineData("/en/Notifications/GetUnreadCount", true)]   // same endpoint, language-prefixed
    [InlineData("/ar/pos/searchproduct", true)]              // case-insensitive
    [InlineData("/branding/logo", true)]
    [InlineData("/js/site.js", true)]                        // static asset by extension
    [InlineData("/images/logo.PNG", true)]
    [InlineData("/en/POS/Checkout", false)]
    [InlineData("/POS/Checkout", false)]
    [InlineData("/en/Products", false)]
    [InlineData("/", false)]
    public void IsExcluded_IgnoresLanguageAndCase(string path, bool expected)
        => Assert.Equal(expected, AuditRequestRules.IsExcluded(path, Excluded));

    [Theory]
    [InlineData(200, false, Core.Entities.Logging.AuditOutcome.Success)]
    [InlineData(302, false, Core.Entities.Logging.AuditOutcome.Success)]
    [InlineData(401, false, Core.Entities.Logging.AuditOutcome.Denied)]
    [InlineData(403, false, Core.Entities.Logging.AuditOutcome.Denied)]
    [InlineData(404, false, Core.Entities.Logging.AuditOutcome.Failure)]
    [InlineData(500, false, Core.Entities.Logging.AuditOutcome.Failure)]
    [InlineData(200, true, Core.Entities.Logging.AuditOutcome.Failure)] // threw before a status was set
    public void OutcomeFor_MapsStatus(int status, bool threw, Core.Entities.Logging.AuditOutcome expected)
        => Assert.Equal(expected, AuditRequestRules.OutcomeFor(status, threw));

    [Theory]
    [InlineData(200, false, Core.Entities.Logging.AuditLogSeverity.Info)]
    [InlineData(404, false, Core.Entities.Logging.AuditLogSeverity.Warning)]
    [InlineData(500, false, Core.Entities.Logging.AuditLogSeverity.Error)]
    [InlineData(200, true, Core.Entities.Logging.AuditLogSeverity.Error)]
    public void SeverityFor_MapsStatus(int status, bool threw, Core.Entities.Logging.AuditLogSeverity expected)
        => Assert.Equal(expected, AuditRequestRules.SeverityFor(status, threw));

    [Fact]
    public void ActionName_UsesControllerAndAction_WhenKnown()
    {
        Assert.Equal("POST POS/Checkout", AuditRequestRules.ActionName("POST", "POS", "Checkout", "/en/POS/Checkout"));
        Assert.Equal("GET /nope", AuditRequestRules.ActionName("GET", null, null, "/nope"));
    }
}
