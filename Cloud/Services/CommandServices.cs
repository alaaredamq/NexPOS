using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Cloud.Data;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Sync;

namespace Cloud.Services;

/// <summary>
/// Wakes a shop PC that is waiting on a held-open request the moment its owner queues something, so a change made on the
/// phone reaches the shop in about a second instead of at the next poll. In memory on purpose: if the cloud restarts, the shop
/// PC simply asks again (its request ends and is repeated), and the command itself is safe in the database.
/// </summary>
public sealed class CommandBroker
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, TaskCompletionSource> _signals = new();

    /// <summary>Take this BEFORE looking for work, then wait on it: a command queued in between still wakes the waiter.</summary>
    public Task Current(Guid siteId)
    {
        lock (_gate)
        {
            if (!_signals.TryGetValue(siteId, out var signal))
                _signals[siteId] = signal = NewSignal();
            return signal.Task;
        }
    }

    public void Notify(Guid siteId)
    {
        TaskCompletionSource? old;
        lock (_gate)
        {
            _signals.TryGetValue(siteId, out old);
            _signals[siteId] = NewSignal();
        }
        old?.TrySetResult();
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}

public static class OwnerTokens
{
    public const string Scheme = "nxo_";
    public static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(30);

    public static (string Token, string Hash) Generate()
    {
        var token = Scheme + WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        return (token, Hash(token));
    }

    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("owner:" + token)));
}

/// <summary>Who is calling the owner API, resolved from the bearer token.</summary>
public sealed record OwnerPrincipal(OwnerUser User, OwnerSession Session, Client Client, Site? Site, SubscriptionState State);

public static class OwnerAuth
{
    public static async Task<OwnerPrincipal?> AuthenticateAsync(HttpContext http, ControlDbContext db, TimeProvider time)
    {
        var header = http.Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return null;
        var token = header["Bearer ".Length..].Trim();
        if (!token.StartsWith(OwnerTokens.Scheme, StringComparison.Ordinal)) return null;

        var hash = OwnerTokens.Hash(token);
        var now = time.GetUtcNow().UtcDateTime;
        var session = await db.OwnerSessions
            .Include(s => s.OwnerUser).ThenInclude(u => u.Client).ThenInclude(c => c.Subscription)
            .Include(s => s.OwnerUser).ThenInclude(u => u.Client).ThenInclude(c => c.Sites)
            .FirstOrDefaultAsync(s => s.TokenHash == hash);

        if (session is null || session.RevokedAt is not null || session.ExpiresAt <= now) return null;
        var user = session.OwnerUser;
        if (!user.IsActive || !user.Client.IsActive) return null;

        // One till = one installation per client for now (WORKLOG F1), so the shop is the client's first enabled site.
        var site = user.Client.Sites.Where(s => !s.IsDisabled).OrderBy(s => s.CreatedAt).FirstOrDefault();
        return new OwnerPrincipal(user, session, user.Client, site, SubscriptionRules.Evaluate(user.Client.Subscription, now));
    }
}
