using System.Text.Json.Serialization;

namespace GuildRoster.Client;

internal sealed record GrmIdentityMember(
    string PlayerGuid,
    string Relationship,
    string? MainPlayerGuid,
    string? AltGroup);

internal sealed record GrmIdentityParseResult(
    RosterIdentityPayload Payload,
    int MainCount,
    int AltCount,
    int UnknownCount);

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
