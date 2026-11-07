using Cloud.Data;
using Cloud.Services;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Sync;

namespace Cloud.Api;

/// <summary>
/// What the owner's phone reads: the cloud copy of the shop, as of the last sync. Every answer says how fresh it is
/// (<c>asOfUtc</c>), because a shop PC that is switched off leaves the copy behind and an old figure must not look current.
/// </summary>
internal static class OwnerReadEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/lookups", Lookups);
    }

    /// <summary>Signs the caller in, opens their shop's copy, runs <paramref name="read"/>, and wraps the answer with its age.</summary>
    private static async Task<IResult> ReadShopAsync(
        HttpContext http, ControlDbContext db, TimeProvider time, MirrorReader reader,
        Func<ErpDbContext, Task<object>> read)
    {
        var who = await OwnerAuth.AuthenticateAsync(http, db, time);
        if (who is null)
            return Results.Json(new SyncError(SyncErrorCodes.Unauthorized, "Please sign in again."), SyncWire.Json, statusCode: StatusCodes.Status401Unauthorized);
        if (who.Site is null)
            return Results.Json(new SyncError("no_shop", "No shop PC is connected to this account yet."), SyncWire.Json, statusCode: StatusCodes.Status409Conflict);

        // Reading follows the plan too: once it has lapsed for good the copy is no longer kept up to date.
        if (who.State is SubscriptionState.Expired or SubscriptionState.Suspended or SubscriptionState.None)
            return Results.Json(new SyncError(SyncErrorCodes.SubscriptionInactive, "The online plan is not active. Contact your provider."),
                SyncWire.Json, statusCode: StatusCodes.Status402PaymentRequired);

        await using var context = reader.Open(who.Site.Id);
        var data = await read(context);
        return Results.Json(new { asOfUtc = AsUtc(who.Site.LastSyncAt), data }, SyncWire.Json);
    }

    private static DateTime? AsUtc(DateTime? value) => value is null ? null : DateTime.SpecifyKind(value.Value, DateTimeKind.Utc);

    private static Task<IResult> Lookups(HttpContext http, ControlDbContext db, TimeProvider time, MirrorReader reader)
        => ReadShopAsync(http, db, time, reader, async context =>
        {
            var categories = await context.ProductCategories.Where(c => c.IsActive).OrderBy(c => c.Name)
                .Select(c => new { id = c.Id, name = c.Name }).ToListAsync();
            var suppliers = await context.Suppliers.Where(s => s.IsActive).OrderBy(s => s.OrderNumber == null).ThenBy(s => s.OrderNumber).ThenBy(s => s.Name)
                .Select(s => new { id = s.Id, name = s.Name }).ToListAsync();
            var currencies = await context.Currencies.Where(c => c.IsActive).OrderByDescending(c => c.IsDefault).ThenBy(c => c.Name)
                .Select(c => new { id = c.Id, code = c.Code, notation = c.Notation, isDefault = c.IsDefault }).ToListAsync();

            return (object)new
            {
                categories,
                suppliers,
                currencies,
                defaultCurrencyId = currencies.FirstOrDefault(c => c.isDefault)?.id,
            };
        });
}
