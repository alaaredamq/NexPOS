using System.Security.Cryptography;
using Application.Services.Licensing;
using Common.Licensing;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tests.Licensing;

/// <summary>Shared fixtures for the licensing tests: a throwaway issuer key pair, fake PC identity, fake clock and in-memory storage.</summary>
internal sealed class LicenseTestIssuer : IDisposable
{
    public ECDsa PrivateKey { get; } = LicenseCodec.CreatePrivateKey();
    public string PublicKeyBase64 => LicenseCodec.ExportPublicKeyBase64(PrivateKey);
    public byte[] PublicKeySpki => PrivateKey.ExportSubjectPublicKeyInfo();

    public string Issue(
        LicenseKind kind,
        MachineId machine,
        DateOnly issuedOn,
        DateOnly validUntil,
        string customer = "Test Shop",
        uint serial = 0xA1B2C3D4) =>
        LicenseCodec.Encode(new LicensePayload(kind, machine, issuedOn, validUntil, serial, customer), PrivateKey);

    public void Dispose() => PrivateKey.Dispose();
}

internal static class TestMachines
{
    /// <summary>Four distinct usable hardware facts, like a normal PC.</summary>
    public static MachineId ShopPc { get; } = MachineId.FromComponents(new[]
    {
        "6F1A2B3C-0001-4D5E-8F90-AAAAAAAAAAAA",
        "4C4C4544-0050-3910-8052-B7C04F4B4E32",
        "BOARD-SERIAL-12345",
        "DISK-SERIAL-98765"
    });

    public static MachineId OtherPc { get; } = MachineId.FromComponents(new[]
    {
        "11111111-2222-3333-4444-555555555555",
        "99999999-8888-7777-6666-555555555555",
        "OTHER-BOARD-777",
        "OTHER-DISK-888"
    });
}

internal sealed class FakeMachineIdProvider : IMachineIdProvider
{
    private readonly MachineId _id;
    public FakeMachineIdProvider(MachineId id) => _id = id;
    public MachineId GetMachineId() => _id;
}

internal sealed class InMemoryLicenseStorage : ILicenseStorage
{
    public string? Text { get; set; }
    public string? Read() => Text;
    public void Write(string text) => Text = text;
}

internal sealed class FakeClock : TimeProvider
{
    private DateTimeOffset _now;

    public FakeClock(DateOnly today) => SetToday(today);

    public void SetToday(DateOnly today) =>
        _now = new DateTimeOffset(today.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now;
    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}

internal static class LicenseServiceFactory
{
    public static LicenseService Create(
        LicenseTestIssuer issuer,
        InMemoryLicenseStorage storage,
        FakeClock clock,
        MachineId? machine = null,
        DateOnly? buildDate = null,
        bool enforce = true) =>
        new(
            new LicenseOptions
            {
                PublicKeySpkiBase64 = issuer.PublicKeyBase64,
                BuildDate = buildDate ?? new DateOnly(2026, 10, 1),
                EnforcementEnabled = enforce
            },
            storage,
            new FakeMachineIdProvider(machine ?? TestMachines.ShopPc),
            clock,
            NullLogger<LicenseService>.Instance);
}
