using Application.Services.Products;
using Application.Services.Users;
using Core.Entities.Products;
using Core.Entities.Users;
using Core.ICache;
using Infrastructure.Data;
using Infrastructure.Repositories.Products;
using Infrastructure.Repositories.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Tests.TestHelpers;
using Xunit;

namespace Tests.Services;

/// <summary>
/// Deleting a category or supplier that products (including soft-deleted ones kept for sales history)
/// still point at used to surface EF's raw "error occurred while saving the entity changes" text.
/// It must now be a clear, actionable message - and an unused one must still delete normally.
/// </summary>
public class DeleteGuardTests : IDisposable
{
    private readonly SqliteInMemoryDbContextFactory _dbFactory = new();
    private readonly IServiceProvider _emptyServiceProvider = new ServiceCollection().BuildServiceProvider();

    [Fact]
    public async Task DeleteCategory_StillUsedBySoftDeletedProduct_ThrowsFriendlyMessage()
    {
        using var context = _dbFactory.CreateContext();
        var category = new ProductCategory("Used Category");
        context.Set<ProductCategory>().Add(category);
        await context.SaveChangesAsync();
        context.Products.Add(NewProduct("DEL-001", category.Id, supplierId: 1, isDeleted: true));
        await context.SaveChangesAsync();

        var sut = new ProductCategoryService(new ProductCategoryRepository(context, _emptyServiceProvider), Mock.Of<ICacheService>());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => sut.DeleteAsync(category.Id));
        Assert.Contains("Deactivate", ex.Message);
        Assert.DoesNotContain("entity changes", ex.Message);
    }

    [Fact]
    public async Task DeleteCategory_Unused_IsRemoved()
    {
        using var context = _dbFactory.CreateContext();
        var category = new ProductCategory("Unused Category");
        context.Set<ProductCategory>().Add(category);
        await context.SaveChangesAsync();

        var sut = new ProductCategoryService(new ProductCategoryRepository(context, _emptyServiceProvider), Mock.Of<ICacheService>());
        await sut.DeleteAsync(category.Id);

        Assert.Null(await context.Set<ProductCategory>().FindAsync(category.Id));
    }

    [Fact]
    public async Task DeleteSupplier_StillUsedByProduct_ThrowsFriendlyMessage()
    {
        using var context = _dbFactory.CreateContext();
        var supplier = new Supplier("Used Supplier") { CurrencyId = 1 };
        context.Set<Supplier>().Add(supplier);
        await context.SaveChangesAsync();
        context.Products.Add(NewProduct("DEL-002", categoryId: 1, supplier.Id, isDeleted: false));
        await context.SaveChangesAsync();

        var sut = new SupplierService(new SupplierRepository(context, _emptyServiceProvider));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => sut.DeleteAsync(supplier.Id));
        Assert.Contains("Deactivate", ex.Message);
        Assert.DoesNotContain("association between entity types", ex.Message);
    }

    [Theory]
    [InlineData(25.5)]  // customer owes the shop
    [InlineData(-10)]   // shop owes the customer
    public async Task DeleteCustomer_WithOutstandingBalance_IsRefused(double balance)
    {
        using var context = _dbFactory.CreateContext();
        var customer = new Customer("Debt", "Holder", "debt@example.test") { Balance = (decimal)balance };
        context.Customers.Add(customer);
        await context.SaveChangesAsync();

        var sut = new CustomerService(new CustomerRepository(context, _emptyServiceProvider));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => sut.DeleteAsync(customer.Id));
        Assert.Contains("outstanding balance", ex.Message);
        Assert.False((await context.Customers.IgnoreQueryFilters().SingleAsync(c => c.Id == customer.Id)).IsDeleted);
    }

    [Fact]
    public async Task DeleteCustomer_SettledBalance_IsSoftDeleted()
    {
        using var context = _dbFactory.CreateContext();
        var customer = new Customer("Settled", "Customer", "settled@example.test") { Balance = 0 };
        context.Customers.Add(customer);
        await context.SaveChangesAsync();

        var sut = new CustomerService(new CustomerRepository(context, _emptyServiceProvider));
        await sut.DeleteAsync(customer.Id);

        Assert.True((await context.Customers.IgnoreQueryFilters().SingleAsync(c => c.Id == customer.Id)).IsDeleted);
    }

    [Fact]
    public async Task DeleteSupplier_WithOutstandingBalance_IsRefused()
    {
        using var context = _dbFactory.CreateContext();
        var supplier = new Supplier("Owed Supplier") { CurrencyId = 1, Balance = 40m };
        context.Set<Supplier>().Add(supplier);
        await context.SaveChangesAsync();

        var sut = new SupplierService(new SupplierRepository(context, _emptyServiceProvider));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => sut.DeleteAsync(supplier.Id));
        Assert.Contains("outstanding balance", ex.Message);
        Assert.NotNull(await context.Set<Supplier>().FindAsync(supplier.Id));
    }

    private static Product NewProduct(string barcode, int categoryId, int supplierId, bool isDeleted) => new()
    {
        Name = "Guarded " + barcode,
        Barcode = barcode,
        SellingPrice = 1m,
        StockQuantity = 5,
        SupplierId = supplierId,
        CurrencyId = 1,
        CategoryId = categoryId,
        IsActive = true,
        IsDeleted = isDeleted
    };

    public void Dispose() => _dbFactory.Dispose();
}
