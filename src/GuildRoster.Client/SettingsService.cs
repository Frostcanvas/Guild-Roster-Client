using System.Text.Json;

namespace GuildRoster.Client;

internal sealed class ClientSettings
{
    public string? SourceSavedVariablesPath { get; set; }
    public string ServerBaseUrl { get; set; } = "http://10.0.10.246:8767";
    public string UpdateChannel { get; set; } = "stable";
}

internal static class SettingsService
{
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
            return JsonSerializer.Deserialize<ClientSettings>(json, JsonOptions) ?? new ClientSettings();
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
