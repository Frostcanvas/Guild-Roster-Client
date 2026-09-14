# Changelog

All notable changes to the FrostLabs Guild Roster Client will be documented in this file.

The project follows a simple chronological changelog while the client is under initial development.

## [Unreleased]

### Added

- Created the private `Frostcanvas/Guild-Roster-Client` repository for the Windows Guild Roster Client.
- Added the initial .NET 10 Windows Forms client shell and self-contained Windows x64 build project.
- Established Guild Roster Manager (GRM) SavedVariables as the initial data source.
- Established `Guild_Roster_Manager.lua` as the file the client will discover and monitor.
- Established `Hogwarts Academy` / `BleedingHollow` as the initial guild scope.
- Established normalized `FGR1` snapshots as the client-to-server upload format.
- Established the Services01 Guild Roster API at `http://10.0.10.246:8767` as the initial LAN-only synchronization target.
- Added GRM source discovery for Retail account SavedVariables folders under common WoW installation roots, including the current `D:\Battle.net\World of Warcraft` installation.
- Confirmed the current development source at `D:\Battle.net\World of Warcraft\_retail_\WTF\Account\50130108#1\SavedVariables`; the account-specific directory is discovered/selected at runtime and is not hard-coded into the client.
- Added a SavedVariables folder picker and file watcher for `Guild_Roster_Manager.lua`.
- Added local settings under `%LOCALAPPDATA%\FrostLabs\GuildRoster\settings.json`.
- Added local retry/outbox storage under `%LOCALAPPDATA%\FrostLabs\GuildRoster\Outbox` for the upcoming roster uploader.
- Added automatic client update checks with `stable` and `beta` channels.
- Added update download staging under `%LOCALAPPDATA%\FrostLabs\GuildRoster\Updates\<version>`.
- Added SHA-256 and file-size verification before an update installer can run.
- Added silent elevated Inno Setup update launch with close/restart application behavior matching the Azeroth Questing Companion update flow.
- Added seven-day cleanup for stale update installer caches.
- Added a LAN-only Services01 update feed design so the private GitHub repository does not require a GitHub PAT inside the installed client.
- Added GitHub Actions Windows builds and private GitHub Release creation for `Release client v...` commits.
- Added the initial Inno Setup installer for `GuildRosterClient.exe`.
- Added client controls for source-file state, Services01 URL, update channel, update checking, settings persistence, and opening the Hogwarts Academy roster site.

### Safety

- The client will parse GRM SavedVariables as data only; it will not execute imported Lua.
- The client will not read WoW process memory, inject into the game, or automate gameplay.
- Partial or ambiguous GRM parses must fail safely and must not upload a snapshot that could incorrectly mark guild members inactive.
- Published builds must not contain a reusable server registration key, bridge key, GitHub PAT, or administrative secret.
- Private GitHub release access is not performed from installed clients; update distribution uses the LAN-only Services01 feed instead.

### Planned integration

- Implement the GRM parser and complete-snapshot validation.
- Upload current roster state to the PostgreSQL-backed Guild Roster API.
- Add SHA-256 snapshot deduplication so unchanged roster data is not repeatedly uploaded.
- Preserve Blizzard player GUIDs as stable WoW character identities.
- Support main/alt relationships and historical GRM backfill where available.
- Link roster identities to Discord numeric user IDs through the shared Guild Roster database.
- Allow Guild Executive to consume the shared Guild Roster person/identity view without becoming the canonical roster store.
