using System.Globalization;
using Cloud.Data;
using Cloud.Services;
using Microsoft.EntityFrameworkCore;

namespace Cloud.Api;

/// <summary>POS licence keys: issue them online, record keys signed elsewhere (the desktop keygen does this by itself), and follow them.</summary>
public static partial class AdminEndpoints
{
    private static void MapLicenses(RouteGroupBuilder g)
    {
        var l = g.MapGroup("/licenses");
        l.MapGet("", ListLicenses);
        l.MapGet("/signer", Signer);
        l.MapGet("/{id:int}", GetLicense);
        l.MapPost("/issue", IssueLicense);
        l.MapPost("/record", RecordLicense);
        l.MapPost("/import", ImportLicenses);
        l.MapPut("/{id:int}", UpdateLicense);
        l.MapDelete("/{id:int}", DeleteLicense);
        g.MapGet("/clients/{id:int}/licenses", ClientLicenses);
    }

    private static object LicenseView(LicenseRow r)
    {
        var l = r.License;
        return new
        {
            id = l.Id, clientId = l.ClientId, clientName = r.ClientName, siteId = l.SiteId,
            machineId = l.MachineId, customer = l.Customer, kind = l.Kind, plan = l.Plan, seats = l.Seats,
            issuedOn = Iso(l.IssuedOn), validUntil = Iso(l.ValidUntil), timeLimited = LicenseRules.IsTimeLimited(l),
            status = r.Status.ToString(), statusLabel = LicenseRules.Pill(r.Status).Label,
            serial = l.Serial, key = l.KeyText, source = l.Source, paymentId = l.PaymentId, note = l.Note,
            createdAtUtc = l.CreatedAt, createdBy = l.CreatedBy,
        };
    }

    private static async Task<object?> LicenseByIdAsync(LicenseBook book, int id)
        => (await book.AllAsync()).FirstOrDefault(r => r.License.Id == id) is { } row ? LicenseView(row) : null;

    /// <summary>
    /// <c>show</c>: current (default: the key in force on each PC), attention (ending or ended), unassigned (no client), all (with replaced keys).
    /// <c>search</c> matches the client, the name in the key, the Machine ID or the serial.
    /// </summary>
    private static async Task<IResult> ListLicenses(LicenseBook book, string? show, string? search, int? clientId)
    {
        IEnumerable<LicenseRow> rows = await book.AllAsync();
        rows = show switch
        {
            "all" => rows,
            "unassigned" => rows.Where(r => r.License.ClientId is null && r.Status != LicenseStatus.Superseded),
            "attention" => rows.Where(r => r.Status is LicenseStatus.EndingSoon or LicenseStatus.Grace or LicenseStatus.Expired or LicenseStatus.UpdatesEnding),
            _ => rows.Where(r => r.Status != LicenseStatus.Superseded),
        };
        if (clientId is { } cid) rows = rows.Where(r => r.License.ClientId == cid);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var q = search.Trim();
            rows = rows.Where(r => (r.ClientName?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
                                   || r.License.Customer.Contains(q, StringComparison.OrdinalIgnoreCase)
                                   || r.License.MachineId.Contains(q, StringComparison.OrdinalIgnoreCase)
                                   || r.License.Serial.Contains(q, StringComparison.OrdinalIgnoreCase));
        }
        return Json(rows.Select(LicenseView));
    }

    private static async Task<IResult> ClientLicenses(int id, ControlDbContext db, LicenseBook book)
        => await db.Clients.AnyAsync(c => c.Id == id)
            ? Json((await book.ForClientAsync(id)).Select(LicenseView))
            : NotFound("That client");

    private static async Task<IResult> GetLicense(int id, LicenseBook book)
        => await LicenseByIdAsync(book, id) is { } view ? Json(view) : NotFound("That licence");

    /// <summary>Whether keys can be issued here now, and if not, why.</summary>
    private static IResult Signer(LicenseSigner signer)
        => Json(new { state = signer.State.ToString(), ready = signer.State == SignerState.Ready, message = LicenseSigner.Explain(signer.State) });

    public sealed record IssueRequest(
        int? ClientId, string? MachineId, string? Customer, string? Kind, string? Plan, int? Seats, int? Days, DateOnly? Until,
        string? Note, decimal? PaymentAmount, string? PaymentMethod, string? Passphrase);

    /// <summary>Signs a new key on the server (the passphrase unlocks the signing key for this one call) and records it.</summary>
    private static async Task<IResult> IssueLicense(IssueRequest body, HttpContext http, LicenseBook book)
    {
        try
        {
            var license = await book.IssueAsync(new IssueInput
            {
                ClientId = body.ClientId, MachineId = body.MachineId, Customer = body.Customer, Kind = body.Kind, Plan = body.Plan,
                Seats = body.Seats ?? 1, Days = body.Days, Until = body.Until, Note = body.Note,
                PaymentAmount = body.PaymentAmount?.ToString(CultureInfo.InvariantCulture), PaymentMethod = body.PaymentMethod,
            }, body.Passphrase, Who(http));
            var view = (await LicenseByIdAsync(book, license.Id))!;
            return Json(new
            {
                license = view,
                whatsAppUrl = "https://wa.me/?text=" + Uri.EscapeDataString(
                    "Your Coduo POS licence key. Open Coduo POS > License, paste it and press Activate.\n" +
                    "مفتاح ترخيص Coduo POS. افتح Coduo POS ثم الترخيص (License)، الصقه واضغط Activate.\n\n" + license.KeyText),
            }, StatusCodes.Status201Created);
        }
        catch (AdminRuleException refused) { return Refused(refused.Code, refused.Message); }
        catch (LicenseSignException refused)
        {
            var status = refused.Code switch
            {
                "rate_limited" => StatusCodes.Status429TooManyRequests,
                "wrong_passphrase" or "passphrase_required" => StatusCodes.Status400BadRequest,
                _ => StatusCodes.Status409Conflict, // the server is not set up to sign
            };
            return Error(status, refused.Code, refused.Message);
        }
    }

    public sealed record RecordRequest(string? Key, int? ClientId, string? Source, string? Note);

    /// <summary>
    /// Records a key signed elsewhere. The desktop keygen calls this after each key (source "Keygen"); a pasted key is "Pasted".
    /// 201 when new, 200 when it was already on record.
    /// </summary>
    private static async Task<IResult> RecordLicense(RecordRequest body, HttpContext http, LicenseBook book)
    {
        try
        {
            var source = string.Equals(body.Source, LicenseSources.Keygen, StringComparison.OrdinalIgnoreCase) ? LicenseSources.Keygen : LicenseSources.Pasted;
            var (license, created) = await book.RecordKeyAsync(body.Key, body.ClientId, source, Who(http), body.Note);
            return Json((await LicenseByIdAsync(book, license.Id))!, created ? StatusCodes.Status201Created : StatusCodes.Status200OK);
        }
        catch (AdminRuleException refused) { return Refused(refused.Code, refused.Message); }
    }

    /// <summary>Imports an issued.csv sent as the request body (text/csv).</summary>
    private static async Task<IResult> ImportLicenses(HttpContext http, LicenseBook book)
    {
        if (http.Request.ContentLength > 4 * 1024 * 1024) return Refused("bad_csv", "That file is too large for an issued.csv.");
        using var reader = new StreamReader(http.Request.Body);
        try
        {
            var result = await book.ImportCsvAsync(await reader.ReadToEndAsync(), Who(http));
            return Json(new { added = result.Added, alreadyKnown = result.AlreadyKnown, linked = result.Assigned, problems = result.Problems });
        }
        catch (AdminRuleException refused) { return Refused(refused.Code, refused.Message); }
    }

    /// <summary>Links the record to a client (null unlinks it); <c>note</c> is replaced when sent.</summary>
    public sealed record LicenseUpdateRequest(int? ClientId, string? Note);

    private static async Task<IResult> UpdateLicense(int id, HttpContext http, LicenseBook book)
    {
        // Read the body by hand: "clientId": null (unlink) and a missing clientId (leave as is) must differ, and so must the note.
        using var doc = await System.Text.Json.JsonDocument.ParseAsync(http.Request.Body);
        var root = doc.RootElement;
        try
        {
            License? license = null;
            var hasClient = root.TryGetProperty("clientId", out var c);
            var hasNote = root.TryGetProperty("note", out var n);
            if (!hasClient && !hasNote) return Refused("nothing_to_change", "Send clientId, note, or both.");
            var existing = (await book.AllAsync()).FirstOrDefault(r => r.License.Id == id)?.License;
            if (existing is null) return NotFound("That licence");
            int? clientId = hasClient ? (c.ValueKind == System.Text.Json.JsonValueKind.Number ? c.GetInt32() : null) : existing.ClientId;
            license = await book.AssignAsync(id, clientId, hasNote && n.ValueKind == System.Text.Json.JsonValueKind.String ? n.GetString() : null, hasNote);
            return license is null ? NotFound("That licence") : Json((await LicenseByIdAsync(book, id))!);
        }
        catch (AdminRuleException refused) { return Refused(refused.Code, refused.Message); }
    }

    private static async Task<IResult> DeleteLicense(int id, LicenseBook book)
    {
        try { return await book.DeleteAsync(id) is null ? NotFound("That licence") : Results.NoContent(); }
        catch (AdminRuleException refused) { return Refused(refused.Code, refused.Message); }
    }
}
