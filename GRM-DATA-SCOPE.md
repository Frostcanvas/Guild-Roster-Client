# Full GRM data capture scope

Status: **Approved design / implementation pending**

Approved: 2026-09-14

The FrostLabs Guild Roster Client will expand beyond selected GRM fields and preserve **all safely parseable Guild Roster Manager SavedVariables data relevant to the configured guild**, while keeping Beta 5 immutable.

## Source

The client reads `Guild_Roster_Manager.lua` as data only. It never executes imported Lua, reads World of Warcraft process memory, injects into WoW, or automates gameplay.

The supplied source contains these top-level GRM assignments, all of which are in archive scope:

- `GRM_PlayersThatLeftHistory_Save`
- `GRM_GuildMemberHistory_Save`
- `GRM_CalendarAddQue_Save`
- `GRM_LogReport_Save`
- `GRM_AddonSettings_Save`
- `GRM_PlayerListOfAlts_Save`
- `GRM_GuildDataBackup_Save`
- `GRM_Restore_Members`
- `GRM_Restore_FormerMembers`
- `GRM_Restore_Log`
- `GRM_Misc`
- `GRM_Alts`
- `GRM_DailyAnnounce`

## Required behavior

The client/server path must support two layers:

1. a lossless parsed archive that preserves known and unknown GRM fields/structures so data is not silently discarded;
2. normalized fields for roster display, search, Discord identity, Guild Executive, and operator-approved restore workflows.

The normalized member data includes, when present, GUID/name/realm, class, race, sex, level, faction, rank metadata/history, notes and note history, custom notes, join-date history, main/alt relationships, birthdays, nicknames, former-member relationships, leave/rejoin history, inviter/event history, last-online/activity data, professions, guild reputation, achievement points, Mythic score, Hardcore/death state, safelist/ban/recommendation metadata, and other GRM member fields.

Guild-level archive data includes GRM logs/events, calendar/anniversary entries, alt lists/groups, guild backup metadata, restore buffers/logs, relevant addon settings, guild creation/rank metadata, and future safely parseable structures.

## Restore boundary

The archive is broader than the write-back feature.

Explicit operator-approved restore may be implemented for:

- public notes;
- officer notes;
- GRM custom notes;
- GRM join date / join-date history;
- main/alt relationships;
- birthday information;
- nickname information.

Every restore must preview current versus recovered values and preserve the pre-restore state first.

## Returning-member recovery trigger

A restore offer is specifically triggered by a validated **Inactive -> Active** transition for an already-known character identity.

- Services01 compares each accepted complete roster snapshot with canonical roster state.
- If an exact historical Blizzard player GUID that was inactive becomes active again, the existing character identity is reactivated rather than duplicated.
- The Windows client creates one pending recovery offer for that reactivation event with **Review & Restore**, **Keep Current**, and **Ask Later** choices.
- **Review & Restore** shows current values beside archived values and allows only the approved restore fields to be selected.
- **Keep Current** records that the offer was dismissed for that reactivation event; **Ask Later** keeps the offer pending.
- The client must not repeat the same prompt on every synchronization.
- Existing history is merged rather than overwritten. Join/rejoin history must preserve the earlier join period, leave event when known, and the new rejoin event.
- Character name and realm are not sufficient identity keys. A same-name record with a different player GUID must never receive automatic historical restore; it requires manual review.
- Before any write-back, preserve the current pre-restore state and log the restored, skipped, or rejected fields.

**Do not implement rank restoration.** Rank names/history are read-only historical data. The client must not automate promotion, demotion, kick, ban, or other guild-management actions from archived GRM state.

Blizzard/runtime facts such as class, race, level, online/last-online state, reputation, achievements, professions, and similar fields are archive/display data and are not written back.

## Release direction

`0.1.0-beta.5` remains immutable. Full-data ingestion work is a later functional beta (`beta.6` or later). No released tag or installer is overwritten.

No roster dump, secret, bearer token, registration key, Discord bot token, private key, password, or other protected value is committed to this repository.
