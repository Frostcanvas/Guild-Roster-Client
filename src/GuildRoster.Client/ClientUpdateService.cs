using System.Diagnostics;
using System.Security.Cryptography;

namespace GuildRoster.Client;

internal sealed class ClientUpdateService
{
    private readonly UpdateFeedClient _feedClient;

    public ClientUpdateService(UpdateFeedClient feedClient)
    {
        _feedClient = feedClient;
    }

    public async Task StageAndLaunchUpdateAsync(
        RemoteClientPackage package,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        AppPaths.EnsureCreated();

        var updateRoot = Path.Combine(AppPaths.UpdatesRoot, package.Version);
        var installerPath = Path.Combine(updateRoot, "GuildRosterClient-Setup.exe");
        Directory.CreateDirectory(updateRoot);

        progress?.Report($"Downloading Guild Roster Client {package.Version}...");
        await _feedClient.DownloadAsync(package.DownloadUrl, installerPath, cancellationToken: cancellationToken);
        VerifyDigest(installerPath, package.Sha256);

        if (package.Size > 0)
        {
            var actualSize = new FileInfo(installerPath).Length;
            if (actualSize != package.Size)
            {
                throw new InvalidDataException("The downloaded Guild Roster Client installer size did not match the update manifest.");
            }
        }

        progress?.Report("Starting the Windows installer. Approve the Windows permission prompt if it appears.");

        var startInfo = new ProcessStartInfo
        {
            FileName = installerPath,
            UseShellExecute = true,
            Verb = "runas",
            WorkingDirectory = updateRoot,
        };
        startInfo.ArgumentList.Add("/VERYSILENT");
        startInfo.ArgumentList.Add("/SUPPRESSMSGBOXES");
        startInfo.ArgumentList.Add("/NORESTART");
        startInfo.ArgumentList.Add("/CLOSEAPPLICATIONS");
        startInfo.ArgumentList.Add("/RESTARTAPPLICATIONS");

        var process = Process.Start(startInfo);
        if (process is null)
        {
            throw new InvalidOperationException("Windows did not start the Guild Roster Client installer.");
        }
    }

    public static void CleanupStaleUpdateDirectories()
    {
        if (!Directory.Exists(AppPaths.UpdatesRoot))
        {
            return;
        }

        foreach (var directory in Directory.EnumerateDirectories(AppPaths.UpdatesRoot))
        {
            try
            {
                var created = Directory.GetCreationTimeUtc(directory);
                if (DateTime.UtcNow - created > TimeSpan.FromDays(7))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
            catch
            {
                // Update cleanup is best effort and must never block startup.
            }
        }
    }

    private static void VerifyDigest(string filePath, string digest)
    {
        var expected = digest.Trim();
        if (expected.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
        {
            expected = expected["sha256:".Length..].Trim();
        }
        if (expected.Length != 64 || expected.Any(value => !Uri.IsHexDigit(value)))
        {
            throw new InvalidDataException("The Guild Roster Client update manifest did not contain a valid SHA-256 digest.");
        }

        using var stream = File.OpenRead(filePath);
        var actual = Convert.ToHexString(SHA256.HashData(stream));
        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The downloaded Guild Roster Client installer failed SHA-256 verification.");
        }
    }
}
