using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GuildRoster.Client;

internal sealed record RegistrationResult(Guid InstallationId, string BearerToken);
internal sealed record UploadResult(string Status, long SnapshotId, int? MemberCount);

internal sealed class GuildRosterApiException : Exception
{
    public GuildRosterApiException(HttpStatusCode statusCode, string message)
        : base(message)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode StatusCode { get; }
}

internal sealed class GuildRosterApiClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public GuildRosterApiClient()
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(45),
        };
        _httpClient.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("FrostLabsGuildRosterClient", "0.1"));
    }

    public async Task<RegistrationResult> EnsureRegisteredAsync(
        ClientSettings settings,
        string companionVersion,
        CancellationToken cancellationToken = default)
    {
        var existingToken = CredentialStore.LoadToken();
        if (!string.IsNullOrWhiteSpace(existingToken))
        {
            _ = Guid.TryParse(settings.InstallationId, out var existingInstallationId);
            return new RegistrationResult(existingInstallationId, existingToken);
        }

        if (!Guid.TryParse(settings.ClientInstanceId, out var clientInstanceId))
        {
            clientInstanceId = Guid.NewGuid();
            settings.ClientInstanceId = clientInstanceId.ToString("D");
            SettingsService.Save(settings);
        }

        try
        {
            return await RegisterOnceAsync(
                settings,
                clientInstanceId,
                companionVersion,
                cancellationToken);
        }
        catch (GuildRosterApiException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
        {
            // Same behavior as Azeroth Questing Companion: if local credential
            // state was lost but the old anonymous instance ID remains server-side,
            // create a fresh local instance identity without asking the user for a
            // pairing code or shared recovery secret.
            clientInstanceId = Guid.NewGuid();
            settings.ClientInstanceId = clientInstanceId.ToString("D");
            settings.InstallationId = null;
            SettingsService.Save(settings);
            return await RegisterOnceAsync(
                settings,
                clientInstanceId,
                companionVersion,
                cancellationToken);
        }
    }

    private async Task<RegistrationResult> RegisterOnceAsync(
        ClientSettings settings,
        Guid clientInstanceId,
        string companionVersion,
        CancellationToken cancellationToken)
    {
        var server = NormalizeServer(settings.ServerBaseUrl);
        var body = new AutoRegistrationRequest
        {
            ClientInstanceId = clientInstanceId,
            Label = Environment.MachineName,
            CompanionVersion = companionVersion,
        };

        using var response = await _httpClient.PostAsJsonAsync(
            $"{server}/api/v1/installations/auto-register",
            body,
            _jsonOptions,
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var result = await response.Content.ReadFromJsonAsync<RegistrationResponse>(_jsonOptions, cancellationToken)
            ?? throw new InvalidDataException("Services01 returned an empty registration response.");
        if (result.InstallationId == Guid.Empty || string.IsNullOrWhiteSpace(result.BearerToken))
        {
            throw new InvalidDataException("Services01 returned an incomplete registration response.");
        }

        CredentialStore.SaveToken(result.BearerToken);
        settings.InstallationId = result.InstallationId.ToString("D");
        SettingsService.Save(settings);
        return new RegistrationResult(result.InstallationId, result.BearerToken);
    }

    public async Task<UploadResult> UploadSnapshotAsync(
        string serverBaseUrl,
        string bearerToken,
        RosterSnapshotPayload payload,
        CancellationToken cancellationToken = default)
    {
        var server = NormalizeServer(serverBaseUrl);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{server}/api/v1/roster/snapshots")
        {
            Content = JsonContent.Create(payload, options: _jsonOptions),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var result = await response.Content.ReadFromJsonAsync<UploadResponse>(_jsonOptions, cancellationToken)
            ?? throw new InvalidDataException("Services01 returned an empty roster-upload response.");

        return new UploadResult(result.Status ?? "unknown", result.SnapshotId, result.MemberCount);
    }

    public async Task<bool> IsHealthyAsync(
        string serverBaseUrl,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var server = NormalizeServer(serverBaseUrl);
            using var response = await _httpClient.GetAsync($"{server}/api/v1/status", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    public static void ClearRegistration(ClientSettings settings)
    {
        CredentialStore.Clear();
        settings.InstallationId = null;
        SettingsService.Save(settings);
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string message = $"Services01 returned HTTP {(int)response.StatusCode}.";
        try
        {
            var error = await response.Content.ReadFromJsonAsync<ApiError>(_jsonOptions, cancellationToken);
            if (!string.IsNullOrWhiteSpace(error?.Detail))
            {
                message = error.Detail;
            }
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
        {
            // Keep the status-code message when the response is not JSON.
        }

        throw new GuildRosterApiException(response.StatusCode, message);
    }

    private static string NormalizeServer(string serverBaseUrl)
    {
        if (string.IsNullOrWhiteSpace(serverBaseUrl))
        {
            throw new ArgumentException("Services01 URL is required.", nameof(serverBaseUrl));
        }

        var value = serverBaseUrl.Trim().TrimEnd('/');
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("Services01 URL must be an absolute HTTP or HTTPS URL.", nameof(serverBaseUrl));
        }

        return value;
    }

    public void Dispose() => _httpClient.Dispose();

    private sealed class AutoRegistrationRequest
    {
        [JsonPropertyName("client_instance_id")]
        public Guid ClientInstanceId { get; init; }

        [JsonPropertyName("label")]
        public string? Label { get; init; }

        [JsonPropertyName("companion_version")]
        public string? CompanionVersion { get; init; }
    }

    private sealed class RegistrationResponse
    {
        [JsonPropertyName("installation_id")]
        public Guid InstallationId { get; init; }

        [JsonPropertyName("bearer_token")]
        public string? BearerToken { get; init; }
    }

    private sealed class UploadResponse
    {
        [JsonPropertyName("status")]
        public string? Status { get; init; }

        [JsonPropertyName("snapshot_id")]
        public long SnapshotId { get; init; }

        [JsonPropertyName("member_count")]
        public int? MemberCount { get; init; }
    }

    private sealed class ApiError
    {
        [JsonPropertyName("detail")]
        public string? Detail { get; init; }
    }
}
