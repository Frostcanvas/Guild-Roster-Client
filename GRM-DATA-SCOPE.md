# Full GRM data capture scope

Status: **Beta 6 implementation committed / validation pending**

Approved: 2026-09-14
Implementation started: 2026-09-15

The FrostLabs Guild Roster Client expands beyond selected GRM fields and preserves **all safely parseable Guild Roster Manager SavedVariables data relevant to the configured guild**, while keeping Beta 5 immutable.

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

## Beta 6 implementation

The committed Beta 6 foundation now provides two layers:

1. a lossless parsed archive that retains every safely parsed top-level `GRM_*` assignment in a mixed keyed/positional representation so unknown fields are not silently discarded;
2. normalized restore profiles keyed by complete Blizzard Player GUID for current/former member recovery workflows.

The client has a separate GRM archive outbox under its FrostLabs local application state. The roster snapshot and full archive are queued independently and retry without dropping validated data if Services01 is unavailable.

The archive parser extracts current/former member restore evidence for public/officer/custom notes, complete join-date history, rank history as read-only evidence, main/alt groups, birthday data, nickname data, leave-time main/alt data, and the raw safely parsed member structure. It also inspects Hogwarts Academy GRM log event types used for public-note and officer-note changes so a removed note can remain a recovery candidate even when the member's current note is blank. This specifically preserves cases such as GRM removing an earlier `Joined:` officer note.

The normalized member data includes, when present, GUID/name/realm, class, race, sex, level, faction, rank metadata/history, notes and note history, custom notes, join-date history, main/alt relationships, birthdays, nicknames, former-member relationships, leave/rejoin history, inviter/event history, last-online/activity data, professions, guild reputation, achievement points, Mythic score, Hardcore/death state, safelist/ban/recommendation metadata, and other GRM member fields.

Guild-level archive data includes GRM logs/events, calendar/anniversary entries, alt lists/groups, guild backup metadata, restore buffers/logs, relevant addon settings, guild creation/rank metadata, and future safely parseable structures.

## Restore boundary

The archive is broader than the write-back feature.

Explicit operator-approved restore scope is limited to:

- public notes;
- officer notes;
- GRM custom notes;
- GRM join date / join-date history;
- main/alt relationships;
- birthday information;
- nickname information.

Every restore must preview current versus recovered values and preserve the pre-restore state first.

The committed Beta 6 foundation implements **detection, preview, field selection, and protected restore-request recording**. It does **not** yet write the selected values back into World of Warcraft or GRM. Actual write-back remains a separate explicit restore step so archived data cannot silently overwrite current guild state.

## Returning-member recovery trigger

A restore offer is specifically triggered by a validated **Inactive -> Active** transition for an already-known character identity.

- Services01 compares each accepted complete roster snapshot with canonical roster state.
- Only the roster-history event `rejoined` creates an automatic recovery offer; ordinary `first_seen` characters do not.
- If an exact historical Blizzard Player GUID that was inactive becomes active again, the existing character identity is reactivated rather than duplicated.
- The Windows client presents one recovery review for that reactivation event with **Review & Restore**, **Keep Current**, and **Ask Later** choices.
- **Review & Restore** shows current values beside archived values and allows only approved restore fields to be selected; the selected field IDs are recorded with the protected recovery request.
- **Keep Current** dismisses that reactivation event.
- **Ask Later** keeps the offer pending and defers the next prompt for one day.
- The same recovery event is unique by Player GUID plus reactivated roster snapshot, so normal repeated synchronization does not create duplicate prompts.
- Existing history is merged rather than overwritten. Join/rejoin history must preserve the earlier join period, leave event when known, and the new rejoin event.
- Character name and realm are not sufficient identity keys. A same-name record with a different player GUID never receives automatic historical restore; it requires manual review.
- Before any future write-back, preserve the current pre-restore state and log the restored, skipped, or rejected fields.

**Do not implement rank restoration.** Rank names/history are read-only historical data. The client must not automate promotion, demotion, kick, ban, or other guild-management actions from archived GRM state.

Blizzard/runtime facts such as class, race, level, online/last-online state, reputation, achievements, professions, and similar fields are archive/display data and are not written back.

## Release direction

`0.1.0-beta.5` remains immutable. The implementation above is the Beta 6 source line. Do not publish `client-v0.1.0-beta.6` until the Windows build/installer workflow passes and the corresponding Services01 archive API has passed its isolated apply/verification. Existing release tags and installers are never overwritten.

No roster dump, secret, bearer token, registration key, Discord bot token, private key, password, or other protected value is committed to this repository.
