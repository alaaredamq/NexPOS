using Cloud.Data;
using Microsoft.EntityFrameworkCore;

namespace Cloud.Services;

/// <summary>
/// Removes branches from the cloud: their mirror file (the shop's data as the cloud holds it) and every control row that
/// points at them. Nothing at the shop is touched; the PC keeps selling and simply gets refused at its next contact.
/// </summary>
public sealed class SiteRemoval(ControlDbContext db, MirrorStore mirrors)
{
    public async Task RemoveAsync(IReadOnlyCollection<Guid> siteIds)
    {
        if (siteIds.Count == 0) return;

        // Files first: if one cannot be deleted we stop with the control rows intact, instead of leaving a deleted
        // client's data behind with nothing in the portal pointing at it.
        foreach (var id in siteIds) await mirrors.DeleteAsync(id);

        await using var tx = await db.Database.BeginTransactionAsync();
        await db.DeviceTokens.Where(x => siteIds.Contains(x.SiteId)).ExecuteDeleteAsync();
        await db.EnrollmentCodes.Where(x => siteIds.Contains(x.SiteId)).ExecuteDeleteAsync();
        await db.SiteHeartbeats.Where(x => siteIds.Contains(x.SiteId)).ExecuteDeleteAsync();
        await db.SyncBatchLogs.Where(x => siteIds.Contains(x.SiteId)).ExecuteDeleteAsync();
        await db.Sites.Where(x => siteIds.Contains(x.Id)).ExecuteDeleteAsync();
        await tx.CommitAsync();
    }

    /// <summary>Removes a client with all its branches, its subscription and its history.</summary>
    public async Task RemoveClientAsync(int clientId)
    {
        var siteIds = await db.Sites.Where(s => s.ClientId == clientId).Select(s => s.Id).ToListAsync();
        await RemoveAsync(siteIds);

        await using var tx = await db.Database.BeginTransactionAsync();
        // Phone sign-ins belong to the client (restricted FK), so they must go before the client row does.
        await db.OwnerSessions.Where(x => x.OwnerUser.ClientId == clientId).ExecuteDeleteAsync();
        await db.OwnerUsers.Where(x => x.ClientId == clientId).ExecuteDeleteAsync();
        await db.Subscriptions.Where(x => x.ClientId == clientId).ExecuteDeleteAsync();
        await db.ClientEvents.Where(x => x.ClientId == clientId).ExecuteDeleteAsync();
        await db.Clients.Where(x => x.Id == clientId).ExecuteDeleteAsync();
        await tx.CommitAsync();
    }
}
