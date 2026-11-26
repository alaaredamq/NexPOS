using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Cloud.Services;

/// <summary>
/// The public face of the cloud host: the Coduo POS landing page at "/", plus robots.txt and sitemap.xml.
/// The provider portal lives under /admin. The page is wwwroot/home/index.html; its "__ORIGIN__" marks
/// are swapped for the site's public address so canonical, Open Graph and structured-data links are absolute.
/// </summary>
public static class PublicSite
{
    public const string LandingFile = "home/index.html";
    public const string AdminPrefix = "admin";
    public const string AdminHome = "/admin";
    public const string AdminLogin = "/admin/Account/Login";
    private const string OriginMark = "__ORIGIN__";

    /// <summary>Paths crawlers are asked to stay out of: the provider portal, the owner website and every API.</summary>
    private static readonly string[] Private =
        ["/admin", "/console", "/Enroll", "/enroll", "/owner", "/api/", "/healthz"];

    /// <summary>Portal folders that used to sit at the root; old bookmarks are sent to their /admin address.</summary>
    private static readonly string[] OldPortalFolders = ["/Account", "/Clients", "/Sites"];

    /// <summary>
    /// One address for the whole site (Cloud:PublicUrl): http:// and the www. twin are sent there for good, so search engines
    /// see one site and logins never travel unencrypted. Hosts other than those two (the hosting's own name, an IP) are left
    /// alone, and nothing changes when PublicUrl is not set (a PC, the tests).
    /// A copy whose public address is not the live one (the test site) asks search engines to keep it out of their results.
    /// </summary>
    public static IApplicationBuilder UseCanonicalAddress(this IApplicationBuilder app) => app.Use(async (ctx, next) =>
    {
        var canonical = PublicUri(ctx);
        if (canonical is not null)
        {
            var host = ctx.Request.Host.Host;
            var isWwwTwin = host.Equals("www." + canonical.Host, StringComparison.OrdinalIgnoreCase);
            var isPlainHttp = host.Equals(canonical.Host, StringComparison.OrdinalIgnoreCase)
                && canonical.Scheme == Uri.UriSchemeHttps && !ctx.Request.IsHttps;
            if (isWwwTwin || isPlainHttp)
            {
                var method = ctx.Request.Method;
                // 308 keeps a POST a POST; GET and HEAD get the 301 every crawler understands.
                ctx.Response.StatusCode = HttpMethods.IsGet(method) || HttpMethods.IsHead(method)
                    ? StatusCodes.Status301MovedPermanently : StatusCodes.Status308PermanentRedirect;
                ctx.Response.Headers.Location = canonical.GetLeftPart(UriPartial.Authority)
                    + ctx.Request.PathBase + ctx.Request.Path + ctx.Request.QueryString;
                return;
            }
        }
        if (!IsIndexable(ctx))
            ctx.Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
        await next();
    });

    /// <summary>Cloud:PublicUrl as an absolute address, or null when it is not set (or not a usable address).</summary>
    private static Uri? PublicUri(HttpContext ctx)
    {
        var configured = ctx.RequestServices.GetRequiredService<IOptions<CloudOptions>>().Value.PublicUrl;
        return Uri.TryCreate(configured?.Trim(), UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" ? uri : null;
    }

    /// <summary>Only the live site (https://coduos.com) belongs in search results; a copy that names another address is a test site.</summary>
    private static bool IsIndexable(HttpContext ctx)
    {
        var canonical = PublicUri(ctx);
        return canonical is null || canonical.Host.Equals(new Uri(Sync.OnlineServer.Live).Host, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Sends a bookmark from before the move (/Clients/Details/3) to the same page under /admin. GET and HEAD only.</summary>
    public static IApplicationBuilder UseOldPortalRedirects(this IApplicationBuilder app) => app.Use(async (ctx, next) =>
    {
        var path = ctx.Request.Path;
        if ((HttpMethods.IsGet(ctx.Request.Method) || HttpMethods.IsHead(ctx.Request.Method))
            && OldPortalFolders.Any(f => path.StartsWithSegments(f, StringComparison.OrdinalIgnoreCase)))
        {
            ctx.Response.Redirect(AdminHome + path + ctx.Request.QueryString);
            return;
        }
        await next();
    });

    /// <summary>Runs before static files so the raw template (with its marks) is never served under its own name.</summary>
    public static IApplicationBuilder UseLandingTemplateGuard(this IApplicationBuilder app) => app.Use(async (ctx, next) =>
    {
        if (ctx.Request.Path.Equals("/" + LandingFile, StringComparison.OrdinalIgnoreCase))
        {
            ctx.Response.Redirect("/", permanent: true);
            return;
        }
        await next();
    });

    /// <summary>"/" is the landing page for everyone, signed in or not.</summary>
    public static IApplicationBuilder UseLandingPage(this IApplicationBuilder app) => app.Use(async (ctx, next) =>
    {
        var isHome = ctx.Request.Path == "/" && (HttpMethods.IsGet(ctx.Request.Method) || HttpMethods.IsHead(ctx.Request.Method));
        if (!isHome)
        {
            await next();
            return;
        }

        var file = ctx.RequestServices.GetRequiredService<IWebHostEnvironment>().WebRootFileProvider.GetFileInfo(LandingFile);
        if (!file.Exists)
        {
            await next();
            return;
        }

        string html;
        using (var reader = new StreamReader(file.CreateReadStream(), Encoding.UTF8))
            html = await reader.ReadToEndAsync();
        html = html.Replace(OriginMark, Origin(ctx));

        ctx.Response.ContentType = "text/html; charset=utf-8";
        ctx.Response.Headers.CacheControl = "public, max-age=300";
        if (HttpMethods.IsHead(ctx.Request.Method))
            return;
        await ctx.Response.WriteAsync(html);
    });

    public static IEndpointRouteBuilder MapPublicSite(this IEndpointRouteBuilder app)
    {
        app.MapGet("/robots.txt", (HttpContext ctx) =>
        {
            var sb = new StringBuilder();
            sb.Append("User-agent: *\n");
            if (!IsIndexable(ctx))
            {
                // The test site: nothing here should be found by searching.
                sb.Append("Disallow: /\n");
                return Results.Text(sb.ToString(), "text/plain; charset=utf-8");
            }
            sb.Append("Allow: /$\n");
            foreach (var path in Private)
                sb.Append("Disallow: ").Append(path).Append('\n');
            sb.Append('\n').Append("Sitemap: ").Append(Origin(ctx)).Append("/sitemap.xml\n");
            return Results.Text(sb.ToString(), "text/plain; charset=utf-8");
        }).AllowAnonymous();

        app.MapGet("/sitemap.xml", async (HttpContext ctx, IWebHostEnvironment env, Cloud.Data.ControlDbContext db, TimeProvider time) =>
        {
            var file = env.WebRootFileProvider.GetFileInfo(LandingFile);
            var lastMod = (file.Exists ? file.LastModified : DateTimeOffset.UtcNow).UtcDateTime
                .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var origin = System.Security.SecurityElement.Escape(Origin(ctx));
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
            sb.Append("<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">\n");
            sb.Append($"  <url>\n    <loc>{origin}/</loc>\n    <lastmod>{lastMod}</lastmod>\n");
            sb.Append("    <changefreq>monthly</changefreq>\n    <priority>1.0</priority>\n  </url>\n");
            // Every published shop menu: a shop gains from being found by its name.
            // Only menus that answer: a client without Online time has its menu offline (SubscriptionRules.MenuIsLive).
            var now = time.GetUtcNow().UtcDateTime;
            var menus = (await db.Menus.Where(m => m.IsPublished && m.Client.IsActive)
                    .Include(m => m.Client).ThenInclude(c => c.Subscription).AsNoTracking().ToListAsync())
                .Where(m => SubscriptionRules.MenuIsLive(m.Client, now));
            foreach (var m in menus)
                sb.Append($"  <url>\n    <loc>{origin}/m/{m.Slug}</loc>\n    <lastmod>{m.UpdatedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}</lastmod>\n  </url>\n");
            sb.Append("</urlset>\n");
            return Results.Text(sb.ToString(), "application/xml; charset=utf-8");
        }).AllowAnonymous();

        return app;
    }

    /// <summary>Puts every Razor page under /admin (Index becomes /admin), except /Enroll: till enrolment links already sent to shops must keep working.</summary>
    public sealed class AdminPrefixConvention : Microsoft.AspNetCore.Mvc.ApplicationModels.IPageRouteModelConvention
    {
        public void Apply(Microsoft.AspNetCore.Mvc.ApplicationModels.PageRouteModel model)
        {
            if (model.ViewEnginePath.Equals("/Enroll", StringComparison.OrdinalIgnoreCase)
                || model.ViewEnginePath.StartsWith("/Public/", StringComparison.OrdinalIgnoreCase))
                return;
            foreach (var selector in model.Selectors)
            {
                var route = selector.AttributeRouteModel;
                if (route?.Template is null)
                    continue;
                route.Template = string.IsNullOrEmpty(route.Template) ? AdminPrefix : $"{AdminPrefix}/{route.Template}";
            }
        }
    }

    /// <summary>Cloud:PublicUrl when set (production), otherwise the address the request came in on.</summary>
    public static string OriginOf(HttpContext ctx) => Origin(ctx);

    private static string Origin(HttpContext ctx)
    {
        var configured = ctx.RequestServices.GetRequiredService<IOptions<CloudOptions>>().Value.PublicUrl;
        if (!string.IsNullOrWhiteSpace(configured))
            return configured.Trim().TrimEnd('/');
        return $"{ctx.Request.Scheme}://{ctx.Request.Host}";
    }
}
