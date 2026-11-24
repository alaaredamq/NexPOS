using Cloud.Data;
using Microsoft.EntityFrameworkCore;

namespace Cloud.Services;

/// <summary>
/// Removes branches from the cloud: their mirror file (the shop's data as the cloud holds it) and every control row that
/// points at them. Nothing at the shop is touched; the PC keeps selling and simply gets refused at its next contact.
/// </summary>
public sealed class SiteRemoval(ControlDbContext db, MirrorStore mirrors, MenuCatalogMerge menus, MenuImages images)
{
    public async Task RemoveAsync(IReadOnlyCollection<Guid> siteIds)
    {
        if (siteIds.Count == 0) return;

        // A menu that a removed branch filled loses that branch's prices; what only it sold stays, as hand-made dishes.
        foreach (var link in await db.MenuBranches.Include(b => b.Menu).Where(b => siteIds.Contains(b.SiteId)).ToListAsync())
            await menus.DetachAsync(link.Menu, link.SiteId);

        // Files first: if one cannot be deleted we stop with the control rows intact, instead of leaving a deleted
        // client's data behind with nothing in the portal pointing at it.
        foreach (var id in siteIds) await mirrors.DeleteAsync(id);

        await InTransactionAsync(async () =>
        {
            await db.DeviceTokens.Where(x => siteIds.Contains(x.SiteId)).ExecuteDeleteAsync();
            await db.EnrollmentCodes.Where(x => siteIds.Contains(x.SiteId)).ExecuteDeleteAsync();
            await db.SiteHeartbeats.Where(x => siteIds.Contains(x.SiteId)).ExecuteDeleteAsync();
            await db.SyncBatchLogs.Where(x => siteIds.Contains(x.SiteId)).ExecuteDeleteAsync();
            // A licence outlives the branch it was seen on: the key still works at the shop.
            await db.Licenses.Where(x => x.SiteId != null && siteIds.Contains(x.SiteId.Value)).ExecuteUpdateAsync(p => p.SetProperty(x => x.SiteId, (Guid?)null));
            await db.Sites.Where(x => siteIds.Contains(x.Id)).ExecuteDeleteAsync();
        });
    }

    /// <summary>
    /// Removes a client with all its branches, its subscription and its history. Its payments stay in the books (they were
    /// received), detached from the client but still carrying its name; so do its licence records (the keys still work).
    /// </summary>
    public async Task RemoveClientAsync(int clientId)
    {
        var siteIds = await db.Sites.Where(s => s.ClientId == clientId).Select(s => s.Id).ToListAsync();
        await RemoveAsync(siteIds);

        // The online menu and its pictures.
        var menuIds = await db.Menus.Where(m => m.ClientId == clientId).Select(m => m.Id).ToListAsync();
        var files = await db.MenuItems.Where(i => menuIds.Contains(i.MenuId) && i.ImageFile != null).Select(i => i.ImageFile)
            .Concat(db.Menus.Where(m => m.ClientId == clientId && m.LogoFile != null).Select(m => m.LogoFile))
            .Concat(db.Menus.Where(m => m.ClientId == clientId && m.CoverFile != null).Select(m => m.CoverFile))
            .Concat(db.MenuSections.Where(s => menuIds.Contains(s.MenuId) && s.ImageFile != null).Select(s => s.ImageFile)).ToListAsync();

        await InTransactionAsync(async () =>
        {
            // Phone sign-ins belong to the client (restricted FK), so they must go before the client row does.
            await db.OwnerSessions.Where(x => x.OwnerUser.ClientId == clientId).ExecuteDeleteAsync();
            await db.OwnerUsers.Where(x => x.ClientId == clientId).ExecuteDeleteAsync();
            await db.Subscriptions.Where(x => x.ClientId == clientId).ExecuteDeleteAsync();
            await db.ClientEvents.Where(x => x.ClientId == clientId).ExecuteDeleteAsync();
            await db.Payments.Where(x => x.ClientId == clientId).ExecuteUpdateAsync(p => p.SetProperty(x => x.ClientId, (int?)null));
            await db.Licenses.Where(x => x.ClientId == clientId).ExecuteUpdateAsync(p => p.SetProperty(x => x.ClientId, (int?)null));
            await db.MenuItems.Where(x => menuIds.Contains(x.MenuId)).ExecuteDeleteAsync(); // their branch and type prices go with them
            await db.MenuOrderTypes.Where(x => menuIds.Contains(x.MenuId)).ExecuteDeleteAsync();
            await db.MenuBranches.Where(x => menuIds.Contains(x.MenuId)).ExecuteDeleteAsync();
            await db.MenuSections.Where(x => menuIds.Contains(x.MenuId)).ExecuteDeleteAsync();
            await db.Menus.Where(x => x.ClientId == clientId).ExecuteDeleteAsync();
            await db.Clients.Where(x => x.Id == clientId).ExecuteDeleteAsync();
        });
        foreach (var f in files) images.Delete(f);
    }

    /// <summary>
    /// Runs the deletes as one transaction through the context's execution strategy. On SQL Server that strategy retries
    /// dropped connections, and it refuses a transaction opened outside it ("does not support user-initiated transactions"),
    /// which made every Remove a 500 on the live site. A retry runs the whole block again; every step is a plain delete, so
    /// running it twice is harmless.
    /// </summary>
    private Task InTransactionAsync(Func<Task> work)
        => db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            await work();
            await tx.CommitAsync();
        });
}
