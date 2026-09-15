using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GuildRoster.Client;

internal sealed class GrmRestoreFieldResult
{
    public string Field { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

internal sealed class GrmRestoreExecutionResult
{
    public long OfferId { get; set; }
    public string PlayerGuid { get; set; } = string.Empty;
    public DateTimeOffset CompletedAt { get; set; }
    public bool Completed { get; set; }
    public bool AlreadyApplied { get; set; }
    public string? SourcePath { get; set; }
    public string? BackupDirectory { get; set; }
    public string? SourceSha256Before { get; set; }
    public string? SourceSha256After { get; set; }
    public List<GrmRestoreFieldResult> Fields { get; set; } = new();
}

internal static class GrmRestoreEngine
{
    private const string CurrentMembersVariable = "GRM_GuildMemberHistory_Save";
    private const string AltGroupsVariable = "GRM_Alts";

    private static readonly HashSet<string> KnownFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "public_note",
        "officer_note",
        "custom_note",
        "join_date_history",
        "main_alt_relationship",
        "birthday",
        "nickname",
    };

    private static readonly HashSet<string> LocalWriteFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "custom_note",
        "join_date_history",
        "main_alt_relationship",
        "birthday",
        "nickname",
    };

    private static readonly JsonSerializerOptions AuditJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public static async Task<GrmRestoreExecutionResult> ApplyAsync(
        ClientSettings settings,
        RecoveryOffer offer,
        IReadOnlyList<string> selectedFields,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(selectedFields);

        AppPaths.EnsureCreated();
        var existingAudit = TryLoadCompletedAudit(offer.Id, offer.PlayerGuid);
        if (existingAudit is not null)
        {
            existingAudit.AlreadyApplied = true;
            return existingAudit;
        }

        if (!SourceLocator.IsValidSavedVariablesDirectory(settings.SourceSavedVariablesPath))
        {
            throw new InvalidOperationException(
                "The configured GRM SavedVariables source is unavailable. Select the GRM source before restoring archived data.");
        }

        var grmFile = SourceLocator.GetGrmFilePath(settings.SourceSavedVariablesPath!);
        if (!File.Exists(grmFile))
        {
            throw new FileNotFoundException("Guild_Roster_Manager.lua was not found.", grmFile);
        }

        var selected = selectedFields
            .Where(field => !string.IsNullOrWhiteSpace(field))
            .Select(field => field.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (selected.Length == 0)
        {
            throw new InvalidOperationException("No archived fields were selected for restore.");
        }

        var requiresLocalWrite = selected.Any(field => LocalWriteFields.Contains(field));
        if (requiresLocalWrite && IsWorldOfWarcraftRunning())
        {
            throw new InvalidOperationException(
                "Close World of Warcraft before restoring GRM-local data. WoW rewrites SavedVariables while it is running, so FrostLabs will not edit Guild_Roster_Manager.lua until the game is closed.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var originalText = await File.ReadAllTextAsync(grmFile, cancellationToken);
        var sourceHashBefore = Sha256(originalText);
        var guildKey = $"{settings.GuildName}-{settings.GuildRealm}";

        var currentRoot = LuaSavedVariablesParser.ParseAssignment(originalText, CurrentMembersVariable);
        if (!currentRoot.TryGetTable(guildKey, out var currentGuild))
        {
            throw new InvalidDataException($"GRM current-member data for '{guildKey}' was not found.");
        }

        var (memberKey, member) = FindCurrentMemberByGuid(currentGuild, offer.PlayerGuid);
        var fullName = member.GetString("name") ?? memberKey;
        if (string.IsNullOrWhiteSpace(fullName))
        {
            throw new InvalidDataException(
                "The returning character record did not contain a usable current character name.");
        }

        var archivedAltGroup = GetArchivedString(offer.ArchivedValues, "alt_group");
        var needsGroupContext = selected.Any(field =>
            string.Equals(field, "main_alt_relationship", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(field, "birthday", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(field, "nickname", StringComparison.OrdinalIgnoreCase));

        LuaTable? altRoot = null;
        LuaTable? guildGroups = null;
        LuaTable? targetGroup = null;
        if (needsGroupContext && !string.IsNullOrWhiteSpace(archivedAltGroup))
        {
            try
            {
                altRoot = LuaSavedVariablesParser.ParseAssignment(originalText, AltGroupsVariable);
                if (altRoot.TryGetTable(guildKey, out var foundGroups))
                {
                    guildGroups = foundGroups;
                    if (foundGroups.TryGetTable(archivedAltGroup!, out var foundGroup))
                    {
                        targetGroup = foundGroup;
                    }
                }
            }
            catch (InvalidDataException)
            {
                // Member-level fields can still be restored. Relationship/group fields fail closed to review.
            }
        }

        var fieldResults = new List<GrmRestoreFieldResult>();
        var memberChanged = false;
        var groupChanged = false;

        foreach (var field in selected)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!KnownFields.Contains(field))
            {
                fieldResults.Add(Result(
                    field,
                    "needs_review",
                    "The selected field is not in the approved restore allowlist."));
                continue;
            }

            if (string.Equals(field, "public_note", StringComparison.OrdinalIgnoreCase))
            {
                fieldResults.Add(Result(
                    field,
                    "manual_required",
                    "Public Note write-back is intentionally not automated on Retail. The recovered value stays available for Review Notes (X)."));
                continue;
            }

            if (string.Equals(field, "officer_note", StringComparison.OrdinalIgnoreCase))
            {
                fieldResults.Add(Result(
                    field,
                    "manual_required",
                    "Officer Note write-back is intentionally not automated on Retail. The recovered value stays available for Review Notes (X)."));
                continue;
            }

            if (string.Equals(field, "custom_note", StringComparison.OrdinalIgnoreCase))
            {
                if (TryGetArchivedLuaTable(offer.ArchivedValues, "custom_note", out var customNote))
                {
                    member.SetField("customNote", customNote);
                    memberChanged = true;
                    fieldResults.Add(Result(
                        field,
                        "restored",
                        "GRM custom-note state restored from the archived GUID profile."));
                }
                else
                {
                    fieldResults.Add(Result(
                        field,
                        "needs_review",
                        "No archived GRM custom-note table was available."));
                }
                continue;
            }

            if (string.Equals(field, "join_date_history", StringComparison.OrdinalIgnoreCase))
            {
                if (TryGetArchivedLuaTable(offer.ArchivedValues, "join_date_history", out var archivedJoinHistory))
                {
                    MergeJoinDateHistory(member, archivedJoinHistory);
                    if (TryGetArchivedBool(
                            offer.ArchivedValues,
                            "join_date_unknown",
                            out var archivedJoinDateUnknown) &&
                        !archivedJoinDateUnknown)
                    {
                        // Never downgrade a currently known history back to unknown.
                        member.SetField("joinDateUnknown", false);
                    }
                    memberChanged = true;
                    fieldResults.Add(Result(
                        field,
                        "restored",
                        "Archived GRM join history was merged with the current history so the new rejoin evidence is preserved."));
                }
                else
                {
                    fieldResults.Add(Result(
                        field,
                        "needs_review",
                        "No archived GRM join-date history was available."));
                }
                continue;
            }

            if (string.Equals(field, "main_alt_relationship", StringComparison.OrdinalIgnoreCase))
            {
                var relationshipResult = RestoreMainAltRelationship(
                    member,
                    targetGroup,
                    offer.ArchivedValues,
                    archivedAltGroup,
                    fullName,
                    out var relationshipMemberChanged,
                    out var relationshipGroupChanged);
                memberChanged |= relationshipMemberChanged;
                groupChanged |= relationshipGroupChanged;
                fieldResults.Add(relationshipResult);
                continue;
            }

            if (string.Equals(field, "birthday", StringComparison.OrdinalIgnoreCase))
            {
                var restored = false;
                if (TryGetArchivedLuaTable(offer.ArchivedValues, "birthday_info", out var birthdayInfo))
                {
                    member.SetField("birthdayInfo", birthdayInfo);
                    memberChanged = true;
                    restored = true;
                }
                if (targetGroup is not null &&
                    TryGetArchivedLuaTable(
                        offer.ArchivedValues,
                        "alt_group_birthday_info",
                        out var groupBirthday))
                {
                    targetGroup.SetField("birthdayInfo", groupBirthday);
                    targetGroup.SetField("timeModified", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                    groupChanged = true;
                    restored = true;
                }
                fieldResults.Add(restored
                    ? Result(
                        field,
                        "restored",
                        "GRM birthday data restored from archived member/group evidence.")
                    : Result(
                        field,
                        "needs_review",
                        "No archived birthday structure was available for safe restore."));
                continue;
            }

            if (string.Equals(field, "nickname", StringComparison.OrdinalIgnoreCase))
            {
                var restored = false;
                if (TryGetArchivedLuaTable(
                        offer.ArchivedValues,
                        "nickname_details",
                        out var nicknameDetails))
                {
                    member.SetField("nicknameDetails", nicknameDetails);
                    memberChanged = true;
                    restored = true;
                }
                if (targetGroup is not null &&
                    TryGetArchivedLuaTable(
                        offer.ArchivedValues,
                        "alt_group_nickname_details",
                        out var groupNickname))
                {
                    targetGroup.SetField("nicknameDetails", groupNickname);
                    targetGroup.SetField("timeModified", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                    groupChanged = true;
                    restored = true;
                }
                fieldResults.Add(restored
                    ? Result(
                        field,
                        "restored",
                        "GRM nickname data restored from archived member/group evidence.")
                    : Result(
                        field,
                        "needs_review",
                        "No archived nickname structure was available for safe restore."));
            }
        }

        var updatedText = originalText;
        string? backupDirectory = null;

        if (memberChanged || groupChanged)
        {
            var replacements = new List<TextReplacement>();
            if (memberChanged)
            {
                var currentRootSpan = FindAssignmentTableSpan(originalText, CurrentMembersVariable);
                var currentGuildSpan = FindDirectChildTableSpan(originalText, currentRootSpan, guildKey);
                var memberSpan = FindDirectChildTableSpan(originalText, currentGuildSpan, memberKey);
                replacements.Add(new TextReplacement(
                    memberSpan.OpenBraceIndex,
                    memberSpan.Length,
                    SerializeLuaTable(member)));
            }

            if (groupChanged)
            {
                if (altRoot is null ||
                    guildGroups is null ||
                    targetGroup is null ||
                    string.IsNullOrWhiteSpace(archivedAltGroup))
                {
                    throw new InvalidDataException(
                        "A GRM alt-group change was prepared without a valid current alt-group table.");
                }
                var altRootSpan = FindAssignmentTableSpan(originalText, AltGroupsVariable);
                var altGuildSpan = FindDirectChildTableSpan(originalText, altRootSpan, guildKey);
                var groupSpan = FindDirectChildTableSpan(originalText, altGuildSpan, archivedAltGroup!);
                replacements.Add(new TextReplacement(
                    groupSpan.OpenBraceIndex,
                    groupSpan.Length,
                    SerializeLuaTable(targetGroup)));
            }

            foreach (var replacement in replacements.OrderByDescending(item => item.Start))
            {
                updatedText = updatedText.Remove(replacement.Start, replacement.Length)
                    .Insert(replacement.Start, replacement.Replacement);
            }

            // Fail closed if our emitted SavedVariables cannot be read back by the safe parser.
            _ = LuaSavedVariablesParser.ParseAssignment(updatedText, CurrentMembersVariable);
            if (groupChanged)
            {
                _ = LuaSavedVariablesParser.ParseAssignment(updatedText, AltGroupsVariable);
            }

            // Abort if something external changed the live file after our original read.
            var liveBeforeWrite = await File.ReadAllTextAsync(grmFile, cancellationToken);
            if (!string.Equals(
                    Sha256(liveBeforeWrite),
                    sourceHashBefore,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Guild_Roster_Manager.lua changed while the restore was being prepared. No restore was written; review the latest GRM data and try again.");
            }

            backupDirectory = CreateBackup(grmFile, offer.Id);
            var tempPath = grmFile + ".frostlabs-restore.tmp";
            try
            {
                await File.WriteAllTextAsync(
                    tempPath,
                    updatedText,
                    new UTF8Encoding(false),
                    cancellationToken);
                File.Move(tempPath, grmFile, true);
            }
            catch
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
                if (!File.Exists(grmFile) && backupDirectory is not null)
                {
                    var backupFile = Path.Combine(
                        backupDirectory,
                        Path.GetFileName(grmFile));
                    if (File.Exists(backupFile))
                    {
                        File.Copy(backupFile, grmFile, true);
                    }
                }
                throw;
            }
        }

        var result = new GrmRestoreExecutionResult
        {
            OfferId = offer.Id,
            PlayerGuid = offer.PlayerGuid,
            CompletedAt = DateTimeOffset.UtcNow,
            Completed = true,
            AlreadyApplied = false,
            SourcePath = grmFile,
            BackupDirectory = backupDirectory,
            SourceSha256Before = sourceHashBefore,
            SourceSha256After = Sha256(updatedText),
            Fields = fieldResults,
        };
        SaveAudit(result);
        return result;
    }

    private static void MergeJoinDateHistory(LuaTable member, LuaTable archivedHistory)
    {
        if (!member.TryGetTable("joinDateHist", out var currentHistory))
        {
            member.SetField("joinDateHist", archivedHistory);
            return;
        }

        // Archived events are older evidence; keep their source order first, then append
        // any current-only events (including the latest rejoin) without duplication.
        var merged = new LuaTable();
        var fingerprints = new HashSet<string>(StringComparer.Ordinal);

        foreach (var value in archivedHistory.Values)
        {
            AddUniqueHistoryValue(merged, fingerprints, value);
        }
        foreach (var value in currentHistory.Values)
        {
            AddUniqueHistoryValue(merged, fingerprints, value);
        }

        foreach (var field in archivedHistory.Fields)
        {
            merged.SetField(field.Key, field.Value);
        }
        foreach (var field in currentHistory.Fields)
        {
            // Current GRM state wins for keyed metadata while positional event history is merged.
            merged.SetField(field.Key, field.Value);
        }

        member.SetField("joinDateHist", merged);
    }

    private static void AddUniqueHistoryValue(
        LuaTable destination,
        ISet<string> fingerprints,
        object? value)
    {
        var fingerprint = SerializeLuaValueToString(value);
        if (fingerprints.Add(fingerprint))
        {
            destination.AddValue(value);
        }
    }

    private static string SerializeLuaValueToString(object? value)
    {
        var builder = new StringBuilder();
        SerializeLuaValue(builder, value, 0);
        return builder.ToString();
    }

    private static GrmRestoreFieldResult RestoreMainAltRelationship(
        LuaTable member,
        LuaTable? targetGroup,
        JsonElement archived,
        string? archivedAltGroup,
        string fullName,
        out bool memberChanged,
        out bool groupChanged)
    {
        memberChanged = false;
        groupChanged = false;
        var relationship = GetArchivedString(archived, "relationship");
        var archivedMain = GetArchivedString(archived, "main_name");

        if (string.IsNullOrWhiteSpace(archivedAltGroup) ||
            string.IsNullOrWhiteSpace(relationship) ||
            string.IsNullOrWhiteSpace(archivedMain))
        {
            return Result(
                "main_alt_relationship",
                "needs_review",
                "Archived main/alt evidence was incomplete; no relationship was guessed.");
        }
        if (targetGroup is null)
        {
            return Result(
                "main_alt_relationship",
                "needs_review",
                "The archived alt group no longer exists in current GRM data; FrostLabs did not recreate it automatically.");
        }

        var currentAltGroup = member.GetString("altGroup");
        if (!string.IsNullOrWhiteSpace(currentAltGroup) &&
            !string.Equals(currentAltGroup, archivedAltGroup, StringComparison.Ordinal))
        {
            return Result(
                "main_alt_relationship",
                "needs_review",
                $"Current GRM alt group '{currentAltGroup}' conflicts with archived group '{archivedAltGroup}'.");
        }

        var currentMain = targetGroup.GetString("main");
        if (!string.IsNullOrWhiteSpace(currentMain) &&
            !string.Equals(currentMain, archivedMain, StringComparison.OrdinalIgnoreCase))
        {
            return Result(
                "main_alt_relationship",
                "needs_review",
                $"Current GRM group main '{currentMain}' conflicts with archived main '{archivedMain}'.");
        }

        if (string.Equals(relationship, "main", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(fullName, archivedMain, StringComparison.OrdinalIgnoreCase))
        {
            return Result(
                "main_alt_relationship",
                "needs_review",
                "Archived relationship says Main, but the archived main name does not match this character.");
        }
        if (string.Equals(relationship, "alt", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(fullName, archivedMain, StringComparison.OrdinalIgnoreCase))
        {
            return Result(
                "main_alt_relationship",
                "needs_review",
                "Archived relationship says Alt, but the archived main name is this same character.");
        }

        member.SetField("altGroup", archivedAltGroup);
        memberChanged = true;

        if (string.IsNullOrWhiteSpace(currentMain))
        {
            targetGroup.SetField("main", archivedMain);
            groupChanged = true;
        }

        var memberAlreadyPresent = targetGroup.Values
            .OfType<LuaTable>()
            .Any(entry => string.Equals(
                entry.GetString("name"),
                fullName,
                StringComparison.OrdinalIgnoreCase));
        if (!memberAlreadyPresent)
        {
            var groupMember = new LuaTable();
            groupMember.SetField("name", fullName);
            var classToken = member.GetString("class");
            if (!string.IsNullOrWhiteSpace(classToken))
            {
                groupMember.SetField("class", classToken);
            }
            targetGroup.AddValue(groupMember);
            groupChanged = true;
        }

        if (groupChanged)
        {
            targetGroup.SetField("timeModified", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        }

        return Result(
            "main_alt_relationship",
            "restored",
            $"GRM relationship restored to group {archivedAltGroup} with main {archivedMain}.");
    }

    private static (string Key, LuaTable Member) FindCurrentMemberByGuid(
        LuaTable currentGuild,
        string playerGuid)
    {
        foreach (var field in currentGuild.Fields)
        {
            if (field.Value is LuaTable member &&
                string.Equals(
                    member.GetString("GUID"),
                    playerGuid,
                    StringComparison.OrdinalIgnoreCase))
            {
                return (field.Key, member);
            }
        }
        throw new InvalidDataException(
            $"The current Hogwarts Academy GRM roster does not contain exact Player GUID '{playerGuid}'. No restore was applied.");
    }

    private static bool TryGetArchivedLuaTable(
        JsonElement archived,
        string propertyName,
        out LuaTable table)
    {
        table = null!;
        if (archived.ValueKind != JsonValueKind.Object ||
            !archived.TryGetProperty(propertyName, out var element) ||
            element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }
        var converted = ConvertJsonValue(element);
        if (converted is not LuaTable found)
        {
            return false;
        }
        table = found;
        return true;
    }

    private static object? ConvertJsonValue(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
            {
                var table = new LuaTable();
                if (element.TryGetProperty("$values", out var values) &&
                    values.ValueKind == JsonValueKind.Array)
                {
                    foreach (var value in values.EnumerateArray())
                    {
                        table.AddValue(ConvertJsonValue(value));
                    }
                }
                if (element.TryGetProperty("$fields", out var fields) &&
                    fields.ValueKind == JsonValueKind.Object)
                {
                    foreach (var property in fields.EnumerateObject())
                    {
                        table.SetField(property.Name, ConvertJsonValue(property.Value));
                    }
                    return table;
                }

                foreach (var property in element.EnumerateObject())
                {
                    if (property.Name is "$fields" or "$values")
                    {
                        continue;
                    }
                    table.SetField(property.Name, ConvertJsonValue(property.Value));
                }
                return table;
            }
            case JsonValueKind.Array:
            {
                var table = new LuaTable();
                foreach (var value in element.EnumerateArray())
                {
                    table.AddValue(ConvertJsonValue(value));
                }
                return table;
            }
            case JsonValueKind.String:
                return element.GetString();
            case JsonValueKind.Number:
                if (element.TryGetInt64(out var integer))
                {
                    return integer;
                }
                return element.GetDouble();
            case JsonValueKind.True:
                return true;
            case JsonValueKind.False:
                return false;
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                return null;
            default:
                throw new InvalidDataException(
                    $"Unsupported archived JSON value kind '{element.ValueKind}'.");
        }
    }

    private static string? GetArchivedString(JsonElement archived, string name)
    {
        if (archived.ValueKind != JsonValueKind.Object ||
            !archived.TryGetProperty(name, out var value))
        {
            return null;
        }
        if (value.ValueKind == JsonValueKind.String)
        {
            return Normalize(value.GetString());
        }
        if (value.ValueKind == JsonValueKind.Number)
        {
            return value.GetRawText();
        }
        return null;
    }

    private static bool TryGetArchivedBool(JsonElement archived, string name, out bool value)
    {
        value = false;
        if (archived.ValueKind != JsonValueKind.Object ||
            !archived.TryGetProperty(name, out var element))
        {
            return false;
        }
        if (element.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            value = element.GetBoolean();
            return true;
        }
        return false;
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string CreateBackup(string grmFile, long offerId)
    {
        AppPaths.EnsureCreated();
        var directory = Path.Combine(
            AppPaths.GrmRestoreBackupRoot,
            $"{DateTime.Now:yyyyMMdd-HHmmssfff}-offer-{offerId}");
        Directory.CreateDirectory(directory);
        File.Copy(
            grmFile,
            Path.Combine(directory, Path.GetFileName(grmFile)),
            false);

        var siblingBak = grmFile + ".bak";
        if (File.Exists(siblingBak))
        {
            File.Copy(
                siblingBak,
                Path.Combine(directory, Path.GetFileName(siblingBak)),
                false);
        }
        return directory;
    }

    private static void SaveAudit(GrmRestoreExecutionResult result)
    {
        AppPaths.EnsureCreated();
        var path = GetAuditPath(result.OfferId);
        var json = JsonSerializer.Serialize(result, AuditJsonOptions);
        var temp = path + ".tmp";
        File.WriteAllText(temp, json, new UTF8Encoding(false));
        File.Move(temp, path, true);
    }

    private static GrmRestoreExecutionResult? TryLoadCompletedAudit(
        long offerId,
        string playerGuid)
    {
        var path = GetAuditPath(offerId);
        if (!File.Exists(path))
        {
            return null;
        }
        try
        {
            var audit = JsonSerializer.Deserialize<GrmRestoreExecutionResult>(
                File.ReadAllText(path),
                AuditJsonOptions);
            if (audit is null ||
                !audit.Completed ||
                !string.Equals(
                    audit.PlayerGuid,
                    playerGuid,
                    StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
            return audit;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string GetAuditPath(long offerId) =>
        Path.Combine(AppPaths.GrmRestoreAuditRoot, $"offer-{offerId}.json");

    private static bool IsWorldOfWarcraftRunning()
    {
        foreach (var name in new[] { "Wow", "WowT", "WowClassic", "WowClassicT" })
        {
            Process[] processes;
            try
            {
                processes = Process.GetProcessesByName(name);
            }
            catch
            {
                continue;
            }
            try
            {
                if (processes.Length > 0)
                {
                    return true;
                }
            }
            finally
            {
                foreach (var process in processes)
                {
                    process.Dispose();
                }
            }
        }
        return false;
    }

    private static string Sha256(string text) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private static GrmRestoreFieldResult Result(
        string field,
        string status,
        string message) => new()
    {
        Field = field,
        Status = status,
        Message = message,
    };

    private readonly record struct TextReplacement(
        int Start,
        int Length,
        string Replacement);

    private readonly record struct TableSpan(int OpenBraceIndex, int CloseBraceIndex)
    {
        public int Length => CloseBraceIndex - OpenBraceIndex + 1;
    }

    private static TableSpan FindAssignmentTableSpan(string text, string variableName)
    {
        var match = Regex.Match(
            text,
            $@"(?m)^\s*{Regex.Escape(variableName)}\s*=\s*",
            RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            throw new InvalidDataException(
                $"SavedVariables assignment '{variableName}' was not found while preparing restore write-back.");
        }

        var open = SkipWhitespace(text, match.Index + match.Length);
        if (open >= text.Length || text[open] != '{')
        {
            throw new InvalidDataException(
                $"SavedVariables assignment '{variableName}' did not start with a Lua table.");
        }
        return new TableSpan(open, FindMatchingBrace(text, open));
    }

    private static TableSpan FindDirectChildTableSpan(
        string text,
        TableSpan parent,
        string wantedKey)
    {
        var depth = 0;
        var index = parent.OpenBraceIndex + 1;
        while (index < parent.CloseBraceIndex)
        {
            var current = text[index];
            if (current is '"' or '\'')
            {
                index = SkipString(text, index);
                continue;
            }
            if (current == '-' &&
                index + 1 < parent.CloseBraceIndex &&
                text[index + 1] == '-')
            {
                index = SkipComment(text, index);
                continue;
            }
            if (current == '{')
            {
                depth++;
                index++;
                continue;
            }
            if (current == '}')
            {
                if (depth > 0)
                {
                    depth--;
                }
                index++;
                continue;
            }

            if (depth == 0 &&
                current == '[' &&
                TryReadBracketStringKey(
                    text,
                    index,
                    out var key,
                    out var afterKey))
            {
                var cursor = SkipWhitespace(text, afterKey);
                if (cursor < parent.CloseBraceIndex && text[cursor] == '=')
                {
                    cursor = SkipWhitespace(text, cursor + 1);
                    if (cursor < parent.CloseBraceIndex &&
                        text[cursor] == '{' &&
                        string.Equals(key, wantedKey, StringComparison.Ordinal))
                    {
                        return new TableSpan(cursor, FindMatchingBrace(text, cursor));
                    }
                }
                index = afterKey;
                continue;
            }
            index++;
        }

        throw new InvalidDataException(
            $"GRM table key '{wantedKey}' was not found at the expected SavedVariables level.");
    }

    private static bool TryReadBracketStringKey(
        string text,
        int start,
        out string key,
        out int afterKey)
    {
        key = string.Empty;
        afterKey = start + 1;
        if (start + 3 >= text.Length ||
            text[start] != '[' ||
            text[start + 1] is not ('"' or '\''))
        {
            return false;
        }

        var quote = text[start + 1];
        var builder = new StringBuilder();
        var index = start + 2;
        while (index < text.Length)
        {
            var current = text[index++];
            if (current == quote)
            {
                if (index < text.Length && text[index] == ']')
                {
                    key = builder.ToString();
                    afterKey = index + 1;
                    return true;
                }
                return false;
            }
            if (current == '\\' && index < text.Length)
            {
                var escaped = text[index++];
                builder.Append(escaped switch
                {
                    'n' => '\n',
                    'r' => '\r',
                    't' => '\t',
                    _ => escaped,
                });
            }
            else
            {
                builder.Append(current);
            }
        }
        return false;
    }

    private static int FindMatchingBrace(string text, int openBrace)
    {
        var depth = 0;
        var index = openBrace;
        while (index < text.Length)
        {
            var current = text[index];
            if (current is '"' or '\'')
            {
                index = SkipString(text, index);
                continue;
            }
            if (current == '-' &&
                index + 1 < text.Length &&
                text[index + 1] == '-')
            {
                index = SkipComment(text, index);
                continue;
            }
            if (current == '{')
            {
                depth++;
            }
            else if (current == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return index;
                }
            }
            index++;
        }
        throw new InvalidDataException(
            "Unbalanced Lua table braces were found while preparing restore write-back.");
    }

    private static int SkipString(string text, int start)
    {
        var quote = text[start];
        var index = start + 1;
        while (index < text.Length)
        {
            var current = text[index++];
            if (current == '\\' && index < text.Length)
            {
                index++;
                continue;
            }
            if (current == quote)
            {
                return index;
            }
        }
        return text.Length;
    }

    private static int SkipComment(string text, int start)
    {
        var index = start + 2;
        if (index + 1 < text.Length &&
            text[index] == '[' &&
            text[index + 1] == '[')
        {
            var end = text.IndexOf("]]", index + 2, StringComparison.Ordinal);
            return end >= 0 ? end + 2 : text.Length;
        }
        while (index < text.Length && text[index] is not '\r' and not '\n')
        {
            index++;
        }
        return index;
    }

    private static int SkipWhitespace(string text, int index)
    {
        while (index < text.Length && char.IsWhiteSpace(text[index]))
        {
            index++;
        }
        return index;
    }

    private static string SerializeLuaTable(LuaTable table)
    {
        var builder = new StringBuilder();
        SerializeLuaValue(builder, table, 0);
        return builder.ToString();
    }

    private static void SerializeLuaValue(StringBuilder builder, object? value, int indent)
    {
        switch (value)
        {
            case null:
                builder.Append("nil");
                return;
            case string text:
                builder.Append('"').Append(EscapeLuaString(text)).Append('"');
                return;
            case bool boolean:
                builder.Append(boolean ? "true" : "false");
                return;
            case long integer:
                builder.Append(integer.ToString(CultureInfo.InvariantCulture));
                return;
            case int integer:
                builder.Append(integer.ToString(CultureInfo.InvariantCulture));
                return;
            case double number:
                builder.Append(number.ToString("R", CultureInfo.InvariantCulture));
                return;
            case float number:
                builder.Append(number.ToString("R", CultureInfo.InvariantCulture));
                return;
            case LuaTable table:
                SerializeTable(builder, table, indent);
                return;
            default:
                throw new InvalidDataException(
                    $"Unsupported Lua value type '{value.GetType().FullName}' while serializing restore data.");
        }
    }

    private static void SerializeTable(StringBuilder builder, LuaTable table, int indent)
    {
        builder.Append('{');
        if (table.Values.Count == 0 && table.Fields.Count == 0)
        {
            builder.Append('}');
            return;
        }

        builder.AppendLine();
        foreach (var value in table.Values)
        {
            AppendIndent(builder, indent + 1);
            SerializeLuaValue(builder, value, indent + 1);
            builder.Append(',').AppendLine();
        }
        foreach (var field in table.Fields)
        {
            AppendIndent(builder, indent + 1);
            builder.Append("[\"")
                .Append(EscapeLuaString(field.Key))
                .Append("\"] = ");
            SerializeLuaValue(builder, field.Value, indent + 1);
            builder.Append(',').AppendLine();
        }
        AppendIndent(builder, indent);
        builder.Append('}');
    }

    private static void AppendIndent(StringBuilder builder, int indent) =>
        builder.Append(' ', Math.Max(0, indent) * 2);

    private static string EscapeLuaString(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("\"", "\\\"", StringComparison.Ordinal)
        .Replace("\r", "\\r", StringComparison.Ordinal)
        .Replace("\n", "\\n", StringComparison.Ordinal)
        .Replace("\t", "\\t", StringComparison.Ordinal);
}
