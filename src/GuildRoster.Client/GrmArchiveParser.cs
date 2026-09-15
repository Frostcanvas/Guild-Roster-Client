using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace GuildRoster.Client;

internal static partial class GrmArchiveParser
{
    private const string CurrentMembersVariable = "GRM_GuildMemberHistory_Save";
    private const string FormerMembersVariable = "GRM_PlayersThatLeftHistory_Save";
    private const string AltGroupsVariable = "GRM_Alts";
    private const string LogReportVariable = "GRM_LogReport_Save";
    private const int MaxArchiveDepth = 200;

    [GeneratedRegex(@"(?m)^\s*(GRM_[A-Za-z0-9_]+)\s*=\s*", RegexOptions.CultureInvariant)]
    private static partial Regex AssignmentRegex();

    public static async Task<ParsedGrmArchive> ParseAsync(
        string filePath,
        ParsedGrmRoster roster,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            throw new FileNotFoundException("Guild_Roster_Manager.lua was not found.", filePath);
        }

        var text = await ReadMatchingStableFileAsync(filePath, roster.CapturedAt, cancellationToken);
        var variableNames = AssignmentRegex()
            .Matches(text)
            .Cast<Match>()
            .Select(match => match.Groups[1].Value)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        var parsedRoots = new Dictionary<string, LuaTable>(StringComparer.Ordinal);
        var variables = new Dictionary<string, object?>(StringComparer.Ordinal);
        var parseErrors = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var variableName in variableNames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var table = LuaSavedVariablesParser.ParseAssignment(text, variableName);
                parsedRoots[variableName] = table;
                variables[variableName] = ConvertLuaValue(table, 0);
            }
            catch (InvalidDataException ex)
            {
                parseErrors[variableName] = ex.Message;
            }
        }

        var guildKey = $"{roster.GuildName}-{roster.GuildRealm}";
        var profiles = BuildRestoreProfiles(parsedRoots, guildKey);
        AttachNoteHistory(parsedRoots, guildKey, profiles);

        var sourceSha256 = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
        var archiveIdentity = Encoding.UTF8.GetBytes($"grm-archive-v1|{guildKey}|{sourceSha256}");
        var archiveKey = Convert.ToHexStringLower(SHA256.HashData(archiveIdentity));

        var payload = new GrmArchivePayload
        {
            ArchiveKey = archiveKey,
            SourceSha256 = sourceSha256,
            CapturedAt = roster.CapturedAt,
            GuildName = roster.GuildName,
            GuildRealm = roster.GuildRealm,
            Variables = variables,
            ParseErrors = parseErrors,
            RestoreProfiles = profiles,
        };

        return new ParsedGrmArchive(
            payload,
            variables.Count,
            parseErrors.Count,
            profiles.Count);
    }

    private static List<GrmRestoreProfilePayload> BuildRestoreProfiles(
        IReadOnlyDictionary<string, LuaTable> parsedRoots,
        string guildKey)
    {
        LuaTable? guildGroups = null;
        if (parsedRoots.TryGetValue(AltGroupsVariable, out var altRoot) &&
            altRoot.TryGetTable(guildKey, out var foundGroups))
        {
            guildGroups = foundGroups;
        }

        var profiles = new List<GrmRestoreProfilePayload>();

        if (parsedRoots.TryGetValue(FormerMembersVariable, out var formerRoot) &&
            formerRoot.TryGetTable(guildKey, out var formerGuild))
        {
            CollectMemberProfiles(formerGuild, "former", guildGroups, profiles, "former");
        }

        if (parsedRoots.TryGetValue(CurrentMembersVariable, out var currentRoot) &&
            currentRoot.TryGetTable(guildKey, out var currentGuild))
        {
            CollectMemberProfiles(currentGuild, "current", guildGroups, profiles, "current");
        }

        var guidByFullName = profiles
            .Where(profile => !string.IsNullOrWhiteSpace(profile.CharacterName) && !string.IsNullOrWhiteSpace(profile.CharacterRealm))
            .GroupBy(
                profile => $"{profile.CharacterName}-{profile.CharacterRealm}",
                StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Last().PlayerGuid,
                StringComparer.Ordinal);

        foreach (var profile in profiles)
        {
            if (profile.RestoreData.TryGetValue("main_name", out var mainValue) &&
                mainValue is string mainName &&
                guidByFullName.TryGetValue(mainName, out var mainGuid))
            {
                profile.RestoreData["main_player_guid"] = mainGuid;
            }
        }

        return profiles;
    }

    private static void AttachNoteHistory(
        IReadOnlyDictionary<string, LuaTable> parsedRoots,
        string guildKey,
        IReadOnlyList<GrmRestoreProfilePayload> profiles)
    {
        if (!parsedRoots.TryGetValue(LogReportVariable, out var logRoot) ||
            !logRoot.TryGetTable(guildKey, out var guildLog))
        {
            return;
        }

        var byFullName = profiles
            .Where(profile => !string.IsNullOrWhiteSpace(profile.CharacterName) && !string.IsNullOrWhiteSpace(profile.CharacterRealm))
            .GroupBy(
                profile => $"{profile.CharacterName}-{profile.CharacterRealm}",
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);

        var byShortName = profiles
            .Where(profile => !string.IsNullOrWhiteSpace(profile.CharacterName))
            .GroupBy(profile => profile.CharacterName!, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Select(profile => profile.PlayerGuid).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);

        foreach (var value in guildLog.Values)
        {
            if (value is not LuaTable row || row.Values.Count < 6)
            {
                continue;
            }

            var eventType = ToInt(row.Values[0]);
            if (eventType is not (4 or 5))
            {
                continue;
            }

            var target = Normalize(StripWowFormatting(ToScalarString(row.Values[2])));
            if (target is null)
            {
                continue;
            }

            GrmRestoreProfilePayload[]? matchingProfiles = null;
            if (target.Contains('-', StringComparison.Ordinal) && byFullName.TryGetValue(target, out var fullMatches))
            {
                matchingProfiles = fullMatches;
            }
            else if (byShortName.TryGetValue(target, out var shortMatches))
            {
                matchingProfiles = shortMatches;
            }

            if (matchingProfiles is null || matchingProfiles.Length == 0)
            {
                continue;
            }

            var historyKey = eventType == 4 ? "public_note_history" : "officer_note_history";
            var eventName = eventType == 4 ? "public_note" : "officer_note";
            var oldValue = Normalize(ToScalarString(row.Values[3]));
            var newValue = Normalize(ToScalarString(row.Values[4]));
            var historyEvent = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["event_type"] = eventName,
                ["message"] = Normalize(ToScalarString(row.Values[1])),
                ["target"] = target,
                ["old_value"] = oldValue,
                ["new_value"] = newValue,
                ["occurred_at"] = ConvertLuaValue(row.Values[5], 0),
            };

            foreach (var profile in matchingProfiles)
            {
                if (!profile.RestoreData.TryGetValue(historyKey, out var existing) || existing is not List<object?> history)
                {
                    history = new List<object?>();
                    profile.RestoreData[historyKey] = history;
                }
                history.Add(historyEvent);
            }
        }

        foreach (var profile in profiles)
        {
            AddLatestRemovedNoteCandidate(profile, "public_note", "public_note_history", "public_note_recovery_candidate");
            AddLatestRemovedNoteCandidate(profile, "officer_note", "officer_note_history", "officer_note_recovery_candidate");
        }
    }

    private static void AddLatestRemovedNoteCandidate(
        GrmRestoreProfilePayload profile,
        string currentKey,
        string historyKey,
        string candidateKey)
    {
        if (profile.RestoreData.TryGetValue(currentKey, out var current) && current is string currentText && !string.IsNullOrWhiteSpace(currentText))
        {
            return;
        }
        if (!profile.RestoreData.TryGetValue(historyKey, out var historyValue) || historyValue is not List<object?> history)
        {
            return;
        }

        for (var index = history.Count - 1; index >= 0; index--)
        {
            if (history[index] is not Dictionary<string, object?> entry)
            {
                continue;
            }

            var newValue = entry.TryGetValue("new_value", out var newObject) ? Normalize(newObject as string) : null;
            if (newValue is not null)
            {
                profile.RestoreData[candidateKey] = newValue;
                return;
            }

            var oldValue = entry.TryGetValue("old_value", out var oldObject) ? Normalize(oldObject as string) : null;
            if (oldValue is not null)
            {
                profile.RestoreData[candidateKey] = oldValue;
                return;
            }
        }
    }

    private static void CollectMemberProfiles(
        LuaTable table,
        string sourceSection,
        LuaTable? guildGroups,
        List<GrmRestoreProfilePayload> destination,
        string path)
    {
        var guid = Normalize(table.GetString("GUID"));
        if (guid is not null && guid.StartsWith("Player-", StringComparison.OrdinalIgnoreCase))
        {
            destination.Add(BuildRestoreProfile(table, sourceSection, guildGroups, path, guid));
            return;
        }

        foreach (var field in table.Fields.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            if (field.Value is LuaTable child)
            {
                CollectMemberProfiles(
                    child,
                    sourceSection,
                    guildGroups,
                    destination,
                    $"{path}.fields[{field.Key}]");
            }
        }

        for (var index = 0; index < table.Values.Count; index++)
        {
            if (table.Values[index] is LuaTable child)
            {
                CollectMemberProfiles(
                    child,
                    sourceSection,
                    guildGroups,
                    destination,
                    $"{path}.values[{index}]");
            }
        }
    }

    private static GrmRestoreProfilePayload BuildRestoreProfile(
        LuaTable member,
        string sourceSection,
        LuaTable? guildGroups,
        string recordPath,
        string guid)
    {
        var fullName = Normalize(member.GetString("name"));
        SplitCharacterName(fullName, out var characterName, out var characterRealm);

        var restoreData = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["source_section"] = sourceSection,
            ["source_record_key"] = recordPath,
            ["player_guid"] = guid,
            ["character_name"] = characterName,
            ["character_realm"] = characterRealm,
            ["public_note"] = Normalize(member.GetString("note")),
            ["officer_note"] = Normalize(member.GetString("officerNote")),
            ["join_date_unknown"] = member.GetBool("joinDateUnknown"),
            ["alt_group"] = Normalize(member.GetString("altGroup")),
            ["alt_group_left"] = Normalize(member.GetString("altGroupLeft")),
            ["raw_member"] = ConvertLuaValue(member, 0),
        };

        AddTableIfPresent(member, "customNote", "custom_note", restoreData);
        if (member.TryGetTable("customNote", out var customNote) && customNote.Values.Count >= 4)
        {
            restoreData["custom_note_text"] = Normalize(ToScalarString(customNote.Values[3]));
        }
        AddTableIfPresent(member, "joinDateHist", "join_date_history", restoreData);
        AddTableIfPresent(member, "rankHist", "rank_history", restoreData);
        AddTableIfPresent(member, "birthdayInfo", "birthday_info", restoreData);
        AddTableIfPresent(member, "nicknameDetails", "nickname_details", restoreData);
        AddTableIfPresent(member, "mainAtTimeOfLeaving", "main_at_time_of_leaving", restoreData);
        AddTableIfPresent(member, "altsAtTimeOfLeaving", "alts_at_time_of_leaving", restoreData);

        var altGroup = Normalize(member.GetString("altGroup"));
        if (altGroup is null)
        {
            var altGroupLeft = member.GetInt("altGroupLeft");
            if (altGroupLeft is > 0)
            {
                altGroup = altGroupLeft.Value.ToString(CultureInfo.InvariantCulture);
            }
        }

        if (altGroup is not null && guildGroups is not null && guildGroups.TryGetTable(altGroup, out var group))
        {
            restoreData["alt_group"] = altGroup;
            restoreData["raw_alt_group"] = ConvertLuaValue(group, 0);
            var mainName = Normalize(group.GetString("main"));
            restoreData["main_name"] = mainName;
            if (fullName is not null && mainName is not null)
            {
                restoreData["relationship"] = string.Equals(fullName, mainName, StringComparison.Ordinal)
                    ? "main"
                    : "alt";
            }
            AddTableIfPresent(group, "birthdayInfo", "alt_group_birthday_info", restoreData);
            AddTableIfPresent(group, "nicknameDetails", "alt_group_nickname_details", restoreData);
        }

        return new GrmRestoreProfilePayload
        {
            PlayerGuid = guid,
            CharacterName = characterName,
            CharacterRealm = characterRealm,
            SourceSection = sourceSection,
            SourceRecordKey = recordPath,
            RestoreData = restoreData,
        };
    }

    private static void AddTableIfPresent(
        LuaTable source,
        string sourceKey,
        string destinationKey,
        IDictionary<string, object?> destination)
    {
        if (source.TryGetTable(sourceKey, out var table))
        {
            destination[destinationKey] = ConvertLuaValue(table, 0);
        }
    }

    private static object? ConvertLuaValue(object? value, int depth)
    {
        if (depth > MaxArchiveDepth)
        {
            throw new InvalidDataException("GRM archive nesting exceeded the client safety limit.");
        }

        if (value is not LuaTable table)
        {
            return value;
        }

        var fields = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var field in table.Fields.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            fields[field.Key] = ConvertLuaValue(field.Value, depth + 1);
        }

        var values = new List<object?>(table.Values.Count);
        foreach (var item in table.Values)
        {
            values.Add(ConvertLuaValue(item, depth + 1));
        }

        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["$fields"] = fields,
            ["$values"] = values,
        };
    }

    private static string StripWowFormatting(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }
        return Regex.Replace(value, @"\|c[0-9A-Fa-f]{8}|\|r", string.Empty, RegexOptions.CultureInvariant);
    }

    private static string? ToScalarString(object? value) => value switch
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

    private static void SplitCharacterName(string? fullName, out string? characterName, out string? characterRealm)
    {
        characterName = null;
        characterRealm = null;
        if (string.IsNullOrWhiteSpace(fullName))
        {
            return;
        }

        var separator = fullName.LastIndexOf('-');
        if (separator <= 0 || separator >= fullName.Length - 1)
        {
            characterName = fullName.Trim();
            return;
        }

        characterName = fullName[..separator].Trim();
        characterRealm = fullName[(separator + 1)..].Trim();
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
                throw new FileNotFoundException("Guild_Roster_Manager.lua disappeared before the full GRM archive could be read.", filePath);
            }

            if (before.LastWriteTimeUtc != expectedCapturedAt.UtcDateTime)
            {
                throw new IOException("GRM changed after the roster snapshot was parsed. The full archive will retry from the next complete save.");
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
                    bufferSize: 128 * 1024,
                    useAsync: true);
                using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
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
                throw new IOException("GRM changed during full archive parsing. The client will retry from the next complete save.");
            }
            if (text.Length == 0)
            {
                throw new InvalidDataException("Guild_Roster_Manager.lua was empty. Full archive rejected.");
            }
            return text;
        }

        throw new IOException("Guild_Roster_Manager.lua is still being written. Full GRM archive will retry after WoW finishes saving it.");
    }
}
