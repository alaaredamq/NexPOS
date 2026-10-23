using Core.Auditing;
using Core.Entities.Products;
using Infrastructure.Auditing;
using Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Tests.Auditing;

/// <summary>
/// The dependency-injection wiring, built the way Program.cs builds it (AddDbContext + AddDbContextFactory +
/// AddAuditTrail). EF Core 9 runs every registered configuration against the same options, so a careless
/// registration attaches each interceptor twice - which silently doubled every bulk-write entry in the first
/// end-to-end run. These tests pin that down.
/// </summary>
public sealed class AuditWiringTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly CollectingAuditTrail _trail = new();
    private readonly ServiceProvider _provider;

    public AuditWiringTests()
    {
        _connection.Open();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IAuditContext>(new FakeAuditContext());
        services.AddDbContext<ErpDbContext>(options => options.UseSqlite(_connection));
        services.AddDbContextFactory<ErpDbContext>(options => options.UseSqlite(_connection), ServiceLifetime.Scoped);
        services.AddAuditTrail("DataSource=:memory:", Path.Combine(Path.GetTempPath(), "nexpos-wiring-test"));
        services.AddSingleton<IAuditTrail>(_trail); // last registration wins: collect instead of writing to disk

        _provider = services.BuildServiceProvider();

        using var seedScope = _provider.CreateScope();
        // Build the schema through a context with no interceptors so the seed rows aren't "audited".
        using var plain = new ErpDbContext(new DbContextOptionsBuilder<ErpDbContext>().UseSqlite(_connection).Options);
        plain.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _provider.Dispose();
        _connection.Dispose();
    }

    private static List<T> InterceptorsOf<T>(DbContext context)
    {
        var extension = ((IInfrastructure<IServiceProvider>)context).Instance
            .GetRequiredService<IDbContextOptions>()
            .FindExtension<Microsoft.EntityFrameworkCore.Infrastructure.CoreOptionsExtension>();
        return extension?.Interceptors?.OfType<T>().ToList() ?? [];
    }

    [Fact]
    public void ContextFromDi_HasEachAuditInterceptorExactlyOnce()
    {
        using var scope = _provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ErpDbContext>();

        Assert.Single(InterceptorsOf<AuditSaveChangesInterceptor>(context));
        Assert.Single(InterceptorsOf<AuditTransactionInterceptor>(context));
        Assert.Single(InterceptorsOf<AuditCommandInterceptor>(context));
    }

    [Fact]
    public void ContextFromTheFactory_IsAuditedToo_AndAlsoExactlyOnce()
    {
        // CacheService and friends build their own contexts through the factory; they must not escape the trail.
        using var scope = _provider.CreateScope();
        using var context = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ErpDbContext>>().CreateDbContext();

        Assert.Single(InterceptorsOf<AuditSaveChangesInterceptor>(context));
        Assert.Single(InterceptorsOf<AuditTransactionInterceptor>(context));
        Assert.Single(InterceptorsOf<AuditCommandInterceptor>(context));
    }

    [Fact]
    public async Task OneBulkUpdate_ProducesOneEntry_NotTwo()
    {
        using var scope = _provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ErpDbContext>();
        var category = new ProductCategory("Beverages");
        context.ProductCategories.Add(category);
        await context.SaveChangesAsync();
        _trail.Clear();

        await context.ProductCategories.Where(c => c.Id == category.Id).ExecuteUpdateAsync(s => s.SetProperty(c => c.IsActive, false));

        Assert.Single(_trail.Entries);
    }

    [Fact]
    public async Task OneSave_ProducesOneEntryPerRecord_ThroughTheFactoryToo()
    {
        using var scope = _provider.CreateScope();
        await using var context = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ErpDbContext>>().CreateDbContext();

        context.ProductCategories.Add(new ProductCategory("Via factory"));
        await context.SaveChangesAsync();

        Assert.Equal("Via factory", Assert.Single(_trail.Entries).EntityLabel);
    }
}
