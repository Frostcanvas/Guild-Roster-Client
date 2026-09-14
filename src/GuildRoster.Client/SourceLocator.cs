namespace GuildRoster.Client;

internal sealed record GrmSourceCandidate(string SavedVariablesDirectory, string FilePath);

internal static class SourceLocator
{
    private const string GrmFileName = "Guild_Roster_Manager.lua";

    private static readonly string[] CommonWowRoots =
    {
        @"D:\Battle.net\World of Warcraft",
        @"C:\Program Files (x86)\World of Warcraft",
        @"C:\Program Files\World of Warcraft",
    };

    public static IReadOnlyList<GrmSourceCandidate> FindCandidates(string? configuredDirectory = null)
    {
        var results = new Dictionary<string, GrmSourceCandidate>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(configuredDirectory))
        {
            AddDirectory(results, configuredDirectory.Trim());
        }

        foreach (var wowRoot in CommonWowRoots)
        {
            var accountRoot = Path.Combine(wowRoot, "_retail_", "WTF", "Account");
            if (!Directory.Exists(accountRoot))
            {
                continue;
            }

            try
            {
                foreach (var accountDirectory in Directory.EnumerateDirectories(accountRoot))
                {
                    AddDirectory(results, Path.Combine(accountDirectory, "SavedVariables"));
                }
            }
            catch (UnauthorizedAccessException)
            {
                // A blocked candidate root should not stop discovery of another installation.
            }
            catch (IOException)
            {
                // A temporarily unavailable drive should not stop discovery.
            }
        }

        return results.Values
            .OrderBy(candidate => candidate.SavedVariablesDirectory, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static bool IsValidSavedVariablesDirectory(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return false;
        }

        return File.Exists(Path.Combine(directory.Trim(), GrmFileName));
    }

    public static string GetGrmFilePath(string savedVariablesDirectory) =>
        Path.Combine(savedVariablesDirectory, GrmFileName);

    private static void AddDirectory(
        IDictionary<string, GrmSourceCandidate> results,
        string directory)
    {
        try
        {
            var fullDirectory = Path.GetFullPath(directory);
            var filePath = Path.Combine(fullDirectory, GrmFileName);
            if (!File.Exists(filePath))
            {
                return;
            }

            results[fullDirectory] = new GrmSourceCandidate(fullDirectory, filePath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            // Invalid configured paths are handled by the UI as an unavailable source.
        }
    }
}
