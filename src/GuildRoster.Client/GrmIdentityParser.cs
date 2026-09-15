namespace GuildRoster.Client;

internal static class GrmIdentityParser
{
    private const string AltGroupsVariable = "GRM_Alts";

    public static async Task<GrmIdentityParseResult> ParseAsync(
        string filePath,
        ParsedGrmRoster roster,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            throw new FileNotFoundException("Guild_Roster_Manager.lua was not found.", filePath);
        }

        var text = await ReadMatchingStableFileAsync(filePath, roster.CapturedAt, cancellationToken);
        var root = LuaSavedVariablesParser.ParseAssignment(text, AltGroupsVariable);
        var guildKey = $"{roster.GuildName}-{roster.GuildRealm}";
        if (!root.TryGetTable(guildKey, out var guildGroups))
        {
            throw new InvalidDataException($"GRM main/alt data does not contain '{guildKey}'. Identity sync was blocked.");
        }

        var byFullName = roster.Members.ToDictionary(
            member => $"{member.Name}-{member.Realm}",
            member => member,
            StringComparer.Ordinal);

        var identityMembers = new List<GrmIdentityMember>(roster.Members.Count);
        var referencedGroups = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (var member in roster.Members)
        {
            var fullName = $"{member.Name}-{member.Realm}";
            var altGroup = Normalize(member.AltGroup);
            if (altGroup is null)
            {
                identityMembers.Add(new GrmIdentityMember(member.PlayerGuid, "unknown", null, null));
                continue;
            }

            if (!guildGroups.TryGetTable(altGroup, out var group))
            {
                throw new InvalidDataException($"GRM alt group '{altGroup}' for '{fullName}' was not found. Identity sync was blocked.");
            }

            var mainName = Normalize(group.GetString("main"));
            if (mainName is null)
            {
                throw new InvalidDataException($"GRM alt group '{altGroup}' does not identify a main character. Identity sync was blocked.");
            }
            if (!byFullName.TryGetValue(mainName, out var mainMember))
            {
                throw new InvalidDataException($"GRM alt group '{altGroup}' main '{mainName}' is not in the current Hogwarts Academy roster. Identity sync was blocked.");
            }
            if (!string.Equals(Normalize(mainMember.AltGroup), altGroup, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"GRM alt group '{altGroup}' main '{mainName}' points to a different current alt group. Identity sync was blocked.");
            }

            var groupNames = GetGroupNames(group);
            if (!groupNames.Contains(fullName))
            {
                throw new InvalidDataException($"GRM alt group '{altGroup}' does not contain current member '{fullName}'. Identity sync was blocked.");
            }
            if (!groupNames.Contains(mainName))
            {
                throw new InvalidDataException($"GRM alt group '{altGroup}' does not contain its declared main '{mainName}'. Identity sync was blocked.");
            }

            if (!referencedGroups.TryGetValue(altGroup, out var assignedNames))
            {
                assignedNames = new HashSet<string>(StringComparer.Ordinal);
                referencedGroups[altGroup] = assignedNames;
            }
            assignedNames.Add(fullName);

            var relationship = string.Equals(fullName, mainName, StringComparison.Ordinal)
                ? "main"
                : "alt";
            identityMembers.Add(new GrmIdentityMember(
                member.PlayerGuid,
                relationship,
                mainMember.PlayerGuid,
                altGroup));
        }

        foreach (var (altGroup, assignedNames) in referencedGroups)
        {
            if (!guildGroups.TryGetTable(altGroup, out var group))
            {
                throw new InvalidDataException($"GRM alt group '{altGroup}' disappeared during validation. Identity sync was blocked.");
            }
            var groupNames = GetGroupNames(group);
            if (!groupNames.SetEquals(assignedNames))
            {
                throw new InvalidDataException($"GRM alt group '{altGroup}' does not exactly match the current roster assignments. Identity sync was blocked.");
            }
        }

        identityMembers.Sort((left, right) => string.Compare(left.PlayerGuid, right.PlayerGuid, StringComparison.Ordinal));
        var payload = new RosterIdentityPayload
        {
            GuildName = roster.GuildName,
            GuildRealm = roster.GuildRealm,
            CapturedAt = roster.CapturedAt,
            Members = identityMembers.Select(row => new RosterIdentityMemberPayload
            {
                PlayerGuid = row.PlayerGuid,
                Relationship = row.Relationship,
                MainPlayerGuid = row.MainPlayerGuid,
                AltGroup = row.AltGroup,
            }).ToList(),
        };

        var mainCount = identityMembers.Count(row => row.Relationship == "main");
        var altCount = identityMembers.Count(row => row.Relationship == "alt");
        var unknownCount = identityMembers.Count - mainCount - altCount;
        return new GrmIdentityParseResult(payload, mainCount, altCount, unknownCount);
    }

    private static HashSet<string> GetGroupNames(LuaTable group)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in group.Values)
        {
            if (value is not LuaTable row)
            {
                continue;
            }
            var name = Normalize(row.GetString("name"));
            if (name is not null)
            {
                names.Add(name);
            }
        }
        return names;
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static async Task<string> ReadMatchingStableFileAsync(
        string filePath,
        DateTimeOffset expectedCapturedAt,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= 6; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var before = new FileInfo(filePath);
            before.Refresh();
            if (!before.Exists)
            {
                throw new FileNotFoundException("Guild_Roster_Manager.lua disappeared before main/alt data could be read.", filePath);
            }

            if (before.LastWriteTimeUtc != expectedCapturedAt.UtcDateTime)
            {
                throw new IOException("GRM changed after the roster snapshot was parsed. The client will retry main/alt identity sync from the next complete save.");
            }

            var expectedLength = before.Length;
            await Task.Delay(250, cancellationToken);

            var settled = new FileInfo(filePath);
            settled.Refresh();
            if (!settled.Exists || settled.Length != expectedLength || settled.LastWriteTimeUtc != before.LastWriteTimeUtc)
            {
                continue;
            }

            string text;
            try
            {
                await using var stream = new FileStream(
                    filePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete,
                    bufferSize: 64 * 1024,
                    useAsync: true);
                using var reader = new StreamReader(stream, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                text = await reader.ReadToEndAsync(cancellationToken);
            }
            catch (IOException) when (attempt < 6)
            {
                continue;
            }

            var after = new FileInfo(filePath);
            after.Refresh();
            if (!after.Exists || after.Length != expectedLength || after.LastWriteTimeUtc != before.LastWriteTimeUtc)
            {
                continue;
            }
            if (after.LastWriteTimeUtc != expectedCapturedAt.UtcDateTime)
            {
                throw new IOException("GRM changed during main/alt identity parsing. The client will retry from the next complete save.");
            }
            return text;
        }

        throw new IOException("Guild_Roster_Manager.lua is still being written. Main/alt identity sync will retry after WoW finishes saving it.");
    }
}
