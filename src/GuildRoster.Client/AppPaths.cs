namespace GuildRoster.Client;

internal static class AppPaths
{
    public static string Root => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FrostLabs",
        "GuildRoster");

    public static string SettingsPath => Path.Combine(Root, "settings.json");
    public static string CredentialPath => Path.Combine(Root, "auth.bin");
    public static string SyncStatePath => Path.Combine(Root, "sync-state.json");
    public static string UpdatesRoot => Path.Combine(Root, "Updates");
    public static string OutboxRoot => Path.Combine(Root, "Outbox");
    public static string GrmArchiveOutboxRoot => Path.Combine(Root, "GrmArchiveOutbox");
    public static string GrmRestoreBackupRoot => Path.Combine(Root, "RestoreBackups");
    public static string GrmRestoreAuditRoot => Path.Combine(Root, "RestoreAudit");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(UpdatesRoot);
        Directory.CreateDirectory(OutboxRoot);
        Directory.CreateDirectory(GrmArchiveOutboxRoot);
        Directory.CreateDirectory(GrmRestoreBackupRoot);
        Directory.CreateDirectory(GrmRestoreAuditRoot);
    }
}
