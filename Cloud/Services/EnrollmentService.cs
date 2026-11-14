using Cloud.Data;
using Microsoft.EntityFrameworkCore;

namespace Cloud.Services;

public sealed record IssuedCode(string Display, DateTime ExpiresAt);

public enum RedeemOutcome { Success, Invalid, Used, Expired, Disabled }

public sealed record RedeemResult(RedeemOutcome Outcome, string? Token = null, string? SiteName = null, string? ClientName = null);

/// <summary>
/// Turns "send the owner a link" into a secure hand-over. The portal issues a code; the till redeems it exactly once
/// for a device token. The code is not the credential: it only buys one token, within a week, and is useless afterwards.
/// </summary>
public sealed class EnrollmentService(ControlDbContext db, TimeProvider time)
{
    public static readonly TimeSpan CodeLifetime = TimeSpan.FromDays(7);

    /// <summary>Issues a new code for the installation. Older unused codes stop working: there is only ever one live link.</summary>
    public async Task<IssuedCode> IssueAsync(Guid siteId)
    {
        var now = time.GetUtcNow().UtcDateTime;
        await db.EnrollmentCodes
            .Where(c => c.SiteId == siteId && c.UsedAt == null && c.ExpiresAt > now)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.ExpiresAt, now));

        var (display, hash) = EnrollmentCodes.Generate();
        var expires = now.Add(CodeLifetime);
        db.EnrollmentCodes.Add(new EnrollmentCode { SiteId = siteId, CodeHash = hash, CreatedAt = now, ExpiresAt = expires });
        await db.SaveChangesAsync();
        return new IssuedCode(display, expires);
    }

    /// <summary>Makes any unused code stop working (the owner changed their mind, or sent it to the wrong person).</summary>
    public async Task CancelOpenCodesAsync(Guid siteId)
    {
        var now = time.GetUtcNow().UtcDateTime;
        await db.EnrollmentCodes
            .Where(c => c.SiteId == siteId && c.UsedAt == null && c.ExpiresAt > now)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.ExpiresAt, now));
    }

    public async Task<RedeemResult> RedeemAsync(string? rawCode, string? machineId, string? deviceName)
    {
        var normalized = EnrollmentCodes.Normalize(rawCode);
        if (normalized is null) return new RedeemResult(RedeemOutcome.Invalid);

        var hash = EnrollmentCodes.Hash(normalized);
        var code = await db.EnrollmentCodes
            .Include(c => c.Site).ThenInclude(s => s.Client)
            .Include(c => c.Site).ThenInclude(s => s.Tokens)
            .FirstOrDefaultAsync(c => c.CodeHash == hash);
        if (code is null) return new RedeemResult(RedeemOutcome.Invalid);

        var now = time.GetUtcNow().UtcDateTime;
        if (code.UsedAt is not null) return new RedeemResult(RedeemOutcome.Used);
        if (code.ExpiresAt <= now) return new RedeemResult(RedeemOutcome.Expired);
        if (code.Site.IsDisabled || !code.Site.Client.IsActive) return new RedeemResult(RedeemOutcome.Disabled);

        // Claim the code atomically: of two simultaneous redemptions only one updates a row.
        var claimed = await db.EnrollmentCodes
            .Where(c => c.Id == code.Id && c.UsedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.UsedAt, now));
        if (claimed == 0) return new RedeemResult(RedeemOutcome.Used);

        // Re-enrolling (a reinstalled PC, a replaced till) replaces the previous credential.
        foreach (var old in code.Site.Tokens.Where(t => t.RevokedAt == null)) old.RevokedAt = now;
        var (token, tokenHash, prefix) = DeviceTokens.Generate();
        code.Site.Tokens.Add(new DeviceToken { TokenHash = tokenHash, Prefix = prefix, CreatedAt = now });

        if (!string.IsNullOrWhiteSpace(machineId)) code.Site.MachineId = Truncate(machineId.Trim(), 64);
        var device = string.IsNullOrWhiteSpace(deviceName) ? "" : $" ({Truncate(deviceName.Trim(), 60)})";
        db.ClientEvents.Add(new ClientEvent
        {
            ClientId = code.Site.ClientId, At = now, Text = $"[{code.Site.Name}] Till connected{device}.",
        });
        await db.SaveChangesAsync();

        return new RedeemResult(RedeemOutcome.Success, token, code.Site.Name, code.Site.Client.Name);
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
