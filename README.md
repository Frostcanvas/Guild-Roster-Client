# FrostLabs Guild Roster Client

Private Windows client for the FrostLabs Hogwarts Academy guild roster system.

This is a **standalone application**. It is separate from Azeroth Questing Companion: it has its own repository, executable, installer, install location, local settings, update channel, release history, and lifecycle. The only thing intentionally reused from Azeroth Questing Companion is the proven user experience pattern for update checks, installer staging, integrity verification, and in-place upgrades.

The client reads Guild Roster Manager (GRM) SavedVariables from World of Warcraft Retail, normalizes the configured guild roster into the FrostLabs `FGR1` protocol, and uploads complete validated snapshots to the LAN-only Guild Roster API on Services01.

## Current status

The standalone Windows client now includes the GRM parser, complete-snapshot safety validation, Services01 one-time pairing, Windows-protected bearer credentials, local retry/outbox handling, SHA-256 roster deduplication, manual `Sync Now`, and automatic synchronization when GRM saves `Guild_Roster_Manager.lua`.

The parser design was checked against the supplied Hogwarts Academy GRM SavedVariables structure. That source contains five recognized guild metadata fields plus 209 current member records; the current-member records carry the required Blizzard GUID and character/realm data used by FGR1. No current member names or raw roster contents are committed to this repository.

## Data source

The client watches:

```text
Guild_Roster_Manager.lua
```

under a Retail WoW account SavedVariables directory. The current development WoW root is:

```text
D:\Battle.net\World of Warcraft
```

The confirmed development SavedVariables directory is:

```text
D:\Battle.net\World of Warcraft\_retail_\WTF\Account\50130108#1\SavedVariables
```

The client discovers account folders under:

```text
D:\Battle.net\World of Warcraft\_retail_\WTF\Account\*\SavedVariables
```

and lets the user select/override the exact SavedVariables folder. The account-specific directory is not hard-coded into the application.

Initial guild scope:

```text
Guild: Hogwarts Academy
Realm: BleedingHollow
WOW_PROJECT_ID: 1 (Retail)
```

The parser reads `GRM_GuildMemberHistory_Save` as structured Lua SavedVariables data and recognizes the current GRM guild metadata keys separately from member entries. Each member must normalize to a valid Blizzard player GUID plus character/realm identity. Duplicate GUIDs, empty rosters, malformed tables, unstable/in-progress file writes, unsupported guild fields, or otherwise ambiguous parses are rejected before an upload can be queued.

Current FGR1 fields include player GUID, character/realm, class, level, race, faction, rank, public/officer notes when available, and online/mobile state. Additional GRM-only history/main-alt fields remain available for later server-side expansion without changing the safe current-roster ingestion rule.

The client reads GRM SavedVariables as structured data only. It does not execute Lua, read WoW process memory, inject into the game, or automate gameplay.

## Services01 target

```text
Guild Roster API: http://10.0.10.246:8767
Roster website:  http://10.0.10.246:8767/roster
```

The API remains LAN-only.

### One-time pairing

The client does not contain the protected Services01 registration key. Instead, Services01 creates a 15-minute, single-use pairing code. The user enters that short-lived code in the standalone client. Services01 returns a per-installation bearer token and stores only its SHA-256 hash server-side.

The Windows client protects its bearer token with Windows DPAPI for the current Windows user. The token is not stored in `settings.json` and is never written to GitHub or the FrostLabs Map.

Services01 pairing helper:

```text
Services01/scripts/create-guild-roster-client-pairing-code.sh
```

## Roster synchronization

The client:

1. waits for `Guild_Roster_Manager.lua` to become stable after WoW/GRM writes it;
2. parses only the configured Hogwarts Academy current-roster table;
3. rejects an incomplete, malformed, empty, duplicate-GUID, or ambiguous roster;
4. normalizes the validated data to `FGR1`;
5. computes a deterministic SHA-256 snapshot key from roster state, excluding the capture timestamp, so unchanged rosters do not create repeated uploads;
6. writes a validated snapshot to the local outbox before network delivery;
7. uploads queued snapshots to Services01 in order;
8. removes an outbox item only after Services01 returns `accepted` or `duplicate`;
9. retains validated snapshots for retry when Services01 is temporarily unavailable.

`Sync Now` forces the current validated state to be queued/uploaded even when it matches the last accepted local snapshot. Automatic synchronization normally skips an unchanged accepted roster.

Only server-accepted complete snapshots can drive Active/Inactive transitions. A client parse failure never sends a partial snapshot that could mark missing members inactive.

## Automatic client updates

The Guild Roster Client owns its **own** update path. It does not update Azeroth Questing Companion and Azeroth Questing Companion does not update it.

Its update experience intentionally mirrors the Azeroth Questing Companion:

- check for updates on client startup;
- support `stable` and `beta` channels;
- download the Windows installer to `%LOCALAPPDATA%\FrostLabs\GuildRoster\Updates\<version>`;
- verify the installer SHA-256 digest and expected size before execution;
- launch the installer elevated;
- use silent Inno Setup flags so the installed client closes/replaces/restarts cleanly;
- clean stale update caches after seven days.

Because this source repository is private, the installed client does **not** embed a GitHub PAT or other reusable GitHub secret. Private GitHub Releases remain the development/release source, while installed clients obtain update metadata and installer packages from the LAN-only Services01 Guild Roster API.

Update feed:

```text
GET http://10.0.10.246:8767/api/v1/client-updates/latest?channel=stable
GET http://10.0.10.246:8767/api/v1/client-updates/latest?channel=beta
```

The Services01 publisher validates the release installer, computes its SHA-256 digest and size, and publishes the selected channel manifest/package. No GitHub credential is shipped inside the Windows client.

## Local client state

```text
%LOCALAPPDATA%\FrostLabs\GuildRoster\settings.json
%LOCALAPPDATA%\FrostLabs\GuildRoster\auth.bin
%LOCALAPPDATA%\FrostLabs\GuildRoster\sync-state.json
%LOCALAPPDATA%\FrostLabs\GuildRoster\Updates
%LOCALAPPDATA%\FrostLabs\GuildRoster\Outbox
```

`auth.bin` contains only the Windows-DPAPI-protected per-installation bearer token. The outbox is retry state only. The canonical roster/history database remains PostgreSQL on Services01.

## Build

The project targets .NET 10 Windows Forms and produces a self-contained Windows x64 build.

GitHub Actions builds `GuildRosterClient-Setup.exe` using Inno Setup on each push to `main`. A commit whose message starts with `Release client v` also creates/updates a private GitHub Release for this client only.

The client build/installer pipeline passed after the parser, pairing, DPAPI credential store, outbox, and automatic synchronization code were added. The initial private builds are prerelease testing builds; code-signing policy can be added before a future broadly distributed stable build.

## Safety rules

- Never embed the Services01 registration key, bridge key, GitHub token, or other reusable secret in source or release binaries.
- Pairing codes are short-lived and single-use; the long-lived registration key remains protected on Services01.
- Only complete validated Hogwarts Academy roster snapshots may be uploaded as complete `FGR1` snapshots.
- A partial or ambiguous GRM parse must fail closed so it cannot incorrectly mark members inactive.
- GRM schema changes must surface as parser errors rather than silently changing roster meaning.
- The client never executes imported Lua and never reads WoW process memory.
