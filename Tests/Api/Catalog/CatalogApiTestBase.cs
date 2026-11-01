using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Tests.Api.Infrastructure;
using Xunit;

namespace Tests.Api.Catalog;

/// <summary>Sign-in and JSON helpers shared by the catalog API tests (same pattern as <c>ApiPipelineTests</c>).</summary>
public abstract class CatalogApiTestBase
{
    protected readonly ApiFactory Factory;

    protected CatalogApiTestBase(ApiFactory factory) => Factory = factory;

    protected static HttpRequestMessage Json(HttpMethod method, string url, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        if (body is not null) request.Content = JsonContent.Create(body);
        request.Headers.Add("X-NexPOS-Client", "web");
        return request;
    }

    protected static async Task<JsonElement> Body(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    protected async Task<HttpClient> SignedIn(string user, string pin)
    {
        var browser = Factory.CreateBrowser();
        var response = await browser.SendAsync(Json(HttpMethod.Post, "/api/v1/auth/login", new { username = user, pin }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return browser;
    }

    protected Task<HttpClient> Admin() => SignedIn(ApiFactory.AdminUser, ApiFactory.AdminPin);

    protected Task<HttpClient> Cashier() => SignedIn(ApiFactory.CashierUser, ApiFactory.CashierPin);

    protected static async Task AssertProblem(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(code, (await Body(response)).GetProperty("code").GetString());
    }
}
