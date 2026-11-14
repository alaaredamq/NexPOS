using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Common.Licensing;

namespace LicenseIssuer;

/// <summary>
/// Issuer-side tool for Coduo POS licenses. Run it on YOUR machine only; it is never shipped to clients.
/// The private signing key lives outside the repository (default: %USERPROFILE%\.nexpos-license\private.pem).
/// </summary>
internal static class Program
{
    private const string Usage = """
        Coduo POS License Issuer

          keygen   [--key <path>] [--encrypt] [--force]
                   Create the signing key pair. Run once; back the private key up in two offline places.

          pubkey   [--key <path>]
                   Print the public key (Base64) that is embedded in the app.

          issue    --machine <ID> --customer "<name>" --kind full|provisional|trial
                   [--days <n> | --until yyyy-MM-dd] [--key <path>] [--out <file>]
                   Sign a license for one PC.
                     full         perpetual; the date is the end of the updates plan   (default 365 days)
                     provisional  expires on the date (instalment sales)               (default 45 days)
                     trial        expires on the date (evaluation)                     (default 14 days)

          inspect  <key text | file>
                   Decode a key and verify it against the public key embedded in the app.

          protect  [--key <path>]
                   Encrypt an existing private key file with a passphrase.

        The passphrase is read from the NEXPOS_LICENSE_PASSPHRASE variable or prompted.
        Key path: --key, else the NEXPOS_LICENSE_KEY variable, else %USERPROFILE%\.nexpos-license\private.pem
        """;

    private static readonly HashSet<string> FlagOptions = new(StringComparer.OrdinalIgnoreCase) { "encrypt", "force", "allow-past" };

    private static int Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            Console.WriteLine(Usage);
            return 0;
        }

        var command = args[0].ToLowerInvariant();
        var (options, positionals) = ParseArguments(args.Skip(1));

        try
        {
            return command switch
            {
                "keygen" => KeyGen(options),
                "pubkey" => PubKey(options),
                "issue" => Issue(options),
                "inspect" => Inspect(positionals),
                "protect" => Protect(options),
                _ => Fail($"Unknown command '{command}'. Run without arguments for help.")
            };
        }
        catch (CliException ex)
        {
            return Fail(ex.Message);
        }
    }

    // ---- commands -------------------------------------------------------------------------

    private static int KeyGen(Dictionary<string, string> options)
    {
        var path = KeyPath(options);
        if (File.Exists(path) && !options.ContainsKey("force"))
            throw new CliException($"A private key already exists at {path}. Overwriting it would invalidate every license you issued. Use --force only if you really mean it.");

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        using var key = LicenseCodec.CreatePrivateKey();
        var pem = options.ContainsKey("encrypt")
            ? key.ExportEncryptedPkcs8PrivateKeyPem(ReadNewPassphrase(), EncryptionParameters)
            : key.ExportPkcs8PrivateKeyPem();
        File.WriteAllText(path, pem);

        Console.WriteLine($"Private key written to: {path}");
        Console.WriteLine("  BACK IT UP in two offline places and never commit it. If it leaks anyone can mint licenses;");
        Console.WriteLine("  if you lose it you cannot issue new ones.");
        Console.WriteLine();
        Console.WriteLine("Public key (embed this in Common/Licensing/LicensePublicKey.cs):");
        Console.WriteLine(LicenseCodec.ExportPublicKeyBase64(key));
        return 0;
    }

    private static int PubKey(Dictionary<string, string> options)
    {
        using var key = LoadPrivateKey(KeyPath(options));
        Console.WriteLine(LicenseCodec.ExportPublicKeyBase64(key));
        return 0;
    }

    private static int Issue(Dictionary<string, string> options)
    {
        var machineText = Required(options, "machine");
        if (!MachineId.TryParse(machineText, out var machine) || machine.IsEmpty)
            throw new CliException("--machine must be the Machine ID shown on the app's License page (XXXXX-XXXXX-XXXXX-XXXXX).");

        var customer = Required(options, "customer").Trim();
        if (customer.Length == 0) throw new CliException("--customer cannot be empty.");

        var kind = Required(options, "kind").ToLowerInvariant() switch
        {
            "full" => LicenseKind.Full,
            "provisional" => LicenseKind.Provisional,
            "trial" => LicenseKind.Trial,
            var other => throw new CliException($"Unknown kind '{other}'. Use full, provisional or trial.")
        };

        var today = DateOnly.FromDateTime(DateTime.Now);
        DateOnly until;
        if (options.TryGetValue("until", out var untilText))
        {
            if (!DateOnly.TryParseExact(untilText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out until))
                throw new CliException("--until must be a date like 2027-12-31.");
        }
        else
        {
            var defaultDays = kind switch { LicenseKind.Full => 365, LicenseKind.Provisional => 45, _ => 14 };
            var days = defaultDays;
            if (options.TryGetValue("days", out var daysText) && (!int.TryParse(daysText, out days) || days <= 0))
                throw new CliException("--days must be a positive number.");
            until = today.AddDays(days);
        }

        if (until < today && !options.ContainsKey("allow-past"))
            throw new CliException("The end date is in the past. (--allow-past exists for testing the expiry screens.)");

        var serial = BitConverter.ToUInt32(RandomNumberGenerator.GetBytes(4));
        var payload = new LicensePayload(kind, machine, today, until, serial, customer);

        using var key = LoadPrivateKey(KeyPath(options));
        var keyText = LicenseCodec.Encode(payload, key);

        // Catch the classic mistake early: signing with a key the shipped app does not trust.
        var embedded = LicensePublicKey.SpkiBase64;
        if (string.IsNullOrEmpty(embedded))
            Console.Error.WriteLine("WARNING: no public key is embedded in the app yet (Common/Licensing/LicensePublicKey.cs).");
        else if (!LicenseCodec.Decode(keyText, Convert.FromBase64String(embedded)).Success)
            throw new CliException("This private key does NOT match the public key embedded in the app. The key would be rejected by clients. Fix LicensePublicKey.cs or use the right private key.");

        if (options.TryGetValue("out", out var outPath)) File.WriteAllText(outPath, keyText + Environment.NewLine);
        AppendToLog(KeyPath(options), payload);

        Console.WriteLine($"Customer : {payload.Customer}");
        Console.WriteLine($"Machine  : {machine.Format()}");
        Console.WriteLine($"Type     : {kind}");
        Console.WriteLine(kind == LicenseKind.Full
            ? $"Updates  : until {until:yyyy-MM-dd} (the till itself never expires)"
            : $"Expires  : {until:yyyy-MM-dd}  (warns {until.AddDays(-7):yyyy-MM-dd}, grace until {until.AddDays(7):yyyy-MM-dd}, then the POS stops)");
        Console.WriteLine($"Serial   : {payload.SerialText}");
        Console.WriteLine();
        Console.WriteLine("License key (send this to the customer):");
        Console.WriteLine(keyText);
        if (options.ContainsKey("out")) Console.WriteLine($"\nAlso saved to {outPath}");
        return 0;
    }

    private static int Inspect(List<string> positionals)
    {
        if (positionals.Count == 0) throw new CliException("Give the key text or a file path.");
        var input = string.Join(' ', positionals);
        var text = File.Exists(input) ? File.ReadAllText(input) : input;

        if (string.IsNullOrEmpty(LicensePublicKey.SpkiBase64))
            throw new CliException("No public key is embedded in the app yet (Common/Licensing/LicensePublicKey.cs).");

        var result = LicenseCodec.Decode(text, Convert.FromBase64String(LicensePublicKey.SpkiBase64));
        if (!result.Success) throw new CliException($"Not a valid license for this app: {result.Error}.");

        var p = result.Payload!;
        Console.WriteLine("Signature : valid");
        Console.WriteLine($"Customer  : {p.Customer}");
        Console.WriteLine($"Machine   : {p.Machine.Format()}");
        Console.WriteLine($"Type      : {p.Kind}");
        Console.WriteLine($"Issued    : {p.IssuedOn:yyyy-MM-dd}");
        Console.WriteLine(p.IsTimeLimited ? $"Expires   : {p.ValidUntil:yyyy-MM-dd}" : $"Updates   : until {p.ValidUntil:yyyy-MM-dd}");
        Console.WriteLine($"Serial    : {p.SerialText}");
        return 0;
    }

    private static int Protect(Dictionary<string, string> options)
    {
        var path = KeyPath(options);
        var pem = File.ReadAllText(path);
        if (pem.Contains("ENCRYPTED PRIVATE KEY", StringComparison.Ordinal))
            throw new CliException("That key is already passphrase-protected.");

        using var key = ECDsa.Create();
        key.ImportFromPem(pem);
        var encrypted = key.ExportEncryptedPkcs8PrivateKeyPem(ReadNewPassphrase(), EncryptionParameters);

        // Write beside the original first so a failure can never leave you without a key.
        var temp = path + ".new";
        File.WriteAllText(temp, encrypted);
        File.Move(temp, path, overwrite: true);
        Console.WriteLine($"Private key at {path} is now passphrase-protected. The key itself did not change, so existing licenses stay valid.");
        return 0;
    }

    // ---- helpers --------------------------------------------------------------------------

    private static readonly PbeParameters EncryptionParameters =
        new(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 200_000);

    private static ECDsa LoadPrivateKey(string path)
    {
        if (!File.Exists(path))
            throw new CliException($"No private key at {path}. Run 'keygen' first, or pass --key <path>.");

        var pem = File.ReadAllText(path);
        var key = ECDsa.Create();
        try
        {
            if (pem.Contains("ENCRYPTED PRIVATE KEY", StringComparison.Ordinal))
                key.ImportFromEncryptedPem(pem, ReadPassphrase("Key passphrase: "));
            else
                key.ImportFromPem(pem);
        }
        catch (CryptographicException)
        {
            key.Dispose();
            throw new CliException("Could not read the private key (wrong passphrase, or the file is damaged).");
        }
        return key;
    }

    private static string KeyPath(Dictionary<string, string> options)
    {
        if (options.TryGetValue("key", out var explicitPath)) return Path.GetFullPath(explicitPath);

        var fromEnv = Environment.GetEnvironmentVariable("NEXPOS_LICENSE_KEY");
        if (!string.IsNullOrWhiteSpace(fromEnv)) return Path.GetFullPath(fromEnv);

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nexpos-license", "private.pem");
    }

    /// <summary>Keeps a CSV of everything you issued, next to the private key, so renewals are easy to find.</summary>
    private static void AppendToLog(string keyPath, LicensePayload p)
    {
        var logPath = Path.Combine(Path.GetDirectoryName(keyPath)!, "issued.csv");
        var isNew = !File.Exists(logPath);
        var sb = new StringBuilder();
        if (isNew) sb.AppendLine("issued_on,customer,kind,valid_until,machine_id,serial");
        sb.AppendLine(string.Join(',',
            p.IssuedOn.ToString("yyyy-MM-dd"),
            Csv(p.Customer),
            p.Kind,
            p.ValidUntil.ToString("yyyy-MM-dd"),
            p.Machine.Format(),
            p.SerialText));
        File.AppendAllText(logPath, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    private static string Csv(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";

    private static string Required(Dictionary<string, string> options, string name) =>
        options.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new CliException($"Missing --{name}.");

    private static (Dictionary<string, string> Options, List<string> Positionals) ParseArguments(IEnumerable<string> args)
    {
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var positionals = new List<string>();
        var list = args.ToList();

        for (var i = 0; i < list.Count; i++)
        {
            if (!list[i].StartsWith("--", StringComparison.Ordinal))
            {
                positionals.Add(list[i]);
                continue;
            }

            var name = list[i][2..];
            if (FlagOptions.Contains(name))
            {
                options[name] = "true";
            }
            else if (i + 1 < list.Count)
            {
                options[name] = list[++i];
            }
            else
            {
                throw new CliException($"--{name} needs a value.");
            }
        }
        return (options, positionals);
    }

    private static string ReadNewPassphrase()
    {
        var env = Environment.GetEnvironmentVariable("NEXPOS_LICENSE_PASSPHRASE");
        if (!string.IsNullOrEmpty(env)) return env;

        var first = ReadPassphrase("New passphrase: ");
        var second = ReadPassphrase("Repeat passphrase: ");
        if (first != second) throw new CliException("The passphrases do not match.");
        if (first.Length < 8) throw new CliException("Use at least 8 characters.");
        return first;
    }

    private static string ReadPassphrase(string prompt)
    {
        var env = Environment.GetEnvironmentVariable("NEXPOS_LICENSE_PASSPHRASE");
        if (!string.IsNullOrEmpty(env)) return env;

        Console.Error.Write(prompt);
        if (Console.IsInputRedirected) return Console.ReadLine() ?? string.Empty;

        var sb = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter) break;
            if (key.Key == ConsoleKey.Backspace) { if (sb.Length > 0) sb.Length--; continue; }
            if (!char.IsControl(key.KeyChar)) sb.Append(key.KeyChar);
        }
        Console.Error.WriteLine();
        return sb.ToString();
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine("ERROR: " + message);
        return 1;
    }

    private sealed class CliException(string message) : Exception(message);
}
