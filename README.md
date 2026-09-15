# FrostLabs Guild Roster Client

Windows client for the FrostLabs Hogwarts Academy guild roster system.

This is a **standalone application**. It is separate from Azeroth Questing Companion: it has its own repository, executable, installer, install location, local settings, update channel, release history, and lifecycle. Its user experience intentionally follows the proven Azeroth Questing Companion pattern: detect the local WoW data source, keep a retry queue, register with Services01 automatically in the background, sync without asking the player for server credentials, and self-update.

Public source and releases:

```text
https://github.com/Frostcanvas/Guild-Roster-Client
Default branch: main
```

The client reads Guild Roster Manager (GRM) SavedVariables from World of Warcraft Retail, validates the configured guild roster, normalizes it into the FrostLabs `FGR1` protocol, and uploads complete snapshots to the LAN-only Guild Roster API on Services01.

## Current status

The standalone Windows client includes:

- an AQ-style dark dashboard and navigation layout;
- automatic GRM source selection when multiple WoW account folders exist;
- GRM parsing and complete-snapshot validation;
- automatic anonymous per-installation registration with Services01;
- a Windows-DPAPI-protected bearer token created behind the scenes;
- local retry/outbox handling;
- SHA-256 roster deduplication;
- manual `Sync Now`;
- automatic synchronization when GRM saves `Guild_Roster_Manager.lua`;
- stable/beta self-update support.

There is **no normal pairing-code step**. Like Azeroth Questing Companion, the client creates a random local client-instance ID, Services01 returns a unique per-installation bearer token, and the client stores that token locally without exposing a reusable server registration secret.

The parser design was checked against the supplied Hogwarts Academy GRM SavedVariables structure. That source contains five recognized guild metadata fields plus 209 current member records; the current-member records carry the Blizzard GUID and character/realm data required by FGR1. No current member names or raw roster contents are committed to this repository.

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

The account-specific directory is **not hard-coded**. The client discovers:

```text
D:\Battle.net\World of Warcraft\_retail_\WTF\Account\*\SavedVariables
```

When more than one GRM file exists, it prefers a source containing `Hogwarts Academy-BleedingHollow` and then the most recently written matching file. The user can still override the source manually.

Initial guild scope:

```text
Guild: Hogwarts Academy
Realm: BleedingHollow
WOW_PROJECT_ID: 1 (Retail)
```

The parser reads `GRM_GuildMemberHistory_Save` as structured Lua SavedVariables data and recognizes current GRM guild metadata separately from member entries. Each member must normalize to a valid Blizzard player GUID plus character/realm identity. Duplicate GUIDs, empty rosters, malformed tables, unstable/in-progress file writes, unsupported guild fields, or otherwise ambiguous parses are rejected before upload.

Current FGR1 fields include player GUID, character/realm, class, level, race, faction, rank, public/officer notes when available, and online/mobile state. Additional GRM-only history/main-alt fields remain available for later server-side expansion.

The client reads GRM SavedVariables as data only. It does not execute Lua, read WoW process memory, inject into the game, or automate gameplay.

## Services01 target

```text
Guild Roster API: http://10.0.10.246:8767
Roster website:  http://10.0.10.246:8767/roster
```

The API remains LAN-only.

## Automatic registration

Normal onboarding mirrors Azeroth Questing Companion and requires no manual code:

1. the client creates and remembers a random `client_instance_id`;
2. it calls `POST /api/v1/installations/auto-register` when it first needs to upload;
3. Services01 creates a unique installation ID and bearer token;
4. Services01 stores only the token hash;
5. the Windows client protects the returned token with DPAPI for the current Windows user;
6. later uploads use that per-installation token automatically.

If the local protected token is lost while the old client-instance ID still exists server-side, the client creates a fresh instance ID and self-registers again, matching the AQ recovery behavior. The protected Services01 registration key is never embedded in the Windows client.

The older one-time pairing endpoints remain server-side only as an optional controlled troubleshooting/recovery path; the normal Guild Roster Client UI does not ask the user to pair.

## Roster synchronization

The client:

1. waits for `Guild_Roster_Manager.lua` to become stable after WoW/GRM writes it;
2. parses only the configured Hogwarts Academy current-roster table;
3. rejects incomplete, malformed, empty, duplicate-GUID, or ambiguous roster data;
4. normalizes validated data to `FGR1`;
5. computes a deterministic SHA-256 snapshot key so unchanged rosters are not repeatedly uploaded;
6. writes a validated snapshot to the local outbox before network delivery;
7. self-registers with Services01 if needed;
8. uploads queued snapshots in order;
9. removes an outbox item only after Services01 returns `accepted` or `duplicate`;
10. retains validated snapshots for retry when Services01 is unavailable.

`Sync Now` forces the current validated state to be queued/uploaded even when it matches the last accepted local snapshot. Automatic synchronization normally skips unchanged accepted roster state.

Only server-accepted complete snapshots can drive Active/Inactive transitions. A client parse failure never sends a partial snapshot that could mark missing members inactive.

## Automatic client updates

Beginning with `0.1.0-beta.4`, normal client self-updates use this repository's **public GitHub Releases directly**, matching Azeroth Questing Companion more closely.

```text
Frostcanvas/Guild-Roster-Client source
  -> GitHub Actions builds GuildRosterClient-Setup.exe
  -> public GitHub Release in the same repository
  -> installed client queries the GitHub Releases API directly
  -> downloads GuildRosterClient-Setup.exe from GitHub
  -> verifies GitHub-provided SHA-256 digest and expected size
  -> launches the elevated silent Inno Setup updater
```

The updater:

- checks for updates on startup;
- supports `stable` and `beta` channels;
- ignores draft releases;
- Stable accepts only non-prerelease GitHub Releases;
- Beta considers prerelease and stable releases and chooses the newest eligible version;
- requires the exact `GuildRosterClient-Setup.exe` release asset;
- requires a valid SHA-256 digest before installation;
- validates expected file size when supplied;
- downloads the installer to `%LOCALAPPDATA%\FrostLabs\GuildRoster\Updates\<version>`;
- launches the installer elevated with silent Inno Setup close/replace/restart behavior;
- cleans stale update caches after seven days.

No GitHub PAT or reusable GitHub credential is embedded in the client.

The existing Services01 `/api/v1/client-updates/...` path remains only as a compatibility/bootstrap bridge for already-installed Beta 1-3 clients while the Beta 4 transition is completed. It is not the normal update source for Beta 4 and later.

### Prerelease version discipline

Every user-testable beta installer gets a new monotonically increasing prerelease number. A released or handed-off beta is never rebuilt or overwritten under the same version. `0.1.0-beta.4` is the first direct-GitHub updater build; later functional test builds are `beta.5`, `beta.6`, and so on. Internal source/documentation commits may occur between releases without consuming a beta number.

The release workflow also refuses to overwrite an existing `client-v<version>` release tag; a new functional build must use a new version.

## Local client state

```text
%LOCALAPPDATA%\FrostLabs\GuildRoster\settings.json
%LOCALAPPDATA%\FrostLabs\GuildRoster\auth.bin
%LOCALAPPDATA%\FrostLabs\GuildRoster\sync-state.json
%LOCALAPPDATA%\FrostLabs\GuildRoster\Updates
%LOCALAPPDATA%\FrostLabs\GuildRoster\Outbox
```

`auth.bin` contains only the Windows-DPAPI-protected per-installation bearer token. The outbox is retry state only. Canonical roster/history remains PostgreSQL on Services01.

## Build

The project targets .NET 10 Windows Forms and produces a self-contained Windows x64 build.

GitHub Actions builds `GuildRosterClient-Setup.exe` with Inno Setup on each push to `main`. A commit whose message starts with `Release client v` creates the corresponding GitHub Release in this public repository. Existing release tags/assets are not overwritten.

## Safety rules

- Never embed the Services01 registration key, bridge key, GitHub token, or another reusable secret in source or release binaries.
- Automatic registration is LAN-only and returns a unique per-installation bearer token; the reusable protected registration key stays server-side.
- Only complete validated Hogwarts Academy roster snapshots may be uploaded as complete `FGR1` snapshots.
- A partial or ambiguous GRM parse must fail closed so it cannot incorrectly mark members inactive.
- GRM schema changes must surface as parser errors rather than silently changing roster meaning.
- The client never executes imported Lua and never reads WoW process memory.
