using System.Text.Json.Serialization;

namespace GuildRoster.Client;

internal sealed record GrmRosterMember(
    string PlayerGuid,
    string Name,
    string Realm,
    string? ClassToken,
    int? Level,
    string? RaceToken,
    string? Faction,
    string? RankName,
    int? RankIndex,
    string? PublicNote,
    string? OfficerNote,
    bool IsOnline,
    bool IsMobile,
    string? AltGroup,
    int? Sex,
    int? GuildRep,
    int? MythicScore);

internal sealed record ParsedGrmRoster(
    string GuildName,
    string GuildRealm,
    DateTimeOffset CapturedAt,
    string SnapshotKey,
    IReadOnlyList<GrmRosterMember> Members);

internal sealed class RosterMemberPayload
{
    [JsonPropertyName("player_guid")]
    public required string PlayerGuid { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("realm")]
    public required string Realm { get; init; }

    [JsonPropertyName("class_token")]
    public string? ClassToken { get; init; }

    [JsonPropertyName("level")]
    public int? Level { get; init; }

    [JsonPropertyName("race_token")]
    public string? RaceToken { get; init; }

    [JsonPropertyName("faction")]
    public string? Faction { get; init; }

    [JsonPropertyName("rank_name")]
    public string? RankName { get; init; }

    [JsonPropertyName("rank_index")]
    public int? RankIndex { get; init; }

    [JsonPropertyName("public_note")]
    public string? PublicNote { get; init; }

    [JsonPropertyName("officer_note")]
    public string? OfficerNote { get; init; }

    [JsonPropertyName("is_online")]
    public bool IsOnline { get; init; }

    [JsonPropertyName("is_mobile")]
    public bool IsMobile { get; init; }
}

internal sealed class RosterSnapshotPayload
{
    [JsonPropertyName("protocol")]
    public string Protocol { get; init; } = "FGR1";

    [JsonPropertyName("client_snapshot_key")]
    public required string ClientSnapshotKey { get; init; }

    [JsonPropertyName("captured_at")]
    public required DateTimeOffset CapturedAt { get; init; }

    [JsonPropertyName("guild_name")]
    public required string GuildName { get; init; }

    [JsonPropertyName("guild_realm")]
    public required string GuildRealm { get; init; }

    [JsonPropertyName("addon_version")]
    public string? AddonVersion { get; init; }

    [JsonPropertyName("companion_version")]
    public string? CompanionVersion { get; init; }

    [JsonPropertyName("wow_project_id")]
    public int WowProjectId { get; init; } = 1;

    [JsonPropertyName("wow_build")]
    public string? WowBuild { get; init; }

    [JsonPropertyName("members")]
    public required List<RosterMemberPayload> Members { get; init; }
}
