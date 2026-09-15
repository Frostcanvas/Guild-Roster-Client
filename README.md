# FrostLabs Guild Roster Client

Windows client for the FrostLabs Hogwarts Academy guild roster system.

This is a **standalone application**. It is separate from Azeroth Questing Companion: it has its own repository, executable, installer, install location, local settings, update channel, release history, and lifecycle. Its user experience intentionally follows the proven Azeroth Questing Companion pattern: detect the local WoW data source, keep retry queues, register with Services01 automatically in the background, sync without asking the player for server credentials, and self-update.

Public source and releases:

```text
https://github.com/Frostcanvas/Guild-Roster-Client
Default branch: main
```

The client reads Guild Roster Manager (GRM) SavedVariables from World of Warcraft Retail, validates the configured guild roster, normalizes complete active-roster snapshots into the FrostLabs `FGR1` protocol, synchronizes GRM main/alt identity, and archives all safely parseable GRM structures to the LAN-only Guild Roster API on Services01.

## Current status

`0.1.0-beta.7` is the current released prerelease.

The standalone Windows client includes:

- an AQ-style dark dashboard and navigation layout;
- automatic GRM source selection when multiple WoW account folders exist;
- safe GRM SavedVariables parsing and complete-snapshot validation;
- FGR1 active-roster synchronization;
- exact GRM main/alt identity synchronization;
- full safely parseable GRM archive capture with a separate retry outbox;
- current/former-member restore evidence keyed by complete Blizzard Player GUID;
- returning-member recovery offers for exact-GUID `Inactive -> Active` rejoin events;
- `Review & Restore`, `Keep Current`, and `Ask Later` recovery decisions;
- a controlled Beta 7 GRM-local restore engine for approved returning-member fields;
- automatic anonymous per-installation registration with Services01;
- a Windows-DPAPI-protected bearer token created behind the scenes;
- local retry/outbox handling;
- SHA-256 roster/archive deduplication;
- manual `Sync Now`;
- automatic synchronization when GRM saves `Guild_Roster_Manager.lua`;
- stable/beta self-update support.

There is **no normal pairing-code step**. Like Azeroth Questing Companion, the client creates a random local client-instance ID, Services01 returns a unique per-installation bearer token, and the client stores that token locally without exposing a reusable server registration secret.

The full-GRM archive preserves safely parsed current/former member structures, public/officer/custom-note evidence and note-change history, complete join-date history, read-only rank history, main/alt structures, birthdays, nicknames, leave-time identity evidence, GRM logs/events, settings, backup/restore structures, and future safely parseable fields. Unknown successfully parsed structures are retained rather than silently discarded.

The returning-member workflow is intentionally narrow: only an exact historical complete Player GUID that transitions from Inactive to Active through an accepted complete roster snapshot may create an automatic recovery offer. Same-name/different-GUID records never auto-restore.

Beta 7 adds the controlled local restore engine. When the operator explicitly chooses `Review & Restore`, the client can restore approved GRM-local custom-note state, merged join-date/history, non-conflicting main/alt relationships, birthday information, and nickname information. World of Warcraft must be closed before a SavedVariables write. The client creates a pre-restore `.lua` backup and preserves the sibling `.bak` when present, reparses its edited GRM assignments before replacing the source, rechecks the source hash immediately before write-back, and keeps a per-offer local audit so the same event is not applied twice after an acknowledgement failure.

Public and Officer Notes remain manual because Retail note-writing restrictions are not bypassed. Guild-rank restoration and automated promote/demote/kick/ban actions are explicitly prohibited.

Manual Public/Officer note repair remains deferred. A later client build will expose a passive `Review Notes (X)` workflow with an A-Z GUID-backed queue; synchronization will not be interrupted by note-repair prompts.

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

The active-roster parser reads `GRM_GuildMemberHistory_Save` as structured Lua SavedVariables data and recognizes current GRM guild metadata separately from member entries. Each active member must normalize to a valid Blizzard player GUID plus character/realm identity. Duplicate GUIDs, empty rosters, malformed tables, unstable/in-progress file writes, unsupported current-roster guild fields, or otherwise ambiguous parses are rejected before a complete roster snapshot is uploaded.

FGR1 carries the validated active-roster identity/state needed for Active/Inactive semantics. Separate archive/identity parsers also read `GRM_Alts` for main/alt identity and every detected top-level `GRM_*` SavedVariables assignment for the lossless archive/recovery layer.

The client reads GRM SavedVariables as data only. It does not execute Lua, read WoW process memory, inject into the game, or automate gameplay.

## Services01 target

```text
Guild Roster API: http://10.0.10.246:8767
Roster website:  http://10.0.10.246:8767/roster
```

The API remains LAN-only.

The authenticated archive/recovery routes are:

```text
POST /api/v1/grm/archive
GET  /api/v1/roster/recovery-offers
POST /api/v1/roster/recovery-offers/{offer_id}/decision
```

## Automatic registration

Normal onboarding mirrors Azeroth Questing Companion and requires no manual code:

1. the client creates and remembers a random `client_instance_id`;
2. it calls `POST /api/v1/installations/auto-register` when it first needs to upload;
3. Services01 creates a unique installation ID and bearer token;
4. Services01 stores only the token hash;
5. the Windows client protects the returned token with DPAPI for the current Windows user;
6. later roster, identity, archive, and recovery requests use that per-installation token automatically.

If the local protected token is lost while the old client-instance ID still exists server-side, the client creates a fresh instance ID and self-registers again, matching the AQ recovery behavior. The protected Services01 registration key is never embedded in the Windows client.

The older one-time pairing endpoints remain server-side only as an optional controlled troubleshooting/recovery path; the normal Guild Roster Client UI does not ask the user to pair.

## Roster synchronization

The client:

1. waits for `Guild_Roster_Manager.lua` to become stable after WoW/GRM writes it;
2. parses and validates the configured Hogwarts Academy current-roster table;
3. rejects incomplete, malformed, empty, duplicate-GUID, or ambiguous roster data;
4. normalizes validated active-roster data to `FGR1`;
5. validates GRM main/alt identity against the complete active roster;
6. parses the complete safe GRM archive and builds GUID-keyed restore profiles;
7. computes deterministic SHA-256 keys so unchanged roster/archive state is not repeatedly uploaded;
8. writes validated roster/archive payloads to independent local outboxes before network delivery;
9. self-registers with Services01 if needed;
10. uploads queued roster snapshots and GRM archives in order;
11. synchronizes the complete main/alt identity map;
12. checks for due exact-GUID returning-member recovery offers;
13. removes outbox items only after Services01 returns an accepted/duplicate result;
14. retains validated items for retry when Services01 is unavailable.

`Sync Now` forces the current validated roster state to be queued/uploaded even when it matches the last accepted local snapshot. Automatic synchronization normally skips unchanged accepted state.

Only server-accepted complete roster snapshots can drive Active/Inactive transitions. Archive or identity uploads cannot manufacture a partial roster or mark missing characters inactive.

## Controlled returning-member restore

`Review & Restore` uses the complete Player GUID from the Services01 offer and requires the same exact GUID to exist in the current Hogwarts Academy GRM member table. The write-back engine is fail-closed:

- WoW must be closed before local SavedVariables write-back;
- current GRM data is reread and hashed;
- archived join history is merged with current history rather than replacing the current rejoin event;
- main/alt state is only restored when the archived group still exists and current group/main evidence does not conflict;
- the affected GRM assignments are safely parsed again after editing;
- the live source hash is checked again just before writing;
- a pre-restore copy of `Guild_Roster_Manager.lua` and its `.bak` file, when present, is kept locally;
- a local per-offer audit prevents duplicate local application after a Services01 acknowledgement failure;
- Public/Officer Notes are classified as manual-required instead of being written automatically;
- guild rank and Blizzard-owned runtime state are never restored.

Membership history remains additive. User-facing history is newest first, for example `Rejoined -> Left -> Joined`.

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

The existing Services01 `/api/v1/client-updates/...` path remains only as a compatibility/bootstrap bridge for already-installed Beta 1-3 clients. It is not the normal update source for Beta 4 and later.

### Prerelease version discipline

Every user-testable beta installer gets a new monotonically increasing prerelease number. A released or handed-off beta is never rebuilt or overwritten under the same version. `0.1.0-beta.7` is immutable; the next functional client installer must use a later beta number. Internal source/documentation commits may occur between releases without consuming a beta number.

The release workflow refuses to overwrite an existing `client-v<version>` release tag.

### Beta 7 release evidence

```text
version: 0.1.0-beta.7
tag: client-v0.1.0-beta.7
release commit: e4f147fb9d4b9db31c7ade5515e4c98fbcbb6638
source verification run: 34937299451
release run: 34937447850
installer: GuildRosterClient-Setup.exe
installer size: 37297988 bytes
SHA-256: a6ddff9518d38127ee7e684c260f33238a4865fa9a27d12b30b4ddd934af29ec
```

The source verification run passed Windows publish, executable verification, installer build, and artifact upload after the restore-engine fixes. The Beta 7 release run passed restore, self-contained publish, executable verification, Inno Setup installer build, artifact upload, and public prerelease publication.

### Beta 6 release evidence

```text
version: 0.1.0-beta.6
tag: client-v0.1.0-beta.6
release commit: f28879699f21b097ff1f708b87d9fc0ce472cb73
GitHub Actions run: 34934087361
installer: GuildRosterClient-Setup.exe
installer size: 37294046 bytes
SHA-256: 65f783cb58dd4ea11d4d501157f8b73902b0397ff01c97b334838cfad71bc9af
```

Windows restore, publish, executable verification, Inno Setup installer build, artifact upload, and GitHub prerelease publication all passed.

## Local client state

```text
%LOCALAPPDATA%\FrostLabs\GuildRoster\settings.json
%LOCALAPPDATA%\FrostLabs\GuildRoster\auth.bin
%LOCALAPPDATA%\FrostLabs\GuildRoster\sync-state.json
%LOCALAPPDATA%\FrostLabs\GuildRoster\Updates
%LOCALAPPDATA%\FrostLabs\GuildRoster\Outbox
%LOCALAPPDATA%\FrostLabs\GuildRoster\GrmArchiveOutbox
%LOCALAPPDATA%\FrostLabs\GuildRoster\RestoreBackups
%LOCALAPPDATA%\FrostLabs\GuildRoster\RestoreAudit
```

`auth.bin` contains only the Windows-DPAPI-protected per-installation bearer token. The outboxes are retry state only. RestoreBackups and RestoreAudit are local safety/audit state for explicit GRM-local recovery. Canonical roster/history/archive/recovery state remains PostgreSQL on Services01 after successful upload.

## Build

The project targets .NET 10 Windows Forms and produces a self-contained Windows x64 build.

GitHub Actions builds `GuildRosterClient-Setup.exe` with Inno Setup on each push to `main`. A commit whose message starts with `Release client v` creates the corresponding GitHub Release in this public repository. Existing release tags/assets are not overwritten.

## Safety rules

- Never embed the Services01 registration key, bridge key, GitHub token, or another reusable secret in source or release binaries.
- Automatic registration is LAN-only and returns a unique per-installation bearer token; the reusable protected registration key stays server-side.
- Only complete validated Hogwarts Academy roster snapshots may be uploaded as complete `FGR1` snapshots.
- A partial or ambiguous GRM parse must fail closed so it cannot incorrectly mark members inactive.
- GRM schema changes must surface as parser errors or safely archived unknown structures rather than silently changing roster meaning.
- Returning-member automatic recovery requires an exact historical complete Player GUID plus canonical `Inactive -> Active` rejoin evidence.
- Same-name/different-GUID identities require manual review.
- WoW must be closed before GRM-local restore write-back.
- Public/Officer Notes remain manual; the client must not bypass Retail restrictions.
- Guild-rank restoration and automated promote/demote/kick/ban actions are prohibited.
- The client never executes imported Lua and never reads WoW process memory.
