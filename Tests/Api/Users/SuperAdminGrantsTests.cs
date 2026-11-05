using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tests.Api.Catalog;
using Tests.Api.Infrastructure;
using Web.Helpers;
using Web.Services;
using Xunit;

namespace Tests.Api.Users;

/// <summary>SuperAdmin must hold every permission as stored grants too, not only in code - including ones a newer version adds.</summary>
[Collection(ApiCollection.Name)]
public sealed class SuperAdminGrantsTests : CatalogApiTestBase
{
    public SuperAdminGrantsTests(ApiFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task Startup_gives_super_admin_a_stored_grant_for_every_permission_even_after_one_went_missing()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ErpDbContext>();
        var superAdmin = await db.Roles.SingleAsync(r => r.IsSystemRole && r.Name == PermissionHelper.Roles.SuperAdmin);

        // A permission that is new in this version has no grant yet; an old row may also have been switched off.
        var purchases = await db.Permissions.SingleAsync(p => p.Name == PermissionHelper.Permissions.Purchases_View);
        db.RolePermissions.RemoveRange(db.RolePermissions.Where(rp => rp.RoleId == superAdmin.Id && rp.PermissionId == purchases.Id));
        var cancel = await db.Permissions.SingleAsync(p => p.Name == PermissionHelper.Permissions.Purchases_Cancel);
        var denied = await db.RolePermissions.SingleOrDefaultAsync(rp => rp.RoleId == superAdmin.Id && rp.PermissionId == cancel.Id);
        if (denied is not null) denied.IsGranted = false;
        await db.SaveChangesAsync();

        await scope.ServiceProvider.GetRequiredService<IRoleAccessService>().EnsureCatalogAsync();

        db.ChangeTracker.Clear();
        var granted = await db.RolePermissions
            .Where(rp => rp.RoleId == superAdmin.Id && rp.IsGranted)
            .Select(rp => rp.Permission.Name)
            .ToListAsync();
        Assert.All(PermissionHelper.Permissions.All, name => Assert.Contains(name, granted));

        // Idempotent: running it again adds nothing.
        var before = await db.RolePermissions.CountAsync(rp => rp.RoleId == superAdmin.Id);
        await scope.ServiceProvider.GetRequiredService<IRoleAccessService>().EnsureCatalogAsync();
        Assert.Equal(before, await db.RolePermissions.CountAsync(rp => rp.RoleId == superAdmin.Id));
    }
}
