using System.Security.Cryptography;
using System.Text;
using Common.Licensing;

namespace LicenseKeygen;

/// <summary>
/// Issuer-side GUI for Coduo POS licenses: Machine ID + number of days in, signed key out.
/// Run it on YOUR machine only; it is never shipped to clients. It signs with the same private key
/// and the same codec as tools/LicenseIssuer, so keys from either tool are interchangeable.
/// </summary>
internal sealed class MainForm : Form
{
    private static readonly string SettingsPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NexPOS-Keygen", "settings.txt");

    private readonly TextBox _machine = new() { CharacterCasing = CharacterCasing.Upper, Font = new Font("Consolas", 12f) };
    private readonly Label _machineHint = new() { AutoSize = true };
    private readonly Label _seatsHint = new() { AutoSize = true };
    private readonly NumericUpDown _days = new() { Minimum = 1, Maximum = 3650, Value = 30 };
    private readonly NumericUpDown _seats = new() { Minimum = 1, Maximum = LicenseCodec.MaxSeats, Value = 1 };
    private readonly ComboBox _kind = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _customer = new() { PlaceholderText = "Optional - shown on the customer's License page" };
    private readonly TextBox _keyPath = new() { ReadOnly = true };
    private readonly TextBox _passphrase = new() { UseSystemPasswordChar = true, Enabled = false };
    private readonly Label _summary = new() { AutoSize = true };
    private readonly TextBox _output = new()
    {
        Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
        Font = new Font("Consolas", 11f), Dock = DockStyle.Fill, BackColor = SystemColors.Window
    };
    private readonly Button _generate = new() { Text = "Generate key", AutoSize = true, Padding = new Padding(14, 4, 14, 4) };
    private readonly Button _copy = new() { Text = "Copy", AutoSize = true, Enabled = false };
    private readonly Button _save = new() { Text = "Save to file...", AutoSize = true, Enabled = false };

    public MainForm()
    {
        Text = "Coduo POS Keygen";
        Font = new Font("Segoe UI", 10f);
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(640, 600);
        MinimumSize = new Size(560, 560);

        _kind.Items.AddRange(["Provisional - expires after N days (instalment sale)", "Trial - expires after N days (evaluation)", "Full - perpetual, N days of updates"]);
        _kind.SelectedIndex = 0;

        _keyPath.Text = LoadKeyPath();

        BuildLayout();
        Wire();
        RefreshMachineHint();
        RefreshSeatsHint();
        RefreshPassphraseState();
    }

    private void BuildLayout()
    {
        var browse = new Button { Text = "Browse...", AutoSize = true };
        browse.Click += (_, _) => BrowseForKey();

        var grid = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3, Padding = new Padding(16, 16, 16, 0) };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        void Row(string label, Control input, Control? extra = null)
        {
            var row = grid.RowCount++;
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grid.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 8, 12, 8) }, 0, row);
            input.Dock = DockStyle.Fill;
            input.Margin = new Padding(0, 6, 0, 6);
            if (extra is null)
            {
                grid.Controls.Add(input, 1, row);
                grid.SetColumnSpan(input, 2);
            }
            else
            {
                grid.Controls.Add(input, 1, row);
                extra.Margin = new Padding(8, 5, 0, 5);
                grid.Controls.Add(extra, 2, row);
            }
        }

        Row("Server Machine ID", _machine);
        grid.RowCount++;
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.Controls.Add(_machineHint, 1, grid.RowCount - 1);
        grid.SetColumnSpan(_machineHint, 2);

        Row("Days", _days);
        Row("PCs (package)", _seats);
        grid.RowCount++;
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.Controls.Add(_seatsHint, 1, grid.RowCount - 1);
        grid.SetColumnSpan(_seatsHint, 2);
        Row("Type", _kind);
        Row("Customer", _customer);
        Row("Private key", _keyPath, browse);
        Row("Passphrase", _passphrase);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(16, 8, 16, 0) };
        actions.Controls.AddRange([_generate, _copy, _save]);

        var summaryHost = new Panel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(16, 10, 16, 0) };
        summaryHost.Controls.Add(_summary);

        var outputHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16) };
        outputHost.Controls.Add(_output);

        Controls.Add(outputHost);
        Controls.Add(summaryHost);
        Controls.Add(actions);
        Controls.Add(grid);

        AcceptButton = _generate;
    }

    private void Wire()
    {
        _machine.TextChanged += (_, _) => RefreshMachineHint();
        _seats.ValueChanged += (_, _) => RefreshSeatsHint();
        _generate.Click += (_, _) => Generate();
        _copy.Click += (_, _) =>
        {
            Clipboard.SetText(_output.Text);
            _summary.ForeColor = Color.SeaGreen;
            _summary.Text = "Copied to the clipboard.";
        };
        _save.Click += (_, _) => SaveToFile();
    }

    private void RefreshSeatsHint()
    {
        var seats = (int)_seats.Value;
        if (seats == 1)
        {
            _seatsHint.ForeColor = SystemColors.GrayText;
            _seatsHint.Text = "One PC. Works with every Coduo POS build.";
        }
        else
        {
            _seatsHint.ForeColor = Color.DarkOrange;
            _seatsHint.Text = $"The server + {seats - 1} till{(seats == 2 ? "" : "s")}. Needs a Coduo POS build that knows multi-seat keys.";
        }
    }

    private void RefreshMachineHint()
    {
        if (string.IsNullOrWhiteSpace(_machine.Text))
        {
            _machineHint.ForeColor = SystemColors.GrayText;
            _machineHint.Text = "Paste the ID from the customer's License page (XXXXX-XXXXX-XXXXX-XXXXX).";
        }
        else if (MachineId.TryParse(_machine.Text, out var id) && !id.IsEmpty)
        {
            _machineHint.ForeColor = Color.SeaGreen;
            _machineHint.Text = "Valid: " + id.Format();
        }
        else
        {
            _machineHint.ForeColor = Color.Firebrick;
            _machineHint.Text = "Not a valid Machine ID (20 letters/digits expected).";
        }
    }

    // ---- key file -------------------------------------------------------------------------

    private static string DefaultKeyPath()
    {
        var fromEnv = Environment.GetEnvironmentVariable("NEXPOS_LICENSE_KEY");
        return !string.IsNullOrWhiteSpace(fromEnv)
            ? Path.GetFullPath(fromEnv)
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nexpos-license", "private.pem");
    }

    private static string LoadKeyPath()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var saved = File.ReadAllText(SettingsPath).Trim();
                if (saved.Length > 0) return saved;
            }
        }
        catch (IOException) { }
        return DefaultKeyPath();
    }

    private void BrowseForKey()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Select the Coduo POS private signing key",
            Filter = "PEM key (*.pem)|*.pem|All files (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        _keyPath.Text = dialog.FileName;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, dialog.FileName);
        }
        catch (IOException) { /* remembering the path is a convenience only */ }
        RefreshPassphraseState();
    }

    private void RefreshPassphraseState()
    {
        var path = _keyPath.Text;
        var exists = File.Exists(path);
        var encrypted = exists && File.ReadAllText(path).Contains("ENCRYPTED PRIVATE KEY", StringComparison.Ordinal);
        _passphrase.Enabled = encrypted;
        _passphrase.PlaceholderText = !exists ? "Key file not found - use Browse..."
            : encrypted ? "Required for this key"
            : "Not needed - key is not encrypted";
        if (!encrypted) _passphrase.Clear();
    }

    private ECDsa LoadPrivateKey()
    {
        var path = _keyPath.Text;
        if (!File.Exists(path))
            throw new UserException($"No private key at:\n{path}\n\nUse Browse... to select it.");

        var pem = File.ReadAllText(path);
        var key = ECDsa.Create();
        try
        {
            if (pem.Contains("ENCRYPTED PRIVATE KEY", StringComparison.Ordinal))
            {
                if (_passphrase.Text.Length == 0) throw new UserException("Enter the key passphrase.");
                key.ImportFromEncryptedPem(pem, _passphrase.Text);
            }
            else
            {
                key.ImportFromPem(pem);
            }
        }
        catch (CryptographicException)
        {
            key.Dispose();
            throw new UserException("Could not read the private key (wrong passphrase, or the file is damaged).");
        }
        catch
        {
            key.Dispose();
            throw;
        }
        return key;
    }

    // ---- issuing --------------------------------------------------------------------------

    private void Generate()
    {
        try
        {
            if (!MachineId.TryParse(_machine.Text, out var machine) || machine.IsEmpty)
                throw new UserException("Enter a valid Machine ID (XXXXX-XXXXX-XXXXX-XXXXX).");

            // Combo order is Provisional, Trial, Full; the enum order is Full, Provisional, Trial.
            var kind = _kind.SelectedIndex switch { 0 => LicenseKind.Provisional, 1 => LicenseKind.Trial, _ => LicenseKind.Full };

            var today = DateOnly.FromDateTime(DateTime.Now);
            var until = today.AddDays((int)_days.Value);
            var serial = BitConverter.ToUInt32(RandomNumberGenerator.GetBytes(4));
            var seats = (int)_seats.Value;
            var payload = new LicensePayload(kind, machine, today, until, serial, _customer.Text.Trim(), seats);

            using var key = LoadPrivateKey();
            var keyText = LicenseCodec.Encode(payload, key);

            // Catch the classic mistake early: signing with a key the shipped app does not trust.
            if (!LicenseCodec.Decode(keyText, Convert.FromBase64String(LicensePublicKey.SpkiBase64)).Success)
                throw new UserException("This private key does NOT match the public key embedded in the app, so clients would reject the key.\n\nSelect the correct private key.");

            AppendToLog(payload);

            _output.Text = keyText;
            _copy.Enabled = _save.Enabled = true;
            _summary.ForeColor = SystemColors.ControlText;
            var pcs = seats == 1 ? "1 PC" : $"{seats} PCs";
            _summary.Text = kind == LicenseKind.Full
                ? $"{machine.Format()}  |  Full  |  {pcs}  |  updates until {until:yyyy-MM-dd}  |  serial {payload.SerialText}"
                : $"{machine.Format()}  |  {kind}  |  {pcs}  |  expires {until:yyyy-MM-dd} ({(int)_days.Value} days)  |  serial {payload.SerialText}";
        }
        catch (UserException ex)
        {
            _summary.ForeColor = Color.Firebrick;
            _summary.Text = "";
            MessageBox.Show(this, ex.Message, "Coduo POS Keygen", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void SaveToFile()
    {
        using var dialog = new SaveFileDialog { Filter = "Text file (*.txt)|*.txt", FileName = "nexpos-license.txt" };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            File.WriteAllText(dialog.FileName, _output.Text + Environment.NewLine);
    }

    /// <summary>Keeps a CSV of everything issued, next to the private key (same file and format as the CLI issuer).</summary>
    private void AppendToLog(LicensePayload p)
    {
        var logPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(_keyPath.Text))!, "issued.csv");
        var sb = new StringBuilder();
        if (!File.Exists(logPath)) sb.AppendLine("issued_on,customer,kind,valid_until,machine_id,serial,seats");
        sb.AppendLine(string.Join(',',
            p.IssuedOn.ToString("yyyy-MM-dd"),
            "\"" + p.Customer.Replace("\"", "\"\"") + "\"",
            p.Kind,
            p.ValidUntil.ToString("yyyy-MM-dd"),
            p.Machine.Format(),
            p.SerialText,
            p.Seats));
        File.AppendAllText(logPath, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    private sealed class UserException(string message) : Exception(message);
}
