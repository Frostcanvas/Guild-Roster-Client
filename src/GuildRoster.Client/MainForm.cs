using System.Diagnostics;
using System.Reflection;

namespace GuildRoster.Client;

internal sealed class MainForm : Form
{
    private readonly ClientSettings _settings;
    private readonly UpdateFeedClient _updateFeedClient = new();
    private readonly ClientUpdateService _updateService;

    private readonly TextBox _sourcePath = new() { Dock = DockStyle.Fill };
    private readonly TextBox _serverUrl = new() { Dock = DockStyle.Fill };
    private readonly ComboBox _updateChannel = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
    private readonly Label _sourceStatus = new() { AutoSize = true };
    private readonly Label _updateStatus = new() { AutoSize = true };
    private readonly Button _checkUpdatesButton = new() { Text = "Check for Updates", AutoSize = true };
    private FileSystemWatcher? _watcher;

    public MainForm()
    {
        _settings = SettingsService.Load();
        _updateService = new ClientUpdateService(_updateFeedClient);

        Text = "FrostLabs Guild Roster Client";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(760, 420);
        Size = new Size(900, 520);

        BuildUi();
        LoadSettingsIntoUi();

        Shown += async (_, _) =>
        {
            DiscoverSource();
            ConfigureWatcher();
            await CheckForUpdatesAsync(silentWhenCurrent: true);
        };
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _watcher?.Dispose();
            _updateFeedClient.Dispose();
        }
        base.Dispose(disposing);
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(20),
            ColumnCount = 1,
            RowCount = 8,
            AutoSize = true,
        };
        Controls.Add(root);

        var title = new Label
        {
            Text = "FrostLabs Guild Roster Client",
            Font = new Font(Font.FontFamily, 18, FontStyle.Bold),
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 4),
        };
        root.Controls.Add(title);
        root.Controls.Add(new Label
        {
            Text = $"Version {GetRunningVersion()} · GRM SavedVariables → Hogwarts Academy roster",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(0, 0, 0, 18),
        });

        root.Controls.Add(CreateLabeledRow(
            "GRM SavedVariables folder",
            _sourcePath,
            new Button { Text = "Browse…", AutoSize = true },
            (_, button) => button.Click += (_, _) => BrowseForSource()));
        root.Controls.Add(_sourceStatus);

        root.Controls.Add(CreateLabeledRow("Services01", _serverUrl));

        _updateChannel.Items.AddRange(new object[] { "stable", "beta" });
        var updateRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Margin = new Padding(0, 14, 0, 0),
        };
        updateRow.Controls.Add(new Label { Text = "Update channel", AutoSize = true, Padding = new Padding(0, 7, 8, 0) });
        updateRow.Controls.Add(_updateChannel);
        updateRow.Controls.Add(_checkUpdatesButton);
        _checkUpdatesButton.Click += async (_, _) => await CheckForUpdatesAsync(silentWhenCurrent: false);
        root.Controls.Add(updateRow);
        root.Controls.Add(_updateStatus);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Margin = new Padding(0, 18, 0, 0),
        };

        var saveButton = new Button { Text = "Save Settings", AutoSize = true };
        saveButton.Click += (_, _) => SaveSettings();
        buttons.Controls.Add(saveButton);

        var openRosterButton = new Button { Text = "Open Hogwarts Academy Roster", AutoSize = true };
        openRosterButton.Click += (_, _) => OpenRoster();
        buttons.Controls.Add(openRosterButton);

        buttons.Controls.Add(new Button
        {
            Text = "Sync Now (parser next)",
            AutoSize = true,
            Enabled = false,
        });
        root.Controls.Add(buttons);
    }

    private static Control CreateLabeledRow(
        string labelText,
        Control mainControl,
        Button? actionButton = null,
        Action<Control, Button>? configureAction = null)
    {
        var outer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = actionButton is null ? 1 : 2,
            Margin = new Padding(0, 8, 0, 0),
        };
        outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        if (actionButton is not null)
        {
            outer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        }

        var label = new Label { Text = labelText, AutoSize = true };
        outer.Controls.Add(label, 0, 0);
        if (actionButton is not null)
        {
            outer.SetColumnSpan(label, 2);
        }

        outer.Controls.Add(mainControl, 0, 1);
        if (actionButton is not null)
        {
            actionButton.Margin = new Padding(8, 0, 0, 0);
            outer.Controls.Add(actionButton, 1, 1);
            configureAction?.Invoke(mainControl, actionButton);
        }

        return outer;
    }

    private void LoadSettingsIntoUi()
    {
        _sourcePath.Text = _settings.SourceSavedVariablesPath ?? string.Empty;
        _serverUrl.Text = _settings.ServerBaseUrl;
        _updateChannel.SelectedItem = string.Equals(_settings.UpdateChannel, "beta", StringComparison.OrdinalIgnoreCase)
            ? "beta"
            : "stable";
    }

    private void DiscoverSource()
    {
        if (SourceLocator.IsValidSavedVariablesDirectory(_sourcePath.Text))
        {
            RefreshSourceStatus();
            return;
        }

        var candidates = SourceLocator.FindCandidates(_sourcePath.Text);
        if (candidates.Count == 1)
        {
            _sourcePath.Text = candidates[0].SavedVariablesDirectory;
            _settings.SourceSavedVariablesPath = candidates[0].SavedVariablesDirectory;
            SettingsService.Save(_settings);
        }

        RefreshSourceStatus(candidates.Count);
    }

    private void RefreshSourceStatus(int? discoveredCount = null)
    {
        if (!SourceLocator.IsValidSavedVariablesDirectory(_sourcePath.Text))
        {
            _sourceStatus.Text = discoveredCount > 1
                ? $"Multiple GRM files were found ({discoveredCount}). Choose the SavedVariables folder to use."
                : "Guild_Roster_Manager.lua was not found in the selected folder.";
            _sourceStatus.ForeColor = Color.DarkOrange;
            return;
        }

        var filePath = SourceLocator.GetGrmFilePath(_sourcePath.Text.Trim());
        var info = new FileInfo(filePath);
        _sourceStatus.Text = $"GRM source ready · {info.Length:N0} bytes · last written {info.LastWriteTime:G}";
        _sourceStatus.ForeColor = Color.DarkGreen;
    }

    private void BrowseForSource()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Select the WoW account SavedVariables folder that contains Guild_Roster_Manager.lua",
            UseDescriptionForTitle = true,
            InitialDirectory = Directory.Exists(_sourcePath.Text) ? _sourcePath.Text : @"D:\Battle.net\World of Warcraft\_retail_\WTF\Account",
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _sourcePath.Text = dialog.SelectedPath;
            RefreshSourceStatus();
            ConfigureWatcher();
        }
    }

    private void SaveSettings()
    {
        _settings.SourceSavedVariablesPath = _sourcePath.Text.Trim();
        _settings.ServerBaseUrl = _serverUrl.Text.Trim().TrimEnd('/');
        _settings.UpdateChannel = _updateChannel.SelectedItem?.ToString() ?? "stable";
        SettingsService.Save(_settings);
        ConfigureWatcher();
        RefreshSourceStatus();
        _updateStatus.Text = "Settings saved.";
    }

    private void ConfigureWatcher()
    {
        _watcher?.Dispose();
        _watcher = null;

        var directory = _sourcePath.Text.Trim();
        if (!SourceLocator.IsValidSavedVariablesDirectory(directory))
        {
            return;
        }

        _watcher = new FileSystemWatcher(directory, "Guild_Roster_Manager.lua")
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
            EnableRaisingEvents = true,
        };
        _watcher.Changed += SourceChanged;
        _watcher.Created += SourceChanged;
        _watcher.Renamed += SourceChanged;
    }

    private void SourceChanged(object? sender, FileSystemEventArgs e)
    {
        if (IsDisposed)
        {
            return;
        }

        BeginInvoke(() =>
        {
            try
            {
                RefreshSourceStatus();
            }
            catch
            {
                _sourceStatus.Text = "GRM source changed; waiting for WoW to finish writing the file.";
                _sourceStatus.ForeColor = Color.DarkOrange;
            }
        });
    }

    private async Task CheckForUpdatesAsync(bool silentWhenCurrent)
    {
        _checkUpdatesButton.Enabled = false;
        try
        {
            var server = _serverUrl.Text.Trim().TrimEnd('/');
            var channel = _updateChannel.SelectedItem?.ToString() ?? "stable";
            _updateStatus.Text = "Checking for Guild Roster Client updates…";

            var package = await _updateFeedClient.GetLatestAsync(server, channel);
            if (package is null)
            {
                _updateStatus.Text = "No Guild Roster Client update has been published yet.";
                return;
            }

            var current = GetRunningVersion();
            if (!ReleaseVersionUtility.IsNewer(current, package.Version))
            {
                _updateStatus.Text = $"Up to date · {current} · {channel} channel";
                if (!silentWhenCurrent)
                {
                    MessageBox.Show(this, "Guild Roster Client is up to date.", "Updates", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                return;
            }

            _updateStatus.Text = $"Update available: {package.Version}";
            var choice = MessageBox.Show(
                this,
                $"Guild Roster Client {package.Version} is available.\n\nDownload and install it now?",
                "Guild Roster Client Update",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information);
            if (choice != DialogResult.Yes)
            {
                return;
            }

            var progress = new Progress<string>(message => _updateStatus.Text = message);
            await _updateService.StageAndLaunchUpdateAsync(package, progress);
        }
        catch (Exception ex)
        {
            _updateStatus.Text = $"Update check failed: {ex.Message}";
            if (!silentWhenCurrent)
            {
                MessageBox.Show(this, ex.Message, "Update Check Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        finally
        {
            _checkUpdatesButton.Enabled = true;
        }
    }

    private void OpenRoster()
    {
        var server = _serverUrl.Text.Trim().TrimEnd('/');
        Process.Start(new ProcessStartInfo
        {
            FileName = $"{server}/roster",
            UseShellExecute = true,
        });
    }

    private static string GetRunningVersion()
    {
        var assembly = typeof(MainForm).Assembly;
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            var buildMetadata = informational.IndexOf('+');
            return buildMetadata >= 0 ? informational[..buildMetadata] : informational;
        }

        return assembly.GetName().Version?.ToString(3) ?? "unknown";
    }
}
