using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

namespace GuildRoster.Client;

internal sealed class MainForm : Form
{
    private static readonly Color Background = Color.FromArgb(13, 16, 24);
    private static readonly Color Sidebar = Color.FromArgb(20, 23, 34);
    private static readonly Color Surface = Color.FromArgb(24, 28, 40);
    private static readonly Color SurfaceAlt = Color.FromArgb(30, 34, 49);
    private static readonly Color TextPrimary = Color.FromArgb(238, 240, 248);
    private static readonly Color TextSecondary = Color.FromArgb(163, 170, 194);
    private static readonly Color Gold = Color.FromArgb(231, 181, 67);
    private static readonly Color Purple = Color.FromArgb(130, 95, 225);
    private static readonly Color Green = Color.FromArgb(78, 214, 142);
    private static readonly Color Orange = Color.FromArgb(238, 160, 74);

    private readonly ClientSettings _settings;
    private readonly UpdateFeedClient _updateFeedClient = new();
    private readonly ClientUpdateService _updateService;
    private readonly GuildRosterApiClient _apiClient = new();
    private readonly RosterSyncService _syncService;

    private readonly Label _clientVersionValue = CreateValueLabel("Starting...");
    private readonly Label _sourceValue = CreateValueLabel("Detecting...");
    private readonly Label _serverValue = CreateValueLabel("Checking...");
    private readonly Label _queueValue = CreateValueLabel("0 queued");
    private readonly Label _rosterValue = CreateValueLabel("Not synced");
    private readonly Label _lastSyncValue = CreateValueLabel("None yet");
    private readonly Label _updateValue = CreateValueLabel("Not checked");
    private readonly Label _heroStatusValue = CreateValueLabel("Detecting Guild Roster Manager...");
    private readonly ToolStripStatusLabel _statusText = new("Starting...");
    private readonly ListBox _activityList = new();
    private readonly CheckBox _autoSync = new()
    {
        Text = "Automatically sync when GRM saves",
        AutoSize = true,
        BackColor = Color.Transparent,
        ForeColor = TextSecondary,
    };

    private readonly Button _checkUpdatesButton = CreateActionButton("Check for Updates", Gold);
    private readonly Button _syncButton = CreateActionButton("Sync Now", Purple);
    private readonly Button _channelButton = CreateActionButton("Channel: STABLE", Purple);
    private readonly Button _openRosterButton = CreateActionButton("Open Roster", Purple);
    private readonly Button _sourceButton = CreateActionButton("Change GRM Source", Purple);

    private FileSystemWatcher? _watcher;
    private CancellationTokenSource? _watchDebounce;
    private bool _syncInProgress;

    public MainForm()
    {
        AppPaths.EnsureCreated();
        _settings = SettingsService.Load();
        _updateService = new ClientUpdateService(_updateFeedClient);
        _syncService = new RosterSyncService(_apiClient);

        Text = "FrostLabs Guild Roster Client";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1040, 650);
        Size = new Size(1280, 760);
        BackColor = Background;
        ForeColor = TextPrimary;
        AutoScaleMode = AutoScaleMode.Dpi;

        BuildUi();
        Shown += async (_, _) => await InitializeAsync();
        FormClosed += (_, _) =>
        {
            _watchDebounce?.Cancel();
            _watchDebounce?.Dispose();
            _watcher?.Dispose();
            _apiClient.Dispose();
            _updateFeedClient.Dispose();
        };
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        TryEnableDarkTitleBar();
    }

    private void BuildUi()
    {
        var shell = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Background,
            ColumnCount = 2,
            RowCount = 1,
            Padding = Padding.Empty,
            Margin = Padding.Empty,
        };
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        shell.Controls.Add(BuildSidebar(), 0, 0);
        shell.Controls.Add(BuildMainArea(), 1, 0);
        Controls.Add(shell);

        _clientVersionValue.Text = GetRunningVersion();
        _autoSync.Checked = _settings.AutoSync;
        _autoSync.CheckedChanged += (_, _) =>
        {
            _settings.AutoSync = _autoSync.Checked;
            SettingsService.Save(_settings);
            LogActivity(_settings.AutoSync ? "Automatic GRM sync enabled." : "Automatic GRM sync disabled.");
        };
        UpdateChannelButtonText();
    }

    private Control BuildSidebar()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Sidebar,
            Padding = new Padding(12),
        };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 9,
            BackColor = Sidebar,
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 86));
        for (var i = 1; i <= 6; i++)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        }
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));

        layout.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = "GR\nGuild Roster",
            Font = new Font("Segoe UI", 14, FontStyle.Bold),
            ForeColor = Gold,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(8, 4, 0, 4),
        }, 0, 0);

        layout.Controls.Add(CreateNavButton("Home", (_, _) => SetStatus("Guild Roster dashboard ready."), true), 0, 1);
        layout.Controls.Add(CreateNavButton("Sync", async (_, _) => await SyncRosterAsync(true, false)), 0, 2);
        layout.Controls.Add(CreateNavButton("Roster", (_, _) => OpenRoster()), 0, 3);
        layout.Controls.Add(CreateNavButton("Source", (_, _) => BrowseForSource()), 0, 4);
        layout.Controls.Add(CreateNavButton("Settings", (_, _) => ToggleUpdateChannel()), 0, 5);
        layout.Controls.Add(CreateNavButton("Logs", (_, _) => OpenPath(AppPaths.Root)), 0, 6);
        layout.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = "HOGWARTS ACADEMY",
            Font = new Font("Segoe UI", 8, FontStyle.Bold),
            ForeColor = TextSecondary,
            TextAlign = ContentAlignment.MiddleCenter,
        }, 0, 8);

        panel.Controls.Add(layout);
        return panel;
    }

    private Control BuildMainArea()
    {
        var main = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Background,
        };
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        main.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        main.Controls.Add(BuildTopBar(), 0, 0);
        main.Controls.Add(BuildDashboardBody(), 0, 1);

        var strip = new StatusStrip
        {
            Dock = DockStyle.Fill,
            BackColor = Sidebar,
            ForeColor = Green,
            SizingGrip = false,
            RenderMode = ToolStripRenderMode.System,
        };
        strip.Items.Add(_statusText);
        main.Controls.Add(strip, 0, 2);
        return main;
    }

    private Control BuildTopBar()
    {
        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Sidebar,
            Padding = new Padding(14, 10, 10, 8),
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
        };

        _checkUpdatesButton.Click += async (_, _) => await CheckForUpdatesAsync(true);
        _syncButton.Click += async (_, _) => await SyncRosterAsync(true, false);
        _channelButton.Click += (_, _) => ToggleUpdateChannel();
        _openRosterButton.Click += (_, _) => OpenRoster();
        _sourceButton.Click += (_, _) => BrowseForSource();

        bar.Controls.Add(_checkUpdatesButton);
        bar.Controls.Add(_syncButton);
        bar.Controls.Add(_channelButton);
        bar.Controls.Add(_openRosterButton);
        bar.Controls.Add(_sourceButton);
        return bar;
    }

    private Control BuildDashboardBody()
    {
        var scroll = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = Background,
            Padding = new Padding(20),
        };
        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            BackColor = Background,
        };

        var hero = new Panel
        {
            Dock = DockStyle.Top,
            Height = 132,
            BackColor = Surface,
            Padding = new Padding(22),
            Margin = new Padding(0, 0, 0, 18),
        };
        hero.Controls.Add(new Label
        {
            AutoSize = true,
            Text = "Hogwarts Academy Guild Roster",
            Font = new Font("Segoe UI", 20, FontStyle.Bold),
            ForeColor = TextPrimary,
            Location = new Point(20, 18),
        });
        _heroStatusValue.Location = new Point(22, 62);
        _heroStatusValue.Font = new Font("Segoe UI", 11, FontStyle.Regular);
        _heroStatusValue.MaximumSize = new Size(860, 0);
        _autoSync.Location = new Point(22, 94);
        hero.Controls.Add(_heroStatusValue);
        hero.Controls.Add(_autoSync);
        body.Controls.Add(hero);

        var cards = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 3,
            RowCount = 2,
            BackColor = Background,
            Margin = new Padding(0, 0, 0, 18),
        };
        cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
        cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
        cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
        cards.Controls.Add(CreateMetricCard("CLIENT VERSION", _clientVersionValue), 0, 0);
        cards.Controls.Add(CreateMetricCard("GRM SOURCE", _sourceValue), 1, 0);
        cards.Controls.Add(CreateMetricCard("SERVICES01", _serverValue), 2, 0);
        cards.Controls.Add(CreateMetricCard("ACTIVE CHARACTERS", _rosterValue), 0, 1);
        cards.Controls.Add(CreateMetricCard("LAST SYNC", _lastSyncValue), 1, 1);
        cards.Controls.Add(CreateMetricCard("QUEUE / UPDATES", BuildQueueUpdateValue()), 2, 1);
        body.Controls.Add(cards);

        body.Controls.Add(new Label
        {
            AutoSize = true,
            MaximumSize = new Size(920, 0),
            Text = "Works like Azeroth Questing Companion: it watches the local SavedVariables file, registers itself with Services01 automatically in the background, keeps a retry queue, and syncs without asking you for a pairing code. GRM Lua is read as data only and is never executed.",
            Font = new Font("Segoe UI", 10),
            ForeColor = TextSecondary,
            Margin = new Padding(2, 4, 2, 18),
        });

        body.Controls.Add(new Label
        {
            AutoSize = true,
            Text = "Recent Activity",
            Font = new Font("Segoe UI", 13, FontStyle.Bold),
            ForeColor = TextPrimary,
            Margin = new Padding(0, 0, 0, 8),
        });

        _activityList.Height = 210;
        _activityList.Dock = DockStyle.Top;
        _activityList.BackColor = Surface;
        _activityList.ForeColor = TextPrimary;
        _activityList.BorderStyle = BorderStyle.FixedSingle;
        _activityList.Font = new Font("Consolas", 9.5f);
        body.Controls.Add(_activityList);

        scroll.Controls.Add(body);
        return scroll;
    }

    private Control BuildQueueUpdateValue()
    {
        var panel = new TableLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = SurfaceAlt,
        };
        panel.Controls.Add(_queueValue, 0, 0);
        _updateValue.Font = new Font("Segoe UI", 9, FontStyle.Regular);
        panel.Controls.Add(_updateValue, 0, 1);
        return panel;
    }

    private async Task InitializeAsync()
    {
        SetStatus("Detecting Guild Roster Manager...");
        try
        {
            DiscoverSource();
            ConfigureWatcher();
            RefreshSyncMetrics();

            var healthy = await _apiClient.IsHealthyAsync(_settings.ServerBaseUrl);
            _serverValue.Text = healthy ? "Connected" : "Offline - queueing locally";
            _serverValue.ForeColor = healthy ? Green : Orange;

            if (_settings.AutoSync && SourceLocator.IsValidSavedVariablesDirectory(_settings.SourceSavedVariablesPath))
            {
                await SyncRosterAsync(false, true);
            }

            await CheckForUpdatesAsync(false);
            SetStatus("Guild Roster Client ready.");
        }
        catch (Exception ex)
        {
            LogActivity("Startup: " + ex.Message);
            SetStatus("Ready with a startup warning.");
        }
    }

    private void DiscoverSource()
    {
        if (SourceLocator.IsValidSavedVariablesDirectory(_settings.SourceSavedVariablesPath))
        {
            RefreshSourceStatus();
            return;
        }

        var preferred = SourceLocator.FindPreferredCandidate(
            _settings.GuildName,
            _settings.GuildRealm,
            _settings.SourceSavedVariablesPath);
        if (preferred is not null)
        {
            _settings.SourceSavedVariablesPath = preferred.SavedVariablesDirectory;
            SettingsService.Save(_settings);
            LogActivity("GRM source selected automatically: " + preferred.SavedVariablesDirectory);
        }

        RefreshSourceStatus();
    }

    private void RefreshSourceStatus()
    {
        var path = _settings.SourceSavedVariablesPath;
        if (!SourceLocator.IsValidSavedVariablesDirectory(path))
        {
            _sourceValue.Text = "Not found";
            _sourceValue.ForeColor = Orange;
            _heroStatusValue.Text = "Guild_Roster_Manager.lua was not found. Choose the GRM source once and the client will remember it.";
            _heroStatusValue.ForeColor = Orange;
            return;
        }

        var filePath = SourceLocator.GetGrmFilePath(path!);
        var info = new FileInfo(filePath);
        var accountName = Directory.GetParent(path!)?.Name ?? "WoW account";
        _sourceValue.Text = $"{accountName} · {info.LastWriteTime:G}";
        _sourceValue.ForeColor = Green;
        _heroStatusValue.Text = "Watching " + filePath;
        _heroStatusValue.ForeColor = TextSecondary;
    }

    private void RefreshSyncMetrics()
    {
        var state = _syncService.LoadState();
        var queued = _syncService.GetQueuedCount();
        _queueValue.Text = $"{queued:N0} queued";
        _queueValue.ForeColor = queued == 0 ? Green : Orange;

        if (state.LastSuccessfulSync is null)
        {
            _rosterValue.Text = "Not synced";
            _lastSyncValue.Text = "None yet";
            _rosterValue.ForeColor = TextSecondary;
            _lastSyncValue.ForeColor = TextSecondary;
        }
        else
        {
            _rosterValue.Text = $"{state.LastAcceptedMemberCount:N0}";
            _lastSyncValue.Text = state.LastSuccessfulSync.Value.LocalDateTime.ToString("g");
            _rosterValue.ForeColor = Green;
            _lastSyncValue.ForeColor = Green;
        }
    }

    private void BrowseForSource()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Select the WoW account SavedVariables folder containing Guild_Roster_Manager.lua",
            UseDescriptionForTitle = true,
            InitialDirectory = Directory.Exists(_settings.SourceSavedVariablesPath)
                ? _settings.SourceSavedVariablesPath
                : @"D:\Battle.net\World of Warcraft\_retail_\WTF\Account",
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        _settings.SourceSavedVariablesPath = dialog.SelectedPath;
        SettingsService.Save(_settings);
        ConfigureWatcher();
        RefreshSourceStatus();
        LogActivity("GRM source changed to " + dialog.SelectedPath);
    }

    private void ConfigureWatcher()
    {
        _watcher?.Dispose();
        _watcher = null;

        var directory = _settings.SourceSavedVariablesPath;
        if (!SourceLocator.IsValidSavedVariablesDirectory(directory))
        {
            return;
        }

        _watcher = new FileSystemWatcher(directory!, "Guild_Roster_Manager.lua")
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
            RefreshSourceStatus();
            LogActivity("GRM SavedVariables changed.");
            if (_settings.AutoSync)
            {
                _ = SyncRosterAsync(false, true);
            }
        }));
    }

    private async Task SyncRosterAsync(bool forceCurrentSnapshot, bool silent)
    {
        if (_syncInProgress)
        {
            return;
        }

        if (!SourceLocator.IsValidSavedVariablesDirectory(_settings.SourceSavedVariablesPath))
        {
            DiscoverSource();
            if (!SourceLocator.IsValidSavedVariablesDirectory(_settings.SourceSavedVariablesPath))
            {
                SetStatus("GRM source not found.");
                if (!silent)
                {
                    MessageBox.Show(this, "Guild_Roster_Manager.lua was not found. Choose the GRM source first.", "Guild Roster Sync", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                return;
            }
        }

        _syncInProgress = true;
        _syncButton.Enabled = false;
        _sourceButton.Enabled = false;
        try
        {
            var progress = new Progress<string>(message =>
            {
                SetStatus(message);
                LogActivity(message);
            });
            var result = await _syncService.SyncAsync(
                _settings,
                GetRunningVersion(),
                forceCurrentSnapshot,
                progress);

            RefreshSyncMetrics();
            RefreshSourceStatus();
            _serverValue.Text = result.QueuedSnapshots == 0 ? "Connected" : "Queueing for retry";
            _serverValue.ForeColor = result.QueuedSnapshots == 0 ? Green : Orange;
            _heroStatusValue.Text = result.Message;
            _heroStatusValue.ForeColor = result.QueuedSnapshots == 0 ? Green : Orange;
            SetStatus(result.Message);
            LogActivity(result.Message);

            if (!silent && result.QueuedSnapshots == 0)
            {
                MessageBox.Show(this, result.Message, "Guild Roster Sync", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        catch (Exception ex)
        {
            RefreshSyncMetrics();
            _heroStatusValue.Text = "Sync blocked: " + ex.Message;
            _heroStatusValue.ForeColor = Orange;
            SetStatus("Sync blocked.");
            LogActivity("Sync blocked: " + ex.Message);
            if (!silent)
            {
                MessageBox.Show(this, ex.Message, "Guild Roster Sync", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        finally
        {
            _syncInProgress = false;
            _syncButton.Enabled = true;
            _sourceButton.Enabled = true;
        }
    }

    private async Task CheckForUpdatesAsync(bool showDialog)
    {
        _checkUpdatesButton.Enabled = false;
        try
        {
            var channel = NormalizeChannel(_settings.UpdateChannel);
            _updateValue.Text = $"Checking {channel}...";
            var package = await _updateFeedClient.GetLatestAsync(_settings.ServerBaseUrl, channel);
            if (package is null)
            {
                _updateValue.Text = $"No {channel} update published";
                return;
            }

            var current = GetRunningVersion();
            if (!ReleaseVersionUtility.IsNewer(current, package.Version))
            {
                _updateValue.Text = $"Up to date · {current}";
                if (showDialog)
                {
                    MessageBox.Show(this, "Guild Roster Client is up to date.", "Updates", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                return;
            }

            _updateValue.Text = $"Update available · {package.Version}";
            if (!showDialog)
            {
                return;
            }

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

            var progress = new Progress<string>(message =>
            {
                _updateValue.Text = message;
                SetStatus(message);
            });
            await _updateService.StageAndLaunchUpdateAsync(package, progress);
        }
        catch (Exception ex)
        {
            _updateValue.Text = "Update check unavailable";
            LogActivity("Update check: " + ex.Message);
            if (showDialog)
            {
                MessageBox.Show(this, ex.Message, "Update Check Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        finally
        {
            _checkUpdatesButton.Enabled = true;
        }
    }

    private void ToggleUpdateChannel()
    {
        _settings.UpdateChannel = NormalizeChannel(_settings.UpdateChannel) == "stable" ? "beta" : "stable";
        SettingsService.Save(_settings);
        UpdateChannelButtonText();
        _updateValue.Text = $"Channel changed to {_settings.UpdateChannel}.";
        LogActivity($"Update channel changed to {_settings.UpdateChannel}.");
    }

    private void UpdateChannelButtonText()
    {
        _channelButton.Text = $"Channel: {NormalizeChannel(_settings.UpdateChannel).ToUpperInvariant()}";
    }

    private static string NormalizeChannel(string? channel) =>
        string.Equals(channel, "beta", StringComparison.OrdinalIgnoreCase) ? "beta" : "stable";

    private void OpenRoster()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = _settings.ServerBaseUrl.Trim().TrimEnd('/') + "/roster",
            UseShellExecute = true,
        });
    }

    private static void OpenPath(string path)
    {
        AppPaths.EnsureCreated();
        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true,
        });
    }

    private void SetStatus(string text)
    {
        _statusText.Text = text;
    }

    private void LogActivity(string text)
    {
        if (IsDisposed)
        {
            return;
        }

        _activityList.Items.Insert(0, $"{DateTime.Now:HH:mm:ss}  {text}");
        while (_activityList.Items.Count > 80)
        {
            _activityList.Items.RemoveAt(_activityList.Items.Count - 1);
        }
    }

    private static Label CreateValueLabel(string text) => new()
    {
        AutoSize = true,
        Text = text,
        Font = new Font("Segoe UI", 11, FontStyle.Bold),
        ForeColor = TextPrimary,
        MaximumSize = new Size(285, 0),
        Margin = new Padding(0, 4, 0, 0),
    };

    private static Control CreateMetricCard(string title, Control value)
    {
        var card = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = SurfaceAlt,
            Margin = new Padding(5),
            Padding = new Padding(16),
            MinimumSize = new Size(250, 104),
        };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = SurfaceAlt,
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(new Label
        {
            AutoSize = true,
            Text = title,
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            ForeColor = TextSecondary,
        }, 0, 0);
        value.Margin = new Padding(0, 2, 0, 0);
        layout.Controls.Add(value, 0, 1);
        card.Controls.Add(layout);
        return card;
    }

    private static Button CreateActionButton(string text, Color accent)
    {
        var button = new Button
        {
            AutoSize = true,
            Text = text,
            FlatStyle = FlatStyle.Flat,
            BackColor = SurfaceAlt,
            ForeColor = TextPrimary,
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            Margin = new Padding(4, 0, 4, 0),
            Padding = new Padding(8, 5, 8, 5),
            Cursor = Cursors.Hand,
        };
        button.FlatAppearance.BorderColor = accent;
        button.FlatAppearance.BorderSize = 1;
        return button;
    }

    private static Button CreateNavButton(string text, EventHandler handler, bool active = false)
    {
        var button = new Button
        {
            Dock = DockStyle.Fill,
            Text = text,
            TextAlign = ContentAlignment.MiddleLeft,
            FlatStyle = FlatStyle.Flat,
            BackColor = active ? SurfaceAlt : Sidebar,
            ForeColor = active ? Gold : TextPrimary,
            Font = new Font("Segoe UI", 10, active ? FontStyle.Bold : FontStyle.Regular),
            Padding = new Padding(12, 0, 0, 0),
            Cursor = Cursors.Hand,
            Margin = new Padding(0, 2, 0, 2),
        };
        button.FlatAppearance.BorderSize = 0;
        button.Click += handler;
        return button;
    }

    private static string GetRunningVersion()
    {
        var assembly = typeof(MainForm).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            var buildMetadata = informational.IndexOf('+');
            return buildMetadata >= 0 ? informational[..buildMetadata] : informational;
        }

        return assembly.GetName().Version?.ToString(3) ?? "unknown";
    }

    private void TryEnableDarkTitleBar()
    {
        try
        {
            var enabled = 1;
            _ = DwmSetWindowAttribute(Handle, 20, ref enabled, sizeof(int));
        }
        catch
        {
            // Cosmetic only.
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
}
