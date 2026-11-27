---
status: accepted
date: 2026-11-26
decision-makers: maintainer
---

# Mark a pin pinned to top with a flag in its file name

## Context and Problem Statement

Issue [#301 (keep a pin at the top)](https://github.com/prisant-labs/windows-explorer-powerpane/issues/301) lets a person keep chosen pins above the rest of a tab, whatever the tab's sort order. Each pin therefore needs a stored "pinned to top" state. Where should that state live?

A pin is a `NNN_Name.lnk` shortcut in a tab folder, and the `NNN_` prefix holds its manual order. A tab's sort mode lives in a `.pane-sort` marker file, as [decision 0001](0001-tab-sort-mode-in-a-marker-file.md) records.

## Decision Drivers

- **Pin names are not unique in a tab.** `GroupStore.UniqueLink` avoids only an identical file name, so `001_Apollo.lnk` and `002_Apollo.lnk` can sit side by side. A list of names cannot point at exactly one pin.
- **Every reorder renames every pin.** `ReorderPins` and `InsertPinAt` renumber the whole tab, so anything keyed by file name drifts.
- **Pins arrive from another process.** The "Add to PowerPane" verb writes shortcuts from a process that loads no settings and never reads the sort marker.
- **The sort marker cannot hold more.** Its parser reads unknown text as Manual, and every reorder deletes it.
- **Existing tabs must load unchanged,** and an older build must not lose pins.

## Considered Options

1. A flag in the pin's file name, after the number: `003!_Apollo.lnk`.
2. A reserved prefix, such as `000_`, for every pinned pin.
3. A `.pane-top` file listing the names of pinned pins.
4. Two numbered bands of prefixes, `0xx_` for pinned pins and `1xx_` for the rest.
5. A `.pane-top` file holding a count: the first N pins in stored order are pinned.

## Decision Outcome

Chosen option: **option 1, a flag in the file name**. The flag belongs to one file, so it cannot point at the wrong pin, and it follows the pin through rename, copy and profile copy without new code. One parser, `GroupStore.SplitPin`, and one formatter, `GroupStore.PinStem`, serve every writer of a pin's name. The verb process needs no change: it writes an ordinary pin, and the scan puts it below the pinned ones.

### Consequences

- Good, because existing tabs need no migration. A tab with no flagged file reads exactly as before.
- Good, because a pin keeps its stored number when it is pinned, so in a Manual tab an unpinned pin returns to its old place.
- Good, because the pin caches from PR #294 are keyed by target path, so the flag adds no cache entry.
- Bad, because an older build shows a pinned pin under its whole file name, such as `003!_Apollo`. The pin still works, and the flag returns on upgrade.
- Bad, because a person who renames the shortcut by hand in Explorer and drops the `!` unpins it. The order number already behaves this way.

### Confirmation

Tests in `tests/PowerPane.Tests` cover the parser and formatter, the scan, and each writer with a pinned pin: `SetPinTop`, `ReorderPins`, `InsertPinAt` through `AddPinAtIndex` and `MovePinToTabAt`, `MovePinToTab`, `MovePin`, `RenamePin`, `SortPinsOnce`, `SetPinSort`, `AddPins`, `RemovePin` and `CopyGroupFolder`. A code review should reject a new writer of a pin's name that does not use `PinStem`.

## Pros and Cons of the Options

### Option 1: a flag in the file name

- Good, because the state cannot drift from the pin it describes.
- Bad, because older builds show the raw name.

### Option 2: a reserved `000_` prefix

- Good, because an older build sorts the pinned pins first anyway.
- Bad, because every pinned pin shares order 0, so pinned pins lose their own order, and every writer must know the pinned count.

### Option 3: a list of names in `.pane-top`

- Good, because an older build ignores the file.
- Bad, because a name can match two pins, and every rename or move must edit the list. A rename in Explorer leaves a stale entry.

### Option 4: two bands of prefixes

- Bad, because existing tabs already number from `001_`, so it needs a migration, and three digits cap each band.

### Option 5: a count in `.pane-top`

- Good, because the verb and `NextPinOrder` need no change.
- Bad, because a pin removed outside PowerPane silently pulls the next pin into the band, and every removal must adjust the count.
