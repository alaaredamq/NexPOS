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
        group.MapGet("/rows", Rows);
        group.MapGet("/row-ids", RowIds);
    }

    /// <summary>Signs the caller in and checks the shop and the plan. Either the principal to serve, or the refusal to send.</summary>
    private static async Task<(OwnerPrincipal? Who, IResult? Refusal)> GateAsync(HttpContext http, ControlDbContext db, TimeProvider time)
    {
        var who = await OwnerAuth.AuthenticateAsync(http, db, time);
        if (who is null)
            return (null, Results.Json(new SyncError(SyncErrorCodes.Unauthorized, "Please sign in again."), SyncWire.Json, statusCode: StatusCodes.Status401Unauthorized));
        if (who.Site is null)
            return (null, Results.Json(new SyncError("no_shop", "No shop PC is connected to this account yet."), SyncWire.Json, statusCode: StatusCodes.Status409Conflict));

        // Reading follows the plan too: once it has lapsed for good the copy is no longer kept up to date.
        if (who.State is SubscriptionState.Expired or SubscriptionState.Suspended or SubscriptionState.None)
            return (null, Results.Json(new SyncError(SyncErrorCodes.SubscriptionInactive, "The online plan is not active. Contact your provider."),
                SyncWire.Json, statusCode: StatusCodes.Status402PaymentRequired));

        return (who, null);
    }

    /// <summary>Signs the caller in, opens their shop's copy, runs <paramref name="read"/>, and wraps the answer with its age.</summary>
    private static async Task<IResult> ReadShopAsync(
        HttpContext http, ControlDbContext db, TimeProvider time, MirrorReader reader,
        Func<ErpDbContext, Task<object>> read)
    {
        var (who, refusal) = await GateAsync(http, db, time);
        if (refusal is not null) return refusal;

        // These server-side reads (only possible for a shop that is not encrypted) are not split per table, so they are for full logins only.
        if (who!.User.Can != OwnerPermissions.All) return NotAllowed();

        // An end-to-end encrypted copy cannot be read here, by design. Saying so beats answering with empty lists.
        if (reader.HasEncryptedRows(who!.Site!.Id))
            return Results.Json(new SyncError("encrypted_copy", "This shop's data is end-to-end encrypted; the app must read it with the shop's key."),
                SyncWire.Json, statusCode: StatusCodes.Status409Conflict);

        await using var context = reader.Open(who.Site.Id);
        var data = await read(context);
        return Results.Json(new { asOfUtc = AsUtc(who.Site.LastSyncAt), data }, SyncWire.Json);
    }

    // ---- the encrypted copy: ciphertext out, decrypted on the phone with the shop's key ----

    private const int MaxRowsPerPage = 1000;

    /// <summary>
    /// One page of a table's encrypted rows after a cursor. The server only ever hands back what it was given; the phone holds the key.
    /// <c>nextCursor</c> is opaque: send it back as <c>cursor</c> to continue. <c>hasMore</c> says whether another page exists.
    /// </summary>
    private static async Task<IResult> Rows(HttpContext http, ControlDbContext db, TimeProvider time, MirrorStore mirrors,
        string? table, string? cursor, int? limit)
    {
        var (who, refusal) = await GateAsync(http, db, time);
        if (refusal is not null) return refusal;
        if (string.IsNullOrWhiteSpace(table) || table.Length > 100)
            return Results.Json(new SyncError("bad_request", "Say which table to read."), SyncWire.Json, statusCode: StatusCodes.Status400BadRequest);
        if (!OwnerRoles.MayRead(who!.User.Can, table)) return NotAllowed();
        if (!TryParseCursor(cursor, out var afterUpdatedAt, out var afterId))
            return Results.Json(new SyncError("bad_request", "That cursor is not one the cloud gave out."), SyncWire.Json, statusCode: StatusCodes.Status400BadRequest);

        var take = Math.Clamp(limit ?? 500, 1, MaxRowsPerPage);
        // One extra row tells us whether there is another page without a second query.
        var found = mirrors.ReadEncryptedRows(who!.Site!.Id, table, afterUpdatedAt, afterId, take + 1);
        var page = found.Take(take).ToList();
        var next = page.Count == 0 ? cursor : FormatCursor(page[^1]);
        return Results.Json(new
        {
            asOfUtc = AsUtc(who.Site.LastSyncAt),
            rows = page.Select(r => new { id = r.Id, payload = r.Payload }),
            nextCursor = next,
            hasMore = found.Count > take,
        }, SyncWire.Json);
    }

    /// <summary>Every row id the cloud holds for a table, so the phone can forget rows the shop deleted.</summary>
    private static async Task<IResult> RowIds(HttpContext http, ControlDbContext db, TimeProvider time, MirrorStore mirrors, string? table)
    {
        var (who, refusal) = await GateAsync(http, db, time);
        if (refusal is not null) return refusal;
        if (string.IsNullOrWhiteSpace(table) || table.Length > 100)
            return Results.Json(new SyncError("bad_request", "Say which table to read."), SyncWire.Json, statusCode: StatusCodes.Status400BadRequest);
        if (!OwnerRoles.MayRead(who!.User.Can, table)) return NotAllowed();

        return Results.Json(new { asOfUtc = AsUtc(who!.Site!.LastSyncAt), ids = mirrors.EncryptedRowIds(who.Site.Id, table) }, SyncWire.Json);
    }

    /// <summary>This login is not allowed to read that table. The phone skips it and hides the screens that need it.</summary>
    private static IResult NotAllowed()
        => Results.Json(new SyncError("forbidden", "This login is not allowed to see that."), SyncWire.Json, statusCode: StatusCodes.Status403Forbidden);

    private static string FormatCursor(MirrorStore.EncryptedRow row) => $"{row.UpdatedAt}|{row.Id}";

    private static bool TryParseCursor(string? cursor, out string? updatedAt, out long id)
    {
        updatedAt = null;
        id = 0;
        if (string.IsNullOrEmpty(cursor)) return true;
        var cut = cursor.LastIndexOf('|');
        if (cut <= 0 || !long.TryParse(cursor[(cut + 1)..], out id)) return false;
        updatedAt = cursor[..cut];
        return true;
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
