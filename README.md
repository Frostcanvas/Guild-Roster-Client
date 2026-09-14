# FrostLabs Guild Roster Client

Private Windows client for the FrostLabs Hogwarts Academy guild roster system.

This is a **standalone application**. It is separate from Azeroth Questing Companion: it has its own repository, executable, installer, install location, local settings, update channel, release history, and lifecycle. The only thing intentionally reused from Azeroth Questing Companion is the proven user experience pattern for update checks, installer staging, integrity verification, and in-place upgrades.

The client reads Guild Roster Manager (GRM) SavedVariables from World of Warcraft Retail, normalizes the configured guild roster into the FrostLabs `FGR1` protocol, and uploads complete validated snapshots to the LAN-only Guild Roster API on Services01.

## Current status

Initial Windows shell and self-update framework are implemented. GRM parsing and roster upload are the next development step.

## Data source

The client watches:

```text
Guild_Roster_Manager.lua
```

under a Retail WoW account SavedVariables directory. The current development WoW root is:

```text
D:\Battle.net\World of Warcraft
```

The client discovers account folders under:

```text
D:\Battle.net\World of Warcraft\_retail_\WTF\Account\*\SavedVariables
```

and lets the user select/override the exact SavedVariables folder. A specific account identifier is not hard-coded into the application.

Initial guild scope:

```text
Guild: Hogwarts Academy
Realm: BleedingHollow
```

The client reads GRM SavedVariables as structured data only. It does not execute Lua, read WoW process memory, inject into the game, or automate gameplay.

## Services01 target

```text
Guild Roster API: http://10.0.10.246:8767
Roster website:  http://10.0.10.246:8767/roster
```

The API remains LAN-only.

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
%LOCALAPPDATA%\FrostLabs\GuildRoster\Updates
%LOCALAPPDATA%\FrostLabs\GuildRoster\Outbox
```

The outbox is retry state only. The canonical roster/history database remains PostgreSQL on Services01.

## Build

The project targets .NET 10 Windows Forms and produces a self-contained Windows x64 build.

GitHub Actions builds `GuildRosterClient-Setup.exe` using Inno Setup on each push to `main`. A commit whose message starts with `Release client v` also creates/updates a private GitHub Release for this client only.

The initial private build is not yet declared a stable production release. Code-signing policy can be added before a future broadly distributed stable build.

## Safety rules

- Never embed the Services01 registration key, bridge key, GitHub token, or other reusable secret in source or release binaries.
- Only complete validated Hogwarts Academy roster snapshots may be uploaded as complete `FGR1` snapshots.
- A partial or ambiguous GRM parse must fail closed so it cannot incorrectly mark members inactive.
- GRM schema changes must surface as parser errors rather than silently changing roster meaning.
