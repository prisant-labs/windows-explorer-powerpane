---
status: accepted
date: 2026-11-12
decision-makers: maintainer
---

# Keep a tab's sort mode in a marker file inside the tab folder

## Context and Problem Statement

Issue [#241 (tab auto-sort)](https://github.com/prisant-labs/windows-explorer-powerpane/issues/241) lets a tab keep its pins sorted by name or by date added, instead of in the order the person arranged them. Each tab therefore needs a stored sort mode: Manual, Name, or Recent. Where should that mode live?

On disk, a group is a folder, a tab is a subfolder named `NN_Name` inside it, and a pin is a `NNN_Name.lnk` shortcut inside the tab. The `NN_` and `NNN_` prefixes hold the manual order. Most of PowerPane's other choices live in `settings.json`, through `AppSettings`.

## Decision Drivers

- **A tab has no stable identity.** Nothing names a tab except its folder path, and that path changes often. `GroupStore.RenameTab` renames the folder. `GroupStore.ReorderTabs` renames every tab folder in the group, because the order is in the prefix.
- **Tabs move as whole folders.** Moving a tab to another group, making a tab its own group, merging groups, and copying a group to another profile all move or copy the folder with everything in it.
- **Profiles must stay separate.** Each profile has its own groups folder, and a sort mode set in one profile must not appear in another.
- **Pins can arrive from another process.** The Explorer context-menu verb from [#242 (Add to PowerPane verb)](https://github.com/prisant-labs/windows-explorer-powerpane/issues/242) writes shortcuts from a separate process that does not load the pane's settings.
- **Settings changes have a cost.** A new `AppSettings` property must be classified in `SidebarControl.PaneShapeKey`, and a reflection test checks that every property is.

## Considered Options

1. A `.pane-sort` marker file inside the tab folder.
2. A dictionary in `settings.json`, keyed by the tab folder's path.
3. Sorting by renaming the pins' `NNN_` prefixes whenever a pin is added, with no stored mode.

## Decision Outcome

Chosen option: **option 1, a `.pane-sort` marker file inside the tab folder**. It is the only option whose stored mode follows the tab through every rename, move, merge, and profile copy without new code. The marker holds the text `name` or `recent`. No marker means Manual, and unreadable or unknown text also reads as Manual.

`GroupStore.LoadPins` reads the marker while it lists the tab folder, and it applies the order through `PinSorter.Order` at scan time. The sort never rewrites the `NNN_` prefixes by itself. A pin written by another process therefore lands in sorted position on the next scan, without that process knowing about sorting.

### Consequences

- Good, because a tab rename or reorder needs no extra code to keep the mode. Neither does a move to another group or a copy to another profile, because `CopyDir` copies every file in the folder.
- Good, because `AppSettings` is unchanged, so `PaneShapeKey` and its reflection test are untouched.
- Good, because a build without this feature ignores the marker and shows the stored order, so going back to an older build is safe.
- Bad, because a person who browses the groups folder in Explorer sees one more file. The folder already shows implementation details such as the `NN_` prefixes, and the name starts with a dot.
- Bad, because the sort mode is not in `settings.json`, so a settings export or reset does not carry or clear it.
- Neutral: the folder watcher ignores a change to the marker's contents, because `GroupStore.TriggersRescan` rescans a content change only for `.lnk` files. The store scans after its own edits, so this does not matter.

### Confirmation

Tests in `tests/PowerPane.Tests` cover the cases that matter. One reads an unknown or empty marker as Manual. One keeps the mode when a tab moves to another group. One creates the tab folder before it writes the marker for a group's implicit empty tab. One removes the marker when the person reorders a sorted tab by hand. A code review should reject any later change that stores a tab's sort mode in `settings.json`.

## Pros and Cons of the Options

### Option 1: a marker file in the tab folder

- Good, because the mode travels with the folder through every operation that moves or copies it.
- Good, because each profile carries its own modes with no extra code.
- Bad, because it adds a visible file in the groups folder.

### Option 2: a dictionary in `settings.json`, keyed by folder path

- Good, because all of PowerPane's other preferences already live there.
- Bad, because every tab rename, tab reorder, move, and merge would need to rewrite the keys, and a missed path loses the mode without a trace.
- Bad, because the verb process from #242 would need to read the settings to place a new pin.
- Bad, because it adds an `AppSettings` property that `PaneShapeKey` must classify.

### Option 3: rewrite the prefixes on every add, with no stored mode

- Good, because there is no new file and no new setting.
- Bad, because nothing records that a tab should stay sorted, so a pin added by another process breaks the order.
- Bad, because every add renames every shortcut in the tab, which is slow and risks losing a pin's name partway through.

## More Information

The full design, with its acceptance criteria, is in the specification for #241. The maintainer chose option 1 in the 2026-11-12 review of that specification's open questions, which numbered this question D4. The "Pin sort modes" row of the "Traps in the code" table in [docs/DEVELOPMENT.md](../../DEVELOPMENT.md) summarises the same rule for people changing `GroupStore`.
