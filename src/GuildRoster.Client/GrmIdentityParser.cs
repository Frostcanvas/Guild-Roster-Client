using System.Globalization;

namespace GuildRoster.Client;

internal static class GrmIdentityParser
{
    private const string AltGroupsVariable = "GRM_Alts";
    private const string MemberHistoryVariable = "GRM_GuildMemberHistory_Save";

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

        LuaTable? guildMembers = null;
        try
        {
            var memberRoot = LuaSavedVariablesParser.ParseAssignment(text, MemberHistoryVariable);
            memberRoot.TryGetTable(guildKey, out guildMembers!);
        }
        catch (InvalidDataException)
        {
            guildMembers = null;
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
            var metadata = GetMetadata(guildMembers, fullName, member.PlayerGuid);
            var altGroup = Normalize(member.AltGroup);
            if (altGroup is null)
            {
                identityMembers.Add(new GrmIdentityMember(
                    member.PlayerGuid,
                    "unknown",
                    null,
                    null,
                    metadata.Present,
                    metadata.CustomNote,
                    metadata.JoinDate,
                    metadata.JoinDateHistory));
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
                altGroup,
                metadata.Present,
                metadata.CustomNote,
                metadata.JoinDate,
                metadata.JoinDateHistory));
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
                GrmMetadataPresent = row.GrmMetadataPresent,
                CustomNote = row.CustomNote,
                JoinDate = row.JoinDate,
                JoinDateHistory = row.JoinDateHistory.Select(item => new RosterJoinDateHistoryPayload
                {
                    Day = item.Day,
                    Month = item.Month,
                    Year = item.Year,
                    DateKey = item.DateKey,
                    Epoch = item.Epoch,
                    Confirmed = item.Confirmed,
                    EventType = item.EventType,
                }).ToList(),
            }).ToList(),
        };

        var mainCount = identityMembers.Count(row => row.Relationship == "main");
        var altCount = identityMembers.Count(row => row.Relationship == "alt");
        var unknownCount = identityMembers.Count - mainCount - altCount;
        return new GrmIdentityParseResult(payload, mainCount, altCount, unknownCount);
    }

    private static (bool Present, string? CustomNote, string? JoinDate, IReadOnlyList<GrmJoinDateHistoryEntry> JoinDateHistory) GetMetadata(
        LuaTable? guildMembers,
        string fullName,
        string expectedGuid)
    {
        if (guildMembers is null || !guildMembers.TryGetTable(fullName, out var memberTable))
        {
            return (false, null, null, Array.Empty<GrmJoinDateHistoryEntry>());
        }

        var sourceGuid = Normalize(memberTable.GetString("GUID"));
        if (!string.Equals(sourceGuid, expectedGuid, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"GRM metadata GUID for '{fullName}' did not match the validated roster GUID. Identity sync was blocked.");
        }

        string? customNote = null;
        if (memberTable.TryGetTable("customNote", out var customTable) && customTable.Values.Count >= 4)
        {
            customNote = Normalize(ToStringValue(customTable.Values[3]));
            if (customNote is { Length: > 2048 })
            {
                customNote = customNote[..2048];
            }
        }

        var history = new List<GrmJoinDateHistoryEntry>();
        if (memberTable.TryGetTable("joinDateHist", out var historyTable))
        {
            foreach (var value in historyTable.Values)
            {
                if (value is not LuaTable row || row.Values.Count < 7)
                {
                    continue;
                }

                var day = ToInt(row.Values[0]);
                var month = ToInt(row.Values[1]);
                var year = ToInt(row.Values[2]);
                var dateKey = Normalize(ToStringValue(row.Values[3]));
                var eventType = ToInt(row.Values[6]);
                if (day is null || month is null || year is null || eventType is null || dateKey is null)
                {
                    continue;
                }

                history.Add(new GrmJoinDateHistoryEntry(
                    day.Value,
                    month.Value,
                    year.Value,
                    dateKey,
                    ToLong(row.Values[4]),
                    ToBool(row.Values[5]),
                    eventType.Value));
            }
        }

        string? joinDate = null;
        if (!memberTable.GetBool("joinDateUnknown") && history.Count > 0)
        {
            var current = history[0];
            if (current.EventType == 2 && history.Count > 1)
            {
                current = history[1];
            }
            if (TryFormatDate(current.Day, current.Month, current.Year, out var formatted))
            {
                joinDate = formatted;
            }
        }

        return (true, customNote, joinDate, history);
    }

    private static bool TryFormatDate(int day, int month, int year, out string value)
    {
        try
        {
            value = new DateOnly(year, month, day).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            value = string.Empty;
            return false;
        }
    }

    private static string? ToStringValue(object? value) => value switch
    {
        string text => text,
        long integer => integer.ToString(CultureInfo.InvariantCulture),
        double number => number.ToString(CultureInfo.InvariantCulture),
        bool boolean => boolean ? "true" : "false",
        _ => null,
    };

    private static int? ToInt(object? value) => value switch
    {
        long integer when integer >= int.MinValue && integer <= int.MaxValue => (int)integer,
        double number when number >= int.MinValue && number <= int.MaxValue => (int)Math.Round(number),
        string text when int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
        _ => null,
    };

    private static long? ToLong(object? value) => value switch
    {
        long integer => integer,
        double number when number >= long.MinValue && number <= long.MaxValue => (long)Math.Round(number),
        string text when long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
        _ => null,
    };

    private static bool ToBool(object? value) => value switch
    {
        bool boolean => boolean,
        long integer => integer != 0,
        double number => Math.Abs(number) > double.Epsilon,
        string text when bool.TryParse(text, out var parsed) => parsed,
        _ => false,
    };

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
