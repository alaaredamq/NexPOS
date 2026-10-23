using Application.DTOs.Licensing;
using Application.Exceptions;
using Application.IServices.Licensing;
using Common.Licensing;
using Microsoft.Extensions.Logging;

namespace Application.Services.Licensing
{
    /// <summary>
    /// Decides what the current license allows. Singleton: the key is verified once (and again after
    /// activation); each <see cref="GetState"/> call afterwards is only date arithmetic.
    ///
    /// Rules:
    /// - Full keys never expire; they only refuse builds released after the updates-until date.
    /// - Provisional/Trial keys: warning during the last <see cref="LicenseOptions.WarningDays"/> days,
    ///   then <see cref="LicenseOptions.GraceDays"/> days of "renew now" (selling still works),
    ///   then the POS and new sales stop. Data is never touched.
    /// </summary>
    public sealed class LicenseService : ILicenseService
    {
        private sealed record Loaded(LicensePayload? Payload, LicenseStatus? Problem);

        private readonly LicenseOptions _options;
        private readonly ILicenseStorage _storage;
        private readonly TimeProvider _time;
        private readonly ILogger<LicenseService> _logger;
        private readonly byte[] _publicKey;
        private readonly Lazy<MachineId> _machineId;
        private readonly object _gate = new();
        private Loaded? _loaded;

        public LicenseService(
            LicenseOptions options,
            ILicenseStorage storage,
            IMachineIdProvider machineIdProvider,
            TimeProvider time,
            ILogger<LicenseService> logger)
        {
            _options = options;
            _storage = storage;
            _time = time;
            _logger = logger;

            try
            {
                _publicKey = Convert.FromBase64String(options.PublicKeySpkiBase64);
            }
            catch (FormatException)
            {
                _publicKey = Array.Empty<byte>();
            }

            if (options.EnforcementEnabled && _publicKey.Length == 0)
            {
                throw new InvalidOperationException(
                    "The license public key is not configured (Common/Licensing/LicensePublicKey.cs). A build without it cannot verify any license.");
            }

            _machineId = new Lazy<MachineId>(() =>
            {
                try
                {
                    return machineIdProvider.GetMachineId();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Could not read this computer's identity for licensing");
                    return MachineId.FromBytes(new byte[MachineId.Length]);
                }
            });
        }

        public LicenseState GetState()
        {
            var loaded = Load();

            if (!_options.EnforcementEnabled)
            {
                return Build(LicenseStatus.Active, loaded.Payload) with { EnforcementDisabled = true };
            }

            if (loaded.Problem is { } problem)
            {
                return Build(problem, loaded.Payload);
            }

            var license = loaded.Payload!;

            if (!license.IsTimeLimited)
            {
                return _options.BuildDate > license.ValidUntil
                    ? Build(LicenseStatus.UpdatesNotCovered, license)
                    : Build(LicenseStatus.Active, license);
            }

            var today = DateOnly.FromDateTime(_time.GetLocalNow().DateTime);
            var daysLeft = license.ValidUntil.DayNumber - today.DayNumber;
            var salesStopAfter = license.ValidUntil.AddDays(_options.GraceDays);

            if (daysLeft > _options.WarningDays)
            {
                return Build(LicenseStatus.Active, license) with { SalesStopAfter = salesStopAfter };
            }

            if (daysLeft >= 0)
            {
                return Build(LicenseStatus.ExpiringSoon, license) with { DaysLeft = daysLeft, SalesStopAfter = salesStopAfter };
            }

            var daysPastExpiry = -daysLeft;
            if (daysPastExpiry <= _options.GraceDays)
            {
                return Build(LicenseStatus.GracePeriod, license) with
                {
                    DaysUntilStop = _options.GraceDays - daysPastExpiry,
                    SalesStopAfter = salesStopAfter
                };
            }

            return Build(LicenseStatus.Expired, license) with { SalesStopAfter = salesStopAfter };
        }

        public void EnsureSalesAllowed()
        {
            var state = GetState();
            if (!state.AllowsSales) throw new LicenseRequiredException(state.Status);
        }

        public LicenseActivationResult Activate(string licenseKeyText)
        {
            lock (_gate)
            {
                var decoded = LicenseCodec.Decode(licenseKeyText, _publicKey);
                if (!decoded.Success)
                {
                    _logger.LogWarning("License activation rejected: {Error}", decoded.Error);
                    return Fail(decoded.Error switch
                    {
                        LicenseDecodeError.BadSignature => LicenseActivationError.BadSignature,
                        LicenseDecodeError.UnsupportedVersion => LicenseActivationError.UnsupportedVersion,
                        _ => LicenseActivationError.Malformed
                    });
                }

                var payload = decoded.Payload!;
                if (!payload.Machine.IsSameMachineAs(_machineId.Value))
                {
                    _logger.LogWarning("License activation rejected: key {Serial} was issued for a different computer", payload.SerialText);
                    return Fail(LicenseActivationError.WrongMachine);
                }

                var current = Load();
                if (current.Problem is null && current.Payload is { } existing && !IsAtLeastAsStrong(payload, existing))
                {
                    _logger.LogWarning("License activation rejected: key {Serial} is older than the active license {Active}", payload.SerialText, existing.SerialText);
                    return Fail(LicenseActivationError.OlderThanCurrent);
                }

                try
                {
                    _storage.Write(LicenseCodec.Canonicalize(licenseKeyText));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Could not save the license");
                    return Fail(LicenseActivationError.StorageFailed);
                }

                Volatile.Write(ref _loaded, new Loaded(payload, null));
                _logger.LogInformation("License activated: {Kind} for {Customer}, serial {Serial}, valid until {ValidUntil}",
                    payload.Kind, payload.Customer, payload.SerialText, payload.ValidUntil);

                return new LicenseActivationResult(true, LicenseActivationError.None, GetState());
            }
        }

        private LicenseActivationResult Fail(LicenseActivationError error) =>
            new(false, error, GetState());

        private Loaded Load()
        {
            var current = Volatile.Read(ref _loaded);
            if (current is not null) return current;

            lock (_gate)
            {
                _loaded ??= ReadFromStorage();
                return _loaded;
            }
        }

        private Loaded ReadFromStorage()
        {
            string? text;
            try
            {
                text = _storage.Read();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not read the saved license");
                return new Loaded(null, LicenseStatus.Invalid);
            }

            if (string.IsNullOrWhiteSpace(text)) return new Loaded(null, LicenseStatus.NotActivated);

            var decoded = LicenseCodec.Decode(text, _publicKey);
            if (!decoded.Success)
            {
                _logger.LogWarning("The saved license is not valid: {Error}", decoded.Error);
                return new Loaded(null, LicenseStatus.Invalid);
            }

            var payload = decoded.Payload!;
            return payload.Machine.IsSameMachineAs(_machineId.Value)
                ? new Loaded(payload, null)
                : new Loaded(payload, LicenseStatus.WrongMachine);
        }

        /// <summary>
        /// A newer key replaces an older one, but an old key cannot replace a newer one (so pasting last
        /// year's key can never downgrade the shop). Same day: Full beats time-limited, then the later date wins.
        /// </summary>
        private static bool IsAtLeastAsStrong(LicensePayload incoming, LicensePayload existing)
        {
            if (incoming.IssuedOn != existing.IssuedOn) return incoming.IssuedOn > existing.IssuedOn;
            if (incoming.Kind == LicenseKind.Full && existing.Kind != LicenseKind.Full) return true;
            if (incoming.Kind != LicenseKind.Full && existing.Kind == LicenseKind.Full) return false;
            return incoming.ValidUntil >= existing.ValidUntil;
        }

        private LicenseState Build(LicenseStatus status, LicensePayload? payload) => new()
        {
            Status = status,
            MachineId = _machineId.Value.Format(),
            BuildDate = _options.BuildDate,
            Customer = payload?.Customer,
            Kind = payload?.Kind,
            Serial = payload?.SerialText,
            IssuedOn = payload?.IssuedOn,
            ValidUntil = payload?.ValidUntil
        };
    }
}
