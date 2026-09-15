using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GuildRoster.Client;

internal sealed record RosterSyncOutcome(
    int MemberCount,
    int UploadedSnapshots,
    int QueuedSnapshots,
    bool CurrentSnapshotChanged,
    DateTimeOffset CapturedAt,
    string Message);

internal sealed class RosterSyncService
{
    private readonly GuildRosterApiClient _apiClient;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };
    private readonly JsonSerializerOptions _archiveJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    public RosterSyncService(GuildRosterApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    public int GetQueuedCount()
    {
        AppPaths.EnsureCreated();
        var roster = Directory.EnumerateFiles(AppPaths.OutboxRoot, "*.json", SearchOption.TopDirectoryOnly).Count();
        var archive = Directory.EnumerateFiles(AppPaths.GrmArchiveOutboxRoot, "*.json", SearchOption.TopDirectoryOnly).Count();
        return roster + archive;
    }

    public SyncState LoadState() => SyncStateService.Load(_jsonOptions);

    public async Task<RosterSyncOutcome> SyncAsync(
        ClientSettings settings,
        string companionVersion,
        bool forceCurrentSnapshot,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(settings.SourceSavedVariablesPath))
        {
            throw new InvalidOperationException("Select the GRM SavedVariables folder before syncing.");
        }

        var grmFile = SourceLocator.GetGrmFilePath(settings.SourceSavedVariablesPath);
        progress?.Report("Reading Guild_Roster_Manager.lua…");
        var parsed = await GrmRosterParser.ParseAsync(
            grmFile,
            settings.GuildName,
            settings.GuildRealm,
            cancellationToken);

        progress?.Report($"Validated {parsed.Members.Count:N0} Hogwarts Academy characters from GRM.");
        var payload = GrmRosterParser.ToPayload(parsed, companionVersion);
        var identity = await GrmIdentityParser.ParseAsync(grmFile, parsed, cancellationToken);
        progress?.Report(
            $"Validated main/alt identity · {identity.MainCount:N0} mains · {identity.AltCount:N0} alts · {identity.UnknownCount:N0} ungrouped.");

        progress?.Report("Parsing the complete GRM archive…");
        var archive = await GrmArchiveParser.ParseAsync(grmFile, parsed, cancellationToken);
        progress?.Report(
            $"GRM archive ready · {archive.VariableCount:N0} variables · {archive.RestoreProfileCount:N0} restore profile record(s) · {archive.ParseErrorCount:N0} unsupported variable(s).");

        var state = SyncStateService.Load(_jsonOptions);
        var currentChanged = !string.Equals(
            state.LastAcceptedSnapshotKey,
            parsed.SnapshotKey,
            StringComparison.OrdinalIgnoreCase);

        var currentOutboxPath = GetOutboxPath(parsed.SnapshotKey);
        if (forceCurrentSnapshot || currentChanged || File.Exists(currentOutboxPath))
        {
            QueuePayload(payload, currentOutboxPath);
        }

        var archiveOutboxPath = GetArchiveOutboxPath(archive.Payload.ArchiveKey);
        if (!string.Equals(
                state.LastAcceptedGrmArchiveKey,
                archive.Payload.ArchiveKey,
                StringComparison.OrdinalIgnoreCase) ||
            File.Exists(archiveOutboxPath))
        {
            QueueArchivePayload(archive.Payload, archiveOutboxPath);
        }

        var queuedBeforeUpload = GetQueuedCount();
        if (queuedBeforeUpload == 0)
        {
            try
            {
                progress?.Report("Synchronizing GRM main/alt identity…");
                await SyncIdentityAsync(
                    settings,
                    companionVersion,
                    identity.Payload,
                    cancellationToken);
            }
            catch (Exception ex) when (IsRetryable(ex, cancellationToken))
            {
                return new RosterSyncOutcome(
                    parsed.Members.Count,
                    0,
                    0,
                    currentChanged,
                    parsed.CapturedAt,
                    $"Connected - roster unchanged · main/alt identity sync deferred and will retry. {ex.Message}");
            }

            return new RosterSyncOutcome(
                parsed.Members.Count,
                0,
                0,
                currentChanged,
                parsed.CapturedAt,
                $"Connected - no roster or GRM archive changes · {parsed.Members.Count:N0} active characters.");
        }

        string bearerToken;
        try
        {
            progress?.Report("Connecting to Services01…");
            var registration = await _apiClient.EnsureRegisteredAsync(
                settings,
                companionVersion,
                cancellationToken);
            bearerToken = registration.BearerToken;
        }
        catch (Exception ex) when (IsRetryable(ex, cancellationToken))
        {
            var queuedDuringRegistration = GetQueuedCount();
            return new RosterSyncOutcome(
                parsed.Members.Count,
                0,
                queuedDuringRegistration,
                currentChanged,
                parsed.CapturedAt,
                $"Services01 unavailable; {queuedDuringRegistration:N0} validated item(s) queued for retry. {ex.Message}");
        }

        var uploaded = 0;
        string? deferredReason = null;
        foreach (var path in Directory.EnumerateFiles(AppPaths.OutboxRoot, "*.json")
                     .OrderBy(File.GetCreationTimeUtc)
                     .ThenBy(file => file, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var queuedPayload = LoadQueuedPayload(path);
            progress?.Report($"Uploading roster snapshot {queuedPayload.ClientSnapshotKey[..12]}…");

            try
            {
                UploadResult result;
                try
                {
                    result = await _apiClient.UploadSnapshotAsync(
                        settings.ServerBaseUrl,
                        bearerToken,
                        queuedPayload,
                        cancellationToken);
                }
                catch (GuildRosterApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
                {
                    GuildRosterApiClient.ClearRegistration(settings);
                    var registration = await _apiClient.EnsureRegisteredAsync(
                        settings,
                        companionVersion,
                        cancellationToken);
                    bearerToken = registration.BearerToken;
                    result = await _apiClient.UploadSnapshotAsync(
                        settings.ServerBaseUrl,
                        bearerToken,
                        queuedPayload,
                        cancellationToken);
                }

                if (!string.Equals(result.Status, "accepted", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(result.Status, "duplicate", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException($"Services01 returned unexpected roster status '{result.Status}'.");
                }

                File.Delete(path);
                uploaded++;
                state.LastAcceptedSnapshotKey = queuedPayload.ClientSnapshotKey;
                state.LastAcceptedMemberCount = queuedPayload.Members.Count;
                state.LastSuccessfulSync = DateTimeOffset.UtcNow;
                state.LastServerSnapshotId = result.SnapshotId;
                SyncStateService.Save(state, _jsonOptions);
            }
            catch (Exception ex) when (IsRetryable(ex, cancellationToken))
            {
                deferredReason = ex.Message;
                break;
            }
        }

        if (deferredReason is null)
        {
            foreach (var path in Directory.EnumerateFiles(AppPaths.GrmArchiveOutboxRoot, "*.json")
                         .OrderBy(File.GetCreationTimeUtc)
                         .ThenBy(file => file, StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var queuedArchive = LoadQueuedArchivePayload(path);
                progress?.Report($"Uploading full GRM archive {queuedArchive.ArchiveKey[..12]}…");

                try
                {
                    GrmArchiveUploadResult result;
                    try
                    {
                        result = await GrmArchiveApiClient.UploadAsync(
                            settings.ServerBaseUrl,
                            bearerToken,
                            queuedArchive,
                            cancellationToken);
                    }
                    catch (GuildRosterApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
                    {
                        GuildRosterApiClient.ClearRegistration(settings);
                        var registration = await _apiClient.EnsureRegisteredAsync(
                            settings,
                            companionVersion,
                            cancellationToken);
                        bearerToken = registration.BearerToken;
                        result = await GrmArchiveApiClient.UploadAsync(
                            settings.ServerBaseUrl,
                            bearerToken,
                            queuedArchive,
                            cancellationToken);
                    }

                    if (!string.Equals(result.Status, "accepted", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(result.Status, "duplicate", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException($"Services01 returned unexpected GRM archive status '{result.Status}'.");
                    }

                    File.Delete(path);
                    uploaded++;
                    state.LastAcceptedGrmArchiveKey = queuedArchive.ArchiveKey;
                    state.LastSuccessfulGrmArchiveSync = DateTimeOffset.UtcNow;
                    SyncStateService.Save(state, _jsonOptions);
                    if (result.RecoveryOfferCount > 0)
                    {
                        progress?.Report($"Detected {result.RecoveryOfferCount:N0} returning member recovery offer(s).");
                    }
                }
                catch (Exception ex) when (IsRetryable(ex, cancellationToken))
                {
                    deferredReason = ex.Message;
                    break;
                }
            }
        }

        var queued = GetQueuedCount();
        if (deferredReason is not null)
        {
            return new RosterSyncOutcome(
                parsed.Members.Count,
                uploaded,
                queued,
                currentChanged,
                parsed.CapturedAt,
                $"Services01 unavailable; {queued:N0} validated item(s) queued for retry. {deferredReason}");
        }

        try
        {
            progress?.Report("Synchronizing GRM main/alt identity…");
            await SyncIdentityAsync(
                settings,
                companionVersion,
                identity.Payload,
                cancellationToken);
        }
        catch (Exception ex) when (IsRetryable(ex, cancellationToken))
        {
            return new RosterSyncOutcome(
                parsed.Members.Count,
                uploaded,
                queued,
                currentChanged,
                parsed.CapturedAt,
                $"Roster and GRM archive synchronized; main/alt identity sync deferred and will retry. {ex.Message}");
        }

        if (!currentChanged && !forceCurrentSnapshot && uploaded == 0)
        {
            return new RosterSyncOutcome(
                parsed.Members.Count,
                0,
                queued,
                false,
                parsed.CapturedAt,
                $"Connected - no changes · {parsed.Members.Count:N0} active characters · queue {queued:N0}.");
        }

        return new RosterSyncOutcome(
            parsed.Members.Count,
            uploaded,
            queued,
            currentChanged,
            parsed.CapturedAt,
            $"Connected - roster and full GRM archive synchronized · {parsed.Members.Count:N0} active characters · queue {queued:N0}.");
    }

    public async Task<IReadOnlyList<RecoveryOffer>> GetPendingRecoveryOffersAsync(
        ClientSettings settings,
        string companionVersion,
        CancellationToken cancellationToken = default)
    {
        var registration = await _apiClient.EnsureRegisteredAsync(settings, companionVersion, cancellationToken);
        try
        {
            return await GrmArchiveApiClient.GetPendingRecoveryOffersAsync(
                settings.ServerBaseUrl,
                registration.BearerToken,
                cancellationToken);
        }
        catch (GuildRosterApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
        {
            GuildRosterApiClient.ClearRegistration(settings);
            registration = await _apiClient.EnsureRegisteredAsync(settings, companionVersion, cancellationToken);
            return await GrmArchiveApiClient.GetPendingRecoveryOffersAsync(
                settings.ServerBaseUrl,
                registration.BearerToken,
                cancellationToken);
        }
    }

    public async Task DecideRecoveryOfferAsync(
        ClientSettings settings,
        string companionVersion,
        long offerId,
        string decision,
        CancellationToken cancellationToken = default)
    {
        var registration = await _apiClient.EnsureRegisteredAsync(settings, companionVersion, cancellationToken);
        try
        {
            await GrmArchiveApiClient.DecideRecoveryOfferAsync(
                settings.ServerBaseUrl,
                registration.BearerToken,
                offerId,
                decision,
                cancellationToken);
        }
        catch (GuildRosterApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
        {
            GuildRosterApiClient.ClearRegistration(settings);
            registration = await _apiClient.EnsureRegisteredAsync(settings, companionVersion, cancellationToken);
            await GrmArchiveApiClient.DecideRecoveryOfferAsync(
                settings.ServerBaseUrl,
                registration.BearerToken,
                offerId,
                decision,
                cancellationToken);
        }
    }

    private async Task<IdentityUploadResult> SyncIdentityAsync(
        ClientSettings settings,
        string companionVersion,
        RosterIdentityPayload payload,
        CancellationToken cancellationToken)
    {
        var registration = await _apiClient.EnsureRegisteredAsync(
            settings,
            companionVersion,
            cancellationToken);
        try
        {
            var result = await RosterIdentityApiClient.UploadAsync(
                settings.ServerBaseUrl,
                registration.BearerToken,
                payload,
                cancellationToken);
            if (!string.Equals(result.Status, "accepted", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Services01 returned unexpected identity status '{result.Status}'.");
            }
            return result;
        }
        catch (GuildRosterApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
        {
            GuildRosterApiClient.ClearRegistration(settings);
            registration = await _apiClient.EnsureRegisteredAsync(
                settings,
                companionVersion,
                cancellationToken);
            var result = await RosterIdentityApiClient.UploadAsync(
                settings.ServerBaseUrl,
                registration.BearerToken,
                payload,
                cancellationToken);
            if (!string.Equals(result.Status, "accepted", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Services01 returned unexpected identity status '{result.Status}'.");
            }
            return result;
        }
    }

    private static bool IsRetryable(Exception ex, CancellationToken cancellationToken) => ex switch
    {
        GuildRosterApiException apiEx when apiEx.StatusCode is
            HttpStatusCode.RequestTimeout or
            HttpStatusCode.TooManyRequests or
            HttpStatusCode.InternalServerError or
            HttpStatusCode.BadGateway or
            HttpStatusCode.ServiceUnavailable or
            HttpStatusCode.GatewayTimeout => true,
        HttpRequestException => true,
        TaskCanceledException when !cancellationToken.IsCancellationRequested => true,
        _ => false,
    };

    private string GetOutboxPath(string snapshotKey) =>
        Path.Combine(AppPaths.OutboxRoot, $"{snapshotKey}.json");

    private string GetArchiveOutboxPath(string archiveKey) =>
        Path.Combine(AppPaths.GrmArchiveOutboxRoot, $"{archiveKey}.json");

    private void QueuePayload(RosterSnapshotPayload payload, string destination)
    {
        AppPaths.EnsureCreated();
        if (File.Exists(destination))
        {
            return;
        }

        var tempPath = destination + ".tmp";
        var json = JsonSerializer.Serialize(payload, _jsonOptions);
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, destination, overwrite: false);
    }

    private void QueueArchivePayload(GrmArchivePayload payload, string destination)
    {
        AppPaths.EnsureCreated();
        if (File.Exists(destination))
        {
            return;
        }

        var tempPath = destination + ".tmp";
        var json = JsonSerializer.Serialize(payload, _archiveJsonOptions);
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, destination, overwrite: false);
    }

    private RosterSnapshotPayload LoadQueuedPayload(string path)
    {
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<RosterSnapshotPayload>(json, _jsonOptions)
                ?? throw new InvalidDataException($"Queued roster snapshot '{Path.GetFileName(path)}' was empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                $"Queued roster snapshot '{Path.GetFileName(path)}' is invalid and was not uploaded.",
                ex);
        }
    }

    private GrmArchivePayload LoadQueuedArchivePayload(string path)
    {
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<GrmArchivePayload>(json, _archiveJsonOptions)
                ?? throw new InvalidDataException($"Queued GRM archive '{Path.GetFileName(path)}' was empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                $"Queued GRM archive '{Path.GetFileName(path)}' is invalid and was not uploaded.",
                ex);
        }
    }
}

internal sealed class SyncState
{
    public string? LastAcceptedSnapshotKey { get; set; }
    public int LastAcceptedMemberCount { get; set; }
    public DateTimeOffset? LastSuccessfulSync { get; set; }
    public long? LastServerSnapshotId { get; set; }
    public string? LastAcceptedGrmArchiveKey { get; set; }
    public DateTimeOffset? LastSuccessfulGrmArchiveSync { get; set; }
}

internal static class SyncStateService
{
    public static SyncState Load(JsonSerializerOptions options)
    {
        AppPaths.EnsureCreated();
        if (!File.Exists(AppPaths.SyncStatePath))
        {
            return new SyncState();
        }

        try
        {
            return JsonSerializer.Deserialize<SyncState>(File.ReadAllText(AppPaths.SyncStatePath), options)
                ?? new SyncState();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new SyncState();
        }
    }

    public static void Save(SyncState state, JsonSerializerOptions options)
    {
        AppPaths.EnsureCreated();
        var destination = AppPaths.SyncStatePath;
        var tempPath = destination + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(state, options));
        File.Move(tempPath, destination, overwrite: true);
    }
}
