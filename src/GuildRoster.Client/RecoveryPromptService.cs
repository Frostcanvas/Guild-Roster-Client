using System.Net;
using System.Reflection;

namespace GuildRoster.Client;

internal sealed class RecoveryPromptService : IDisposable
{
    private readonly Form _owner;
    private readonly GuildRosterApiClient _apiClient = new();
    private readonly System.Windows.Forms.Timer _timer;
    private readonly HashSet<long> _suppressedThisSession = new();
    private bool _checking;
    private bool _disposed;

    public RecoveryPromptService(Form owner)
    {
        _owner = owner;
        _timer = new System.Windows.Forms.Timer
        {
            Interval = (int)TimeSpan.FromMinutes(5).TotalMilliseconds,
        };
        _timer.Tick += async (_, _) => await CheckAsync();
        _owner.Shown += OwnerShown;
        _owner.FormClosed += OwnerClosed;
    }

    private async void OwnerShown(object? sender, EventArgs e)
    {
        _timer.Start();
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(12));
            await CheckAsync();
        }
        catch (ObjectDisposedException)
        {
            // App is closing.
        }
    }

    private void OwnerClosed(object? sender, FormClosedEventArgs e) => Dispose();

    private async Task CheckAsync()
    {
        if (_disposed || _checking || _owner.IsDisposed)
        {
            return;
        }

        var settings = SettingsService.Load();
        if (!SourceLocator.IsValidSavedVariablesDirectory(settings.SourceSavedVariablesPath))
        {
            return;
        }

        _checking = true;
        try
        {
            var version = GetRunningVersion();
            var token = await GetBearerTokenAsync(settings, version);
            var offers = await GetOffersWithRetryAsync(settings, version, token);

            foreach (var offer in offers)
            {
                if (_disposed || _owner.IsDisposed)
                {
                    return;
                }
                if (_suppressedThisSession.Contains(offer.Id))
                {
                    continue;
                }

                using var dialog = new RecoveryOfferDialog(offer);
                _ = dialog.ShowDialog(_owner);

                if (dialog.Choice == RecoveryOfferChoice.AskLater)
                {
                    await SaveDecisionWithRetryAsync(
                        settings,
                        version,
                        offer.Id,
                        "ask_later",
                        Array.Empty<string>());
                    continue;
                }

                if (dialog.Choice == RecoveryOfferChoice.KeepCurrent)
                {
                    await SaveDecisionWithRetryAsync(
                        settings,
                        version,
                        offer.Id,
                        "keep_current",
                        Array.Empty<string>());
                    continue;
                }

                var selectedFields = dialog.SelectedFields;
                GrmRestoreExecutionResult restoreResult;
                try
                {
                    restoreResult = await GrmRestoreEngine.ApplyAsync(
                        settings,
                        offer,
                        selectedFields);
                }
                catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or IOException or UnauthorizedAccessException)
                {
                    _suppressedThisSession.Add(offer.Id);
                    MessageBox.Show(
                        _owner,
                        ex.Message + "\n\nNothing was marked restored on Services01. This recovery offer will remain available for a later session.",
                        "GRM Restore Not Applied",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    continue;
                }

                try
                {
                    await SaveDecisionWithRetryAsync(
                        settings,
                        version,
                        offer.Id,
                        "review_restore",
                        selectedFields);
                }
                catch (Exception ex) when (ex is GuildRosterApiException or HttpRequestException or TaskCanceledException)
                {
                    MessageBox.Show(
                        _owner,
                        "The local GRM restore completed, but Services01 could not save the recovery decision. The local audit prevents the same restore from being applied twice.\n\n" + ex.Message,
                        "Restore Applied · Services01 Update Pending",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    _suppressedThisSession.Add(offer.Id);
                    continue;
                }

                var restoredCount = restoreResult.Fields.Count(field =>
                    string.Equals(field.Status, "restored", StringComparison.OrdinalIgnoreCase));
                var manualCount = restoreResult.Fields.Count(field =>
                    string.Equals(field.Status, "manual_required", StringComparison.OrdinalIgnoreCase));
                var reviewCount = restoreResult.Fields.Count(field =>
                    string.Equals(field.Status, "needs_review", StringComparison.OrdinalIgnoreCase));

                var message = new List<string>
                {
                    restoreResult.AlreadyApplied
                        ? "This exact recovery event had already been applied locally; the existing audit was reused."
                        : "The approved GRM-local restore completed.",
                    $"Restored GRM-local fields: {restoredCount:N0}",
                    $"Manual Public/Officer note items: {manualCount:N0}",
                    $"Needs further review: {reviewCount:N0}",
                };
                if (!string.IsNullOrWhiteSpace(restoreResult.BackupDirectory))
                {
                    message.Add("Pre-restore backup: " + restoreResult.BackupDirectory);
                }
                message.Add("Guild rank and other Blizzard-owned state were not changed.");

                MessageBox.Show(
                    _owner,
                    string.Join(Environment.NewLine, message),
                    "Returning Member Restore Complete",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
        }
        catch (Exception ex) when (ex is GuildRosterApiException or HttpRequestException or TaskCanceledException)
        {
            // Recovery review is best-effort. Normal roster synchronization remains independent.
        }
        finally
        {
            _checking = false;
        }
    }

    private async Task<string> GetBearerTokenAsync(ClientSettings settings, string version)
    {
        var registration = await _apiClient.EnsureRegisteredAsync(settings, version);
        return registration.BearerToken;
    }

    private async Task<IReadOnlyList<RecoveryOffer>> GetOffersWithRetryAsync(
        ClientSettings settings,
        string version,
        string bearerToken)
    {
        try
        {
            return await GrmArchiveApiClient.GetPendingRecoveryOffersAsync(
                settings.ServerBaseUrl,
                bearerToken);
        }
        catch (GuildRosterApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
        {
            GuildRosterApiClient.ClearRegistration(settings);
            var registration = await _apiClient.EnsureRegisteredAsync(settings, version);
            return await GrmArchiveApiClient.GetPendingRecoveryOffersAsync(
                settings.ServerBaseUrl,
                registration.BearerToken);
        }
    }

    private async Task SaveDecisionWithRetryAsync(
        ClientSettings settings,
        string version,
        long offerId,
        string decision,
        IReadOnlyList<string> selectedFields)
    {
        var token = await GetBearerTokenAsync(settings, version);
        try
        {
            await GrmArchiveApiClient.DecideRecoveryOfferAsync(
                settings.ServerBaseUrl,
                token,
                offerId,
                decision,
                selectedFields);
        }
        catch (GuildRosterApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
        {
            GuildRosterApiClient.ClearRegistration(settings);
            var registration = await _apiClient.EnsureRegisteredAsync(settings, version);
            await GrmArchiveApiClient.DecideRecoveryOfferAsync(
                settings.ServerBaseUrl,
                registration.BearerToken,
                offerId,
                decision,
                selectedFields);
        }
    }

    private static string GetRunningVersion()
    {
        var assembly = typeof(RecoveryPromptService).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            var buildMetadata = informational.IndexOf('+');
            return buildMetadata >= 0 ? informational[..buildMetadata] : informational;
        }
        return assembly.GetName().Version?.ToString(3) ?? "unknown";
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _timer.Stop();
        _timer.Dispose();
        _apiClient.Dispose();
        _owner.Shown -= OwnerShown;
        _owner.FormClosed -= OwnerClosed;
    }
}
