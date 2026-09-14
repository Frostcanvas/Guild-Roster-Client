namespace GuildRoster.Client;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        AppPaths.EnsureCreated();
        ClientUpdateService.CleanupStaleUpdateDirectories();
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
