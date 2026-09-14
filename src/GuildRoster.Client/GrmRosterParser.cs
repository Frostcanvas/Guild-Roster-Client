using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GuildRoster.Client;

internal static class GrmRosterParser
{
    private const string CurrentRosterVariable = "GRM_GuildMemberHistory_Save";
    private const int MaxExpectedMembers = 1000;

    private static readonly JsonSerializerOptions HashJsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    public static async Task<ParsedGrmRoster> ParseAsync(
        string filePath,
        string guildName,
        string guildRealm,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            throw new FileNotFoundException("Guild_Roster_Manager.lua was not found.", filePath);
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(guildName);
        ArgumentException.ThrowIfNullOrWhiteSpace(guildRealm);

        var stableFile = await ReadStableFileAsync(filePath, cancellationToken);
        var root = LuaSavedVariablesParser.ParseAssignment(stableFile.Text, CurrentRosterVariable);
        var guildKey = $"{guildName.Trim()}-{guildRealm.Trim()}";
        if (!root.TryGetTable(guildKey, out var guildTable))
        {
            throw new InvalidDataException($"GRM current-roster data does not contain '{guildKey}'.");
        }

        if (guildTable.Fields.Count == 0)
        {
            throw new InvalidDataException("GRM returned an empty current guild roster. Empty snapshots are rejected for safety.");
        }
        if (guildTable.Fields.Count > MaxExpectedMembers)
        {
            throw new InvalidDataException($"GRM returned {guildTable.Fields.Count:N0} members, above the client safety limit of {MaxExpectedMembers:N0}.");
        }

        var members = new List<GrmRosterMember>(guildTable.Fields.Count);
        var seenGuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in guildTable.Fields.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (entry.Value is not LuaTable memberTable)
            {
                throw new InvalidDataException($"GRM roster entry '{entry.Key}' was not a member table. Snapshot rejected.");
            }

            var member = ParseMember(entry.Key, memberTable);
            if (!seenGuids.Add(member.PlayerGuid))
            {
                throw new InvalidDataException($"GRM current roster contains duplicate player GUID '{member.PlayerGuid}'. Snapshot rejected.");
            }
            members.Add(member);
        }

        if (members.Count != guildTable.Fields.Count)
        {
            throw new InvalidDataException("Not every GRM current-roster entry could be normalized. Snapshot rejected.");
        }

        members.Sort((left, right) => string.Compare(left.PlayerGuid, right.PlayerGuid, StringComparison.Ordinal));
        var snapshotKey = ComputeSnapshotKey(guildName.Trim(), guildRealm.Trim(), members);

        return new ParsedGrmRoster(
            guildName.Trim(),
            guildRealm.Trim(),
            stableFile.LastWriteTimeUtc,
            snapshotKey,
            members);
    }

    private static GrmRosterMember ParseMember(string entryKey, LuaTable table)
    {
        var guid = Normalize(table.GetString("GUID"));
        if (guid is null || !guid.StartsWith("Player-", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"GRM roster entry '{entryKey}' is missing a valid Blizzard player GUID.");
        }

        var fullName = Normalize(table.GetString("name")) ?? entryKey.Trim();
        var separator = fullName.LastIndexOf('-');
        if (separator <= 0 || separator >= fullName.Length - 1)
        {
            throw new InvalidDataException($"GRM roster entry '{entryKey}' does not contain a character-realm name.");
        }

        var characterName = fullName[..separator].Trim();
        var characterRealm = fullName[(separator + 1)..].Trim();
        if (characterName.Length == 0 || characterRealm.Length == 0)
        {
            throw new InvalidDataException($"GRM roster entry '{entryKey}' has an invalid character-realm name.");
        }

        var level = table.GetInt("level");
        if (level is <= 0 or > 1000)
        {
            throw new InvalidDataException($"GRM roster entry '{entryKey}' has an invalid level.");
        }

        var rankIndex = table.GetInt("rankIndex");
        if (rankIndex is < 0 or > 100)
        {
            throw new InvalidDataException($"GRM roster entry '{entryKey}' has an invalid rank index.");
        }

        return new GrmRosterMember(
            guid,
            characterName,
            characterRealm,
            Limit(Normalize(table.GetString("class")), 32),
            level,
            Limit(Normalize(table.GetString("race")), 64),
            NormalizeFaction(table.GetInt("faction")),
            Limit(Normalize(table.GetString("rankName")), 128),
            rankIndex,
            Limit(Normalize(table.GetString("note")), 2048),
            Limit(Normalize(table.GetString("officerNote")), 2048),
            table.GetBool("isOnline"),
            table.GetBool("isMobile"),
            Limit(Normalize(table.GetString("altGroup")), 64),
            table.GetInt("sex"),
            table.GetInt("guildRep"),
            table.GetInt("MythicScore"));
    }

    public static RosterSnapshotPayload ToPayload(ParsedGrmRoster roster, string companionVersion)
    {
        return new RosterSnapshotPayload
        {
            ClientSnapshotKey = roster.SnapshotKey,
            CapturedAt = roster.CapturedAt,
            GuildName = roster.GuildName,
            GuildRealm = roster.GuildRealm,
            CompanionVersion = companionVersion,
            WowProjectId = 1,
            Members = roster.Members.Select(member => new RosterMemberPayload
            {
                PlayerGuid = member.PlayerGuid,
                Name = member.Name,
                Realm = member.Realm,
                ClassToken = member.ClassToken,
                Level = member.Level,
                RaceToken = member.RaceToken,
                Faction = member.Faction,
                RankName = member.RankName,
                RankIndex = member.RankIndex,
                PublicNote = member.PublicNote,
                OfficerNote = member.OfficerNote,
                IsOnline = member.IsOnline,
                IsMobile = member.IsMobile,
            }).ToList(),
        };
    }

    private static string ComputeSnapshotKey(
        string guildName,
        string guildRealm,
        IReadOnlyList<GrmRosterMember> members)
    {
        var canonical = new
        {
            protocol = "FGR1",
            guild_name = guildName,
            guild_realm = guildRealm,
            members = members.Select(member => new
            {
                player_guid = member.PlayerGuid,
                name = member.Name,
                realm = member.Realm,
                class_token = member.ClassToken,
                level = member.Level,
                race_token = member.RaceToken,
                faction = member.Faction,
                rank_name = member.RankName,
                rank_index = member.RankIndex,
                public_note = member.PublicNote,
                officer_note = member.OfficerNote,
                is_online = member.IsOnline,
                is_mobile = member.IsMobile,
            }).ToArray(),
        };

        var json = JsonSerializer.Serialize(canonical, HashJsonOptions);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }

    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }

    private static string? Limit(string? value, int maxLength)
    {
        if (value is null || value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength];
    }

    private static string? NormalizeFaction(int? value) => value switch
    {
        0 => "Horde",
        1 => "Alliance",
        null => null,
        _ => value.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };

    private static async Task<StableFile> ReadStableFileAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= 6; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var before = new FileInfo(filePath);
            before.Refresh();
            if (!before.Exists)
            {
                throw new FileNotFoundException("Guild_Roster_Manager.lua disappeared before it could be read.", filePath);
            }

            var expectedLength = before.Length;
            var expectedWrite = before.LastWriteTimeUtc;
            await Task.Delay(300, cancellationToken);

            var settled = new FileInfo(filePath);
            settled.Refresh();
            if (!settled.Exists || settled.Length != expectedLength || settled.LastWriteTimeUtc != expectedWrite)
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
                using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                text = await reader.ReadToEndAsync(cancellationToken);
            }
            catch (IOException) when (attempt < 6)
            {
                continue;
            }

            var after = new FileInfo(filePath);
            after.Refresh();
            if (!after.Exists || after.Length != expectedLength || after.LastWriteTimeUtc != expectedWrite)
            {
                continue;
            }

            if (text.Length == 0)
            {
                throw new InvalidDataException("Guild_Roster_Manager.lua was empty. Snapshot rejected.");
            }

            return new StableFile(text, new DateTimeOffset(after.LastWriteTimeUtc));
        }

        throw new IOException("Guild_Roster_Manager.lua is still being written. The client will retry after WoW finishes saving it.");
    }

    private sealed record StableFile(string Text, DateTimeOffset LastWriteTimeUtc);
}
