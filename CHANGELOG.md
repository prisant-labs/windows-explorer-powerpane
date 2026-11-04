# Changelog

## [Unreleased]

### Changed
- Each row on the Pane sections settings page has its own icon. They all shared one list icon, which looked like the drag grip beside it ([#207](https://github.com/prisant-labs/windows-explorer-quickpane/issues/207)).
- Rows on the Pane sections and Quick actions settings pages have an info button beside the label. Its tooltip says what the row is, and for a section, when it does not appear. Built-in quick actions move their description there, so their rows are shorter and more fit on screen. A custom command keeps its template on the row ([#205](https://github.com/prisant-labs/windows-explorer-quickpane/issues/205)).
- The SSH section is now called Remote drives on the Pane sections page, in the pane's section header, and in its row menu, the same as its Settings page. A header you renamed keeps your name ([#204](https://github.com/prisant-labs/windows-explorer-quickpane/issues/204)).

### Fixed
- The Backup & restore card in Settings stays at its own height. It stretched to fill the page, with its icon at the top and its buttons in the middle ([#201](https://github.com/prisant-labs/windows-explorer-quickpane/issues/201)).
- Rows on the Quick actions settings page show the same icon as their row in the pane, instead of a blank gap ([#202](https://github.com/prisant-labs/windows-explorer-quickpane/issues/202)).
- On the Pane sections and Quick actions settings pages, the on/off switch has more room from the Move buttons ([#203](https://github.com/prisant-labs/windows-explorer-quickpane/issues/203)).

## [3.12.0.0] - 2026-11-04

Quick actions you can extend: 18 more built-in actions, custom commands, and actions on the files selected in Explorer. QuickPane also tells you when a new version exists. See the [release notes](docs/releases/3.12.0.0.md).

### Added
- QuickPane tells you when a new version exists. At most once a day, it asks GitHub for the latest release. A newer version shows one notice per version, an item in the tray menu, and a dot on About in Settings. Nothing is downloaded or installed, and the notice opens the release page. Turn off "Check for updates automatically" on the About page to stop the daily check ([#176](https://github.com/prisant-labs/windows-explorer-quickpane/issues/176)).
- Ten more quick actions, each off until you turn it on: Copy folder name, Copy parent folder path, Copy as file URL, Copy PowerShell change-directory command, New PowerShell script, New README, New .gitignore, Open PowerShell here, Open WSL here, and Pin this folder ([#189](https://github.com/prisant-labs/windows-explorer-quickpane/issues/189)).
- Custom commands. On the Quick actions settings page, give a command a name and a template such as `code {path}`, and it becomes a row you can turn on, reorder, edit, and delete. The placeholders are `{path}`, `{paths}`, `{dir}`, and `{name}`, and QuickPane quotes each value, so names with spaces or symbols are safe. A program comes from PATH or a full path, starts directly in the folder, and never runs elevated. A placeholder is refused for a program that would read a folder name as code, such as `cmd` or a batch file. Commands in a restored backup arrive turned off ([#190](https://github.com/prisant-labs/windows-explorer-quickpane/issues/190)).
- Quick actions can act on the files selected in Explorer. Eight new actions, each off until you turn it on, copy the selected names, paths, quoted paths, forward-slash paths, WSL paths, file URLs, or Markdown links, one per line, or open the selection in Visual Studio Code. Custom command placeholders `{path}`, `{paths}`, and `{name}` also use the selection when there is one. "Copy path" and the other folder actions still act on the folder. When the selection cannot be read, an action says so in its row and stops, so a command never falls back to the folder by mistake ([#191](https://github.com/prisant-labs/windows-explorer-quickpane/issues/191)).

### Changed
- A quick action added in a new release now starts in its own default state, so an update does not add rows to your pane. Reset to default restores that same state ([#189](https://github.com/prisant-labs/windows-explorer-quickpane/issues/189)).

### Fixed
- With a window maximized, the width grip of a desktop dock with auto-hide off can be dragged again. The maximized window's invisible frame covered the whole grip, so the grip is now wider on that dock ([#185](https://github.com/prisant-labs/windows-explorer-quickpane/issues/185)).

## [3.11.0.2] - 2026-11-03

The first published release of the redesigned Settings window. 3.11.0.0 and 3.11.0.1 were built as drafts and never published, so this release also carries everything listed under both.

### Added
- The desktop dock can sit on the left or the right edge of its display. A new Edge row under the Desktop dock card on the Pane page picks the side; left stays the default, so existing settings keep today's placement. The width grip and the title chevron follow the edge ([#177](https://github.com/prisant-labs/windows-explorer-quickpane/issues/177)).
- On an edge that is shared with another display, auto-hide now waits about 300 ms before opening, so moving the pointer from one display to the other no longer flashes the dock open ([#177](https://github.com/prisant-labs/windows-explorer-quickpane/issues/177)).
- On the Pane sections and Quick actions Settings pages, drag a row by the dotted handle at its left edge to move it, with a line showing where it will land. Press Escape to cancel a drag. Move up and Move down stay for keyboard use ([#178](https://github.com/prisant-labs/windows-explorer-quickpane/issues/178)).
- Both pages now have a Reset to default button, after a confirmation, that restores the default order and the on/off state of every row. Section names you renamed are kept ([#178](https://github.com/prisant-labs/windows-explorer-quickpane/issues/178)).

### Changed
- Settings now opens on the Pane page. The navigation order is Pane, Pane sections, Groups & profiles, Quick actions, Recent apps, Keyboard, Remote drives, Backup & restore, and About ([#180](https://github.com/prisant-labs/windows-explorer-quickpane/issues/180)).
- The Settings page for SSH connections is now called Remote drives, which names what you get (a remote folder that shows up as a drive letter) rather than the protocol behind it. A search for "ssh" still finds it ([#175](https://github.com/prisant-labs/windows-explorer-quickpane/issues/175)).

### Fixed
- A long inline confirmation in Settings wraps its message and scrolls into view, so its buttons stay inside the window.

## [3.11.0.1] - 2026-11-02

Never published as a release. Its changes first ship in 3.11.0.2. 3.11.0.0 was built as a draft and never published, because a desktop test found the defects fixed below, so this version also carries everything listed under 3.11.0.0.

### Added
- A Quick actions section in the pane, off by default: one click to copy the current folder's path (plain, quoted, forward-slash, WSL, or a Markdown link), add a new text file, Markdown file, or dated folder, or open the folder in Windows Terminal (falling back to PowerShell) or Visual Studio Code. Turn it on from the new Quick actions Settings page, which also enables, disables, and reorders each action; a pinned folder's right-click menu offers the same actions against that folder. Inside a file dialog, or on the desktop dock, the section stays hidden, since neither has a current folder the way an Explorer window does.
- A Pane color card on the Settings Pane page offers a row of color swatches for the pane's background, matching Windows 11 Personalization > Colors. Default keeps Explorer's own color; any other choice is your own opt-in and applies to every open pane at once, with no restart ([#134](https://github.com/prisant-labs/windows-explorer-quickpane/issues/134)).
- On a multi-monitor computer, a new Display row under the Desktop dock card picks which monitor the dock sits on, by name and resolution. "Primary display" (the default) always tracks whichever monitor Windows currently calls primary; a specific choice is remembered by a stable device id and falls back to the primary display if that monitor is ever disconnected. The dock re-places itself when a display is connected, disconnected, rearranged, or changes resolution ([#146](https://github.com/prisant-labs/windows-explorer-quickpane/issues/146)).

### Changed
- When the groups folder changes in several bursts while background work is busy, QuickPane now rescans it once instead of once per burst. The log also notes when background work backs up or one task runs longer than 5 seconds ([#77](https://github.com/prisant-labs/windows-explorer-quickpane/issues/77)).
- Icons and drive details now load in the background. A pane appears at once with generic icons, and the real ones fill in a moment later, so a slow disk, a custom icon handler, or an expanded folder with thousands of subfolders no longer holds up Explorer. A finished icon now updates only the rows that show it, instead of rebuilding every pane section ([#76](https://github.com/prisant-labs/windows-explorer-quickpane/issues/76), [#78](https://github.com/prisant-labs/windows-explorer-quickpane/issues/78)).
- Changing a setting that does not affect what the pane shows, such as ending a drag of the pane's width or editing the keyboard shortcut, no longer rebuilds every open pane, so expanded folders stay open. Changes to sections, profiles, the groups folder, or SSH drives still rebuild the pane ([#51](https://github.com/prisant-labs/windows-explorer-quickpane/issues/51)).
- Renaming, moving, merging, adding, and deleting groups, tabs, and pins from the pane now happens in the background, so a groups folder on a slow or network disk no longer freezes Explorer during an edit. An edit started just before a profile switch is dropped instead of landing in the other profile, and checking whether a drop on the pane holds folders gives up after a short wait instead of the network timeout ([#75](https://github.com/prisant-labs/windows-explorer-quickpane/issues/75)).
- Each row on the Pane sections page is now a compact, single-line card instead of a full two-line one, since the On/Off switch already says whether the section shows ([#154](https://github.com/prisant-labs/windows-explorer-quickpane/issues/154)).
- About now sits as the last item in the navigation pane's own list, directly under Backup & restore, instead of pinned below it behind a divider ([#154](https://github.com/prisant-labs/windows-explorer-quickpane/issues/154)).
- Settings changes are now written to `settings.json` in the background, so a slow or redirected AppData folder no longer freezes the Settings window or stalls Explorer. QuickPane waits for the last write before it exits and before it makes a backup, and a backup now reports "Backup failed" if the settings could not be written, instead of saving an older copy ([#163](https://github.com/prisant-labs/windows-explorer-quickpane/issues/163)).

### Fixed
- A beside pane (a "Beside Explorer window" pane, or a dialog's pane after it was moved or forced beside) no longer trails behind the window it follows while that window is dragged. It now hides for the length of the drag and reappears in place once the drag ends ([#147](https://github.com/prisant-labs/windows-explorer-quickpane/issues/147)).
- Each Settings navigation item's icon now sits on the same line as its label, instead of slightly above it ([#154](https://github.com/prisant-labs/windows-explorer-quickpane/issues/154)).
- Keyboard focus on a Settings navigation item now draws a ring around the item's own rounded highlight, instead of thin lines above and below it across the full width of the column ([#150](https://github.com/prisant-labs/windows-explorer-quickpane/issues/150)).
- Turning on the desktop dock with auto-hide off no longer walks the dock across the screen and squeezes every maximized window into a narrow strip at the far edge. The dock now reserves one strip at the monitor's left edge and keeps it through settings changes and taskbar changes ([#145](https://github.com/prisant-labs/windows-explorer-quickpane/issues/145)).
- The icon at the left of each Settings card is now drawn in the theme's text color. In dark mode it was near-black on a dark card and almost invisible ([#149](https://github.com/prisant-labs/windows-explorer-quickpane/issues/149)).
- Auto-hide and Show on all virtual desktops, under the Desktop dock card, and Auto-hide under Profile tabs, now grey out while their own card's switch is off, instead of staying active controls that do nothing until the switch is back on ([#150](https://github.com/prisant-labs/windows-explorer-quickpane/issues/150)).
- The Settings Pane page shows its icons again, in the navigation pane and on the Pane mode, File dialogs, Desktop dock, and Profile tabs cards. An edit made for the pane color card had emptied them ([#134](https://github.com/prisant-labs/windows-explorer-quickpane/issues/134)).
- QuickPane now guards against a crash that could end it after Explorer restarts. When a window it draws into loses the window above it, QuickPane releases that window's content before Windows text input can trip over the dead handle ([#148](https://github.com/prisant-labs/windows-explorer-quickpane/issues/148)).
- Opening Settings no longer logs a "Specified index is already in use" crash, and each profile on the Groups page now shows its group count instead of staying at "Loading groups…" ([#143](https://github.com/prisant-labs/windows-explorer-quickpane/issues/143)).
- The guard against the crash after an Explorer restart now also checks whenever any window closes, and whenever one of QuickPane's own windows starts to move, so a pane whose host window vanished is released sooner. The once-a-second check also keeps running while QuickPane is busy, which it is during an Explorer restart ([#159](https://github.com/prisant-labs/windows-explorer-quickpane/issues/159), [#148](https://github.com/prisant-labs/windows-explorer-quickpane/issues/148)).
- A beside pane stays hidden for the whole of a keyboard move of the window it follows (Alt+Space, Move, then the arrow keys) and of a drag with the mouse buttons swapped, instead of reappearing after 4 seconds in the middle of the move ([#161](https://github.com/prisant-labs/windows-explorer-quickpane/issues/161)).
- When the display chosen for the desktop dock is disconnected, the Display row now shows "Saved display (not connected)" instead of pretending "Primary display" is chosen, so picking "Primary display" really switches the dock to it ([#162](https://github.com/prisant-labs/windows-explorer-quickpane/issues/162)).
- Choosing the Display result in Settings search now moves keyboard focus to the Display box, as every other result does ([#164](https://github.com/prisant-labs/windows-explorer-quickpane/issues/164)).
- In a high-contrast theme, a switch that is On but greyed out, such as Auto-hide while Desktop dock is off, keeps its outline instead of fading into the card behind it ([#165](https://github.com/prisant-labs/windows-explorer-quickpane/issues/165)).

## [3.11.0.0] - 2026-10-30

Never published as a release. A desktop test of its draft found defects, and its changes first ship in 3.11.0.2.

Settings is now a Windows 11 Settings-style window, with its own navigation pane, search box, and cards, in place of the old tabbed panel.

### Added
- The Settings window now has a navigation pane, a search box that jumps straight to a setting, a breadcrumb for sub-pages, and a Mica backdrop on Windows 11. Its visual style is ported from Microsoft's WPF Fluent theme and stays scoped to the Settings window, so the pane keeps its existing look ([#89](https://github.com/prisant-labs/windows-explorer-quickpane/issues/89), [#90](https://github.com/prisant-labs/windows-explorer-quickpane/issues/90)).
- Eight pages replace the old tabs: Groups & profiles, Pane, Pane sections, Recent apps, Keyboard, Backup & restore, SSH drives, and About ([#90](https://github.com/prisant-labs/windows-explorer-quickpane/issues/90), [#91](https://github.com/prisant-labs/windows-explorer-quickpane/issues/91), [#92](https://github.com/prisant-labs/windows-explorer-quickpane/issues/92), [#93](https://github.com/prisant-labs/windows-explorer-quickpane/issues/93), [#94](https://github.com/prisant-labs/windows-explorer-quickpane/issues/94), [#95](https://github.com/prisant-labs/windows-explorer-quickpane/issues/95)).
- Every Settings control now has an accessible name, and keyboard navigation moves through the navigation pane, then the open page, in reading order, with a visible focus ring throughout ([#96](https://github.com/prisant-labs/windows-explorer-quickpane/issues/96)).

### Changed
- The update check on the About page now runs only when that page opens or you press "Check for updates," not every time Settings opens ([#95](https://github.com/prisant-labs/windows-explorer-quickpane/issues/95)).
- Renaming a profile, reordering its groups, and moving a group to another profile now use buttons, such as Rename, Move up, Move down, and Move to profile, instead of drag and drop ([#91](https://github.com/prisant-labs/windows-explorer-quickpane/issues/91)).
- Reordering pane sections now uses Move up and Move down buttons instead of dragging a row ([#92](https://github.com/prisant-labs/windows-explorer-quickpane/issues/92)).
- Settings now writes how long it took to open, and how long it took to switch pages, to the log, to help decide whether it needs its own UI thread ([#97](https://github.com/prisant-labs/windows-explorer-quickpane/issues/97)).

## [3.10.0.11] - 2026-10-30

### Fixed
- While an Explorer window's border is dragged, the pane now steps aside, so Explorer's own navigation tree stays fully visible during the drag instead of sitting under the pane. The pane returns beside the tree on release. Moving a window by its title bar leaves the pane in place ([#102](https://github.com/prisant-labs/windows-explorer-quickpane/issues/102)).
- An Open or Save dialog with the pane inside no longer switches to the pane beside it. The common Save dialog applies its remembered size just after it appears, and that size change was read as the dialog fighting the pane, which sent that dialog and every later one from the same app beside. The pane is now fitted inside the size the dialog chose, and the dialog keeps one size from one open to the next. Dragging a dialog's border hides the pane until release, and a dialog dragged too narrow to share goes beside on its own without affecting the app's later dialogs ([#103](https://github.com/prisant-labs/windows-explorer-quickpane/issues/103)).
## [3.10.0.10] - 2026-10-30

Never published as a release. Its fix first shipped in 3.10.0.11.

### Fixed
- Dragging the border of an Explorer window that has the pane no longer makes the window flicker. Explorer puts its content back at full width on every step of a drag, and the pane moved it back each time, so Explorer's own navigation tree and the file list jumped under the pane and back about a hundred times in a two-second drag. The pane now leaves the window to Explorer until the mouse button is released, and then restores the layout once. During the drag, Explorer's navigation tree sits under the pane, and it returns beside the pane on release ([#99](https://github.com/prisant-labs/windows-explorer-quickpane/issues/99)).

## [3.10.0.9] - 2026-10-29

These fixes come from an independent review of 3.10.0.8.

### Fixed
- After a profile switch, an edit made before the new profile's groups finished loading was written into the previous profile's folder, and reordering groups could move the previous profile's group folders into the new one. The pane now drops the old groups at once, discards a scan that finished for the old folder, and refuses an edit on a group, tab, or pin outside the current groups folder. This was a regression in 3.10.0.8 ([#68](https://github.com/prisant-labs/windows-explorer-quickpane/issues/68)).
- A pane section closed while a rebuild was waiting could subscribe itself again, so it stayed in memory and kept rebuilding for the rest of the session. Closing a section is now final, and a closed pane ignores rebuilds that were already queued ([#69](https://github.com/prisant-labs/windows-explorer-quickpane/issues/69)).
- With several Windows 11 tabs, the tab in front was read before the list of Explorer windows, so a tab switch during a slow listing could send a click to the previous tab. The tab in front is now read again after the list ([#72](https://github.com/prisant-labs/windows-explorer-quickpane/issues/72)).

### Changed
- Loading settings at startup no longer creates the groups folder on the UI thread. A groups folder on a share that does not answer no longer holds up startup; the background scan creates the folder ([#70](https://github.com/prisant-labs/windows-explorer-quickpane/issues/70)).
- A pin click in an Open or Save dialog's pane goes to the navigation thread first. Recording the app for Recent Apps now happens afterwards in the background, and writes to `recentapps.txt` take turns ([#71](https://github.com/prisant-labs/windows-explorer-quickpane/issues/71)).
- The shared window event hook reports in the log when a hook cannot be removed, or is removed from the wrong thread ([#73](https://github.com/prisant-labs/windows-explorer-quickpane/issues/73)).
- `docs/DEVELOPMENT.md` records that the Recent check can miss a short visit to a folder with the same name as the one before ([#74](https://github.com/prisant-labs/windows-explorer-quickpane/issues/74)).

## [3.10.0.8] - 2026-10-29

### Fixed
- A folder click in the pane could wait for unrelated background work to finish first, such as the Recent check, a probe of an offline network path, or a large drag-and-drop copy. Folder clicks in Explorer windows, file dialogs, and the desktop dock now run on a thread of their own, so a click waits only for an earlier click ([#38](https://github.com/prisant-labs/windows-explorer-quickpane/issues/38)).
- Clicking Browse in Edit path, or opening any folder picker, froze the pane and Explorer for the network timeout, twice, when the starting folder was on a share that did not answer. A disconnected mapped drive timed out once for every level of the path. Each check now gives up after a second, and the picker opens without a starting folder ([#41](https://github.com/prisant-labs/windows-explorer-quickpane/issues/41)).

### Changed
- In an Explorer window with only one tab, a folder click and the Recent check stop at the first matching shell window instead of reading the list of every open tab on the desktop ([#40](https://github.com/prisant-labs/windows-explorer-quickpane/issues/40)).
- The Recent section's 2-second check of the Explorer window in front no longer queries every open Explorer window each time. It first compares the window's title and front tab with the last check, and runs the full query only when one of them changed, or every 30 seconds otherwise ([#39](https://github.com/prisant-labs/windows-explorer-quickpane/issues/39)).
- QuickPane reads the groups folder in the background at startup and when the groups folder setting changes. A groups folder on a slow or unreachable network share no longer holds up startup and the tray icon. Pins appear a moment after the pane does ([#42](https://github.com/prisant-labs/windows-explorer-quickpane/issues/42)).
- The log keeps its files open for the session instead of opening and closing a file for every line. Each line is still written to disk at once, the files can still be opened in an editor while QuickPane runs, and a log file deleted while open is created again ([#46](https://github.com/prisant-labs/windows-explorer-quickpane/issues/46)).
- When the shared icon cache is full, it drops only the icons used longest ago, instead of emptying completely and making every visible row load its icon again ([#47](https://github.com/prisant-labs/windows-explorer-quickpane/issues/47)).
- The record of which paths exist, used to dim offline pins, now holds at most 4,096 paths and forgets the ones used longest ago. It used to keep every path ever shown for as long as QuickPane ran ([#48](https://github.com/prisant-labs/windows-explorer-quickpane/issues/48)).
- The groups folder is rescanned only for changes that can alter the groups: a folder or shortcut added, removed, or renamed, or a shortcut edited in place. Write-time changes on folders and on files such as `desktop.ini` no longer each set off a rescan of every pin ([#49](https://github.com/prisant-labs/windows-explorer-quickpane/issues/49)).
- The crash guard's heartbeat file is rewritten when QuickPane's recorded activity changes, or once a minute when idle, instead of every 4 seconds all day ([#50](https://github.com/prisant-labs/windows-explorer-quickpane/issues/50)).
- Each layout pass on an Explorer window walks its tabs once instead of two or three times ([#45](https://github.com/prisant-labs/windows-explorer-quickpane/issues/45)).
- The once-a-second check for new Explorer windows asks Windows for Explorer windows directly, instead of reading the class name of every window on the desktop ([#44](https://github.com/prisant-labs/windows-explorer-quickpane/issues/44)).
- QuickPane installs each system-wide window event hook once and shares it among the parts that need it. Window moves and resizes anywhere on the desktop used to reach QuickPane twice, and focus changes three times ([#43](https://github.com/prisant-labs/windows-explorer-quickpane/issues/43)).

## [3.10.0.7] - 2026-10-29

Never published as a release. Its fix first shipped in 3.10.0.8.

### Fixed
- In a Windows 11 Explorer window with more than one tab, clicking a folder in the pane opened it in the first tab instead of the tab on screen, and the Recent section recorded the first tab's folder instead of the one being viewed. Both now follow the tab in front ([#35](https://github.com/prisant-labs/windows-explorer-quickpane/issues/35)).

## [3.10.0.6] - 2026-10-28

### Changed
- The settings gear now sits at the bottom left of the pane, under the QuickPane icon, instead of the bottom right. It stays in the same place when the pane collapses ([#33](https://github.com/prisant-labs/windows-explorer-quickpane/issues/33)).

### Fixed
- In the collapsed pane, the expand arrow sat to the right of the strip's centre. It now centres in the strip ([#33](https://github.com/prisant-labs/windows-explorer-quickpane/issues/33)).

## [3.10.0.5] - 2026-10-28

Never published as a release. Its changes first shipped in 3.10.0.6.

### Changed
- On Windows 11 the pane and Settings draw their icons with Segoe Fluent Icons, the shell's current icon set. Windows 10 keeps Segoe MDL2 Assets ([#24](https://github.com/prisant-labs/windows-explorer-quickpane/issues/24)).
- The settings gear stays at the bottom of the pane when the pane is collapsed to its narrow strip, so Settings opens in one click without expanding the pane first ([#22](https://github.com/prisant-labs/windows-explorer-quickpane/issues/22)).

### Fixed
- With a Windows contrast theme on, the pane and Settings kept their own light or dark colors. They now take the contrast theme's colors, and switch back when it is turned off, with no restart ([#30](https://github.com/prisant-labs/windows-explorer-quickpane/issues/30)).
- Screen readers and other accessibility tools could not see any control inside a Settings tab, and several buttons had no name, or only a symbol such as "+" or "✕" for one. Every Settings control is now reachable, and icon and symbol buttons, steppers, text boxes, and the section check boxes carry names that say what they do, such as "Delete group Work" or "Increase recents count" ([#28](https://github.com/prisant-labs/windows-explorer-quickpane/issues/28)).
- In dark mode, the Settings window had a white title bar over its dark content. The title bar now follows the Windows light or dark mode, including a switch made while Settings is open ([#26](https://github.com/prisant-labs/windows-explorer-quickpane/issues/26)).
- The pane always used the default blue instead of the Windows accent color, because it read the accent from a registry key that does not hold it. The active tab, the drag drop line, and the accent-colored Settings controls now follow the accent chosen in Windows, including changes made while QuickPane runs ([#20](https://github.com/prisant-labs/windows-explorer-quickpane/issues/20)).

## [3.10.0.4] - 2026-10-27

Never published as a release. Its changes first shipped in 3.10.0.6.

### Changed
- Add/Remove Programs names the app "QuickPane 3.10.0.4" instead of "QuickPane version 3.10.0.4" ([#18](https://github.com/prisant-labs/windows-explorer-quickpane/issues/18)).

### Removed
- The "Buy me a coffee" button, both at the bottom of the pane and in the Settings footer. It opened the original author's donation page, which does not fund this fork ([#18](https://github.com/prisant-labs/windows-explorer-quickpane/issues/18)).

## [3.10.0.3] - 2026-10-27

### Added
- Each release includes an installer, `QuickPane-<version>-x64-setup.exe`, built by the release workflow and covered by its checksums and attestation. Run over an existing QuickPane, including one from the original project's installer, it upgrades that install in place, so Add/Remove Programs keeps one entry and shows the new version.
- The installer puts `LICENSE.txt` beside the program.

### Changed
- Add/Remove Programs lists Prisant Labs as the publisher, and its links go to this fork.
- The installer reads its version from the built program, so a release no longer needs `Installer.iss` edited.

## [3.10.0.2] - 2026-10-27

This release includes every change listed under 3.10.0.1 in [CHANGELOG.md](https://github.com/prisant-labs/windows-explorer-quickpane/blob/main/CHANGELOG.md), plus the fixes below.

### Added
- Releases are built by a GitHub Actions workflow when a version tag is pushed, from exactly the tagged commit on `main`. Each release ships a zip, `SHA256SUMS.txt`, and a build provenance attestation that `gh attestation verify` checks.
- The release zip includes `LICENSE`.

### Fixed
- In a Windows 11 Explorer window with more than one tab, only one tab made room for the pane. Every other tab stayed at full width, so either the pane covered Explorer's navigation pane or the tab covered the pane. Every tab now makes room, including tabs opened later, and the layout keeps working after the first tab is closed ([#4](https://github.com/prisant-labs/windows-explorer-quickpane/issues/4), [lso2/QuickPane#3](https://github.com/lso2/QuickPane/issues/3)).
- The pane showed its profile row even with only one profile, as a lone "Profile 1" chip that switched nothing. The row now appears once a second profile exists ([#5](https://github.com/prisant-labs/windows-explorer-quickpane/issues/5)).

## [3.10.0.1] - 2026-10-21

Never published as a release. Its changes first shipped in 3.10.0.2, the first published release of the prisant-labs/windows-explorer-quickpane fork. Both build on upstream 3.10.0. Fork releases add a fourth version number to the upstream version they build on.

### Added
- A "File dialogs" setting on the Pane tab, so the pane in Open and Save dialogs can follow Explorer windows, sit beside the dialog, or stay off.
- A "Browse..." button in the "Edit path" prompt, which opens a folder or file picker at the pin's current target.
- A unit-test project in `tests/QuickPane.Tests`, run with `scripts/test.ps1`.
- Build and test scripts in `scripts/` that work with or without the .NET Framework 4.8 targeting pack installed.
- A GitHub Actions workflow that builds the app and runs the tests on every push and pull request.

### Changed
- "Add folder..." and the groups-folder "..." button open the modern Explorer-style folder picker instead of the legacy "Browse For Folder" tree ([lso2/QuickPane#2](https://github.com/lso2/QuickPane/issues/2)).
- The update notice in the Settings footer checks this fork's releases instead of upstream's, so it never offers an upstream installer that would replace the fork's fixes. Until the fork has a published release, the footer reads "Up to date" instead of "Update check failed".
- The update notice shows a fork release's full four-part version, such as 3.10.0.2, instead of dropping the fourth part.

### Fixed
- A group with more than one tab had no way to add a folder or file from a tab's menu. The tab menu now has "Add folder..." and "Add file...", and the pin lands in that tab ([lso2/QuickPane#1](https://github.com/lso2/QuickPane/issues/1)).
- "Delete tab" erased the tab and its pins immediately. It now asks first when the tab has pins, and sends the tab's folder to the Recycle Bin ([lso2/QuickPane#5](https://github.com/lso2/QuickPane/issues/5)).

## [3.10.0] - 2026-09-14

### Added
- Search box at the top of the pane that filters every folder as you type.
- App Folders section, which offers the folder you set for the app whose Save or Open dialog the pane is in.
- "Always start {app} here" on a folder's right-click menu inside a file dialog.
- Recent Apps section listing the applications you have been using, where a row opens its app and its right-click menu offers the folder that app's dialog was last sent to.
- Browser rows in Recent Apps open out into that browser's profiles, where each row opens its own window.
- Setting for which apps Recent Apps lists: the ones you open, the ones whose Save and Open dialogs you use, or both.
- Keyboard shortcut that moves focus into the pane, which stays off until it is turned on under Settings, Hotkeys.
- A This PC row inside the This PC section, which opens This PC and expands to the drives beneath it.
- Files dragged from Explorer onto a pane folder move, copy, or become shortcuts, under the same Ctrl, Shift and Alt rules Explorer uses.
- Settings footer showing the running version and a Download button when a newer release is published.
- Connections tab names both components an SSH mount needs, links each download, and offers the install commands to copy.

### Changed
- Settings are grouped into tabs: Groups, Pane, Hotkeys, Connections and Backup.
- The settings Pane tab is laid out in two columns.
- Search box, App Folders and Recent Apps each have a checkbox and a place in the section order.
- The magnifier sits on the right of the pane's search field, as it does in Explorer's.
- The active profile in settings is marked by a filled Active badge.
- Larger type and more spacing on the Pane, Hotkeys, Connections and Backup tabs.
- Section headers show their expand caret at all times.
- Recent folders open out into their subfolders and line up with every other folder row in the pane.
- Programs carry the name and icon Windows shows for them, which tells side-by-side installs such as Chrome and Chrome Canary apart.
- Deleting a group sends it to the Recycle Bin.
- The mouse wheel scrolls the pane.
- A sideways swipe, a tilt wheel, or Shift with the wheel scrolls the tab strips and the settings profile columns.

### Fixed
- The pane's search box takes the keyboard when it is clicked.
- The search box filters pinned folders and tree rows.
- A heading disappears along with the rows beneath it while a filter is active.

## [3.8.7] - 2026-09-14

### Fixed
- Save and Open dialogs lay out correctly, with the file list, filename box and Save and Cancel buttons in place.
- Save and Open dialogs open faster, with the pane following sooner behind them.
- The pane appears in Save and Open dialogs reliably, including after QuickPane has been running for days.
- Save and Open dialogs keep the size you left them at, instead of growing each time they open.
- The pane is reachable when a dialog opens against the left edge of the screen, and the dialog moves over to make room.
- Ordinary Windows Open and Save dialogs use the pane mode you chose in settings.
- The pane shows up for apps that use the older style of Save dialog, such as IrfanView and Photoshop, sitting against the dialog's left edge.
- Word's Save and Publish dialogs keep their file list and controls visible, with the pane moving to the dialog's left edge.
- Resizing a Save or Open dialog leaves its file list clean, with no leftover pane text painted across the rows.
- The Network section opens Network when you click it.
- Resizing a Save or Open dialog leaves its buttons and bottom row in place, with the pane moving to the dialog's edge.
- Clicking a drive letter in the pane navigates the dialog instead of closing it, and the pane keeps working in that app afterwards.
- Panes sit in the right place and at the right size on a second monitor and on screens at a different scale.
- The group picker opens on the screen you are working on.
- Clicking a folder reaches an Explorer window that is mid-operation.
- QuickPane keeps noticing dialogs and Explorer windows for the whole session.
- Memory use stays steady over a long session.
- The log keeps a month of history without filling up with one repeated line.

## [3.7.0] - 2026-08-13

### Added
- Crash detection with automatic restart, capped at three restarts in ten minutes.
- Start menu shortcut, an optional desktop shortcut, and a "Create desktop shortcut" tray item.
- Troubleshooting submenu in the tray menu, with "Open log folder" and "Restart QuickPane".
- `events.log`, a journal of crashes, restarts, and dialog problems that accumulates across sessions in `%APPDATA%\QuickPane\Logs`.
- Background thread and unobserved task exceptions are recorded.

### Fixed
- Closing QuickPane from the tray menu counted as a crash.
- Save As navigation could report success when the dialog had not moved.
- Clicking a folder did nothing in a Save or Open dialog that exposes no address bar.

## [3.5.1] - 2026-07-11

### Fixed
- Groups could not be reordered within a profile's own list on the settings page, in either the tray window or the sidebar's embedded copy. Dragging a group's grip picked it up and showed a valid drop cursor, but dropping it back into the same list did nothing, because the drop handler only ever moved a group to a different profile column and silently ignored a same-profile drop. Each group row now accepts the drop itself and reorders in place; moving a group to another profile column is unchanged.

### Changed
- The sidebar's gear button now opens the same Settings window the tray icon opens, instead of sliding a second, cramped copy of the settings UI into the sidebar itself. The compact single-column settings layout introduced in 3.1.0 for that embedded copy is removed along with it, since nothing uses it anymore.

## [3.4.1] - 2026-07-06

### Fixed
- System-wide input lag while navigating. Everything that can touch a slow disk, the network, or cross-process COM now runs on one background worker (`WorkQueue`) instead of the UI thread, which shares Explorer's input queue in Inside mode, so a dead network target or a busy Explorer can no longer freeze input across the desktop while CPU sits idle. This covers shortcut resolution, Shell.Application navigation and path queries, the Windows Recent scan, drive detail queries, existence probes, shell file operations from drops, and slow icon extraction.
- Recent tracking no longer installs a global NAMECHANGE hook. That hook fired for nearly every control text change on the desktop and answered each with a cross-process COM enumeration; activation events plus a slow poll now do the same job for a fraction of the cost.
- The Windows Recent folder is scanned newest-first with a resolution budget, so a Recent folder holding hundreds of shortcuts costs only as much as the visible list.
- Pin, group, and recent rows never probe the disk while building. Existence and file-vs-folder facts come from a background probe cache (`PathStatus`); unknown targets render optimistically and correct themselves moments later, dimming exactly like Explorer does when a share is gone.
- Save/Open dialog panes no longer glitch after the dialog is dragged. A pure move fires a location event per pixel but needs no relayout (controls live in client coordinates), and those events used to trip the relayout back-off and permanently stop pane maintenance for that dialog. Back-off now also resets when the dialog really resizes.
- Dialog detection results are cached per window. Foreground changes and sweep passes no longer re-enumerate the child tree of every open dialog in every app (Photoshop's dialogs were enumerated on every focus change), and an unfamiliar picker is logged once instead of every time it takes focus.
- Every message sent into another app's dialog (navigation text, Enter, OK clicks) uses a timeout, so a host app that is busy saving can never hang QuickPane and, through the shared input queue, the shell.
- Unhandled exceptions are now logged to debug.log and dispatcher exceptions are contained, so crashes are visible and one bad event no longer silently kills the tray app. Missed window-destroy events are swept up, so lost hooks no longer leak panes.
- Group reorders are crash-safe. A reorder interrupted midway (locked folder, antivirus, crash) rolls back instead of stranding groups under temp names, and any temp names left by a hard crash are recovered on the next scan.
- Recents and groups change events always arrive on the UI thread, removing a race between the FileSystemWatcher thread and the pane.
- A navigation no longer rebuilds every section of every open sidebar; each section rebuilds only on its own data, coalesced to one rebuild per change burst.
- Settings saves no longer reload the group store (and its FileSystemWatcher) unless the groups folder actually changed.
- Drag-over highlight sticks no more: rows track the active drop target centrally, so highlight always clears when the drag moves on, drops, or leaves the pane.

### Added
- File pins. Files can be pinned everywhere folders can: drop a file on a group, between pins, or on empty section space, or use the new "Add file..." menu item. File pins open the file on click, offer "Open containing folder", and show the file's own icon; recent files can be pinned directly as file pins.
- Drop-intent menu. Dropping a folder (no modifier) onto a pinned folder now asks: Move into, Copy into, Create shortcut inside, Pin to the group, or Cancel, because a silent default was moving folders people meant to pin. Files keep Explorer's silent semantics (move on same drive, copy across), and modifiers always act silently exactly like Explorer: Ctrl copies, Shift moves, Alt or Ctrl+Shift links.
- Dropping files or folders on empty Groups-section space pins them to the last group (creating a "Pinned" group when none exists), and the drag cursor shows the shortcut arrow wherever a drop will pin rather than move.

### Changed
- Drive details (labels, free space, readiness) load from a cached snapshot and refresh in the background on device changes, so an unreachable mapped drive cannot stall pane rebuilds.
- Expanding a folder row enumerates its children on the worker and fills the subtree when ready, so expanding a slow or dead folder cannot freeze input.

## [3.2.15] - 2026-07-05

### Fixed
- White pane on load and toolbar overlap, fixed with a readiness gate: nothing is shifted and the pane stays hidden until the Explorer window is genuinely presented and its toolbar has laid out, with a five-second fallback for windows that never report one.
- Resize lag, fixed by replacing the perpetual 250 Hz enforcer poll with short bursts triggered only when a window first shows its strip, is activated, or attaches; resizes are held purely by location events with nothing polling against them.
- Two regressions were reverted along the way: hand-driving Measure/Arrange/UpdateLayout on the pane's visual root (corrupted the host's layout pipeline), and `SWP_ASYNCWINDOWPOS` on content-host moves (ignored because the reparent attaches input queues, and it broke the move dedup guards).

## [3.1.1] - 2026-06-01

### Added
- Persistent expand/collapse memory. Section headers, groups, and folder subtrees now remember whether they are open, keyed by section type, group folder, and tree path, so the state survives pane rebuilds, profile switches, window switches, and restarts. State is stored in `uistate.txt` in the QuickPane data folder.
- Horizontal scrolling for the profile tab row, so extra profiles stay reachable instead of clipping, using the same wheel handling as the group tabs.

## [3.1.0] - 2026-06-01

### Fixed
- Critical stability fix. File dialog detection now requires the dialog window class (`#32770`). The Windows desktop (Progman/WorkerW) also hosts a `SHELLDLL_DefView`, so the earlier broad match reparented and resized the desktop, which corrupted explorer.exe and took down every Explorer window at once.
- Empty profile columns accept dropped groups, because the card now has a solid fill and is a valid drop target.
- Group rows in the settings page drag from anywhere on the row rather than only the small grip, and the row's delete button is excluded from the drag.

### Added
- Profile tab row beneath the QuickPane title that switches the shown profile, with a "Show profiles tab row" setting and an "Auto-hide (slide out on hover)" setting that mirrors the desktop dock behavior.
- Compact single-column settings layout for the narrow embedded pane, kept separate from the wide multi-column layout used in the tray settings window.
- Profile columns drawn as cards a step lighter than the panel, each with a clear Activate button and an Active label on the shown profile.

## [3.0.0] - 2026-05-31

### Added
- Profiles. Each profile is an independent workspace with its own groups folder, recents count, and sidebar width, and the active profile's values mirror into the live settings the rest of the app reads.
- Profile switching from the tray icon's right-click menu, with a check on the active profile.
- Multi-column profile management in the settings window, with add, remove, and rename for each profile.
- Dragging a group between profile columns to move it, with Ctrl held to drop a copy so the same group can live in more than one profile.
- Resizable settings window.

### Changed
- A settings file from before profiles migrates automatically into "Profile 1" on first load.

## [2.4.0] - 2026-05-30

### Added
- QuickPane attaches to file Open and Save dialogs and honors the selected pane mode. Inside mode shifts every one of the dialog's controls and widens the dialog so nothing overlaps the pane, and beside mode floats a pane against the dialog's edge.
- Dialog navigation by writing the folder path into the File name box and submitting, working with both the classic dialog and the modern common item dialog.

## [2.3.0] - 2026-05-28

### Changed
- The beside-window pane is now an owned window of its Explorer window, so Windows keeps it at that window's z-level, on that window's virtual desktop, and never forces it above other windows. It stays visible when the window loses focus and hides only when the window is minimized or moves to another desktop.

### Added
- Horizontal wheel input scrolls a tab row left and right, while the vertical wheel only ever scrolls the pane.
- All-desktops dock pinning retries after the window has rendered so the pin reliably takes effect.

### Fixed
- Recent reflects folders browsed directly in Explorer, captured through window activation, title changes, and a light periodic check, with a location-URL fallback when the shell folder object returns nothing.

## [2.2.0] - 2026-05-25

### Added
- Native file drag operations. Dragging files from Explorer onto a folder moves on the same drive and copies across drives, with Ctrl to copy, Shift to move, and Alt to create a shortcut, using the shell so progress, conflicts, and undo behave normally.
- Title header with the QuickPane logo and a chevron that collapses or expands the pane in the window modes and toggles auto-hide on the desktop dock.

### Changed
- Helper windows and the dock are tool windows, so QuickPane no longer appears as a blank entry in Alt+Tab.
- The support button reads "Buy me a coffee".

## [2.1.0] - 2026-05-23

### Added
- Optional desktop dock at the left screen edge, registered as an AppBar and independent of the window pane.
- Dock auto-hide that collapses to a thin strip and slides out on hover.
- Setting to show the dock on every virtual desktop.
- Dock clicks navigate the active Explorer window and bring it forward, or open a new window when none is open.

## [2.0.0] - 2026-05-21

### Added
- Beside-window mode that floats a follower pane at the left of each Explorer window without shifting its content, alongside the original inside mode that reparents the pane.
- Off mode for dock-only use.
- Pane mode selector in settings.

## [1.2.0] - 2026-05-18

### Added
- Network and Linux (WSL) as their own sections, each with a visibility checkbox and a proper icon.
- Expandable drives under This PC and expandable folder subtrees throughout, enumerated lazily like the native Explorer tree.

## [1.1.0] - 2026-05-15

### Added
- Group, tab, and pin management with create, rename, and delete.
- Drag to reorder groups, tabs, and pins with a visible drop line, plus a semi-transparent placeholder when reordering tabs.
- Dragging a group onto another merges its tabs, and dragging a tab to empty space makes it a new group.
- A seeded default group on first run so the pane is never empty.

### Changed
- All text entry goes through top-level prompt windows, because the embedded pane cannot take keyboard focus.

## [1.0.0] - 2026-05-12

### Added
- First usable version. A sidebar that reparents into each Explorer window and shifts the window's content to make room.
- Pinned folder groups with horizontally scrollable tabs, where a single-tab group renders like a plain group.
- This PC and Recent sections.
- Settings panel, tray icon, and a "Pin to Quick Pane" folder context-menu verb.
- One-click Inno Setup installer that runs without administrator rights.
