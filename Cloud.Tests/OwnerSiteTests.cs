using System.Net;
using Cloud.Tests.Support;
using Xunit;

namespace Cloud.Tests;

/// <summary>The owner's website is a set of static files. It must open without the provider's admin sign-in (owners have their own), and say so in the page.</summary>
public class OwnerSiteTests : IClassFixture<CloudFactory>
{
    private readonly CloudFactory _cloud;

    public OwnerSiteTests(CloudFactory cloud) => _cloud = cloud;

    [Fact]
    public async Task Owner_page_opens_without_the_admin_sign_in()
    {
        var http = _cloud.CreateClient(new() { AllowAutoRedirect = false });

        var redirect = await http.GetAsync("/owner");
        Assert.Equal(HttpStatusCode.Redirect, redirect.StatusCode);
        Assert.Equal("/owner/index.html", redirect.Headers.Location?.OriginalString);

        foreach (var file in new[] { "/owner/index.html", "/owner/owner.js", "/owner/owner.css", "/owner/brand.js" })
            Assert.Equal(HttpStatusCode.OK, (await http.GetAsync(file)).StatusCode);
    }

    [Fact]
    public async Task Provider_portal_still_needs_its_own_sign_in()
    {
        var http = _cloud.CreateClient(new() { AllowAutoRedirect = false });
        var home = await http.GetAsync("/");
        Assert.Equal(HttpStatusCode.Redirect, home.StatusCode);
        Assert.Contains("/Account/Login", home.Headers.Location?.OriginalString);
    }
}
