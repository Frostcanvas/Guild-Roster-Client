using System.Text.Json.Serialization;

namespace GuildRoster.Client;

internal sealed class GrmRestoreProfilePayload
{
    [JsonPropertyName("player_guid")]
    public required string PlayerGuid { get; init; }

    [JsonPropertyName("character_name")]
    public string? CharacterName { get; init; }

    [JsonPropertyName("character_realm")]
    public string? CharacterRealm { get; init; }

    [JsonPropertyName("source_section")]
    public required string SourceSection { get; init; }

    [JsonPropertyName("source_record_key")]
    public string? SourceRecordKey { get; init; }

    [JsonPropertyName("restore_data")]
    public required Dictionary<string, object?> RestoreData { get; init; }
}

internal sealed class GrmArchivePayload
{
    [JsonPropertyName("archive_key")]
    public required string ArchiveKey { get; init; }

    [JsonPropertyName("source_sha256")]
    public required string SourceSha256 { get; init; }

    [JsonPropertyName("captured_at")]
    public required DateTimeOffset CapturedAt { get; init; }

    [JsonPropertyName("guild_name")]
    public required string GuildName { get; init; }

    [JsonPropertyName("guild_realm")]
    public required string GuildRealm { get; init; }

    [JsonPropertyName("variables")]
    public required Dictionary<string, object?> Variables { get; init; }

    [JsonPropertyName("parse_errors")]
    public required Dictionary<string, string> ParseErrors { get; init; }

    [JsonPropertyName("restore_profiles")]
    public required List<GrmRestoreProfilePayload> RestoreProfiles { get; init; }
}

internal sealed record ParsedGrmArchive(
    GrmArchivePayload Payload,
    int VariableCount,
    int ParseErrorCount,
    int RestoreProfileCount);
