using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GuildRoster.Client;

internal sealed record IdentityUploadResult(
    string Status,
    int MemberCount,
    int MainCount,
    int AltCount,
    int UnknownCount);

internal static class RosterIdentityApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static async Task<IdentityUploadResult> UploadAsync(
        string serverBaseUrl,
        string bearerToken,
        RosterIdentityPayload payload,
        CancellationToken cancellationToken = default)
    {
        var server = NormalizeServer(serverBaseUrl);
        using var httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(45),
        };
        httpClient.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("FrostLabsGuildRosterClient", "0.1"));

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{server}/api/v1/roster/identity-links")
        {
            Content = JsonContent.Create(payload, options: JsonOptions),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            string message = $"Services01 returned HTTP {(int)response.StatusCode} while syncing main/alt identity.";
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

        var result = await response.Content.ReadFromJsonAsync<IdentityUploadResponse>(JsonOptions, cancellationToken)
            ?? throw new InvalidDataException("Services01 returned an empty main/alt identity response.");

        await RecoveryPromptService.PromptPendingAsync(
            serverBaseUrl,
            bearerToken,
            cancellationToken);

        return new IdentityUploadResult(
            result.Status ?? "unknown",
            result.MemberCount,
            result.MainCount,
            result.AltCount,
            result.UnknownCount);
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

    private sealed class IdentityUploadResponse
    {
        [JsonPropertyName("status")]
        public string? Status { get; init; }

        [JsonPropertyName("member_count")]
        public int MemberCount { get; init; }

        [JsonPropertyName("main_count")]
        public int MainCount { get; init; }

        [JsonPropertyName("alt_count")]
        public int AltCount { get; init; }

        [JsonPropertyName("unknown_count")]
        public int UnknownCount { get; init; }
    }

    private sealed class ApiError
    {
        [JsonPropertyName("detail")]
        public string? Detail { get; init; }
    }
}
