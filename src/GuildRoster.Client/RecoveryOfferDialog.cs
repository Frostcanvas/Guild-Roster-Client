using System.Text.Json;

namespace GuildRoster.Client;

internal enum RecoveryOfferChoice
{
    AskLater,
    KeepCurrent,
    ReviewRestore,
}

internal sealed class RecoveryOfferDialog : Form
{
    private static readonly Color Background = Color.FromArgb(13, 16, 24);
    private static readonly Color Surface = Color.FromArgb(24, 28, 40);
    private static readonly Color TextPrimary = Color.FromArgb(238, 240, 248);
    private static readonly Color TextSecondary = Color.FromArgb(163, 170, 194);
    private static readonly Color Gold = Color.FromArgb(231, 181, 67);
    private static readonly Color Purple = Color.FromArgb(130, 95, 225);
    private static readonly Color Green = Color.FromArgb(78, 214, 142);

    private readonly CheckedListBox _fieldList = new();
    private readonly List<string> _fieldIds = new();

    public RecoveryOfferDialog(RecoveryOffer offer)
    {
        Choice = RecoveryOfferChoice.AskLater;
        Text = "Returning Guild Member · Recovery";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true;
        ClientSize = new Size(860, 650);
        BackColor = Background;
        ForeColor = TextPrimary;
        AutoScaleMode = AutoScaleMode.Dpi;

        var characterName = GetString(offer.CurrentValues, "character_name")
            ?? GetString(offer.ArchivedValues, "character_name")
            ?? "Returning character";
        var realm = GetString(offer.CurrentValues, "character_realm")
            ?? GetString(offer.ArchivedValues, "character_realm");
        var displayName = string.IsNullOrWhiteSpace(realm)
            ? characterName
            : $"{characterName}-{realm}";

        var shell = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(20),
            ColumnCount = 1,
            RowCount = 6,
            BackColor = Background,
        };
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 210));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));

        shell.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = "Returning guild member detected",
            Font = new Font("Segoe UI", 18, FontStyle.Bold),
            ForeColor = Gold,
            TextAlign = ContentAlignment.MiddleLeft,
        }, 0, 0);

        shell.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = $"{displayName}\n{offer.PlayerGuid}",
            Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
            ForeColor = TextPrimary,
            TextAlign = ContentAlignment.MiddleLeft,
        }, 0, 1);

        var summary = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = Surface,
            ForeColor = TextPrimary,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Consolas", 9.5f),
            Text = BuildSummary(offer),
        };
        shell.Controls.Add(summary, 0, 2);

        shell.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = "Select the archived GRM information you want prepared for restore:",
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            ForeColor = TextSecondary,
            TextAlign = ContentAlignment.MiddleLeft,
        }, 0, 3);

        _fieldList.Dock = DockStyle.Fill;
        _fieldList.CheckOnClick = true;
        _fieldList.BackColor = Surface;
        _fieldList.ForeColor = TextPrimary;
        _fieldList.BorderStyle = BorderStyle.FixedSingle;
        _fieldList.Font = new Font("Segoe UI", 10);
        AddRestoreFields(offer.ArchivedValues);
        shell.Controls.Add(_fieldList, 0, 4);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 12, 0, 0),
            BackColor = Background,
        };

        var reviewButton = CreateButton("Review & Restore", Green);
        reviewButton.Click += (_, _) =>
        {
            if (SelectedFields.Count == 0)
            {
                MessageBox.Show(
                    this,
                    "Select at least one archived field to prepare for restore.",
                    "Returning Member Recovery",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }
            Choice = RecoveryOfferChoice.ReviewRestore;
            DialogResult = DialogResult.OK;
            Close();
        };

        var keepButton = CreateButton("Keep Current", Purple);
        keepButton.Click += (_, _) =>
        {
            Choice = RecoveryOfferChoice.KeepCurrent;
            DialogResult = DialogResult.No;
            Close();
        };

        var laterButton = CreateButton("Ask Later", Gold);
        laterButton.Click += (_, _) =>
        {
            Choice = RecoveryOfferChoice.AskLater;
            DialogResult = DialogResult.Cancel;
            Close();
        };

        buttons.Controls.Add(reviewButton);
        buttons.Controls.Add(keepButton);
        buttons.Controls.Add(laterButton);
        shell.Controls.Add(buttons, 0, 5);
        Controls.Add(shell);
    }

    public RecoveryOfferChoice Choice { get; private set; }

    public IReadOnlyList<string> SelectedFields
    {
        get
        {
            var selected = new List<string>();
            foreach (var index in _fieldList.CheckedIndices.Cast<int>())
            {
                if (index >= 0 && index < _fieldIds.Count)
                {
                    selected.Add(_fieldIds[index]);
                }
            }
            return selected;
        }
    }

    private void AddRestoreFields(JsonElement archived)
    {
        AddField(
            "public_note",
            "Public note",
            HasText(archived, "public_note_recovery_candidate") || HasText(archived, "public_note"));
        AddField(
            "officer_note",
            "Officer note",
            HasText(archived, "officer_note_recovery_candidate") || HasText(archived, "officer_note"));
        AddField("custom_note", "GRM custom note", HasText(archived, "custom_note_text") || HasProperty(archived, "custom_note"));
        AddField("join_date_history", "GRM join date / complete join-date history", HasProperty(archived, "join_date_history"));
        AddField(
            "main_alt_relationship",
            "Main / alt relationship",
            HasText(archived, "relationship") || HasText(archived, "main_name") || HasProperty(archived, "main_at_time_of_leaving"));
        AddField(
            "birthday",
            "Birthday information",
            HasProperty(archived, "birthday_info") || HasProperty(archived, "alt_group_birthday_info"));
        AddField(
            "nickname",
            "Nickname information",
            HasProperty(archived, "nickname_details") || HasProperty(archived, "alt_group_nickname_details"));
    }

    private void AddField(string fieldId, string label, bool available)
    {
        if (!available)
        {
            return;
        }
        _fieldIds.Add(fieldId);
        var index = _fieldList.Items.Add(label);
        _fieldList.SetItemChecked(index, true);
    }

    private static string BuildSummary(RecoveryOffer offer)
    {
        var lines = new List<string>
        {
            "Exact historical Player GUID matched an Inactive -> Active transition.",
            "The current guild state will not be overwritten automatically.",
            "Guild rank restoration is never performed.",
            string.Empty,
        };

        AppendComparison(
            lines,
            "Public note",
            GetString(offer.CurrentValues, "public_note"),
            GetString(offer.ArchivedValues, "public_note_recovery_candidate")
                ?? GetString(offer.ArchivedValues, "public_note"));
        AppendComparison(
            lines,
            "Officer note",
            GetString(offer.CurrentValues, "officer_note"),
            GetString(offer.ArchivedValues, "officer_note_recovery_candidate")
                ?? GetString(offer.ArchivedValues, "officer_note"));
        AppendComparison(
            lines,
            "Custom note",
            null,
            GetString(offer.ArchivedValues, "custom_note_text"));

        var relationship = GetString(offer.ArchivedValues, "relationship");
        var mainName = GetString(offer.ArchivedValues, "main_name");
        if (!string.IsNullOrWhiteSpace(relationship) || !string.IsNullOrWhiteSpace(mainName))
        {
            lines.Add($"Main / Alt : archived = {FormatRelationship(relationship, mainName)}");
        }

        var joinCount = GetLuaArrayCount(offer.ArchivedValues, "join_date_history");
        if (joinCount > 0)
        {
            lines.Add($"Join history: {joinCount:N0} archived GRM entr{(joinCount == 1 ? "y" : "ies")}");
        }
        if (HasProperty(offer.ArchivedValues, "birthday_info") || HasProperty(offer.ArchivedValues, "alt_group_birthday_info"))
        {
            lines.Add("Birthday    : archived GRM birthday data is available");
        }
        if (HasProperty(offer.ArchivedValues, "nickname_details") || HasProperty(offer.ArchivedValues, "alt_group_nickname_details"))
        {
            lines.Add("Nickname    : archived GRM nickname data is available");
        }

        lines.Add(string.Empty);
        lines.Add("Review & Restore records the selected protected restore request.");
        lines.Add("No field is written back until the explicit restore step is completed.");
        return string.Join(Environment.NewLine, lines);
    }

    private static void AppendComparison(List<string> lines, string label, string? current, string? archived)
    {
        if (string.IsNullOrWhiteSpace(current) && string.IsNullOrWhiteSpace(archived))
        {
            return;
        }
        lines.Add($"{label,-12}: current = {Display(current)}");
        lines.Add($"{string.Empty,-12}  archived = {Display(archived)}");
    }

    private static string FormatRelationship(string? relationship, string? mainName)
    {
        if (string.Equals(relationship, "main", StringComparison.OrdinalIgnoreCase))
        {
            return "Main";
        }
        if (string.Equals(relationship, "alt", StringComparison.OrdinalIgnoreCase))
        {
            return string.IsNullOrWhiteSpace(mainName) ? "Alt" : $"Alt of {mainName}";
        }
        return mainName ?? relationship ?? "Unknown";
    }

    private static string Display(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "(blank)" : value.Trim();

    private static bool HasText(JsonElement element, string name) =>
        !string.IsNullOrWhiteSpace(GetString(element, name));

    private static bool HasProperty(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out _);

    private static string? GetString(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value))
        {
            return null;
        }
        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private static int GetLuaArrayCount(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(name, out var table) ||
            table.ValueKind != JsonValueKind.Object ||
            !table.TryGetProperty("$values", out var values) ||
            values.ValueKind != JsonValueKind.Array)
        {
            return 0;
        }
        return values.GetArrayLength();
    }

    private static Button CreateButton(string text, Color borderColor)
    {
        var button = new Button
        {
            AutoSize = true,
            Text = text,
            FlatStyle = FlatStyle.Flat,
            BackColor = Surface,
            ForeColor = TextPrimary,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            Padding = new Padding(10, 6, 10, 6),
            Margin = new Padding(8, 0, 0, 0),
            Cursor = Cursors.Hand,
        };
        button.FlatAppearance.BorderColor = borderColor;
        button.FlatAppearance.BorderSize = 1;
        return button;
    }
}
