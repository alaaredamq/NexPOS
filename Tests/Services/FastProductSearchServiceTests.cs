using Application.Services.Products;
using Core.DTOs;
using Core.ICache;
using Core.IRepositories.Products;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Tests.Services;

/// <summary>
/// The POS search box used to get a hard cap of 5 name matches. It now pages through all of them,
/// so <c>skip</c> has to walk the same ordered match list without dropping or repeating rows.
/// </summary>
public class FastProductSearchServiceTests
{
    private static FastProductSearchService CreateSut(IEnumerable<string> productNames)
    {
        var products = productNames
            .Select((name, i) => new ProductDTO { Id = i + 1, Name = name })
            .ToList();

        var cache = new Mock<ICacheService>();
        cache.Setup(c => c.Exists(It.IsAny<string>())).Returns(true); // already warm, so no repository refresh
        cache.Setup(c => c.GetPOSProductsAsync()).ReturnsAsync(products);

        return new FastProductSearchService(
            Mock.Of<IProductRepository>(),
            cache.Object,
            Mock.Of<ILogger<FastProductSearchService>>());
    }

    [Fact]
    public async Task SearchByName_MoreMatchesThanOnePage_SkipWalksThemAll()
    {
        var sut = CreateSut(Enumerable.Range(1, 45).Select(i => $"Cola {i}").Append("Water"));

        var first = (await sut.SearchByNameAsync("cola", 20, 0)).ToList();
        var second = (await sut.SearchByNameAsync("cola", 20, 20)).ToList();
        var third = (await sut.SearchByNameAsync("cola", 20, 40)).ToList();

        Assert.Equal(20, first.Count);
        Assert.Equal(20, second.Count);
        Assert.Equal(5, third.Count);
        Assert.Equal(45, first.Concat(second).Concat(third).Select(p => p.Id).Distinct().Count());
        Assert.DoesNotContain(first.Concat(second).Concat(third), p => p.Name == "Water");
    }

    [Fact]
    public async Task SearchByName_SkipPastLastMatch_ReturnsEmpty()
    {
        var sut = CreateSut(["Cola", "Cola Zero"]);

        var result = await sut.SearchByNameAsync("cola", 20, 2);

        Assert.Empty(result);
    }

    [Fact]
    public async Task SearchByName_NoSkip_StillBehavesLikeBefore()
    {
        var sut = CreateSut(["Pepsi", "pepsi max", "Cola"]);

        var result = (await sut.SearchByNameAsync("PEPSI", 10)).ToList();

        Assert.Equal(["Pepsi", "pepsi max"], result.Select(p => p.Name));
    }

    [Fact]
    public async Task SearchByName_NegativeSkip_IsTreatedAsZero()
    {
        var sut = CreateSut(["Cola", "Cola Zero"]);

        var result = await sut.SearchByNameAsync("cola", 20, -5);

        Assert.Equal(2, result.Count());
    }
}
