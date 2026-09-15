using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GuildRoster.Client;

internal sealed record GrmArchiveUploadResult(
    string Status,
    long ArchiveId,
    int RestoreProfileCount,
    int RecoveryOfferCount);

internal sealed record RecoveryOffer(
    long Id,
    string PlayerGuid,
    string Reason,
    DateTimeOffset DetectedAt,
    JsonElement ArchivedValues,
    JsonElement CurrentValues);

internal static class GrmArchiveApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static async Task<GrmArchiveUploadResult> UploadAsync(
        string serverBaseUrl,
        string bearerToken,
        GrmArchivePayload payload,
        CancellationToken cancellationToken = default)
    {
        var server = NormalizeServer(serverBaseUrl);
        using var httpClient = CreateClient(TimeSpan.FromMinutes(2));
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{server}/api/v1/grm/archive")
        {
            Content = JsonContent.Create(payload, options: JsonOptions),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, "uploading the full GRM archive", cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<ArchiveUploadResponse>(JsonOptions, cancellationToken)
            ?? throw new InvalidDataException("Services01 returned an empty GRM archive response.");
        return new GrmArchiveUploadResult(
            body.Status ?? "unknown",
            body.ArchiveId,
            body.RestoreProfileCount,
            body.RecoveryOfferCount);
    }

    public static async Task<IReadOnlyList<RecoveryOffer>> GetPendingRecoveryOffersAsync(
        string serverBaseUrl,
        string bearerToken,
        CancellationToken cancellationToken = default)
    {
        var server = NormalizeServer(serverBaseUrl);
        using var httpClient = CreateClient(TimeSpan.FromSeconds(45));
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{server}/api/v1/roster/recovery-offers");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, "checking returning-member recovery offers", cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<RecoveryOfferResponse>(JsonOptions, cancellationToken)
            ?? throw new InvalidDataException("Services01 returned an empty recovery-offer response.");

        return body.Offers.Select(row => new RecoveryOffer(
            row.Id,
            row.PlayerGuid ?? string.Empty,
            row.Reason ?? string.Empty,
            row.DetectedAt,
            row.ArchivedValues,
            row.CurrentValues)).ToArray();
    }

    public static Task DecideRecoveryOfferAsync(
        string serverBaseUrl,
        string bearerToken,
        long offerId,
        string decision,
        CancellationToken cancellationToken = default) =>
        DecideRecoveryOfferAsync(
            serverBaseUrl,
            bearerToken,
            offerId,
            decision,
            Array.Empty<string>(),
            cancellationToken);

    public static async Task DecideRecoveryOfferAsync(
        string serverBaseUrl,
        string bearerToken,
        long offerId,
        string decision,
        IReadOnlyList<string> selectedFields,
        CancellationToken cancellationToken = default)
    {
        var server = NormalizeServer(serverBaseUrl);
        using var httpClient = CreateClient(TimeSpan.FromSeconds(45));
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{server}/api/v1/roster/recovery-offers/{offerId}/decision")
        {
            Content = JsonContent.Create(
                new RecoveryDecisionRequest
                {
                    Decision = decision,
                    SelectedFields = selectedFields.ToList(),
                },
                options: JsonOptions),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, "recording the returning-member recovery decision", cancellationToken);
    }

    private static HttpClient CreateClient(TimeSpan timeout)
    {
        var httpClient = new HttpClient { Timeout = timeout };
        httpClient.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("FrostLabsGuildRosterClient", "0.1"));
        return httpClient;
    }

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        string operation,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var message = $"Services01 returned HTTP {(int)response.StatusCode} while {operation}.";
        try
        {
            var error = await response.Content.ReadFromJsonAsync<ApiError>(JsonOptions, cancellationToken);
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

    private sealed class ArchiveUploadResponse
    {
        [JsonPropertyName("status")]
        public string? Status { get; init; }

        [JsonPropertyName("archive_id")]
        public long ArchiveId { get; init; }

        [JsonPropertyName("restore_profile_count")]
        public int RestoreProfileCount { get; init; }

        [JsonPropertyName("recovery_offer_count")]
        public int RecoveryOfferCount { get; init; }
    }

    private sealed class RecoveryOfferResponse
    {
        [JsonPropertyName("offers")]
        public List<RecoveryOfferWire> Offers { get; init; } = new();
    }

    private sealed class RecoveryOfferWire
    {
        [JsonPropertyName("id")]
        public long Id { get; init; }

        [JsonPropertyName("player_guid")]
        public string? PlayerGuid { get; init; }

        [JsonPropertyName("reason")]
        public string? Reason { get; init; }

        [JsonPropertyName("detected_at")]
        public DateTimeOffset DetectedAt { get; init; }

        [JsonPropertyName("archived_values")]
        public JsonElement ArchivedValues { get; init; }

        [JsonPropertyName("current_values")]
        public JsonElement CurrentValues { get; init; }
    }

    private sealed class RecoveryDecisionRequest
    {
        [JsonPropertyName("decision")]
        public required string Decision { get; init; }

        [JsonPropertyName("selected_fields")]
        public required List<string> SelectedFields { get; init; }
    }

    private sealed class ApiError
    {
        [JsonPropertyName("detail")]
        public string? Detail { get; init; }
    }
}
