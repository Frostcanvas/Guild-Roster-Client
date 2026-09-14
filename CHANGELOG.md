# Changelog

All notable changes to the FrostLabs Guild Roster Client will be documented in this file.

The project follows a simple chronological changelog while the client is under initial development.

## [Unreleased]

### Added

- Created the private `Frostcanvas/Guild-Roster-Client` repository for the Windows Guild Roster Client.
- Established Guild Roster Manager (GRM) SavedVariables as the initial data source.
- Established `Guild_Roster_Manager.lua` as the file the client will discover and monitor.
- Established `Hogwarts Academy` / `BleedingHollow` as the initial guild scope.
- Established normalized `FGR1` snapshots as the client-to-server upload format.
- Established the Services01 Guild Roster API at `http://10.0.10.246:8767` as the initial LAN-only synchronization target.
- Planned local retry/outbox storage under `%LOCALAPPDATA%\FrostLabs\GuildRoster\Outbox`.
- Planned SHA-256 snapshot deduplication so unchanged roster data is not repeatedly uploaded.
- Planned client status controls for source-file state, server state, last successful parse/sync, queued items, client version, and manual `Sync Now`.
- Recorded the current development WoW installation root as `D:\Battle.net\World of Warcraft`. The client must detect installations and allow a user-selected override rather than hard-coding this path.

### Safety

- The client will parse GRM SavedVariables as data only; it will not execute imported Lua.
- The client will not read WoW process memory, inject into the game, or automate gameplay.
- Partial or ambiguous GRM parses must fail safely and must not upload a snapshot that could incorrectly mark guild members inactive.
- Published builds must not contain a reusable server registration or administrative secret.

### Planned integration

- Upload current roster state to the PostgreSQL-backed Guild Roster API.
- Preserve Blizzard player GUIDs as stable WoW character identities.
- Support main/alt relationships and historical GRM backfill where available.
- Link roster identities to Discord numeric user IDs through the shared Guild Roster database.
- Allow Guild Executive to consume the shared Guild Roster person/identity view without becoming the canonical roster store.
