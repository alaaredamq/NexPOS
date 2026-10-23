using System.Management;
using System.Runtime.Versioning;
using Common.Licensing;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace Infrastructure.Licensing;

/// <summary>
/// Reads four hardware facts from Windows and turns them into a <see cref="MachineId"/>
/// (slot order is part of the license format, see <see cref="MachineId"/>):
/// 0 = MachineGuid (changes when Windows is reinstalled), 1 = SMBIOS system UUID,
/// 2 = motherboard serial, 3 = system disk serial.
/// A fact that cannot be read, or is a known placeholder, is simply left out.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsMachineIdProvider : IMachineIdProvider
{
    private readonly ILogger<WindowsMachineIdProvider> _logger;

    public WindowsMachineIdProvider(ILogger<WindowsMachineIdProvider> logger) => _logger = logger;

    public MachineId GetMachineId()
    {
        var components = new string?[]
        {
            Safe("MachineGuid", ReadMachineGuid),
            Safe("SMBIOS UUID", () => QueryFirst("SELECT UUID FROM Win32_ComputerSystemProduct", "UUID")),
            Safe("motherboard serial", () => QueryFirst("SELECT SerialNumber FROM Win32_BaseBoard", "SerialNumber")),
            Safe("disk serial", ReadSystemDiskSerial)
        };

        var id = MachineId.FromComponents(components);
        _logger.LogInformation("Machine identity: {Usable} of {Total} hardware facts usable", components.Count(MachineId.IsUsableComponent), components.Length);
        return id;
    }

    private string? Safe(string what, Func<string?> read)
    {
        try
        {
            return read();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not read {Fact} for the machine identity", what);
            return null;
        }
    }

    private static string? ReadMachineGuid()
    {
        // The 64-bit view: a 32-bit process would otherwise read the redirected (empty) key.
        using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var key = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
        return key?.GetValue("MachineGuid") as string;
    }

    private static string? QueryFirst(string query, string property)
    {
        using var searcher = new ManagementObjectSearcher(query);
        foreach (var obj in searcher.Get())
        {
            using (obj)
            {
                var value = obj[property]?.ToString();
                if (MachineId.IsUsableComponent(value)) return value;
            }
        }
        return null;
    }

    /// <summary>Serial of the first internal (non-USB) fixed disk, so a plugged-in USB stick never changes the identity.</summary>
    private static string? ReadSystemDiskSerial()
    {
        using var searcher = new ManagementObjectSearcher("SELECT Index, SerialNumber, InterfaceType, MediaType FROM Win32_DiskDrive");
        var disks = new List<(uint Index, string Serial)>();

        foreach (var obj in searcher.Get())
        {
            using (obj)
            {
                var interfaceType = obj["InterfaceType"]?.ToString();
                if (string.Equals(interfaceType, "USB", StringComparison.OrdinalIgnoreCase)) continue;

                var mediaType = obj["MediaType"]?.ToString();
                if (mediaType != null && mediaType.Contains("Removable", StringComparison.OrdinalIgnoreCase)) continue;

                var serial = obj["SerialNumber"]?.ToString()?.Trim();
                if (!MachineId.IsUsableComponent(serial)) continue;

                var index = obj["Index"] is uint i ? i : uint.MaxValue;
                disks.Add((index, serial!));
            }
        }

        return disks.OrderBy(d => d.Index).Select(d => d.Serial).FirstOrDefault();
    }
}
