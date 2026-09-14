namespace GuildRoster.Client;

internal static class AppPaths
{
    public static string Root => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FrostLabs",
        "GuildRoster");

    public static string SettingsPath => Path.Combine(Root, "settings.json");
    public static string UpdatesRoot => Path.Combine(Root, "Updates");
    public static string OutboxRoot => Path.Combine(Root, "Outbox");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(UpdatesRoot);
        Directory.CreateDirectory(OutboxRoot);
    }
}
