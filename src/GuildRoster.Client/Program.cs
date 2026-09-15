namespace GuildRoster.Client;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        AppPaths.EnsureCreated();
        ClientUpdateService.CleanupStaleUpdateDirectories();
        ApplicationConfiguration.Initialize();

        using var form = new MainForm();
        using var recoveryPrompts = new RecoveryPromptService(form);
        Application.Run(form);
    }
}
