using Core.Entities.Users;
using Infrastructure.Repositories.Users;
using Microsoft.Extensions.DependencyInjection;
using Tests.TestHelpers;
using Xunit;

namespace Tests.Repositories;

/// <summary>
/// An online guest types their phone however they like; the till finds the customer with that number however it was saved,
/// so the cashier can be offered "this is Maya Saab" instead of searching.
/// </summary>
public class CustomerPhoneMatchTests : IDisposable
{
    private readonly SqliteInMemoryDbContextFactory _dbFactory = new();
    private readonly IServiceProvider _services = new ServiceCollection().BuildServiceProvider();

    public void Dispose() => _dbFactory.Dispose();

    [Theory]
    [InlineData("+961 70 123 456", "70123456", true)]
    [InlineData("0096170123456", "70-123-456", true)]
    [InlineData("+961 1 234 567", "01 234 567", true)]   // country code instead of the leading 0
    [InlineData("70 123 456", "71 123 456", false)]
    [InlineData("12345", "12345", false)]                 // too short to tell anyone apart
    [InlineData(null, "70123456", false)]
    public void Two_numbers_are_the_same_whatever_way_they_are_written(string? a, string? b, bool same)
        => Assert.Equal(same, PhoneMatch.Same(a, b));

    [Fact]
    public async Task The_customer_saved_with_that_number_is_found_and_no_other()
    {
        using var context = _dbFactory.CreateContext();
        context.Customers.AddRange(
            new Customer("Maya", "Saab", "", "70 123 456"),
            new Customer("Rami", "Haddad", "", "71123456"),
            new Customer("Nour", "Khalil", "", null));
        await context.SaveChangesAsync();
        var repo = new CustomerRepository(context, _services);

        var found = await repo.FindByPhoneAsync("+961 70-123456");

        Assert.Equal("Maya", Assert.Single(found).FirstName);
        Assert.Empty(await repo.FindByPhoneAsync("123"));
    }
}
