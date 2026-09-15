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
    private const string Owner = "Frostcanvas";
    private const string ReleaseRepository = "Guild-Roster-Client-Releases";
    private const string InstallerAssetName = "GuildRosterClient-Setup.exe";

    private readonly HttpClient _httpClient;

    public UpdateFeedClient()
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(45),
        };
        _httpClient.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("FrostLabsGuildRosterClient", "0.1"));
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    public async Task<RemoteClientPackage?> GetLatestAsync(
        string serverBaseUrl,
        string channel,
        CancellationToken cancellationToken = default)
    {
        _ = serverBaseUrl;

        var normalizedChannel = string.Equals(channel, "beta", StringComparison.OrdinalIgnoreCase)
            ? "beta"
            : "stable";

        var requestUri = $"https://api.github.com/repos/{Owner}/{ReleaseRepository}/releases?per_page=100";
        using var response = await _httpClient.GetAsync(requestUri, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        RemoteClientPackage? best = null;
        foreach (var release in document.RootElement.EnumerateArray())
        {
            if (IsDraft(release))
            {
                continue;
            }

            var prerelease = IsPrerelease(release);
            if (normalizedChannel == "stable" && prerelease)
            {
                continue;
            }

            var package = TryReadPackage(release, prerelease ? "beta" : "stable");
            if (package is null)
            {
                continue;
            }

            if (best is null || ReleaseVersionUtility.IsNewer(best.Version, package.Version))
            {
                best = package;
            }
        }

        return best;
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

    private static RemoteClientPackage? TryReadPackage(JsonElement release, string channel)
    {
        var tag = release.TryGetProperty("tag_name", out var tagValue)
            ? tagValue.GetString()
            : null;
        var version = ReleaseVersionUtility.Normalize(tag);
        if (string.IsNullOrWhiteSpace(version) || string.Equals(version, "unknown", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!release.TryGetProperty("assets", out var assets))
        {
            return null;
        }

        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.TryGetProperty("name", out var nameValue)
                ? nameValue.GetString()
                : null;
            if (!string.Equals(name, InstallerAssetName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var downloadUrl = asset.TryGetProperty("browser_download_url", out var urlValue)
                ? urlValue.GetString()
                : null;
            var digest = asset.TryGetProperty("digest", out var digestValue)
                ? digestValue.GetString()
                : null;
            var size = asset.TryGetProperty("size", out var sizeValue) && sizeValue.TryGetInt64(out var parsedSize)
                ? parsedSize
                : 0;
            var publishedAt = release.TryGetProperty("published_at", out var publishedValue)
                ? publishedValue.GetString()
                : null;

            if (string.IsNullOrWhiteSpace(downloadUrl) || string.IsNullOrWhiteSpace(digest))
            {
                return null;
            }

            return new RemoteClientPackage(
                channel,
                version,
                downloadUrl,
                digest,
                size,
                publishedAt ?? string.Empty);
        }

        return null;
    }

    private static bool IsDraft(JsonElement release) =>
        release.TryGetProperty("draft", out var draftValue) && draftValue.ValueKind == JsonValueKind.True;

    private static bool IsPrerelease(JsonElement release) =>
        release.TryGetProperty("prerelease", out var prereleaseValue) && prereleaseValue.ValueKind == JsonValueKind.True;

    public void Dispose() => _httpClient.Dispose();
}
