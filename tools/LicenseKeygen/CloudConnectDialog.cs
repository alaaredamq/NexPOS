using Coduo.Tools;

namespace LicenseKeygen;

/// <summary>Signs the keygen in to the cloud admin portal, so the keys it signs are recorded there. Only a token is kept.</summary>
internal sealed class CloudConnectDialog : Form
{
    private readonly TextBox _url = new() { PlaceholderText = "https://coduos.com" };
    private readonly TextBox _user = new();
    private readonly TextBox _password = new() { UseSystemPasswordChar = true };
    private readonly Label _message = new() { AutoSize = true, MaximumSize = new Size(380, 0), ForeColor = Color.Firebrick };
    private readonly Button _connect = new() { Text = "Connect", AutoSize = true };

    public CloudConnectDialog()
    {
        Text = "Connect to the cloud";
        Font = new Font("Segoe UI", 10f);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(16);

        var grid = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Dock = DockStyle.Fill };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 280));
        void Row(string label, Control input)
        {
            var row = grid.RowCount++;
            grid.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 8, 12, 8) }, 0, row);
            input.Dock = DockStyle.Fill;
            grid.Controls.Add(input, 1, row);
        }
        Row("Cloud address", _url);
        Row("Admin name", _user);
        Row("Password", _password);
        var help = new Label
        {
            Text = "The same sign-in as the admin portal. The password is not saved; this PC keeps a token, which you can sign out from the portal's API sessions.",
            AutoSize = true, MaximumSize = new Size(380, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(0, 8, 0, 0),
        };
        grid.Controls.Add(help, 0, grid.RowCount++);
        grid.SetColumnSpan(help, 2);
        grid.Controls.Add(_message, 0, grid.RowCount++);
        grid.SetColumnSpan(_message, 2);

        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Bottom };
        var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        buttons.Controls.AddRange([cancel, _connect]);

        Controls.Add(grid);
        Controls.Add(buttons);
        AcceptButton = _connect;
        CancelButton = cancel;
        _connect.Click += async (_, _) => await ConnectAsync();
    }

    private async Task ConnectAsync()
    {
        _connect.Enabled = false;
        _message.ForeColor = SystemColors.GrayText;
        _message.Text = "Connecting...";
        var (link, message) = await CloudLicenseLog.SignInAsync(_url.Text, _user.Text.Trim(), _password.Text);
        _connect.Enabled = true;
        if (link is null)
        {
            _message.ForeColor = Color.Firebrick;
            _message.Text = message;
            return;
        }
        _password.Clear();
        DialogResult = DialogResult.OK;
        Close();
    }
}
