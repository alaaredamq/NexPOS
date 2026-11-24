using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace LicenseKeygen;

/// <summary>
/// The Installer tab: set the version, choose live or test, and build a new Coduo POS installer.
/// It writes the version into package.json and runs build-electron.ps1 in the repository, showing its output as it goes.
/// </summary>
internal sealed class InstallerPage : UserControl
{
    private const string LiveServer = "coduos.com";
    private const string TestServer = "nexpos.tryasp.net";
    private static readonly string RepoSettingPath = Path.Combine(MainForm.SettingsFolder, "repository.txt");
    private static readonly Regex VersionInPackage = new(@"(""version""\s*:\s*"")([^""]*)("")", RegexOptions.Compiled);
    private static readonly Regex ThreeNumbers = new(@"^\d+\.\d+\.\d+$", RegexOptions.Compiled);
    private static readonly Regex AnsiCodes = new(@"\x1B\[[0-9;?]*[A-Za-z]", RegexOptions.Compiled);

    private readonly TextBox _repo = new() { ReadOnly = true };
    private readonly Label _repoHint = new() { AutoSize = true };
    private readonly TextBox _version = new() { Width = 110, Font = new Font("Consolas", 12f) };
    private readonly Button _nextPatch = new() { AutoSize = true };
    private readonly Button _nextMinor = new() { AutoSize = true };
    private readonly Button _nextMajor = new() { AutoSize = true };
    private readonly Label _versionHint = new() { AutoSize = true, MaximumSize = new Size(560, 0) };
    private readonly RadioButton _live = new() { Text = $"Live ({LiveServer})", AutoSize = true, Checked = true };
    private readonly RadioButton _test = new() { Text = $"Test ({TestServer})", AutoSize = true };
    private readonly Label _serverHint = new() { AutoSize = true, MaximumSize = new Size(560, 0) };
    private readonly ComboBox _package = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Button _build = new() { Text = "Build installer", AutoSize = true, Padding = new Padding(14, 4, 14, 4) };
    private readonly Button _stop = new() { Text = "Stop", AutoSize = true, Enabled = false };
    private readonly Button _openFolder = new() { Text = "Open dist folder", AutoSize = true };
    private readonly Label _status = new() { AutoSize = true, MaximumSize = new Size(700, 0) };
    private readonly TextBox _log = new()
    {
        Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Dock = DockStyle.Fill,
        Font = new Font("Consolas", 9.5f), BackColor = Color.FromArgb(24, 26, 29), ForeColor = Color.FromArgb(220, 224, 228),
    };

    private static readonly Color Amber = Color.FromArgb(166, 84, 0); // warnings, readable on the light window
    private readonly ToolTip _tips = new();
    private Process? _process;
    private bool _stopped;
    private string? _lastBuilt;

    public InstallerPage()
    {
        Dock = DockStyle.Fill;
        _package.Items.AddRange(["Setup installer (what customers get)", "Portable .exe (runs without installing)", "Both"]);
        _package.SelectedIndex = 0;

        BuildLayout();
        Wire();
        UseRepository(FindRepository());
        RefreshServerHint();
    }

    /// <summary>The main action of this tab; the window makes it the Enter key while the tab is open.</summary>
    public Button DefaultButton => _build;

    public bool IsBuilding => _process is not null;

    public void Stop()
    {
        if (_process is null) return;
        _stopped = true;
        try { _process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { /* already finished */ }
    }

    // ---- layout ---------------------------------------------------------------------------

    private void BuildLayout()
    {
        var browse = new Button { Text = "Browse...", AutoSize = true };
        browse.Click += (_, _) => BrowseForRepository();

        var grid = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3, Padding = new Padding(16, 16, 16, 0) };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        void Row(string label, Control input, Control? extra = null)
        {
            var row = grid.RowCount++;
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grid.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 8, 12, 8) }, 0, row);
            input.Margin = new Padding(0, 6, 0, 6);
            if (input is not FlowLayoutPanel) input.Dock = DockStyle.Fill;
            grid.Controls.Add(input, 1, row);
            if (extra is null) grid.SetColumnSpan(input, 2);
            else
            {
                extra.Margin = new Padding(8, 5, 0, 5);
                grid.Controls.Add(extra, 2, row);
            }
        }

        void Hint(Label hint)
        {
            var row = grid.RowCount++;
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            hint.Margin = new Padding(0, 0, 0, 6);
            grid.Controls.Add(hint, 1, row);
            grid.SetColumnSpan(hint, 2);
        }

        static FlowLayoutPanel Line(params Control[] controls)
        {
            var line = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
            foreach (var c in controls) c.Margin = new Padding(0, 0, 8, 0);
            line.Controls.AddRange(controls);
            return line;
        }

        Row("Repository", _repo, browse);
        Hint(_repoHint);
        Row("Version", Line(_version, _nextPatch, _nextMinor, _nextMajor));
        Hint(_versionHint);
        Row("Server", Line(_live, _test));
        Hint(_serverHint);
        Row("Package", _package);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(16, 8, 16, 0) };
        actions.Controls.AddRange([_build, _stop, _openFolder]);

        var statusHost = new Panel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(16, 10, 16, 0) };
        statusHost.Controls.Add(_status);

        var logHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16) };
        logHost.Controls.Add(_log);

        Controls.Add(logHost);
        Controls.Add(statusHost);
        Controls.Add(actions);
        Controls.Add(grid);
    }

    private void Wire()
    {
        foreach (var bump in new[] { _nextPatch, _nextMinor, _nextMajor })
            bump.Click += (s, _) => _version.Text = ((Button)s!).Tag as string ?? _version.Text;
        _version.TextChanged += (_, _) => RefreshVersionHint();
        _live.CheckedChanged += (_, _) => RefreshServerHint();
        _build.Click += async (_, _) => await BuildAsync();
        _stop.Click += (_, _) => Stop();
        _openFolder.Click += (_, _) => OpenDist();
    }

    // ---- repository -----------------------------------------------------------------------

    private static bool IsRepository(string? path) =>
        !string.IsNullOrWhiteSpace(path) && File.Exists(Path.Combine(path, "build-electron.ps1")) && File.Exists(Path.Combine(path, "package.json"));

    /// <summary>The folder chosen last time, else the repository this keygen was built from, else nothing.</summary>
    private static string? FindRepository()
    {
        try
        {
            if (File.Exists(RepoSettingPath))
            {
                var saved = File.ReadAllText(RepoSettingPath).Trim();
                if (IsRepository(saved)) return saved;
            }
        }
        catch (IOException) { }

        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (IsRepository(dir.FullName)) return dir.FullName;
        return null;
    }

    private void BrowseForRepository()
    {
        using var dialog = new FolderBrowserDialog { Description = "Select the Coduo POS repository (the folder with build-electron.ps1)", UseDescriptionForTitle = true };
        if (IsRepository(_repo.Text)) dialog.InitialDirectory = _repo.Text;
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        if (!IsRepository(dialog.SelectedPath))
        {
            MessageBox.Show(this, "That folder has no build-electron.ps1 and package.json. Select the repository's top folder.", "Coduo POS Keygen",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        try
        {
            Directory.CreateDirectory(MainForm.SettingsFolder);
            File.WriteAllText(RepoSettingPath, dialog.SelectedPath);
        }
        catch (IOException) { /* remembering the folder is a convenience only */ }
        UseRepository(dialog.SelectedPath);
    }

    private void UseRepository(string? path)
    {
        _repo.Text = path ?? "";
        if (!IsRepository(path))
        {
            _repoHint.ForeColor = Color.Firebrick;
            _repoHint.Text = "Repository not found. Use Browse... to select the folder with build-electron.ps1.";
            _build.Enabled = false;
            return;
        }

        _build.Enabled = !IsBuilding;
        var current = ReadVersion(path!);
        var branch = ReadBranch(path!);
        var last = LatestInstaller(path!);
        _repoHint.ForeColor = SystemColors.GrayText;
        _repoHint.Text = (branch is null ? "" : $"Branch {branch}.  ")
                         + (last is null ? "No installer built yet." : $"Last installer: {last.Name} ({last.LastWriteTime:yyyy-MM-dd HH:mm}).");

        SuggestVersions(current);
        _version.Text = _nextPatch.Tag as string ?? current ?? "";
    }

    private static string? ReadVersion(string repo)
    {
        var match = VersionInPackage.Match(File.ReadAllText(Path.Combine(repo, "package.json")));
        return match.Success ? match.Groups[2].Value : null;
    }

    private static string? ReadBranch(string repo)
    {
        try
        {
            var head = File.ReadAllText(Path.Combine(repo, ".git", "HEAD")).Trim();
            return head.StartsWith("ref: refs/heads/", StringComparison.Ordinal) ? head["ref: refs/heads/".Length..] : "detached " + head[..Math.Min(7, head.Length)];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    private static FileInfo? LatestInstaller(string repo)
    {
        var dist = new DirectoryInfo(Path.Combine(repo, "dist"));
        return dist.Exists ? dist.GetFiles("*.exe").OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault() : null;
    }

    // ---- version --------------------------------------------------------------------------

    private string? CurrentVersion => IsRepository(_repo.Text) ? ReadVersion(_repo.Text) : null;

    private void SuggestVersions(string? current)
    {
        var buttons = new[] { _nextPatch, _nextMinor, _nextMajor };
        if (current is null || !ThreeNumbers.IsMatch(current) || !Version.TryParse(current, out var v))
        {
            foreach (var b in buttons) b.Visible = false;
            return;
        }
        string[] next = [$"{v.Major}.{v.Minor}.{v.Build + 1}", $"{v.Major}.{v.Minor + 1}.0", $"{v.Major + 1}.0.0"];
        string[] what = ["small fix", "new features", "big change"];
        for (var i = 0; i < 3; i++)
        {
            buttons[i].Visible = true;
            buttons[i].Tag = next[i];
            buttons[i].Text = next[i];
            _tips.SetToolTip(buttons[i], $"{next[i]}: {what[i]}");
        }
    }

    private void RefreshVersionHint()
    {
        var text = _version.Text.Trim();
        var current = CurrentVersion;
        if (!ThreeNumbers.IsMatch(text))
        {
            _versionHint.ForeColor = Color.Firebrick;
            _versionHint.Text = "Use three numbers, like 2.6.7.";
            return;
        }

        var notHigher = current is not null && Version.TryParse(current, out var now) && Version.Parse(text) <= now;
        _versionHint.ForeColor = notHigher ? Amber : SystemColors.GrayText;
        _versionHint.Text = (current is null ? "" : notHigher ? $"Not higher than the current {current}. " : $"Now {current}. ")
                            + $"Build date will be {DateTime.Now:yyyy-MM-dd}: Full licences whose updates ended before that will not run it.";
    }

    private void RefreshServerHint()
    {
        if (_live.Checked)
        {
            _serverHint.ForeColor = SystemColors.GrayText;
            _serverHint.Text = $"Shops connect to {LiveServer}. This is the one you give customers.";
            _build.Text = "Build installer";
        }
        else
        {
            _serverHint.ForeColor = Amber;
            _serverHint.Text = $"Connects to the test site {TestServer}. The file name ends in TEST: never give it to a customer.";
            _build.Text = "Build TEST installer";
        }
    }

    /// <summary>Writes the new version into package.json, changing nothing else in the file.</summary>
    private static void WriteVersion(string repo, string version)
    {
        var path = Path.Combine(repo, "package.json");
        var bytes = File.ReadAllBytes(path);
        var hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        var text = new UTF8Encoding(false).GetString(bytes, hasBom ? 3 : 0, bytes.Length - (hasBom ? 3 : 0));
        var updated = VersionInPackage.Replace(text, m => m.Groups[1].Value + version + m.Groups[3].Value, 1);
        File.WriteAllText(path, updated, new UTF8Encoding(hasBom));
    }

    // ---- building -------------------------------------------------------------------------

    private async Task BuildAsync()
    {
        if (IsBuilding) return;
        var repo = _repo.Text;
        if (!IsRepository(repo))
        {
            Warn("Select the repository first (Browse...).");
            return;
        }

        var version = _version.Text.Trim();
        if (!ThreeNumbers.IsMatch(version))
        {
            Warn("Enter the version as three numbers, like 2.6.7.");
            return;
        }

        var current = ReadVersion(repo);
        if (current is not null && Version.TryParse(current, out var now) && Version.Parse(version) <= now)
        {
            var again = MessageBox.Show(this, $"{version} is not higher than the current version {current}.\n\nBuild it anyway?", "Coduo POS Keygen",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
            if (again != DialogResult.Yes) return;
        }

        var test = _test.Checked;
        var type = _package.SelectedIndex switch { 1 => "win-portable", 2 => "win", _ => "win-installer" };

        try
        {
            if (version != current) WriteVersion(repo, version);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Warn("Could not write the version into package.json:\n" + ex.Message);
            return;
        }

        _log.Clear();
        AppendLog($"Coduo POS {version} - {(test ? "TEST build for " + TestServer : "live build for " + LiveServer)} - {_package.Text}");
        AppendLog(current == version ? $"package.json already says {version}." : $"package.json: {current} -> {version}");
        AppendLog("");

        var command = "[Console]::OutputEncoding = [Text.Encoding]::UTF8; "
                      + $"& '.\\build-electron.ps1' {type}{(test ? " -Test" : "")}; exit $LASTEXITCODE";
        var start = new ProcessStartInfo("powershell.exe")
        {
            WorkingDirectory = repo,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", command })
            start.ArgumentList.Add(arg);
        start.Environment["NO_COLOR"] = "1";
        start.Environment["FORCE_COLOR"] = "0";

        var clock = Stopwatch.StartNew();
        var startedAt = DateTime.Now;
        _stopped = false;
        _lastBuilt = null;
        SetBuilding(true);
        SetStatus(SystemColors.ControlText, $"Building {version}... this takes a few minutes.");

        try
        {
            using var process = new Process { StartInfo = start };
            process.OutputDataReceived += (_, e) => { if (e.Data is not null) Post(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data is not null) Post(e.Data); };
            process.Start();
            _process = process;
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync();

            var took = clock.Elapsed.TotalMinutes >= 1 ? $"{(int)clock.Elapsed.TotalMinutes}m {clock.Elapsed.Seconds}s" : $"{clock.Elapsed.Seconds}s";
            if (_stopped)
            {
                SetStatus(Amber, $"Stopped after {took}. package.json stays at {version}.");
            }
            else if (process.ExitCode != 0)
            {
                SetStatus(Color.Firebrick, $"The build failed after {took} (exit code {process.ExitCode}). The log says why. package.json stays at {version}.");
            }
            else
            {
                var built = new DirectoryInfo(Path.Combine(repo, "dist")).GetFiles("*.exe")
                    .Where(f => f.LastWriteTime >= startedAt.AddSeconds(-2))
                    .OrderBy(f => f.Name)
                    .ToList();
                _lastBuilt = built.FirstOrDefault(f => f.Name.Contains("Setup", StringComparison.OrdinalIgnoreCase))?.FullName ?? built.FirstOrDefault()?.FullName;
                SetStatus(test ? Amber : Color.SeaGreen,
                    built.Count == 0 ? $"Finished in {took}, but no new .exe is in dist. Check the log."
                                     : $"Done in {took}: {string.Join(", ", built.Select(f => f.Name))}");
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            SetStatus(Color.Firebrick, "Could not start PowerShell: " + ex.Message);
        }
        finally
        {
            _process = null;
            SetBuilding(false);
            UseRepository(repo);
            if (_lastBuilt is null) _version.Text = version; // not built: keep it on screen to try again; built: the next number is suggested
        }
    }

    private void SetBuilding(bool building)
    {
        _build.Enabled = !building && IsRepository(_repo.Text);
        _stop.Enabled = building;
        _version.Enabled = _live.Enabled = _test.Enabled = _package.Enabled = !building;
        _nextPatch.Enabled = _nextMinor.Enabled = _nextMajor.Enabled = !building;
    }

    /// <summary>Output arrives on other threads; it is dropped once the window is closing.</summary>
    private void Post(string line)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try { BeginInvoke(() => AppendLog(line)); }
        catch (InvalidOperationException) { /* handle gone while closing */ }
    }

    private void AppendLog(string line)
    {
        _log.AppendText(AnsiCodes.Replace(line, "") + Environment.NewLine);
    }

    private void SetStatus(Color color, string text)
    {
        _status.ForeColor = color;
        _status.Text = text;
    }

    private void OpenDist()
    {
        if (_lastBuilt is not null && File.Exists(_lastBuilt))
        {
            Process.Start("explorer.exe", $"/select,\"{_lastBuilt}\"");
            return;
        }
        var dist = Path.Combine(_repo.Text, "dist");
        if (Directory.Exists(dist)) Process.Start("explorer.exe", $"\"{dist}\"");
        else Warn("There is no dist folder yet. Build an installer first.");
    }

    private void Warn(string message) =>
        MessageBox.Show(this, message, "Coduo POS Keygen", MessageBoxButtons.OK, MessageBoxIcon.Warning);
}
