namespace LicenseKeygen;

/// <summary>
/// Coduo POS Keygen: issuer-side tools that stay on YOUR machine and are never shipped to clients.
/// Two tabs: Keys (sign a licence for a customer's PC) and Installer (set the version and build a new setup file).
/// </summary>
internal sealed class MainForm : Form
{
    /// <summary>Where the keygen remembers its choices (key file, repository folder).</summary>
    public static readonly string SettingsFolder =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NexPOS-Keygen");

    private readonly KeysPage _keys = new();
    private readonly InstallerPage _installer = new();

    public MainForm()
    {
        Text = "Coduo POS Keygen";
        Font = new Font("Segoe UI", 10f);
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(760, 700);
        MinimumSize = new Size(620, 620);

        var tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(16, 6) };
        var keysTab = new TabPage("Keys") { UseVisualStyleBackColor = true };
        var installerTab = new TabPage("Installer") { UseVisualStyleBackColor = true };
        keysTab.Controls.Add(_keys);
        installerTab.Controls.Add(_installer);
        tabs.TabPages.AddRange([keysTab, installerTab]);
        Controls.Add(tabs);

        // Enter presses the main button of whichever tab is open.
        AcceptButton = _keys.DefaultButton;
        tabs.SelectedIndexChanged += (_, _) => AcceptButton = tabs.SelectedTab == keysTab ? _keys.DefaultButton : _installer.DefaultButton;

        FormClosing += (_, e) =>
        {
            if (!_installer.IsBuilding) return;
            var answer = MessageBox.Show(this, "An installer is still being built. Stop the build and close?", Text,
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (answer == DialogResult.Yes) _installer.Stop();
            else e.Cancel = true;
        };
    }
}
