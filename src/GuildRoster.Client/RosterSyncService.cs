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

    public RosterSyncService(GuildRosterApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    public int GetQueuedCount()
    {
        AppPaths.EnsureCreated();
        return Directory.EnumerateFiles(AppPaths.OutboxRoot, "*.json", SearchOption.TopDirectoryOnly).Count();
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

        var queuedBeforeUpload = GetQueuedCount();
        if (queuedBeforeUpload == 0)
        {
            return new RosterSyncOutcome(
                parsed.Members.Count,
                0,
                0,
                currentChanged,
                parsed.CapturedAt,
                $"Connected - no roster changes · {parsed.Members.Count:N0} active characters.");
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
            var queued = GetQueuedCount();
            return new RosterSyncOutcome(
                parsed.Members.Count,
                0,
                queued,
                currentChanged,
                parsed.CapturedAt,
                $"Services01 unavailable; {queued:N0} validated snapshot(s) queued for retry. {ex.Message}");
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

        var queued = GetQueuedCount();
        if (deferredReason is not null)
        {
            return new RosterSyncOutcome(
                parsed.Members.Count,
                uploaded,
                queued,
                currentChanged,
                parsed.CapturedAt,
                $"Services01 unavailable; {queued:N0} validated snapshot(s) queued for retry. {deferredReason}");
        }

        if (!currentChanged && !forceCurrentSnapshot && uploaded == 0)
        {
            return new RosterSyncOutcome(
                parsed.Members.Count,
                0,
                queued,
                false,
                parsed.CapturedAt,
                $"Connected - no roster changes · {parsed.Members.Count:N0} active characters · queue {queued:N0}.");
        }

        return new RosterSyncOutcome(
            parsed.Members.Count,
            uploaded,
            queued,
            currentChanged,
            parsed.CapturedAt,
            $"Connected - roster synchronized · {parsed.Members.Count:N0} active characters · queue {queued:N0}.");
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
}

internal sealed class SyncState
{
    public string? LastAcceptedSnapshotKey { get; set; }
    public int LastAcceptedMemberCount { get; set; }
    public DateTimeOffset? LastSuccessfulSync { get; set; }
    public long? LastServerSnapshotId { get; set; }
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
