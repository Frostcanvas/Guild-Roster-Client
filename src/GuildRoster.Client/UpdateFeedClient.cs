using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace GuildRoster.Client;

internal sealed record RemoteClientPackage(
    string Channel,
    string Version,
    string DownloadUrl,
    string Sha256,
    long Size,
    string PublishedAt);

internal sealed class UpdateFeedClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public UpdateFeedClient()
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(45),
        };
        _httpClient.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("FrostLabsGuildRosterClient", "0.1"));
    }

    public async Task<RemoteClientPackage?> GetLatestAsync(
        string serverBaseUrl,
        string channel,
        CancellationToken cancellationToken = default)
    {
        var normalizedChannel = string.Equals(channel, "beta", StringComparison.OrdinalIgnoreCase)
            ? "beta"
            : "stable";

        var stable = await TryGetChannelAsync(serverBaseUrl, "stable", cancellationToken);
        if (normalizedChannel == "stable")
        {
            return stable;
        }

        var beta = await TryGetChannelAsync(serverBaseUrl, "beta", cancellationToken);
        if (stable is null)
        {
            return beta;
        }
        if (beta is null)
        {
            return stable;
        }

        return ReleaseVersionUtility.Compare(beta.Version, stable.Version) >= 0 ? beta : stable;
    }

    public async Task DownloadAsync(
        string url,
        string destination,
        IProgress<long>? progress = null,
        CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(
            url,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
        var buffer = new byte[1024 * 128];
        long total = 0;
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            total += read;
            progress?.Report(total);
        }
    }

    private async Task<RemoteClientPackage?> TryGetChannelAsync(
        string serverBaseUrl,
        string channel,
        CancellationToken cancellationToken)
    {
        var baseUri = new Uri(serverBaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
        var requestUri = new Uri(baseUri, $"api/v1/client-updates/latest?channel={Uri.EscapeDataString(channel)}");

        using var response = await _httpClient.GetAsync(requestUri, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var wire = await JsonSerializer.DeserializeAsync<UpdateManifestWire>(stream, _jsonOptions, cancellationToken);
        if (wire is null || string.IsNullOrWhiteSpace(wire.Version) || string.IsNullOrWhiteSpace(wire.DownloadUrl))
        {
            throw new InvalidDataException("The Guild Roster Client update feed returned an incomplete manifest.");
        }

        var downloadUri = Uri.TryCreate(wire.DownloadUrl, UriKind.Absolute, out var absolute)
            ? absolute
            : new Uri(baseUri, wire.DownloadUrl.TrimStart('/'));

        return new RemoteClientPackage(
            wire.Channel ?? channel,
            ReleaseVersionUtility.Normalize(wire.Version),
            downloadUri.ToString(),
            wire.Sha256 ?? string.Empty,
            wire.Size,
            wire.PublishedAt ?? string.Empty);
    }

    public void Dispose() => _httpClient.Dispose();

    private sealed class UpdateManifestWire
    {
        public string? Channel { get; set; }
        public string? Version { get; set; }
        public string? DownloadUrl { get; set; }
        public string? Sha256 { get; set; }
        public long Size { get; set; }
        public string? PublishedAt { get; set; }
    }
}
