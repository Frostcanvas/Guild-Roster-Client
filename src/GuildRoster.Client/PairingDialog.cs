namespace GuildRoster.Client;

internal sealed class PairingDialog : Form
{
    private readonly TextBox _code = new()
    {
        CharacterCasing = CharacterCasing.Upper,
        Dock = DockStyle.Fill,
        MaxLength = 32,
    };

    public PairingDialog()
    {
        Text = "Pair Guild Roster Client";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(430, 175);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18),
            ColumnCount = 1,
            RowCount = 4,
        };
        Controls.Add(root);

        root.Controls.Add(new Label
        {
            Text = "Enter the one-time pairing code generated on Services01.",
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 10),
        });
        root.Controls.Add(_code);
        root.Controls.Add(new Label
        {
            Text = "The long-lived server registration key is never stored in this client.",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(0, 8, 0, 12),
        });

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
        };
        var ok = new Button { Text = "Pair", DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);
        root.Controls.Add(buttons);

        AcceptButton = ok;
        CancelButton = cancel;
        Shown += (_, _) => _code.Focus();
        FormClosing += (_, e) =>
        {
            if (DialogResult == DialogResult.OK && string.IsNullOrWhiteSpace(_code.Text))
            {
                MessageBox.Show(this, "Enter the Services01 pairing code.", "Pair Client", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                e.Cancel = true;
            }
        };
    }

    public string PairingCode => _code.Text.Trim();
}
