using System.Security.Cryptography;
using Cloud.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Cloud.Services;

/// <summary>
/// A request the rules refuse ("Enter the shop's name", "That email already has a phone login"). <see cref="Code"/> is stable for
/// an app to branch on; <see cref="Message"/> is the sentence to show a person.
/// </summary>
public sealed class AdminRuleException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public sealed record NewClientResult(Client Client, Site Site, string Link, DateTime LinkExpiresAt);
public sealed record IssuedLink(string Url, string Code, DateTime ExpiresAt);
public sealed record OwnerHandOver(OwnerUser Owner, string Password);

/// <summary>
/// Everything the provider can do to clients, their branches and their phone logins, in one place. The admin portal's pages and the
/// admin app's API (/api/admin/v1) both call it, so a rule is written once and the two can never disagree. Each method saves its
/// own changes and notes them in the client's history. A missing client, branch or login gives null (the caller says "not found");
/// a refused request throws <see cref="AdminRuleException"/>.
/// </summary>
public sealed class AdminOperations(
    ControlDbContext db, EnrollmentService enrollment, SiteRemoval removal, TimeProvider time, IOptions<CloudOptions> options)
{
    public const int MaxMonths = 60;
    public const int MinOwnerPasswordLength = 8;
    // No 0/O/1/l/I: the owner may have to read it off a screen and type it.
    private const string PasswordAlphabet = "abcdefghijkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    private DateTime Now => time.GetUtcNow().UtcDateTime;

    private void Log(int clientId, string text) => db.ClientEvents.Add(new ClientEvent { ClientId = clientId, At = Now, Text = text });

    /// <summary>The address links are built on: Cloud:PublicUrl, else the one the request came in on.</summary>
    public string BaseUrl(HttpRequest request) => string.IsNullOrWhiteSpace(options.Value.PublicUrl)
        ? $"{request.Scheme}://{request.Host}"
        : options.Value.PublicUrl.TrimEnd('/');

    private static string? Trim(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var t = value.Trim();
        return t.Length <= max ? t : t[..max];
    }

    // ---- clients ----

    /// <summary>The client, its first branch ("Main branch") and that branch's connection link: what a new shop needs in one step.</summary>
    public async Task<NewClientResult> CreateClientAsync(string? name, string? phone, string? email, string? notes, int months, string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new AdminRuleException("name_required", "Enter the shop's name.");
        if (months is < 0 or > MaxMonths) throw new AdminRuleException("bad_months", $"Months must be between 0 and {MaxMonths}.");

        var now = Now;
        var client = new Client
        {
            Name = Trim(name, 200)!, Phone = Trim(phone, 100), Email = Trim(email, 255), Notes = Trim(notes, 4000), CreatedAt = now,
        };
        if (months > 0)
            client.Subscription = new Subscription { StartsAt = now, ExpiresAt = now.AddMonths(months) };
        client.Events.Add(new ClientEvent { At = now, Text = months > 0 ? $"Client created with {months} month(s) of Online." : "Client created." });

        // The usual case is one branch, so set it up now: the next screen already holds the link to send.
        var site = new Site { Id = Guid.NewGuid(), Name = "Main branch", CreatedAt = now };
        client.Sites.Add(site);
        db.Clients.Add(client);
        await db.SaveChangesAsync();

        var issued = await enrollment.IssueAsync(site.Id);
        return new NewClientResult(client, site, $"{baseUrl}/enroll/{issued.Display}", issued.ExpiresAt);
    }

    private Task<Client?> TrackedClientAsync(int id) => db.Clients.Include(c => c.Subscription).FirstOrDefaultAsync(c => c.Id == id);

    public async Task<Client?> UpdateClientAsync(int id, string? name, string? phone, string? email, string? notes)
    {
        var client = await TrackedClientAsync(id);
        if (client is null) return null;
        if (string.IsNullOrWhiteSpace(name)) throw new AdminRuleException("name_required", "Enter the shop's name.");
        client.Name = Trim(name, 200)!;
        client.Phone = Trim(phone, 100);
        client.Email = Trim(email, 255);
        client.Notes = Trim(notes, 4000);
        await db.SaveChangesAsync();
        return client;
    }

    /// <summary>Switches the client on or off. Off blocks all its branches from the cloud; its shops keep selling.</summary>
    public async Task<Client?> SetClientActiveAsync(int id, bool active)
    {
        var client = await TrackedClientAsync(id);
        if (client is null) return null;
        if (client.IsActive != active)
        {
            client.IsActive = active;
            Log(id, active ? "Client reactivated." : "Client deactivated.");
            await db.SaveChangesAsync();
        }
        return client;
    }

    /// <summary>Removes the client, its branches (and their cloud data), subscription and history. Its payments stay in the books.</summary>
    public async Task<string?> RemoveClientAsync(int id)
    {
        var name = await db.Clients.Where(c => c.Id == id).Select(c => c.Name).FirstOrDefaultAsync();
        if (name is null) return null;
        await removal.RemoveClientAsync(id);
        return name;
    }

    // ---- the Online subscription ----

    /// <summary>Adds months of Online: renewing early keeps the time left, renewing late starts now. Lifts a suspension.</summary>
    public async Task<Subscription?> ExtendAsync(int id, int months)
    {
        var client = await TrackedClientAsync(id);
        if (client is null) return null;
        if (months is < 1 or > MaxMonths) throw new AdminRuleException("bad_months", $"Months must be between 1 and {MaxMonths}.");

        var now = Now;
        if (client.Subscription is null)
        {
            client.Subscription = new Subscription { StartsAt = now, ExpiresAt = now.AddMonths(months) };
        }
        else
        {
            client.Subscription.ExpiresAt = SubscriptionRules.ExtendedExpiry(client.Subscription, months, now);
            client.Subscription.IsSuspended = false;
        }
        Log(id, $"Online extended by {months} month(s), now until {client.Subscription.ExpiresAt:yyyy-MM-dd}.");
        await db.SaveChangesAsync();
        return client.Subscription;
    }

    /// <summary>Suspends or resumes Online. Null when the client or its subscription does not exist.</summary>
    public async Task<Subscription?> SetSuspendedAsync(int id, bool suspended)
    {
        var client = await TrackedClientAsync(id);
        if (client?.Subscription is null) return null;
        if (client.Subscription.IsSuspended != suspended)
        {
            client.Subscription.IsSuspended = suspended;
            Log(id, suspended ? "Online suspended." : "Online resumed.");
            await db.SaveChangesAsync();
        }
        return client.Subscription;
    }

    // ---- branches ----

    public async Task<Site?> AddSiteAsync(int clientId, string? name, string? machineId)
    {
        if (!await db.Clients.AnyAsync(c => c.Id == clientId)) return null;
        if (string.IsNullOrWhiteSpace(name)) throw new AdminRuleException("name_required", "Enter the branch's name.");
        var site = new Site { Id = Guid.NewGuid(), ClientId = clientId, Name = Trim(name, 200)!, MachineId = Trim(machineId, 100), CreatedAt = Now };
        db.Sites.Add(site);
        Log(clientId, $"Branch '{site.Name}' added.");
        await db.SaveChangesAsync();
        return site;
    }

    private Task<Site?> TrackedSiteAsync(Guid id) => db.Sites.Include(s => s.Tokens).FirstOrDefaultAsync(s => s.Id == id);

    private void Log(Site site, string text) => Log(site.ClientId, $"[{site.Name}] {text}");

    public async Task<Site?> RenameSiteAsync(Guid id, string? name, string? machineId)
    {
        var site = await TrackedSiteAsync(id);
        if (site is null) return null;
        if (string.IsNullOrWhiteSpace(name)) throw new AdminRuleException("name_required", "Enter the branch's name.");
        site.Name = Trim(name, 200)!;
        site.MachineId = Trim(machineId, 100);
        await db.SaveChangesAsync();
        return site;
    }

    /// <summary>A fresh connection link for the branch (older unused ones stop working).</summary>
    public async Task<IssuedLink?> NewLinkAsync(Guid id, string baseUrl)
    {
        var site = await TrackedSiteAsync(id);
        if (site is null) return null;
        var issued = await enrollment.IssueAsync(id);
        Log(site, "Connection link created.");
        await db.SaveChangesAsync();
        return new IssuedLink($"{baseUrl}/enroll/{issued.Display}", issued.Display, issued.ExpiresAt);
    }

    /// <summary>Revokes the branch's credential and any open link: the shop PC is refused at its next contact.</summary>
    public async Task<Site?> DisconnectAsync(Guid id)
    {
        var site = await TrackedSiteAsync(id);
        if (site is null) return null;
        var now = Now;
        foreach (var t in site.Tokens.Where(t => t.RevokedAt == null)) t.RevokedAt = now;
        await enrollment.CancelOpenCodesAsync(id);
        Log(site, "Disconnected.");
        await db.SaveChangesAsync();
        return site;
    }

    /// <summary>Replaces the branch's device token by hand (support only; links are the normal way). The token is returned once.</summary>
    public async Task<string?> IssueTokenAsync(Guid id)
    {
        var site = await TrackedSiteAsync(id);
        if (site is null) return null;
        var now = Now;
        foreach (var old in site.Tokens.Where(t => t.RevokedAt == null)) old.RevokedAt = now;
        var (token, hash, prefix) = DeviceTokens.Generate();
        site.Tokens.Add(new DeviceToken { TokenHash = hash, Prefix = prefix, CreatedAt = now });
        Log(site, "Device token issued manually.");
        await db.SaveChangesAsync();
        return token;
    }

    public async Task<Site?> RequestResyncAsync(Guid id)
    {
        var site = await TrackedSiteAsync(id);
        if (site is null) return null;
        site.ResyncRequested = true;
        Log(site, "Full resync requested.");
        await db.SaveChangesAsync();
        return site;
    }

    public async Task<Site?> SetSiteDisabledAsync(Guid id, bool disabled)
    {
        var site = await TrackedSiteAsync(id);
        if (site is null) return null;
        if (site.IsDisabled != disabled)
        {
            site.IsDisabled = disabled;
            Log(site, disabled ? "Branch disabled." : "Branch enabled.");
            await db.SaveChangesAsync();
        }
        return site;
    }

    /// <summary>Removes the branch and its cloud data. Returns its client's id, or null when it was not found.</summary>
    public async Task<int?> RemoveSiteAsync(Guid id)
    {
        var site = await db.Sites.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id);
        if (site is null) return null;
        await removal.RemoveAsync([id]);
        Log(site.ClientId, $"Branch '{site.Name}' removed.");
        await db.SaveChangesAsync();
        return site.ClientId;
    }

    // ---- phone logins (the owner's sign-in to the phone app and owner website) ----

    public static string GeneratePassword(int length = 12)
    {
        var chars = new char[length];
        for (var i = 0; i < length; i++)
            chars[i] = PasswordAlphabet[RandomNumberGenerator.GetInt32(PasswordAlphabet.Length)];
        return new string(chars);
    }

    /// <summary>Adds a sign-in for the client's owner or staff. With no password one is made up. The password is returned once.</summary>
    public async Task<OwnerHandOver?> AddOwnerAsync(int clientId, string? signInName, string? displayName, string? password, string? role)
    {
        if (!await db.Clients.AnyAsync(c => c.Id == clientId)) return null;

        // Email, phone number or username: whichever the owner would rather type (see OwnerSignIn).
        if (OwnerSignIn.Normalize(signInName) is not { } signIn) throw new AdminRuleException("bad_sign_in", OwnerSignIn.Rules);
        var email = signIn.Value;
        if (!string.IsNullOrEmpty(password) && (password.Length < MinOwnerPasswordLength || password.Length > 100))
            throw new AdminRuleException("weak_password", $"The password needs at least {MinOwnerPasswordLength} characters.");
        // The sign-in name is unique across ALL clients: the phone app asks only for it and the password.
        if (await OwnerSignIn.TakenAsync(db.OwnerUsers, email, signIn.Kind))
            throw new AdminRuleException("sign_in_taken", signIn.Kind switch
            {
                OwnerSignIn.Kind.Email => "That email already has a phone login.",
                OwnerSignIn.Kind.Phone => "That phone number already has a phone login.",
                _ => "That username already has a phone login.",
            });

        var finalPassword = string.IsNullOrEmpty(password) ? GeneratePassword() : password;
        var name = !string.IsNullOrWhiteSpace(displayName) ? displayName.Trim()
            : signIn.Kind == OwnerSignIn.Kind.Email ? email[..email.IndexOf('@')] : email;
        var owner = new OwnerUser
        {
            ClientId = clientId, Email = email, DisplayName = name.Length > 100 ? name[..100] : name,
            PasswordHash = PasswordHasher.Hash(finalPassword), CreatedAt = Now, Permissions = (int)OwnerRoles.For(role),
        };
        db.OwnerUsers.Add(owner);
        Log(clientId, $"Phone login added for {email} ({OwnerRoles.NameOf(OwnerRoles.For(role))}).");  // never the password
        await db.SaveChangesAsync();
        return new OwnerHandOver(owner, finalPassword);
    }

    private Task<OwnerUser?> OwnerAsync(int clientId, int ownerId)
        => db.OwnerUsers.FirstOrDefaultAsync(u => u.Id == ownerId && u.ClientId == clientId);

    /// <summary>Signs every phone of this owner out, so a change takes effect now and not when the session would have ended.</summary>
    private async Task RevokeSessionsAsync(int ownerUserId)
    {
        var now = Now;
        foreach (var session in await db.OwnerSessions.Where(s => s.OwnerUserId == ownerUserId && s.RevokedAt == null).ToListAsync())
            session.RevokedAt = now;
    }

    public async Task<OwnerUser?> SetOwnerRoleAsync(int clientId, int ownerId, string? role)
    {
        var owner = await OwnerAsync(clientId, ownerId);
        if (owner is null) return null;
        var permissions = OwnerRoles.For(role);
        if ((int)permissions != owner.Permissions)
        {
            owner.Permissions = (int)permissions;
            // The phone keeps what it was told at sign-in; signing it out makes the new limits apply now.
            await RevokeSessionsAsync(owner.Id);
            Log(clientId, $"Phone login role for {owner.Email} set to {OwnerRoles.NameOf(permissions)}.");
            await db.SaveChangesAsync();
        }
        return owner;
    }

    /// <summary>A new made-up password, returned once; the owner's phones are signed out.</summary>
    public async Task<OwnerHandOver?> ResetOwnerPasswordAsync(int clientId, int ownerId)
    {
        var owner = await OwnerAsync(clientId, ownerId);
        if (owner is null) return null;
        var password = GeneratePassword();
        owner.PasswordHash = PasswordHasher.Hash(password);
        await RevokeSessionsAsync(owner.Id);
        Log(clientId, $"Phone login password reset for {owner.Email}.");
        await db.SaveChangesAsync();
        return new OwnerHandOver(owner, password);
    }

    public async Task<OwnerUser?> SetOwnerActiveAsync(int clientId, int ownerId, bool active)
    {
        var owner = await OwnerAsync(clientId, ownerId);
        if (owner is null) return null;
        if (owner.IsActive != active)
        {
            owner.IsActive = active;
            if (!active) await RevokeSessionsAsync(owner.Id);
            Log(clientId, active ? $"Phone login enabled for {owner.Email}." : $"Phone login disabled for {owner.Email}.");
            await db.SaveChangesAsync();
        }
        return owner;
    }

    public async Task<bool> RemoveOwnerAsync(int clientId, int ownerId)
    {
        var owner = await OwnerAsync(clientId, ownerId);
        if (owner is null) return false;
        await db.OwnerSessions.Where(s => s.OwnerUserId == ownerId).ExecuteDeleteAsync();
        db.OwnerUsers.Remove(owner);
        Log(clientId, $"Phone sign-in removed for {owner.Email}.");
        await db.SaveChangesAsync();
        return true;
    }
}
