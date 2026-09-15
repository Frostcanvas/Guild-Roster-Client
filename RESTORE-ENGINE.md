# GRM restore engine

Approved: 2026-09-15
Released: **0.1.0-beta.7**

The FrostLabs Guild Roster Client includes a controlled local restore engine for returning-member recovery. The engine is deliberately narrower than the lossless GRM archive: it can only act on explicitly approved GRM-local fields and never changes guild rank, promotion/demotion, kick/ban state, or other Blizzard-owned runtime state.

## Safety model

1. Recovery identity is the complete Blizzard Player GUID.
2. The current Hogwarts Academy GRM record must contain that exact GUID.
3. World of Warcraft must be closed before any local SavedVariables write.
4. A pre-restore copy of `Guild_Roster_Manager.lua` is created under the FrostLabs local application data restore-backup directory before write-back.
5. The sibling `.bak` file is copied too when present.
6. The edited GRM assignments are reparsed before the source file is replaced.
7. The live source hash is rechecked immediately before write-back; if GRM changed while the restore was being prepared, the write is aborted.
8. Restore outcomes are written to a local per-offer audit file so an already-completed event is not applied twice if Services01 acknowledgement later fails.
9. Same-name/different-GUID data never qualifies for automatic write-back.

## Approved automatic GRM-local fields

The restore engine can apply only the fields selected in the returning-member review:

- GRM custom-note state;
- GRM join-date/history state, merged with current history so a new rejoin does not erase the earlier membership period;
- main/alt relationship when the archived group still exists and current group/main evidence does not conflict;
- birthday information;
- nickname information.

Main/alt restore is fail-closed. If the current character is already assigned to a different group, the archived group no longer exists, or the current group main conflicts with the archived main, the relationship is marked **Needs Review** instead of being guessed or silently rewritten.

## Manual-only note fields

Public Note and Officer Note are never written automatically. When selected, they are recorded as `manual_required` and remain available to the later `Review Notes (X)` workflow for manual Blizzard guild-UI entry.

## History rule

Membership evidence is preserved rather than flattened. The visible history remains newest first, for example:

```text
Rejoined
Left
Joined
```

The write-back engine merges archived join events with current GRM join history and de-duplicates matching entries. Current-only events, including the new rejoin, remain present.

## Recovery prompt behavior

Returning-member recovery offers remain limited to exact-GUID Inactive -> Active events created by Services01. The Windows client checks for pending offers without affecting normal roster synchronization. `Review & Restore` runs the controlled restore engine, `Keep Current` dismisses the event, and `Ask Later` uses the existing server defer behavior.

The note-cleanup queue remains passive and separate from ordinary sync. This restore engine does not implement the general `Review Notes (X)` queue itself.

## Beta 7 release evidence

- version: `0.1.0-beta.7`
- tag: `client-v0.1.0-beta.7`
- release commit: `e4f147fb9d4b9db31c7ade5515e4c98fbcbb6638`
- source verification run: `34937299451` — Windows publish, executable verification, installer build, and artifact upload passed after the restore-engine fixes
- release run: `34937447850` — restore, self-contained publish, executable verification, installer build, artifact upload, and public prerelease publication all passed
- installer: `GuildRosterClient-Setup.exe`
- installer size: `37,297,988` bytes
- SHA-256: `a6ddff9518d38127ee7e684c260f33238a4865fa9a27d12b30b4ddd934af29ec`

Beta 7 is immutable. A later functional client installer must use a later beta number.
