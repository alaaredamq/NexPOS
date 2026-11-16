using System.Net;
using Cloud.Tests.Support;
using Xunit;

namespace Cloud.Tests;

/// <summary>The public landing page at "/", and what crawlers are told (robots.txt, sitemap.xml).</summary>
public class PublicSiteTests : IClassFixture<CloudFactory>
{
    private readonly CloudFactory _cloud;

    public PublicSiteTests(CloudFactory cloud) => _cloud = cloud;

    private HttpClient Browser() => _cloud.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = true });

    [Fact]
    public async Task A_visitor_at_the_root_gets_the_landing_page_with_absolute_links()
    {
        var response = await Browser().GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("The till that keeps selling", html);
        Assert.Contains("id=\"plans\"", html);
        Assert.DoesNotContain("__ORIGIN__", html);
        Assert.Contains("<link rel=\"canonical\" href=\"http://localhost/\">", html);
    }

    [Fact]
    public async Task The_root_is_the_landing_page_even_for_a_signed_in_admin_and_the_portal_is_at_admin()
    {
        var c = Browser();
        var login = await HtmlForms.PostAsync(c, "/admin/Account/Login", "/admin/Account/Login", ("Username", CloudFactory.AdminUser), ("Password", CloudFactory.AdminPassword));
        Assert.Equal("/admin", login.Headers.Location?.OriginalString);

        Assert.Contains("The till that keeps selling", await c.GetStringAsync("/"));
        var portal = await c.GetStringAsync("/admin");
        Assert.DoesNotContain("The till that keeps selling", portal);
        Assert.Contains("Coduo POS Cloud", portal);
    }

    [Theory]
    [InlineData("/Account/Login", "/admin/Account/Login")]
    [InlineData("/Clients/Details/7", "/admin/Clients/Details/7")]
    [InlineData("/Sites/Details/3f2504e0-4f89-11d3-9a0c-0305e82c3301?x=1", "/admin/Sites/Details/3f2504e0-4f89-11d3-9a0c-0305e82c3301?x=1")]
    public async Task Old_portal_bookmarks_move_to_admin(string oldUrl, string newUrl)
    {
        var response = await Browser().GetAsync(oldUrl);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(newUrl, response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Till_enrolment_links_stay_at_the_root()
    {
        var response = await Browser().GetAsync("/Enroll");

        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
        Assert.False(response.Headers.Location?.OriginalString.StartsWith("/admin") ?? false);
    }

    [Fact]
    public async Task The_raw_template_is_never_served_under_its_own_name()
    {
        var response = await Browser().GetAsync("/home/index.html");

        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal("/", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Robots_keeps_crawlers_out_of_the_portal_and_points_at_the_sitemap()
    {
        var robots = await Browser().GetStringAsync("/robots.txt");

        Assert.Contains("User-agent: *", robots);
        foreach (var path in new[] { "/admin", "/owner", "/api/" })
            Assert.Contains($"Disallow: {path}", robots);
        Assert.Contains("Sitemap: http://localhost/sitemap.xml", robots);
    }

    [Fact]
    public async Task Sitemap_lists_the_landing_page()
    {
        var response = await Browser().GetAsync("/sitemap.xml");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/xml", response.Content.Headers.ContentType?.MediaType);
        var xml = await response.Content.ReadAsStringAsync();
        Assert.Contains("<loc>http://localhost/</loc>", xml);
        Assert.Matches(@"<lastmod>\d{4}-\d{2}-\d{2}</lastmod>", xml);
    }

    [Theory]
    [InlineData("/favicon.ico")]
    [InlineData("/favicon.svg")]
    [InlineData("/apple-touch-icon.png")]
    [InlineData("/site.webmanifest")]
    [InlineData("/home/home.css")]
    [InlineData("/home/home.js")]
    [InlineData("/home/og.png")]
    public async Task Landing_assets_open_without_sign_in(string url)
    {
        Assert.Equal(HttpStatusCode.OK, (await Browser().GetAsync(url)).StatusCode);
    }
}
