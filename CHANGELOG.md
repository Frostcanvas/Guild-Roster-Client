# Changelog

All notable changes to the FrostLabs Guild Roster Client will be documented in this file.

The project follows a simple chronological changelog while the client is under initial development.

## [Unreleased]

### Changed

- Switched the next-release updater design from the Services01 manifest feed to direct anonymous GitHub Releases access, matching Azeroth Questing Companion more closely.
- The updater now targets the planned public release-only repository `Frostcanvas/Guild-Roster-Client-Releases` while source remains private in `Frostcanvas/Guild-Roster-Client`.
- Stable update checks ignore prereleases; Beta checks include prereleases and stable releases and select the newest eligible version.
- The updater requires the exact `GuildRosterClient-Setup.exe` asset plus a GitHub-provided SHA-256 digest before an installer can run.
- Services01 client-update routes remain only as a compatibility/bootstrap bridge for Beta 1-3 during the transition to Beta 4; they are not the intended normal update source for Beta 4 and later.
- No GitHub PAT or reusable GitHub credential will be embedded in the installed client.

### Release process

- Every user-testable beta installer gets a new monotonically increasing prerelease number. A released or handed-off beta is never rebuilt or overwritten under the same version.
- Current progression is `0.1.0-beta.3`; the next functional client test build will be `0.1.0-beta.4`, followed by `beta.5`, `beta.6`, and so on.
- Internal source/documentation commits may occur between installer releases without consuming a beta number.
- This policy avoids stale installer caches, ambiguous update manifests, and same-version replacement issues encountered in earlier client work.

### Planned integration

- Create the public release-only repository `Frostcanvas/Guild-Roster-Client-Releases` before Beta 4 is handed off.
- Publish the Beta 4 installer to that public GitHub Releases repository and use the existing Services01 feed only once as the upgrade bridge for already-installed Beta 1-3 clients.
- Extend the normalized server model for GRM main/alt relationships and selected GRM historical/backfill fields.
- Link roster identities to Discord numeric user IDs through the shared Guild Roster database.
- Allow Guild Executive to consume the shared Guild Roster person/identity view without becoming the canonical roster store.

## 0.1.0 Beta 3 - 2026-09-14

### Changed

- Reworked the client to match the Azeroth Questing Companion user experience instead of requiring a visible manual pairing step.
- Removed pairing from the normal client workflow. The client now self-registers with Services01 in the background using a random local `client_instance_id`, receives its own bearer token, and protects that token with Windows DPAPI.
- Added automatic recovery behavior matching AQ: if a local token is lost but its old client-instance ID is already registered, the client creates a new random instance ID and registers itself again without asking for a code or shared secret.
- Replaced the basic default WinForms layout with an AQ-style dark dashboard, sidebar navigation, top action bar, status cards, and recent-activity view.
- Changed multi-account GRM discovery so the client automatically prefers a `Guild_Roster_Manager.lua` source containing `Hogwarts Academy-BleedingHollow`, then chooses the most recently written matching source. Manual source override remains available.
- Simplified the normal workflow to: detect GRM -> validate roster -> queue -> automatically register if needed -> upload -> retry when offline.

### Compatibility

- The older one-time pairing endpoints may remain available on Services01 as an optional controlled troubleshooting/recovery path, but Beta 3 does not display a Pair Client button and does not require a pairing code for normal operation.
- FGR1 complete-snapshot validation, SHA-256 deduplication, local retry/outbox behavior, DPAPI token protection, automatic GRM watching, and Active/Inactive safety semantics remain unchanged.

## 0.1.0 Beta 2 - 2026-09-14

### Added

- Implemented a safe Lua SavedVariables subset parser for `Guild_Roster_Manager.lua`; imported Lua is parsed as data and never executed.
- Implemented direct parsing of `GRM_GuildMemberHistory_Save` for `Hogwarts Academy-BleedingHollow`.
- Added explicit handling for the five current GRM guild metadata fields (`grmName`, `grmClubID`, `grmNumRanks`, `ranks`, and `grmCreationDate`) so metadata is not mistaken for a guild member.
- Validated the parser design against the supplied GRM structure: five recognized guild metadata fields plus 209 current member records, without committing current member names or raw roster contents.
- Added complete-snapshot validation for Blizzard player GUIDs, character/realm identity, member count, duplicate GUIDs, level/rank bounds, stable file writes, and unknown guild-table fields.
- Added deterministic SHA-256 FGR1 snapshot keys so unchanged accepted rosters are not repeatedly uploaded by automatic synchronization.
- Added a retryable local outbox at `%LOCALAPPDATA%\FrostLabs\GuildRoster\Outbox`; validated snapshots are removed only after Services01 returns `accepted` or `duplicate`.
- Added persisted non-secret sync state at `%LOCALAPPDATA%\FrostLabs\GuildRoster\sync-state.json`.
- Added `Sync Now` and automatic synchronization after GRM writes `Guild_Roster_Manager.lua`.
- Added one-time Services01 client pairing in Beta 2. This manual onboarding flow is superseded by Beta 3 automatic registration for normal use.
- Added per-installation bearer authentication for FGR1 uploads.
- Added Windows DPAPI protection for the per-installation bearer token in `%LOCALAPPDATA%\FrostLabs\GuildRoster\auth.bin`.
- Added pairing/sync status, queued snapshot count, last successful sync information, and automatic retry behavior to the Windows client UI.
- Kept Retail uploads explicitly scoped to `WOW_PROJECT_ID 1`.
- Added the Services01 one-time pairing helper `Services01/scripts/create-guild-roster-client-pairing-code.sh` to the infrastructure repository; it reads the protected registration key internally and prints only the short-lived single-use pairing code.

### Safety

- Empty rosters are rejected rather than being uploaded as complete snapshots.
- Unknown GRM guild-table fields fail closed until the parser is reviewed for the new schema.
- A file changing while it is being read is retried instead of parsed as a complete roster.
- Duplicate player GUIDs or malformed member identities block the entire snapshot.
- A parser failure never sends a partial snapshot that could mark missing guild members inactive.
- The Services01 registration key, Discord bridge key, GitHub credentials, and bearer-token plaintext are not written to source, the changelog, or the FrostLabs Map.

## 0.1.0 Beta 1 - 2026-09-14

### Added

- Created the private `Frostcanvas/Guild-Roster-Client` repository.
- Established the Guild Roster Client as a standalone Windows application with its own executable, installer, install location, settings, update channel, release history, and lifecycle, separate from Azeroth Questing Companion.
- Added the initial .NET 10 Windows Forms client shell and self-contained Windows x64 build project.
- Established Guild Roster Manager (GRM) SavedVariables as the initial data source.
- Established `Guild_Roster_Manager.lua` as the file the client discovers and monitors.
- Established `Hogwarts Academy` / `BleedingHollow` as the initial guild scope.
- Established normalized `FGR1` snapshots as the client-to-server upload format.
- Established the Services01 Guild Roster API at `http://10.0.10.246:8767` as the LAN-only synchronization target.
- Added GRM source discovery for Retail account SavedVariables folders under common WoW installation roots, including `D:\Battle.net\World of Warcraft`.
- Confirmed the development source at `D:\Battle.net\World of Warcraft\_retail_\WTF\Account\50130108#1\SavedVariables`; the account-specific directory is discovered/selected at runtime and is not hard-coded.
- Added a SavedVariables folder picker and file watcher.
- Added automatic client update checks with `stable` and `beta` channels.
- Added update download staging under `%LOCALAPPDATA%\FrostLabs\GuildRoster\Updates\<version>`.
- Added SHA-256 and file-size verification before an update installer can run.
- Added silent elevated Inno Setup update launch with close/restart behavior patterned after Azeroth Questing Companion while remaining fully independent from that application.
- Added seven-day cleanup for stale update installer caches.
- Added a LAN-only Services01 update-feed design so the private GitHub repository does not require a GitHub PAT inside the installed client.
- Added GitHub Actions Windows builds and private GitHub Release creation for `Release client v...` commits.
- Added the initial Inno Setup installer for `GuildRosterClient.exe`.

### Safety

- GRM SavedVariables are data only; the client does not execute imported Lua.
- The client does not read WoW process memory, inject into the game, or automate gameplay.
- Published builds do not contain a reusable server registration key, bridge key, GitHub PAT, or administrative secret.
- Private GitHub release access is not performed from installed clients.
