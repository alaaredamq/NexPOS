using Application.Services.Products;
using Infrastructure.Data;
using Infrastructure.Repositories.Products;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Tests.Auditing;

namespace Tests.TestHelpers;

/// <summary>A real product-history service over a test database (signed in as "alice", id 7).</summary>
public static class TestProductHistory
{
    public static ProductHistoryService Create(ErpDbContext context)
    {
        var services = new ServiceCollection().BuildServiceProvider();
        return new ProductHistoryService(
            new ProductHistoryRepository(context),
            new ProductRepository(context, services),
            new FakeAuditContext(),
            NullLogger<ProductHistoryService>.Instance);
    }
}
