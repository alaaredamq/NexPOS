using System.Globalization;
using System.Security.Cryptography;
using Cloud.Data;
using Common.Licensing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Cloud.Services;

/// <summary>What the "Issue a key" form (and POST /licenses/issue) sends.</summary>
public sealed class IssueInput
{
    public int? ClientId { get; set; }
    /// <summary>The shop PC's Machine ID from its License page (or picked from the client's branches).</summary>
    public string? MachineId { get; set; }
    /// <summary>The name written in the key; the client's name when empty.</summary>
    public string? Customer { get; set; }
    /// <summary>full, provisional or trial.</summary>
    public string? Kind { get; set; }
    /// <summary>basic or pro. Required: a key must never come out Pro because nobody looked.</summary>
    public string? Plan { get; set; }
    public int Seats { get; set; } = 1;
    /// <summary>Length in days from today; the kind's default when neither this nor <see cref="Until"/> is set.</summary>
    public int? Days { get; set; }
    public DateOnly? Until { get; set; }
    public string? Note { get; set; }
    /// <summary>Also record a payment for the key ("POS licence"), linked to it.</summary>
    public string? PaymentAmount { get; set; }
    public string? PaymentMethod { get; set; }
}

public sealed record LicenseRow(License License, LicenseStatus Status, string? ClientName);

public sealed record ImportResult(int Added, int AlreadyKnown, int Assigned, IReadOnlyList<string> Problems);

/// <summary>
/// The licence records: issuing keys online, recording keys signed elsewhere, importing issued.csv, linking each to its client and
/// branch, and the renewals and warnings that follow from them. Used by the portal and the admin API alike.
/// </summary>
public sealed class LicenseBook(ControlDbContext db, LicenseSigner signer, FinanceBook finance, PortalFormat fmt, IOptions<CloudOptions> options)
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // ---- reading ----

    /// <summary>Every record with its status, newest first.</summary>
    public async Task<List<LicenseRow>> AllAsync()
    {
        var all = await db.Licenses.AsNoTracking().Include(l => l.Client).ToListAsync();
        var superseded = LicenseRules.Superseded(all);
        var today = fmt.Today;
        return all.Select(l => new LicenseRow(l, LicenseRules.Status(l, today, superseded.Contains(l.Id)), l.Client?.Name))
            .OrderByDescending(r => r.License.IssuedOn).ThenByDescending(r => r.License.Id).ToList();
    }

    public async Task<List<LicenseRow>> ForClientAsync(int clientId)
        => (await AllAsync()).Where(r => r.License.ClientId == clientId).ToList();

    /// <summary>Keys in force that need the provider: time-limited ones ending or ended, and nothing on record for a POS-only client.</summary>
    public async Task<List<AttentionItem>> AttentionAsync(IReadOnlyList<LicenseRow>? rows = null)
    {
        rows ??= await AllAsync();
        var items = new List<AttentionItem>();
        foreach (var r in rows.Where(r => r.Status is LicenseStatus.EndingSoon or LicenseStatus.Grace or LicenseStatus.Expired))
        {
            var l = r.License;
            var who = r.ClientName ?? l.Customer;
            var when = l.ValidUntil.ToString("yyyy-MM-dd", Inv);
            var reason = r.Status switch
            {
                LicenseStatus.EndingSoon => $"{l.Kind} licence ends {when}. Issue the next key (a Full key once it is paid off).",
                LicenseStatus.Grace => $"{l.Kind} licence ended {when}: the POS stops selling after {l.ValidUntil.AddDays(LicenseRules.GraceDays).ToString("yyyy-MM-dd", Inv)}.",
                _ => $"{l.Kind} licence ended {when}: the POS has stopped selling.",
            };
            items.Add(new AttentionItem(r.Status == LicenseStatus.EndingSoon ? AttentionSeverity.Warn : AttentionSeverity.Bad, who, reason,
                $"/admin/Licenses?renew={l.Id}#issue", "Issue key"));
        }

        var unassigned = rows.Count(r => r.License.ClientId is null && r.Status != LicenseStatus.Superseded);
        if (unassigned > 0)
            items.Add(new AttentionItem(AttentionSeverity.Warn, $"{unassigned} licence{(unassigned == 1 ? "" : "s")} not linked to a client",
                "Pick the client for each, so renewals and payments line up.", "/admin/Licenses?show=unassigned", "Link"));
        return items;
    }

    /// <summary>Keys in force whose end (or updates plan end) is within 30 days, or in the grace week: money that should come in.</summary>
    public async Task<List<Renewal>> RenewalsAsync(IReadOnlyList<LicenseRow>? rows = null)
    {
        rows ??= await AllAsync();
        var today = fmt.Today;
        var payments = await db.Payments.AsNoTracking().Where(p => p.Purpose == FinanceRules.Purposes[1]).ToListAsync();
        return rows.Where(r => r.Status is LicenseStatus.EndingSoon or LicenseStatus.Grace or LicenseStatus.UpdatesEnding
                               || (r.Status == LicenseStatus.Active && r.License.ValidUntil <= today.AddDays(30)))
            .Where(r => r.License.ClientId is not null)
            .Select(r => new Renewal(r.License.ClientId!.Value, r.ClientName ?? r.License.Customer,
                r.License.ValidUntil.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                payments.Where(p => p.ClientId == r.License.ClientId).MaxBy(p => p.ReceivedOn)?.Amount,
                LicenseRules.IsTimeLimited(r.License) ? $"{r.License.Kind} licence ends" : "Updates plan ends", r.License.Id))
            .ToList();
    }

    // ---- issuing ----

    /// <summary>Signs a new key and records it (and its payment). Throws <see cref="AdminRuleException"/> or <see cref="LicenseSignException"/>.</summary>
    public async Task<License> IssueAsync(IssueInput input, string? passphrase, string by)
    {
        if (!Common.Licensing.MachineId.TryParse(input.MachineId, out var machine) || machine.IsEmpty)
            throw new AdminRuleException("bad_machine_id", "Enter the Machine ID from the shop PC's License page (XXXXX-XXXXX-XXXXX-XXXXX).");

        Client? client = null;
        if (input.ClientId is { } clientId && (client = await db.Clients.FindAsync(clientId)) is null)
            throw new AdminRuleException("not_found", "That client no longer exists.");

        var customer = (string.IsNullOrWhiteSpace(input.Customer) ? client?.Name : input.Customer)?.Trim() ?? "";
        if (customer.Length == 0) throw new AdminRuleException("customer_required", "Pick the client, or type the customer name for the key.");
        // The key has room for 60 bytes of UTF-8 (Arabic letters take 2): cut on a whole character.
        while (System.Text.Encoding.UTF8.GetByteCount(customer) > LicenseCodec.MaxCustomerBytes) customer = customer[..^1].TrimEnd();

        var kind = input.Kind?.Trim().ToLowerInvariant() switch
        {
            "full" => LicenseKind.Full,
            "provisional" => LicenseKind.Provisional,
            "trial" => LicenseKind.Trial,
            _ => throw new AdminRuleException("bad_kind", "Pick the type: Full, Provisional or Trial."),
        };
        var plan = input.Plan?.Trim().ToLowerInvariant() switch
        {
            "basic" => LicensePlan.Basic,
            "pro" => LicensePlan.Pro,
            _ => throw new AdminRuleException("bad_plan", "Pick the plan: Basic or Pro."),
        };
        if (input.Seats < 1 || input.Seats > LicenseCodec.MaxSeats)
            throw new AdminRuleException("bad_seats", $"PCs must be from 1 to {LicenseCodec.MaxSeats}.");
        if (plan == LicensePlan.Basic && input.Seats != PlanRules.BasicSeats)
            throw new AdminRuleException("bad_seats", "A Basic licence covers exactly 1 PC. Use Pro for extra tills.");

        var today = fmt.Today;
        DateOnly until;
        if (input.Until is { } u) until = u;
        else
        {
            var days = input.Days ?? kind switch { LicenseKind.Full => 365, LicenseKind.Provisional => 45, _ => 14 };
            if (days is < 1 or > 3650) throw new AdminRuleException("bad_length", "The length must be from 1 to 3650 days.");
            until = today.AddDays(days);
        }
        if (until < today) throw new AdminRuleException("bad_length", "The end date is in the past.");
        if (until > today.AddYears(10)) throw new AdminRuleException("bad_length", "The end date is more than 10 years away.");

        decimal? paid = null;
        if (!string.IsNullOrWhiteSpace(input.PaymentAmount))
        {
            if (!FinanceRules.TryParseAmount(input.PaymentAmount, out var amount))
                throw new AdminRuleException("invalid_payment", "The payment must be a positive amount, or left empty.");
            if (client is null) throw new AdminRuleException("invalid_payment", "Pick the client to record a payment with the key.");
            paid = amount;
        }

        var serial = BitConverter.ToUInt32(RandomNumberGenerator.GetBytes(4));
        var payload = new LicensePayload(kind, machine, today, until, serial, customer, input.Seats, plan);
        var keyText = signer.Sign(payload, passphrase, by);

        var license = FromPayload(payload, keyText, LicenseSources.Cloud, by);
        license.Note = FinanceRules.Clean(input.Note, 500);
        await LinkAsync(license, client?.Id);
        db.Licenses.Add(license);
        if (license.ClientId is { } cid)
            db.ClientEvents.Add(new ClientEvent { ClientId = cid, At = fmt.Now, Text = $"Licence key issued: {LicenseRules.Describe(license)} for {license.MachineId} (serial {license.Serial})." });
        await db.SaveChangesAsync();

        if (paid is { } amountPaid)
        {
            var (payment, error) = await finance.RecordPaymentAsync(new PaymentInput
            {
                ClientId = license.ClientId, Amount = amountPaid.ToString(Inv), Date = today,
                Method = input.PaymentMethod ?? FinanceRules.Methods[0], Purpose = FinanceRules.Purposes[1], Reference = "Licence " + license.Serial,
            }, by);
            if (payment is not null)
            {
                license.PaymentId = payment.Id;
                await db.SaveChangesAsync();
            }
            else throw new AdminRuleException("invalid_payment", error ?? "The payment could not be recorded.");
        }
        return license;
    }

    private License FromPayload(LicensePayload p, string? keyText, string source, string by) => new()
    {
        MachineId = p.Machine.Format(), Kind = p.Kind.ToString(), Plan = p.Plan.ToString(),
        Seats = PlanRules.EffectiveSeats(p.Plan, p.Seats), IssuedOn = p.IssuedOn, ValidUntil = p.ValidUntil, Serial = p.SerialText,
        Customer = p.Customer.Length > 120 ? p.Customer[..120] : p.Customer, KeyText = keyText, Source = source, CreatedAt = fmt.Now, CreatedBy = by,
    };

    /// <summary>
    /// Finds the client and branch for a key: the one given; else the branch whose shop PC has the same Machine ID; else the client of
    /// an earlier key for that PC; else the one client whose name is exactly the key's customer name. Otherwise it stays unlinked.
    /// </summary>
    private async Task LinkAsync(License license, int? clientId)
    {
        var sites = await db.Sites.AsNoTracking().Where(s => s.MachineId != null).Select(s => new { s.Id, s.ClientId, s.MachineId }).ToListAsync();
        var samePc = sites.Where(s => Same(s.MachineId, license.MachineId)).ToList();

        if (clientId is null)
        {
            clientId = samePc.Select(s => (int?)s.ClientId).FirstOrDefault()
                ?? await db.Licenses.Where(l => l.MachineId == license.MachineId && l.ClientId != null).OrderByDescending(l => l.Id).Select(l => l.ClientId).FirstOrDefaultAsync();
        }
        if (clientId is null && license.Customer.Length > 0)
        {
            var name = license.Customer.ToLower();
            var matches = await db.Clients.Where(c => c.Name.ToLower() == name).Select(c => c.Id).Take(2).ToListAsync();
            if (matches.Count == 1) clientId = matches[0];
        }
        license.ClientId = clientId;
        license.SiteId = samePc.FirstOrDefault(s => s.ClientId == clientId)?.Id;
    }

    private static bool Same(string? a, string b)
        => Common.Licensing.MachineId.TryParse(a, out var x) && Common.Licensing.MachineId.TryParse(b, out var y) && x.Format() == y.Format();

    // ---- recording keys signed elsewhere ----

    private byte[] TrustedKey()
    {
        try { return Convert.FromBase64String(options.Value.LicensePublicKey); }
        catch (FormatException) { return []; }
    }

    /// <summary>
    /// Records an existing key (pasted, or sent by the desktop keygen after signing). The signature is checked, so only real keys get in.
    /// Returns the record and whether it was new; a key already on record is linked to <paramref name="clientId"/> if it had no client.
    /// </summary>
    public async Task<(License License, bool Created)> RecordKeyAsync(string? keyText, int? clientId, string source, string by, string? note = null)
    {
        var text = keyText?.Trim() ?? "";
        if (text.Length is 0 or > 600) throw new AdminRuleException("bad_key", "Paste the whole licence key (NXL1-...).");
        var decoded = LicenseCodec.Decode(text, TrustedKey());
        if (!decoded.Success) throw new AdminRuleException("bad_key", $"That is not a valid Coduo POS licence key ({decoded.Error}).");
        if (clientId is { } id && !await db.Clients.AnyAsync(c => c.Id == id))
            throw new AdminRuleException("not_found", "That client no longer exists.");

        var p = decoded.Payload!;
        var machine = p.Machine.Format();
        var existing = await db.Licenses.FirstOrDefaultAsync(l => l.Serial == p.SerialText && l.MachineId == machine);
        if (existing is not null)
        {
            existing.KeyText ??= LicenseCodec.Canonicalize(text);
            if (existing.ClientId is null && clientId is not null) await LinkAsync(existing, clientId);
            await db.SaveChangesAsync();
            return (existing, false);
        }

        var license = FromPayload(p, LicenseCodec.Canonicalize(text), source, by);
        license.Note = FinanceRules.Clean(note, 500);
        await LinkAsync(license, clientId);
        db.Licenses.Add(license);
        if (license.ClientId is { } cid)
            db.ClientEvents.Add(new ClientEvent { ClientId = cid, At = fmt.Now, Text = $"Licence key recorded ({source}): {LicenseRules.Describe(license)} for {license.MachineId}." });
        await db.SaveChangesAsync();
        return (license, true);
    }

    /// <summary>
    /// What a connected shop PC showed at check-in (its signature already verified). A key the cloud has not seen is recorded, linked
    /// to this branch; the key text itself is not kept, as before.
    /// </summary>
    public async Task SeenAtShopAsync(Site site, string clientName, LicensePayload p)
    {
        var machine = p.Machine.Format();
        var existing = await db.Licenses.FirstOrDefaultAsync(l => l.Serial == p.SerialText && l.MachineId == machine);
        if (existing is not null)
        {
            if (existing.ClientId is null || existing.SiteId is null)
            {
                existing.ClientId ??= site.ClientId;
                if (existing.ClientId == site.ClientId) existing.SiteId ??= site.Id;
                await db.SaveChangesAsync();
            }
            return;
        }
        var license = FromPayload(p, null, LicenseSources.Till, "shop PC");
        // The provider knows the customer; the name in the key is not copied off the shop PC.
        license.Customer = clientName.Length > 120 ? clientName[..120] : clientName;
        license.ClientId = site.ClientId;
        license.SiteId = site.Id;
        db.Licenses.Add(license);
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException) { db.Entry(license).State = EntityState.Detached; } // two check-ins at once: the other one recorded it
    }

    /// <summary>
    /// Reads the issued.csv the desktop tools keep (issued_on, customer, kind, valid_until, machine_id, serial[, seats[, plan]]). Rows
    /// carry no key text, so they are recorded as details only, and linked by Machine ID or exact customer name where possible.
    /// </summary>
    public async Task<ImportResult> ImportCsvAsync(string? csv, string by)
    {
        var problems = new List<string>();
        int added = 0, known = 0, assigned = 0;
        var lines = (csv ?? "").Replace("\r\n", "\n").Split('\n').Select(l => l.TrimStart('﻿')).ToList();
        if (lines.Count == 0 || !lines[0].StartsWith("issued_on,", StringComparison.OrdinalIgnoreCase))
            throw new AdminRuleException("bad_csv", "That is not an issued.csv file (its first line starts with issued_on,customer,kind,...).");
        if (lines.Count > 20_001) throw new AdminRuleException("bad_csv", "That file is too long (more than 20,000 keys).");

        for (var i = 1; i < lines.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;
            var f = SplitCsv(lines[i]);
            if (f.Count < 6
                || !DateOnly.TryParseExact(f[0], "yyyy-MM-dd", Inv, DateTimeStyles.None, out var issued)
                || !Enum.TryParse<LicenseKind>(f[2], true, out var kind)
                || !DateOnly.TryParseExact(f[3], "yyyy-MM-dd", Inv, DateTimeStyles.None, out var until)
                || !Common.Licensing.MachineId.TryParse(f[4], out var machine) || machine.IsEmpty
                || !uint.TryParse(f[5], NumberStyles.HexNumber, Inv, out var serial))
            {
                if (problems.Count < 20) problems.Add($"Line {i + 1} could not be read.");
                continue;
            }
            var seats = f.Count > 6 && int.TryParse(f[6], NumberStyles.None, Inv, out var s) && s is >= 1 and <= LicenseCodec.MaxSeats ? s : 1;
            var plan = f.Count > 7 && Enum.TryParse<LicensePlan>(f[7], true, out var pl) ? pl : LicensePlan.Pro; // keys from before plans are Pro

            var payload = new LicensePayload(kind, machine, issued, until, serial, f[1], seats, plan);
            var serialText = payload.SerialText;
            var machineText = machine.Format();
            if (await db.Licenses.AnyAsync(l => l.Serial == serialText && l.MachineId == machineText)) { known++; continue; }

            var license = FromPayload(payload, null, LicenseSources.Imported, by);
            await LinkAsync(license, null);
            if (license.ClientId is not null) assigned++;
            db.Licenses.Add(license);
            await db.SaveChangesAsync();
            added++;
        }
        return new ImportResult(added, known, assigned, problems);
    }

    private static List<string> SplitCsv(string line)
    {
        var fields = new List<string>();
        var sb = new System.Text.StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else sb.Append(c);
            }
            else if (c == '"') quoted = true;
            else if (c == ',') { fields.Add(sb.ToString().Trim()); sb.Clear(); }
            else sb.Append(c);
        }
        fields.Add(sb.ToString().Trim());
        return fields;
    }

    // ---- editing ----

    /// <summary>Links a record to a client (or unlinks it with null) and sets its note. Null when the record does not exist.</summary>
    public async Task<License?> AssignAsync(int id, int? clientId, string? note, bool setNote)
    {
        var license = await db.Licenses.FindAsync(id);
        if (license is null) return null;
        if (clientId is { } cid && !await db.Clients.AnyAsync(c => c.Id == cid))
            throw new AdminRuleException("not_found", "That client no longer exists.");
        if (license.ClientId != clientId)
        {
            license.ClientId = clientId;
            license.SiteId = null;
            if (clientId is not null)
            {
                var site = await db.Sites.AsNoTracking().Where(s => s.ClientId == clientId && s.MachineId != null).ToListAsync();
                license.SiteId = site.FirstOrDefault(s => Same(s.MachineId, license.MachineId))?.Id;
                db.ClientEvents.Add(new ClientEvent { ClientId = clientId.Value, At = fmt.Now, Text = $"Licence {license.Serial} ({LicenseRules.Describe(license)}) linked to this client." });
            }
        }
        if (setNote) license.Note = FinanceRules.Clean(note, 500);
        await db.SaveChangesAsync();
        return license;
    }

    /// <summary>
    /// Removes a record. Keys issued here stay, as the record of what the server signed; anything else can go. The key itself keeps
    /// working at the shop either way (offline keys cannot be revoked).
    /// </summary>
    public async Task<bool?> DeleteAsync(int id)
    {
        var license = await db.Licenses.FindAsync(id);
        if (license is null) return null;
        if (license.Source == LicenseSources.Cloud)
            throw new AdminRuleException("issued_here", "Keys issued from the portal stay on record. Add a note instead.");
        db.Licenses.Remove(license);
        await db.SaveChangesAsync();
        return true;
    }
}
