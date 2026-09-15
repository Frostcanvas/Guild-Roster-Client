using System.Text.Json.Serialization;

namespace GuildRoster.Client;

internal sealed record GrmJoinDateHistoryEntry(
    int Day,
    int Month,
    int Year,
    string DateKey,
    long? Epoch,
    bool Confirmed,
    int EventType);

internal sealed record GrmIdentityMember(
    string PlayerGuid,
    string Relationship,
    string? MainPlayerGuid,
    string? AltGroup,
    bool GrmMetadataPresent,
    string? CustomNote,
    string? JoinDate,
    IReadOnlyList<GrmJoinDateHistoryEntry> JoinDateHistory);

internal sealed record GrmIdentityParseResult(
    RosterIdentityPayload Payload,
    int MainCount,
    int AltCount,
    int UnknownCount);

internal sealed class RosterJoinDateHistoryPayload
{
    [JsonPropertyName("day")]
    public int Day { get; init; }

    [JsonPropertyName("month")]
    public int Month { get; init; }

    [JsonPropertyName("year")]
    public int Year { get; init; }

    [JsonPropertyName("date_key")]
    public required string DateKey { get; init; }

    [JsonPropertyName("epoch")]
    public long? Epoch { get; init; }

    [JsonPropertyName("confirmed")]
    public bool Confirmed { get; init; }

    [JsonPropertyName("event_type")]
    public int EventType { get; init; }
}

internal sealed class RosterIdentityMemberPayload
{
    [JsonPropertyName("player_guid")]
    public required string PlayerGuid { get; init; }

    [JsonPropertyName("relationship")]
    public required string Relationship { get; init; }

    [JsonPropertyName("main_player_guid")]
    public string? MainPlayerGuid { get; init; }

    [JsonPropertyName("alt_group")]
    public string? AltGroup { get; init; }

    [JsonPropertyName("grm_metadata_present")]
    public bool GrmMetadataPresent { get; init; }

    [JsonPropertyName("custom_note")]
    public string? CustomNote { get; init; }

    [JsonPropertyName("join_date")]
    public string? JoinDate { get; init; }

    [JsonPropertyName("join_date_history")]
    public required List<RosterJoinDateHistoryPayload> JoinDateHistory { get; init; }
}

internal sealed class RosterIdentityPayload
{
    [JsonPropertyName("guild_name")]
    public required string GuildName { get; init; }

    [JsonPropertyName("guild_realm")]
    public required string GuildRealm { get; init; }

    [JsonPropertyName("captured_at")]
    public required DateTimeOffset CapturedAt { get; init; }

    [JsonPropertyName("members")]
    public required List<RosterIdentityMemberPayload> Members { get; init; }
}
