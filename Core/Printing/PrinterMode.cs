using System.Text.Json;

namespace Core.Printing;

/// <summary>
/// The language a printer is listening for. A 2-in-1 printer such as the Xprinter XP-365B is set to one of them on the printer itself
/// (Pause + power, then Feed; or a DIP switch) and silently ignores the other, so the till has to send what the printer is set to:
/// <list type="bullet">
/// <item><see cref="Receipt"/>: ESC/POS, what receipt printers speak (receipts as text or a picture, labels as a picture).</item>
/// <item><see cref="Label"/>: TSPL, what label printers speak (labels as today, receipts as one long picture on continuous paper).</item>
/// </list>
/// </summary>
public enum PrinterMode { Receipt, Label }

/// <summary>
/// Each printer's mode, kept in one setting (<see cref="SettingKey"/>) as a small JSON object of printer name to "receipt" / "label".
/// A printer that is not listed keeps what each job sends by default: receipts in ESC/POS, direct labels in TSPL.
/// </summary>
public static class PrinterModes
{
    public const string SettingKey = "PrinterModes";

    public static Dictionary<string, PrinterMode> Parse(string? json)
    {
        var modes = new Dictionary<string, PrinterMode>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(json)) return modes;
        try
        {
            foreach (var (name, value) in JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [])
                if (TryRead(value, out var mode)) modes[name] = mode;
        }
        catch (JsonException)
        {
            // A damaged value reads as "nothing set": every printer falls back to its job's default.
        }
        return modes;
    }

    public static string Write(IReadOnlyDictionary<string, PrinterMode> modes)
        => JsonSerializer.Serialize(modes.ToDictionary(p => p.Key, p => Name(p.Value)));

    public static string Name(PrinterMode mode) => mode == PrinterMode.Label ? "label" : "receipt";

    public static bool TryRead(string? value, out PrinterMode mode)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "label": mode = PrinterMode.Label; return true;
            case "receipt": mode = PrinterMode.Receipt; return true;
            default: mode = PrinterMode.Receipt; return false;
        }
    }
}
