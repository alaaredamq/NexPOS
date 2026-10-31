using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Tests.Api.Infrastructure;

/// <summary>The sign-in throttle (needs its own, tightly limited host), and proof that the legacy <c>/api/*</c> controllers are untouched.</summary>
[Collection(ApiCollection.Name)]
public sealed class ApiThrottleAndLegacyTests
{
    private readonly ApiFactory _factory;

    public ApiThrottleAndLegacyTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Too_many_sign_in_attempts_are_answered_429_problem_json()
    {
        using var strict = ApiFactory.WithLoginLimit(3);
        var browser = strict.CreateBrowser();

        HttpResponseMessage? last = null;
        for (var i = 0; i < 4; i++)
        {
            var attempt = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
            {
                Content = JsonContent.Create(new { username = ApiFactory.AdminUser, pin = "0000" })
            };
            attempt.Headers.Add("X-NexPOS-Client", "web");
            last = await browser.SendAsync(attempt);
        }

        Assert.True(last!.StatusCode == HttpStatusCode.TooManyRequests, await last.Content.ReadAsStringAsync());
        Assert.Equal("application/problem+json", last.Content.Headers.ContentType?.MediaType);
        var body = JsonDocument.Parse(await last.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("rate_limited", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Legacy_api_controllers_do_not_need_the_client_header()
    {
        // The Razor pages' jQuery calls /api/* without it; the header gate is for /api/v1 only until cutover.
        var browser = _factory.CreateBrowser();

        var response = await browser.PostAsJsonAsync("/api/auth/login", new { username = "nobody", password = "x", pin = "1234" });

        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.DoesNotContain("client_header_required", await response.Content.ReadAsStringAsync());
    }

}
