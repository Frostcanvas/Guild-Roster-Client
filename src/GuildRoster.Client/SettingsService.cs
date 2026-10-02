using System.Text.Json;

namespace GuildRoster.Client;

internal sealed class ClientSettings
{
    public string? SourceSavedVariablesPath { get; set; }
    public string ServerBaseUrl { get; set; } = SettingsService.DefaultServerBaseUrl;
    public string UpdateChannel { get; set; } = "stable";
    public string GuildName { get; set; } = "Hogwarts Academy";
    public string GuildRealm { get; set; } = "BleedingHollow";
    public string? ClientInstanceId { get; set; }
    public string? InstallationId { get; set; }
    public bool AutoSync { get; set; } = true;
}

internal static class SettingsService
{
    public const string DefaultServerBaseUrl = "http://10.1.10.246:8767";
    private const string LegacyServerBaseUrl = "http://10.0.10.246:8767";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public static ClientSettings Load()
    {
        AppPaths.EnsureCreated();
        if (!File.Exists(AppPaths.SettingsPath))
        {
            return new ClientSettings();
        }

        try
        {
            var json = File.ReadAllText(AppPaths.SettingsPath);
            var settings = JsonSerializer.Deserialize<ClientSettings>(json, JsonOptions) ?? new ClientSettings();
            var normalizedServerBaseUrl = settings.ServerBaseUrl?.Trim().TrimEnd('/');

            if (string.IsNullOrWhiteSpace(normalizedServerBaseUrl) ||
                string.Equals(normalizedServerBaseUrl, LegacyServerBaseUrl, StringComparison.OrdinalIgnoreCase))
            {
                settings.ServerBaseUrl = DefaultServerBaseUrl;
                Save(settings);
            }

            return settings;
        }
        catch
        {
            return new ClientSettings();
        }
    }

    public static void Save(ClientSettings settings)
    {
        AppPaths.EnsureCreated();
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(AppPaths.SettingsPath, json);
    }
}
