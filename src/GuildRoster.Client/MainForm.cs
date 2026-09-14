using System.Diagnostics;
using System.Net;
using System.Reflection;

namespace GuildRoster.Client;

internal sealed class MainForm : Form
{
    private readonly ClientSettings _settings;
    private readonly UpdateFeedClient _updateFeedClient = new();
    private readonly ClientUpdateService _updateService;
    private readonly GuildRosterApiClient _apiClient = new();
    private readonly RosterSyncService _syncService;

    private readonly TextBox _sourcePath = new() { Dock = DockStyle.Fill };
    private readonly TextBox _serverUrl = new() { Dock = DockStyle.Fill };
    private readonly ComboBox _updateChannel = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
    private readonly CheckBox _autoSync = new() { Text = "Automatically sync when GRM saves", AutoSize = true };
    private readonly Label _sourceStatus = new() { AutoSize = true };
    private readonly Label _pairStatus = new() { AutoSize = true };
    private readonly Label _syncStatus = new() { AutoSize = true };
    private readonly Label _updateStatus = new() { AutoSize = true };
    private readonly Button _pairButton = new() { Text = "Pair Client", AutoSize = true };
    private readonly Button _syncButton = new() { Text = "Sync Now", AutoSize = true };
    private readonly Button _checkUpdatesButton = new() { Text = "Check for Updates", AutoSize = true };

    private FileSystemWatcher? _watcher;
    private CancellationTokenSource? _watchDebounce;
    private bool _syncInProgress;

    public MainForm()
    {
        _settings = SettingsService.Load();
        _updateService = new ClientUpdateService(_updateFeedClient);
        _syncService = new RosterSyncService(_apiClient);

        Text = "FrostLabs Guild Roster Client";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(820, 560);
        Size = new Size(980, 650);

        BuildUi();
        LoadSettingsIntoUi();

        Shown += async (_, _) =>
        {
            DiscoverSource();
            ConfigureWatcher();
            RefreshPairStatus();
            RefreshSyncStatusFromState();

            if (_settings.AutoSync && CredentialStore.HasToken && SourceLocator.IsValidSavedVariablesDirectory(_sourcePath.Text))
            {
                await SyncRosterAsync(forceCurrentSnapshot: false, silent: true);
            }

            await CheckForUpdatesAsync(silentWhenCurrent: true);
        };
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _watchDebounce?.Cancel();
            _watchDebounce?.Dispose();
            _watcher?.Dispose();
            _apiClient.Dispose();
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
            AutoScroll = true,
            AutoSize = true,
        };
        Controls.Add(root);

        root.Controls.Add(new Label
        {
            Text = "FrostLabs Guild Roster Client",
            Font = new Font(Font.FontFamily, 18, FontStyle.Bold),
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 4),
        });
        root.Controls.Add(new Label
        {
            Text = $"Version {GetRunningVersion()} · GRM → FGR1 → Hogwarts Academy",
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

        root.Controls.Add(CreateLabeledRow("Services01 Guild Roster API", _serverUrl));
        root.Controls.Add(new Label
        {
            Text = "Guild: Hogwarts Academy · Realm: BleedingHollow · Retail WOW_PROJECT_ID 1",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(0, 4, 0, 8),
        });

        var pairingRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Margin = new Padding(0, 8, 0, 0),
        };
        pairingRow.Controls.Add(_pairButton);
        pairingRow.Controls.Add(_pairStatus);
        _pairButton.Click += async (_, _) => await PairClientAsync();
        root.Controls.Add(pairingRow);

        var syncRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Margin = new Padding(0, 10, 0, 0),
        };
        syncRow.Controls.Add(_syncButton);
        syncRow.Controls.Add(_autoSync);
        _syncButton.Click += async (_, _) => await SyncRosterAsync(forceCurrentSnapshot: true, silent: false);
        root.Controls.Add(syncRow);
        root.Controls.Add(_syncStatus);

        _updateChannel.Items.AddRange(new object[] { "stable", "beta" });
        var updateRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Margin = new Padding(0, 16, 0, 0),
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
        saveButton.Click += (_, _) => SaveSettings(showConfirmation: true);
        buttons.Controls.Add(saveButton);

        var openRosterButton = new Button { Text = "Open Hogwarts Academy Roster", AutoSize = true };
        openRosterButton.Click += (_, _) => OpenRoster();
        buttons.Controls.Add(openRosterButton);
        root.Controls.Add(buttons);

        root.Controls.Add(new Label
        {
            Text = "Safety: the client reads Guild_Roster_Manager.lua as data only. Incomplete or ambiguous GRM parses are blocked before upload, and queued snapshots retry without reading WoW process memory or automating gameplay.",
            AutoSize = true,
            MaximumSize = new Size(880, 0),
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(0, 18, 0, 0),
        });
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
        _autoSync.Checked = _settings.AutoSync;
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

    private void RefreshPairStatus()
    {
        if (CredentialStore.HasToken)
        {
            _pairStatus.Text = string.IsNullOrWhiteSpace(_settings.InstallationId)
                ? "Paired to Services01"
                : $"Paired · installation {_settings.InstallationId}";
            _pairStatus.ForeColor = Color.DarkGreen;
            _pairButton.Text = "Pair Again";
        }
        else
        {
            _pairStatus.Text = "Not paired · generate a one-time code on Services01 first.";
            _pairStatus.ForeColor = Color.DarkOrange;
            _pairButton.Text = "Pair Client";
        }
    }

    private void RefreshSyncStatusFromState()
    {
        var state = _syncService.LoadState();
        var queued = _syncService.GetQueuedCount();
        if (state.LastSuccessfulSync is null)
        {
            _syncStatus.Text = $"No successful roster sync yet · queue {queued:N0}.";
            _syncStatus.ForeColor = SystemColors.GrayText;
            return;
        }

        _syncStatus.Text = $"Last sync {state.LastSuccessfulSync.Value.LocalDateTime:G} · {state.LastAcceptedMemberCount:N0} characters · queue {queued:N0}.";
        _syncStatus.ForeColor = Color.DarkGreen;
    }

    private void BrowseForSource()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Select the WoW account SavedVariables folder that contains Guild_Roster_Manager.lua",
            UseDescriptionForTitle = true,
            InitialDirectory = Directory.Exists(_sourcePath.Text)
                ? _sourcePath.Text
                : @"D:\Battle.net\World of Warcraft\_retail_\WTF\Account",
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _sourcePath.Text = dialog.SelectedPath;
            SaveSettings(showConfirmation: false);
            RefreshSourceStatus();
            ConfigureWatcher();
        }
    }

    private void SaveSettings(bool showConfirmation)
    {
        _settings.SourceSavedVariablesPath = _sourcePath.Text.Trim();
        _settings.ServerBaseUrl = _serverUrl.Text.Trim().TrimEnd('/');
        _settings.UpdateChannel = _updateChannel.SelectedItem?.ToString() ?? "stable";
        _settings.GuildName = "Hogwarts Academy";
        _settings.GuildRealm = "BleedingHollow";
        _settings.AutoSync = _autoSync.Checked;
        SettingsService.Save(_settings);
        ConfigureWatcher();
        RefreshSourceStatus();
        if (showConfirmation)
        {
            _updateStatus.Text = "Settings saved.";
        }
    }

    private async Task PairClientAsync()
    {
        SaveSettings(showConfirmation: false);
        using var dialog = new PairingDialog();
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        _pairButton.Enabled = false;
        try
        {
            _pairStatus.Text = "Pairing with Services01…";
            _pairStatus.ForeColor = SystemColors.GrayText;
            var result = await _apiClient.PairAsync(
                _settings.ServerBaseUrl,
                dialog.PairingCode,
                Environment.MachineName,
                GetRunningVersion());

            CredentialStore.SaveToken(result.BearerToken);
            _settings.InstallationId = result.InstallationId.ToString();
            SettingsService.Save(_settings);
            RefreshPairStatus();
            await SyncRosterAsync(forceCurrentSnapshot: true, silent: true);
        }
        catch (Exception ex)
        {
            _pairStatus.Text = $"Pairing failed: {ex.Message}";
            _pairStatus.ForeColor = Color.DarkRed;
            MessageBox.Show(this, ex.Message, "Pairing Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            _pairButton.Enabled = true;
        }
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
        _ = ScheduleSourceRefreshAndSyncAsync();
    }

    private async Task ScheduleSourceRefreshAndSyncAsync()
    {
        _watchDebounce?.Cancel();
        _watchDebounce?.Dispose();
        var debounce = new CancellationTokenSource();
        _watchDebounce = debounce;

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(3), debounce.Token);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        if (IsDisposed || debounce.IsCancellationRequested)
        {
            return;
        }

        BeginInvoke(new Action(() =>
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

            if (_autoSync.Checked && CredentialStore.HasToken)
            {
                _ = SyncRosterAsync(forceCurrentSnapshot: false, silent: true);
            }
        }));
    }

    private async Task SyncRosterAsync(bool forceCurrentSnapshot, bool silent)
    {
        if (_syncInProgress)
        {
            return;
        }

        SaveSettings(showConfirmation: false);
        var token = CredentialStore.LoadToken();
        if (string.IsNullOrWhiteSpace(token))
        {
            _syncStatus.Text = "Pair the Guild Roster Client with Services01 before syncing.";
            _syncStatus.ForeColor = Color.DarkOrange;
            if (!silent)
            {
                MessageBox.Show(this, "Pair the client with Services01 first.", "Guild Roster Sync", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            return;
        }

        if (!SourceLocator.IsValidSavedVariablesDirectory(_settings.SourceSavedVariablesPath))
        {
            _syncStatus.Text = "Guild_Roster_Manager.lua was not found. Select the correct SavedVariables folder.";
            _syncStatus.ForeColor = Color.DarkOrange;
            return;
        }

        _syncInProgress = true;
        _syncButton.Enabled = false;
        try
        {
            var progress = new Progress<string>(message =>
            {
                _syncStatus.Text = message;
                _syncStatus.ForeColor = SystemColors.GrayText;
            });

            var outcome = await _syncService.SyncAsync(
                _settings,
                token,
                GetRunningVersion(),
                forceCurrentSnapshot,
                progress);

            _syncStatus.Text = outcome.Message;
            _syncStatus.ForeColor = outcome.QueuedSnapshots == 0 ? Color.DarkGreen : Color.DarkOrange;
        }
        catch (GuildRosterApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
        {
            CredentialStore.Clear();
            _settings.InstallationId = null;
            SettingsService.Save(_settings);
            RefreshPairStatus();
            _syncStatus.Text = "Services01 rejected this pairing. Generate a new one-time pairing code and pair again.";
            _syncStatus.ForeColor = Color.DarkRed;
            if (!silent)
            {
                MessageBox.Show(this, _syncStatus.Text, "Guild Roster Sync", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        catch (InvalidDataException ex)
        {
            _syncStatus.Text = $"Sync blocked for safety: {ex.Message}";
            _syncStatus.ForeColor = Color.DarkRed;
            if (!silent)
            {
                MessageBox.Show(this, ex.Message, "GRM Snapshot Rejected", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        catch (Exception ex)
        {
            _syncStatus.Text = $"Sync failed: {ex.Message}";
            _syncStatus.ForeColor = Color.DarkRed;
            if (!silent)
            {
                MessageBox.Show(this, ex.Message, "Guild Roster Sync Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        finally
        {
            _syncInProgress = false;
            _syncButton.Enabled = true;
        }
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
