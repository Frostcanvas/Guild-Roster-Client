# Review Notes / Recovery Review workflow

Approved: 2026-09-15

This document defines the shared Windows-client review surface for manual guild-note repair and historical recovery review. It does not bypass Blizzard protections and does not automate unsupported Public/Officer note writes.

## Core UX

The client exposes a passive dashboard/top-bar button labeled `Review Notes (X)`.

- `X` counts unresolved pending and **Needs Review** note/recovery items.
- already-correct, **Done**, and intentionally **Skip** items are excluded from `X`.
- synchronization never opens this workflow automatically for ordinary note cleanup.
- progress is persisted so the operator can stop and resume later.
- queue order is **A-Z by character name**, case-insensitive, with realm as the deterministic tie-breaker.
- the complete Blizzard Player GUID remains the identity key behind every item.

The note-repair queue and returning-member restore review use the same review shell. Returning-member events may still surface automatically under the existing exact-GUID Inactive -> Active recovery rules, but the visual review component is shared rather than maintaining a second unrelated UI.

## Review header

Every review item begins with the character identity in this order:

```text
<CharacterName> - <Realm>
Player GUID: <complete Blizzard Player GUID>
```

The header may also show Active/Inactive state and Main/Alt classification as read-only context. Name/realm are display attributes only; the complete Player GUID is the actual identity key.

## Note review section

Show current, historical, and suggested values side-by-side:

- current Public Note;
- suggested Public Note;
- current Officer Note;
- suggested Officer Note;
- recovered historical Public/Officer note values when GRM retained them;
- GRM custom note when useful as context.

Suggestion rules:

- declared main -> `<CharacterName>-Main`;
- declared alt -> `<MainCharacterName>-Alt`;
- unknown/ambiguous main-alt evidence -> no guessed Public Note; mark **Needs Review**;
- Officer Note -> `Joined: <date>` only when GRM join/history evidence supports the date;
- unknown/conflicting join-date evidence -> mark **Needs Review** instead of guessing;
- exact already-correct notes are skipped automatically;
- differing non-empty current notes are never silently replaced.

## Membership/history context

The same review window includes the historical context needed to make a safe decision:

- membership history displayed newest first, for example `Rejoined` -> `Left` -> `Joined`;
- former-member and leave/rejoin history;
- retained GRM note history/recovery candidates;
- current and historical main/alt evidence;
- GRM administrative/history information as read-only context;
- join-date evidence and any conflicting/unknown state;
- source/archive timestamp or provenance when useful.

No guild rank is restored or changed from this workflow. Rank history may be displayed read-only.

## Actions

Manual note-review actions:

- **Done** — operator completed the required manual guild-UI change or confirmed the item is correct;
- **Skip** — intentionally omit the item from the current repair plan;
- **Needs Review** — preserve it as unresolved and keep it in `Review Notes (X)`.

Returning-member recovery actions continue to be:

- **Review & Restore**;
- **Keep Current**;
- **Ask Later**.

These actions use the same character header/history component. `Review & Restore` may only select approved recoverable categories and must preserve pre-restore state before any future write-back implementation.

## Roster-site relationship

The main Hogwarts Academy Active/Inactive table remains compact. GRM administrative/history information and former-member/leave history belong in the character detail/expanded view rather than extra main-table columns. The review window may reuse that historical information so note/recovery decisions do not require switching between multiple screens.

Achievement Points remain archived but are not shown by default. Mythic score, guild reputation, and professions are optional secondary details and are not required in the note-review workflow.

## Safety

- Never automate Blizzard-protected Public/Officer note writes.
- Never automate guild-rank changes, promotion/demotion, kick, or ban actions.
- Never use character name/realm as the recovery identity key when a Player GUID is available.
- Same-name/different-GUID records require manual review.
- Preserve historical data rather than overwriting it.
- Do not commit roster contents or protected credentials to GitHub.
