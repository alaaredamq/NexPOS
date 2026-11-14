using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Cloud.Data;
using Cloud.Services;
using Cloud.Tests.Support;
using Core.Entities.Products;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Cloud.Tests;

/// <summary>What the owner's phone reads from the cloud copy of the shop: signed-in only, only its own shop, never writable.</summary>
public class OwnerReadTests : IClassFixture<CloudFactory>
{
    private readonly CloudFactory _cloud;

    public OwnerReadTests(CloudFactory cloud) => _cloud = cloud;

    private async Task<(SeededSite Site, HttpClient Http)> SeedOwnerAsync(string email, string clientName, int months = 1)
    {
        var site = await _cloud.SeedSiteAsync(months, clientName);
        await _cloud.WithControlDbAsync(async db =>
        {
            db.OwnerUsers.Add(new OwnerUser
            {
                ClientId = site.ClientId, Email = email, DisplayName = "Owner",
                PasswordHash = PasswordHasher.Hash("pass-1234-ok"), CreatedAt = _cloud.Clock.GetUtcNow().UtcDateTime,
            });
            await db.SaveChangesAsync();
            return 0;
        });
        var http = _cloud.CreateClient();
        var login = await http.PostAsJsonAsync("/owner/v1/auth/login", new { email, password = "pass-1234-ok" });
        var token = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (site, http);
    }

    private ErpDbContext MirrorOf(Guid siteId)
    {
        _cloud.Mirrors.EnsureReady(siteId);
        return new ErpDbContext(new DbContextOptionsBuilder<ErpDbContext>().UseSqlite($"Data Source={_cloud.Mirrors.PathFor(siteId)}").Options);
    }

    [Fact]
    public async Task Reading_needs_a_sign_in()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _cloud.CreateClient().GetAsync("/owner/v1/lookups")).StatusCode);
    }

    [Fact]
    public async Task Lookups_list_the_shops_active_categories_and_say_how_old_they_are()
    {
        var owner = await SeedOwnerAsync("lookups@shop.test", "Lookup Shop");
        using (var mirror = MirrorOf(owner.Site.SiteId))
        {
            mirror.ProductCategories.Add(new ProductCategory("Dairy"));
            mirror.ProductCategories.Add(new ProductCategory("Retired") { IsActive = false });
            await mirror.SaveChangesAsync();
        }

        var answer = await owner.Http.GetFromJsonAsync<JsonElement>("/owner/v1/lookups");

        var names = answer.GetProperty("data").GetProperty("categories").EnumerateArray().Select(c => c.GetProperty("name").GetString()).ToList();
        Assert.Contains("Dairy", names);
        Assert.DoesNotContain("Retired", names);
        Assert.True(answer.TryGetProperty("asOfUtc", out _), "every read says how fresh it is");
    }

    [Fact]
    public async Task An_owner_only_ever_reads_their_own_shop()
    {
        var alice = await SeedOwnerAsync("alice-read@shop.test", "Alice Read");
        var bob = await SeedOwnerAsync("bob-read@shop.test", "Bob Read");
        using (var mirror = MirrorOf(alice.Site.SiteId))
        {
            mirror.ProductCategories.Add(new ProductCategory("Alices Secret Aisle"));
            await mirror.SaveChangesAsync();
        }
        MirrorOf(bob.Site.SiteId).Dispose();

        var bobs = await bob.Http.GetStringAsync("/owner/v1/lookups");

        Assert.DoesNotContain("Alices Secret Aisle", bobs);
    }

    [Fact]
    public async Task The_cloud_copy_is_opened_read_only()
    {
        var owner = await SeedOwnerAsync("readonly@shop.test", "Readonly Shop");
        await using var context = _cloud.Services.GetRequiredService<MirrorReader>().Open(owner.Site.SiteId);

        context.ProductCategories.Add(new ProductCategory("Sneaky"));

        await Assert.ThrowsAnyAsync<Exception>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Reading_stops_when_the_plan_has_lapsed()
    {
        var owner = await SeedOwnerAsync("lapsed@shop.test", "Lapsed Shop");
        Assert.Equal(HttpStatusCode.OK, (await owner.Http.GetAsync("/owner/v1/lookups")).StatusCode);

        await _cloud.WithControlDbAsync(async db =>
        {
            // Ended well before the grace week: the phone's sign-in is still good, the plan is not.
            var subscription = await db.Subscriptions.FirstAsync(s => s.ClientId == owner.Site.ClientId);
            subscription.ExpiresAt = _cloud.Clock.GetUtcNow().UtcDateTime.AddDays(-30);
            await db.SaveChangesAsync();
            return 0;
        });

        Assert.Equal(HttpStatusCode.PaymentRequired, (await owner.Http.GetAsync("/owner/v1/lookups")).StatusCode);
    }
}
