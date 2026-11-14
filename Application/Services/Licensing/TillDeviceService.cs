using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Application.DTOs.Licensing;
using Application.IServices.Licensing;
using Core.Entities.Licensing;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Application.Services.Licensing
{
    /// <summary>Remembers wrong pairing codes per caller so the 8-character code cannot be guessed. Singleton.</summary>
    public sealed class PairingThrottle
    {
        public const int MaxFailures = 5;
        public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

        private readonly ConcurrentDictionary<string, List<DateTime>> _failures = new();

        public bool IsBlocked(string key, DateTime now)
        {
            if (!_failures.TryGetValue(key, out var list)) return false;
            lock (list)
            {
                list.RemoveAll(t => now - t > Window);
                return list.Count >= MaxFailures;
            }
        }

        public void RecordFailure(string key, DateTime now)
        {
            var list = _failures.GetOrAdd(key, _ => new List<DateTime>());
            lock (list)
            {
                list.RemoveAll(t => now - t > Window);
                list.Add(now);
            }
        }

        public void Clear(string key) => _failures.TryRemove(key, out _);
    }

    public sealed class TillDeviceService : ITillDeviceService
    {
        public static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(15);

        /// <summary>How often a till's "last seen" is written, so a busy till does not write on every request.</summary>
        private static readonly TimeSpan LastSeenResolution = TimeSpan.FromMinutes(5);

        // No 0/O/1/I: the code is read aloud and typed by hand.
        private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        private const int CodeLength = 8;

        private readonly ErpDbContext _db;
        private readonly ILicenseService _license;
        private readonly PairingThrottle _throttle;
        private readonly TimeProvider _time;

        public TillDeviceService(ErpDbContext db, ILicenseService license, PairingThrottle throttle, TimeProvider time)
        {
            _db = db;
            _license = license;
            _throttle = throttle;
            _time = time;
        }

        private DateTime Now => _time.GetUtcNow().UtcDateTime;

        public async Task<TillSeatsInfo> GetSeatsAsync(CancellationToken ct = default)
        {
            var state = _license.GetState();
            var tillSeats = TillSeatsOf(state);
            var now = Now;

            var rows = await _db.TillDevices.AsNoTracking()
                .Where(d => d.RevokedAt == null && (d.TokenHash != null || d.PairingExpiresAt > now))
                .OrderBy(d => d.CreatedAt).ThenBy(d => d.Id)
                .ToListAsync(ct);

            // Same order the access check uses: the oldest tills keep their seats if the licence shrinks.
            var activeRank = 0;
            var devices = new List<TillDeviceInfo>(rows.Count);
            foreach (var d in rows)
            {
                var active = d.TokenHash != null;
                var overflow = active && activeRank++ >= tillSeats;
                devices.Add(new TillDeviceInfo(d.Id, d.Name, active ? TillDeviceState.Active : TillDeviceState.Pending,
                    d.CreatedAt, d.PairedAt, d.LastSeenAt, active ? null : d.PairingExpiresAt, overflow));
            }

            return new TillSeatsInfo(state.Seats, tillSeats, rows.Count, !state.EnforcementDisabled, devices);
        }

        public async Task<IssuePairingResult> IssuePairingCodeAsync(string name, CancellationToken ct = default)
        {
            name = (name ?? string.Empty).Trim();
            if (name.Length == 0) return new IssuePairingResult(IssuePairingOutcome.NameRequired);
            if (name.Length > 100) name = name[..100];

            var state = _license.GetState();
            if (!state.AllowsSales && !state.EnforcementDisabled)
                return new IssuePairingResult(IssuePairingOutcome.LicenseInactive);

            var now = Now;

            // Unused codes that ran out stop holding a seat (and stop cluttering the list).
            await _db.TillDevices
                .Where(d => d.TokenHash == null && d.RevokedAt == null && d.PairingExpiresAt <= now)
                .ExecuteDeleteAsync(ct);

            var used = await _db.TillDevices.CountAsync(d => d.RevokedAt == null && (d.TokenHash != null || d.PairingExpiresAt > now), ct);
            if (used >= TillSeatsOf(state)) return new IssuePairingResult(IssuePairingOutcome.SeatsFull);

            var (display, hash) = NewCode();
            var device = new TillDevice
            {
                Name = name,
                PairingCodeHash = hash,
                PairingExpiresAt = now.Add(CodeLifetime),
                CreatedAt = now
            };
            _db.TillDevices.Add(device);
            await _db.SaveChangesAsync(ct);

            return new IssuePairingResult(IssuePairingOutcome.Success, device.Id, display, device.PairingExpiresAt);
        }

        public async Task<PairResult> PairAsync(string? code, string clientKey, CancellationToken ct = default)
        {
            var now = Now;
            if (_throttle.IsBlocked(clientKey, now)) return new PairResult(PairOutcome.TooManyAttempts);

            var state = _license.GetState();
            if (!state.AllowsSales && !state.EnforcementDisabled) return new PairResult(PairOutcome.LicenseInactive);

            var normalized = NormalizeCode(code);
            var codeHash = normalized is null ? null : Hash(normalized);
            var device = codeHash is null
                ? null
                : await _db.TillDevices.FirstOrDefaultAsync(d => d.PairingCodeHash == codeHash
                    && d.TokenHash == null && d.RevokedAt == null && d.PairingExpiresAt > now, ct);
            if (device is null)
            {
                _throttle.RecordFailure(clientKey, now);
                return new PairResult(PairOutcome.InvalidCode);
            }

            // The code reserved a seat when it was issued, but the licence may have shrunk since: check again against the active tills.
            var active = await _db.TillDevices.CountAsync(d => d.TokenHash != null && d.RevokedAt == null, ct);
            if (active >= TillSeatsOf(state)) return new PairResult(PairOutcome.SeatsFull);

            var token = NewToken();
            var tokenHash = Hash(token);
            // Claim the code atomically: of two simultaneous redemptions only one updates a row.
            var claimed = await _db.TillDevices
                .Where(d => d.Id == device.Id && d.TokenHash == null && d.PairingCodeHash == codeHash)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(d => d.TokenHash, tokenHash)
                    .SetProperty(d => d.PairingCodeHash, (string?)null)
                    .SetProperty(d => d.PairingExpiresAt, (DateTime?)null)
                    .SetProperty(d => d.PairedAt, now)
                    .SetProperty(d => d.LastSeenAt, now), ct);
            if (claimed == 0)
            {
                _throttle.RecordFailure(clientKey, now);
                return new PairResult(PairOutcome.InvalidCode);
            }

            _throttle.Clear(clientKey);
            return new PairResult(PairOutcome.Success, token, device.Name);
        }

        public async Task<bool> RevokeAsync(int id, CancellationToken ct = default)
        {
            var now = Now;
            var changed = await _db.TillDevices
                .Where(d => d.Id == id && d.RevokedAt == null)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(d => d.RevokedAt, now)
                    .SetProperty(d => d.PairingCodeHash, (string?)null)
                    .SetProperty(d => d.PairingExpiresAt, (DateTime?)null), ct);
            return changed > 0;
        }

        public async Task<TillAccess> CheckAccessAsync(string? token, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(token)) return TillAccess.NotRegistered;

            var hash = Hash(token.Trim());
            var device = await _db.TillDevices.AsNoTracking()
                .Where(d => d.TokenHash == hash && d.RevokedAt == null)
                .Select(d => new { d.Id, d.CreatedAt, d.LastSeenAt })
                .FirstOrDefaultAsync(ct);
            if (device is null) return TillAccess.NotRegistered;

            var tillSeats = TillSeatsOf(_license.GetState());
            if (tillSeats != int.MaxValue)
            {
                var older = await _db.TillDevices.CountAsync(d => d.TokenHash != null && d.RevokedAt == null
                    && (d.CreatedAt < device.CreatedAt || (d.CreatedAt == device.CreatedAt && d.Id < device.Id)), ct);
                if (older >= tillSeats) return TillAccess.SeatsExceeded;
            }

            var now = Now;
            if (device.LastSeenAt is null || now - device.LastSeenAt > LastSeenResolution)
            {
                await _db.TillDevices.Where(d => d.Id == device.Id).ExecuteUpdateAsync(s => s.SetProperty(d => d.LastSeenAt, now), ct);
            }

            return TillAccess.Allowed;
        }

        /// <summary>Seats left for tills: the licence's PCs minus the server. A build that does not enforce licences has no limit.</summary>
        private static int TillSeatsOf(LicenseState state) =>
            state.EnforcementDisabled ? int.MaxValue : Math.Max(0, state.Seats - 1);

        private static (string Display, string Hash) NewCode()
        {
            var chars = new char[CodeLength];
            for (var i = 0; i < chars.Length; i++) chars[i] = CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)];
            var plain = new string(chars);
            return ($"{plain[..4]}-{plain[4..]}", Hash(plain));
        }

        /// <summary>Upper-cases and drops everything but letters and digits, so "abcd 2345" works. Null if it cannot be a code.</summary>
        private static string? NormalizeCode(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var sb = new StringBuilder(raw.Length);
            foreach (var c in raw) if (char.IsAsciiLetterOrDigit(c)) sb.Append(char.ToUpperInvariant(c));
            return sb.Length == CodeLength ? sb.ToString() : null;
        }

        private static string NewToken() =>
            "nxt_" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        private static string Hash(string value) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }
}
