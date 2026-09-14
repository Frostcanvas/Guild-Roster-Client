namespace GuildRoster.Client;

internal static class ReleaseVersionUtility
{
    public static string Normalize(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return "unknown";
        }

        var value = version.Trim();
        foreach (var prefix in new[] { "client-v", "v" })
        {
            if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                value = value[prefix.Length..];
                break;
            }
        }

        var buildMetadata = value.IndexOf('+');
        if (buildMetadata >= 0)
        {
            value = value[..buildMetadata];
        }

        return value;
    }

    public static bool IsNewer(string currentVersion, string candidateVersion) =>
        Compare(candidateVersion, currentVersion) > 0;

    public static int Compare(string? left, string? right)
    {
        var leftParts = Parse(left);
        var rightParts = Parse(right);

        for (var i = 0; i < Math.Max(leftParts.Core.Length, rightParts.Core.Length); i++)
        {
            var leftValue = i < leftParts.Core.Length ? leftParts.Core[i] : 0;
            var rightValue = i < rightParts.Core.Length ? rightParts.Core[i] : 0;
            var comparison = leftValue.CompareTo(rightValue);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        if (leftParts.PreRelease.Count == 0 && rightParts.PreRelease.Count == 0)
        {
            return 0;
        }
        if (leftParts.PreRelease.Count == 0)
        {
            return 1;
        }
        if (rightParts.PreRelease.Count == 0)
        {
            return -1;
        }

        var max = Math.Max(leftParts.PreRelease.Count, rightParts.PreRelease.Count);
        for (var i = 0; i < max; i++)
        {
            if (i >= leftParts.PreRelease.Count)
            {
                return -1;
            }
            if (i >= rightParts.PreRelease.Count)
            {
                return 1;
            }

            var leftToken = leftParts.PreRelease[i];
            var rightToken = rightParts.PreRelease[i];
            var leftNumeric = int.TryParse(leftToken, out var leftNumber);
            var rightNumeric = int.TryParse(rightToken, out var rightNumber);

            if (leftNumeric && rightNumeric)
            {
                var comparison = leftNumber.CompareTo(rightNumber);
                if (comparison != 0)
                {
                    return comparison;
                }
                continue;
            }
            if (leftNumeric != rightNumeric)
            {
                return leftNumeric ? -1 : 1;
            }

            var textComparison = string.Compare(leftToken, rightToken, StringComparison.OrdinalIgnoreCase);
            if (textComparison != 0)
            {
                return textComparison;
            }
        }

        return 0;
    }

    private static ParsedVersion Parse(string? version)
    {
        var normalized = Normalize(version);
        var dash = normalized.IndexOf('-');
        var coreText = dash >= 0 ? normalized[..dash] : normalized;
        var preText = dash >= 0 ? normalized[(dash + 1)..] : string.Empty;

        var core = coreText
            .Split('.', StringSplitOptions.RemoveEmptyEntries)
            .Select(value => int.TryParse(value, out var number) ? number : 0)
            .ToArray();

        var pre = preText
            .Split('.', StringSplitOptions.RemoveEmptyEntries)
            .ToList();

        return new ParsedVersion(core, pre);
    }

    private sealed record ParsedVersion(int[] Core, List<string> PreRelease);
}
