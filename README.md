# QuickPane

![Windows](https://img.shields.io/badge/Windows-10%20%7C%2011-0078D6.svg?logo=windows&logoColor=white)
![.NET Framework](https://img.shields.io/badge/.NET%20Framework-4.8-512BD4.svg?logo=dotnet&logoColor=white)
![C#](https://img.shields.io/badge/C%23-7.3-239120.svg?logo=csharp&logoColor=white)
![UI](https://img.shields.io/badge/UI-WPF-1f6feb.svg)
![Version](https://img.shields.io/github/v/release/prisant-labs/windows-explorer-quickpane?label=Version&color=success)
![License](https://img.shields.io/badge/License-MIT-orange.svg)

> **About this fork.** This is a fork of [lso2/QuickPane](https://github.com/lso2/QuickPane). It adds fixes that were first reported upstream as issues, plus unit tests and CI. Fork versions carry a fourth number (for example 3.10.0.1), and [CHANGELOG.md](CHANGELOG.md) lists what each one changes. To build from source, see [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md). The in-app update notice checks this fork's releases, not upstream's. Everything below this note is upstream's README.
>
> **What this fork adds.** Each item links to the release notes for the published release that first shipped it. Downloads are on the [Releases](https://github.com/prisant-labs/windows-explorer-quickpane/releases) page.
>
> - **Windows 11 tabs:** every tab in an Explorer window makes room for the pane, including tabs opened later, so the pane no longer covers Explorer's sidebar or hides under a tab. [3.10.0.2](docs/releases/3.10.0.2.md) ([#4](https://github.com/prisant-labs/windows-explorer-quickpane/issues/4), [lso2/QuickPane#3](https://github.com/lso2/QuickPane/issues/3))
> - **Add to a tab:** a tab's right-click menu has "Add folder..." and "Add file...", and the pin lands in that tab. [3.10.0.2](docs/releases/3.10.0.2.md) ([lso2/QuickPane#1](https://github.com/lso2/QuickPane/issues/1))
> - **Safer "Delete tab":** deleting a tab that holds pins asks first, and sends the tab's folder to the Recycle Bin. [3.10.0.2](docs/releases/3.10.0.2.md) ([lso2/QuickPane#5](https://github.com/lso2/QuickPane/issues/5))
> - **Profile row:** the profile row appears only once a second profile exists. [3.10.0.2](docs/releases/3.10.0.2.md) ([#5](https://github.com/prisant-labs/windows-explorer-quickpane/issues/5))
> - **File dialogs setting:** the pane in Open and Save dialogs can follow Explorer windows, sit beside the dialog, or stay off. [3.10.0.2](docs/releases/3.10.0.2.md)
> - **Browse in "Edit path":** the "Edit path" prompt has a "Browse..." button that opens a picker at the pin's current location. [3.10.0.2](docs/releases/3.10.0.2.md)
> - **Modern folder picker:** choosing a folder opens the Explorer-style picker instead of the old "Browse For Folder" tree. [3.10.0.2](docs/releases/3.10.0.2.md) ([lso2/QuickPane#2](https://github.com/lso2/QuickPane/issues/2))
> - **Update notice follows this fork:** the Settings footer checks this fork's releases and shows the full four-part version. [3.10.0.2](docs/releases/3.10.0.2.md) ([#2](https://github.com/prisant-labs/windows-explorer-quickpane/pull/2))
> - **Tested, verifiable builds:** unit tests and a build on every pull request, and releases built from the tagged commit with checksums and a provenance attestation. [3.10.0.2](docs/releases/3.10.0.2.md)
> - **Installer:** each release includes a per-user installer that upgrades an existing QuickPane in place, so Add/Remove Programs keeps one entry with the right version. [3.10.0.3](https://github.com/prisant-labs/windows-explorer-quickpane/releases/tag/v3.10.0.3) ([#15](https://github.com/prisant-labs/windows-explorer-quickpane/issues/15))
> - **No donation button:** the "Buy me a coffee" button is gone, because it funded the original author rather than this fork, and Add/Remove Programs names the app "QuickPane <version>". [3.10.0.6](https://github.com/prisant-labs/windows-explorer-quickpane/releases/tag/v3.10.0.6) ([#18](https://github.com/prisant-labs/windows-explorer-quickpane/issues/18))
> - **Windows accent color:** the active tab, the drop line, and accent-colored Settings controls follow the accent chosen in Windows, including changes made while QuickPane runs. [3.10.0.6](https://github.com/prisant-labs/windows-explorer-quickpane/releases/tag/v3.10.0.6) ([#20](https://github.com/prisant-labs/windows-explorer-quickpane/issues/20))
> - **Contrast themes:** with a Windows contrast theme on, the pane and Settings take its colors, and switch back when it is turned off. [3.10.0.6](https://github.com/prisant-labs/windows-explorer-quickpane/releases/tag/v3.10.0.6) ([#30](https://github.com/prisant-labs/windows-explorer-quickpane/issues/30))
> - **Settings for screen readers:** every Settings control is reachable by accessibility tools, and icon buttons carry names that say what they do. [3.10.0.6](https://github.com/prisant-labs/windows-explorer-quickpane/releases/tag/v3.10.0.6) ([#28](https://github.com/prisant-labs/windows-explorer-quickpane/issues/28))
> - **Dark Settings title bar:** the Settings title bar follows the Windows light or dark mode. [3.10.0.6](https://github.com/prisant-labs/windows-explorer-quickpane/releases/tag/v3.10.0.6) ([#26](https://github.com/prisant-labs/windows-explorer-quickpane/issues/26))
> - **Windows 11 icons:** on Windows 11 the pane and Settings use Segoe Fluent Icons, and Windows 10 keeps Segoe MDL2 Assets. [3.10.0.6](https://github.com/prisant-labs/windows-explorer-quickpane/releases/tag/v3.10.0.6) ([#24](https://github.com/prisant-labs/windows-explorer-quickpane/issues/24))
> - **Settings gear always in reach:** the gear sits at the bottom left of the pane and stays there when the pane collapses, so Settings opens in one click. [3.10.0.6](https://github.com/prisant-labs/windows-explorer-quickpane/releases/tag/v3.10.0.6) ([#22](https://github.com/prisant-labs/windows-explorer-quickpane/issues/22), [#33](https://github.com/prisant-labs/windows-explorer-quickpane/issues/33))
> - **Clicks follow the tab in front:** in a Windows 11 window with several tabs, a folder click opens in the tab on screen, and Recent records that tab's folder. [3.10.0.8](docs/releases/3.10.0.8.md) ([#35](https://github.com/prisant-labs/windows-explorer-quickpane/issues/35))
> - **Clicks that never wait:** folder clicks run on a thread of their own, folder pickers give up quickly on a share that does not answer, and startup no longer waits for the groups folder. [3.10.0.8](docs/releases/3.10.0.8.md) ([#38](https://github.com/prisant-labs/windows-explorer-quickpane/issues/38), [#41](https://github.com/prisant-labs/windows-explorer-quickpane/issues/41), [#42](https://github.com/prisant-labs/windows-explorer-quickpane/issues/42))
> - **Less background work:** the Recent check, the Explorer window sweep, the groups folder watcher, the log, and the crash guard do far less work while you are not using the pane, and the caches have size limits. [3.10.0.8](docs/releases/3.10.0.8.md) ([#39](https://github.com/prisant-labs/windows-explorer-quickpane/issues/39), [#40](https://github.com/prisant-labs/windows-explorer-quickpane/issues/40), [#43](https://github.com/prisant-labs/windows-explorer-quickpane/issues/43), [#44](https://github.com/prisant-labs/windows-explorer-quickpane/issues/44), [#45](https://github.com/prisant-labs/windows-explorer-quickpane/issues/45), [#46](https://github.com/prisant-labs/windows-explorer-quickpane/issues/46), [#47](https://github.com/prisant-labs/windows-explorer-quickpane/issues/47), [#48](https://github.com/prisant-labs/windows-explorer-quickpane/issues/48), [#49](https://github.com/prisant-labs/windows-explorer-quickpane/issues/49), [#50](https://github.com/prisant-labs/windows-explorer-quickpane/issues/50))
> - **Profile switches keep edits in place:** an edit made while a new profile's groups are loading can no longer land in the previous profile's folder. [3.10.0.9](docs/releases/3.10.0.9.md) ([#68](https://github.com/prisant-labs/windows-explorer-quickpane/issues/68))
> - **Closed panes release memory:** a pane section closed while a rebuild was waiting no longer stays in memory. [3.10.0.9](docs/releases/3.10.0.9.md) ([#69](https://github.com/prisant-labs/windows-explorer-quickpane/issues/69))
> - **Dialog clicks navigate first:** a pin click in an Open or Save dialog navigates before Recent Apps records the app, and startup no longer creates the groups folder on the UI thread. [3.10.0.9](docs/releases/3.10.0.9.md) ([#70](https://github.com/prisant-labs/windows-explorer-quickpane/issues/70), [#71](https://github.com/prisant-labs/windows-explorer-quickpane/issues/71))
> - **No flicker while resizing:** dragging an Explorer window's border no longer makes the tree and the file list jump under the pane and back. The pane steps aside during the drag, so Explorer's own tree stays in view, and it returns on release. [3.10.0.11](https://github.com/prisant-labs/windows-explorer-quickpane/releases/tag/v3.10.0.11) ([#99](https://github.com/prisant-labs/windows-explorer-quickpane/issues/99), [#102](https://github.com/prisant-labs/windows-explorer-quickpane/issues/102))
> - **Dialogs keep the pane inside:** a Save dialog that resizes itself as it opens, or that the user resizes, keeps the pane inside instead of moving it beside, and reopens at the same size each time. [3.10.0.11](https://github.com/prisant-labs/windows-explorer-quickpane/releases/tag/v3.10.0.11) ([#103](https://github.com/prisant-labs/windows-explorer-quickpane/issues/103))
> - **Settings redesigned as a Windows 11 Settings page:** a navigation pane, search, cards, and a Mica backdrop replace the old tabbed panel, with full keyboard and screen reader support. It opens on the Pane page, and pane sections and quick actions reorder by drag or by button, with a Reset to default. [3.11.0.2](docs/releases/3.11.0.2.md) ([#89](https://github.com/prisant-labs/windows-explorer-quickpane/issues/89)-[#96](https://github.com/prisant-labs/windows-explorer-quickpane/issues/96))
> - **Quick actions:** an optional pane section that copies the current folder's path in several formats, creates a new file or dated folder, or opens the folder in Windows Terminal or Visual Studio Code. [3.11.0.2](docs/releases/3.11.0.2.md) ([#144](https://github.com/prisant-labs/windows-explorer-quickpane/pull/144))
> - **Pane color and dock display:** a row of background swatches for the pane, with Explorer's own color as the default, and a choice of monitor and screen edge for the desktop dock. [3.11.0.2](docs/releases/3.11.0.2.md) ([#134](https://github.com/prisant-labs/windows-explorer-quickpane/issues/134), [#146](https://github.com/prisant-labs/windows-explorer-quickpane/issues/146), [#177](https://github.com/prisant-labs/windows-explorer-quickpane/issues/177))
> - **Less waiting on the disk:** icons, pane edits, and Settings changes load or save in the background, so a slow or network disk no longer stalls Explorer. [3.11.0.2](docs/releases/3.11.0.2.md) ([#75](https://github.com/prisant-labs/windows-explorer-quickpane/issues/75), [#76](https://github.com/prisant-labs/windows-explorer-quickpane/issues/76), [#163](https://github.com/prisant-labs/windows-explorer-quickpane/issues/163))
> - **Update notice:** QuickPane tells you when a new version exists, at most once a day and once per version, and it never downloads anything. You can turn the daily check off on the About page. [3.12.0.0](docs/releases/3.12.0.0.md) ([#176](https://github.com/prisant-labs/windows-explorer-quickpane/issues/176))
> - **Quick actions you can extend:** ten more built-in actions, your own custom commands from a template such as `code {path}`, and eight actions on the files selected in Explorer. All of them start off. [3.12.0.0](docs/releases/3.12.0.0.md) ([#189](https://github.com/prisant-labs/windows-explorer-quickpane/issues/189), [#190](https://github.com/prisant-labs/windows-explorer-quickpane/issues/190), [#191](https://github.com/prisant-labs/windows-explorer-quickpane/issues/191))
> - **Dock grip with a maximized window:** the width grip of a reserved desktop dock can be dragged while a window is maximized. [3.12.0.0](docs/releases/3.12.0.0.md) ([#185](https://github.com/prisant-labs/windows-explorer-quickpane/issues/185))
> - **Lighter with many pins:** a pinned folder's right-click menu is built when it opens, so many pins and windows use far less memory and the pane rebuilds faster. [3.12.0.1](docs/releases/3.12.0.1.md) ([#210](https://github.com/prisant-labs/windows-explorer-quickpane/issues/210))
> - **What's new and clearer settings rows:** the About page summarizes each version and links to its full notes, and rows on the Pane sections and Quick actions pages have their own icons and an info button. [3.12.0.1](docs/releases/3.12.0.1.md) ([#208](https://github.com/prisant-labs/windows-explorer-quickpane/issues/208), [#207](https://github.com/prisant-labs/windows-explorer-quickpane/issues/207), [#205](https://github.com/prisant-labs/windows-explorer-quickpane/issues/205))

A folder sidebar for Windows File Explorer. QuickPane embeds a pinned-folders pane inside every Explorer window, so your groups, recent locations, drives, network shares, and WSL distros travel with the window you are already using. Pin it inside the window, float it beside the window, or run it as a slim screen-edge dock. Everything runs locally as a single tray app, no code is ever injected into Explorer, and all data stays in plain files under your user profile.

![QuickPane embedded inside a File Explorer window, showing pinned folder groups with tabs](screens/quickpane-demo.jpg)

## Summary

QuickPane puts your folders one click away inside Explorer itself, with:

- 📁 **Inside, beside, or dock**: embed the pane in each Explorer window and shift its content, float a follower pane at the window's edge, or run dock-only
- 🗂️ **Pinned folder groups**: organize folders into groups and tabs, backed by real folders and shortcuts on disk
- 🕘 **Real Recent**: it tracks the folders you actually browse in Explorer, not only the ones clicked inside the pane
- 🖥️ **This PC, Network, and Linux**: expandable drives and lazy folder trees, plus Network and WSL sections
- 🪟 **Stays at the window's z-level**: the beside pane is an owned window of its Explorer window, never forced above everything else
- 📌 **Desktop dock**: an optional screen-edge AppBar on the left or right edge of a display, with auto-hide and all-virtual-desktops pinning
- 💾 **Open and Save dialog support**: the pane attaches to file dialogs and navigates them too
- 🔎 **Search box**: filters every folder in the pane as you type
- 🚀 **Recent Apps**: the programs you have been using, where a row opens its app and browser rows open out into their profiles
- ⚡ **Quick actions**: copy a path in several formats, create a starter file, open a terminal, run your own commands, and act on the files selected in Explorer
- 🎯 **Per-app folders**: set a folder as an app's starting point and its Save and Open dialogs land there
- 👤 **Profiles**: independent workspaces, each with its own groups, recents count, and width
- 🎨 **Native feel**: dark and light themes that blend into the Windows shell
- 🔒 **Local storage**: groups are folders of shortcuts and settings are a JSON file you can back up

## Features

### Inside, beside, and dock modes

Three ways to show the pane, chosen in Settings:

- **Inside** reparents the pane into each Explorer window and shifts the file view to the right to make room, so the pane reads as part of the window.
- **Beside** floats a follower pane against the left edge of each window without shifting its content. The pane is an owned window of its Explorer window, so Windows keeps it at that window's z-level, on that window's virtual desktop, and hides it when the window is minimized.
- **Off** uses only the desktop dock and leaves Explorer windows untouched.

### Pinned folder groups

Folders live in groups, and a group can hold several tabs.

- Pin a folder from the **Pin to Quick Pane** context-menu verb on any folder, from the folder background, or by dragging it into the pane
- Create, rename, and delete groups, tabs, and pins
- Drag to reorder groups, tabs, and pins, with a visible drop line while reordering
- Drag one group onto another to merge their tabs, or drag a tab into empty space to make it a new group
- A group with a single tab renders like a plain list; with several tabs it shows a horizontally scrollable tab row
- A seeded default group appears on first run so the pane is never empty

### Sections

The pane is a stack of sections you can show, hide, reorder, and rename:

- **Search box**: filters the rows already on screen as you type, so it stays instant however many groups are pinned
- **App Folders**: inside a file dialog, the folder you set as that app's starting point
- **Groups**: your pinned folder groups
- **Recent**: folders browsed directly in Explorer, captured through window activation, title changes, and a light periodic check, with a location-URL fallback when the shell returns nothing, and a configurable count from 5 to 50
- **This PC**: a This PC row that opens the folder and expands to the drives, each drive expanding into subtrees that enumerate lazily like the native Explorer tree
- **Network** and **Linux (WSL)**: each its own section with a proper icon and a visibility toggle
- **Recent Apps**: the programs you have been using, each opening its app

![The pane showing This PC, Network, and a Linux (WSL) distro browsed like a local drive](screens/quickpane-demo-2.jpg)

### Native file drag

Drag files from Explorer onto a pinned folder to file them. Dropping moves on the same drive and copies across drives, while Ctrl forces a copy, Shift forces a move, and Alt creates a shortcut. The operation runs through the shell, so progress, conflicts, and undo behave exactly as they do in Explorer.

### Desktop dock

An optional AppBar at the left or right screen edge, independent of the window pane.

- Auto-hide collapses it to a thin strip that slides out on hover
- A setting shows it on every virtual desktop instead of only the one it opened on
- On a multi-monitor computer, a Display setting picks which monitor it sits on; "Primary display" (the default) tracks whichever monitor Windows currently calls primary, and a specific choice falls back to Primary display if that monitor is ever disconnected ([#146](https://github.com/prisant-labs/windows-explorer-quickpane/issues/146))
- An Edge setting puts it on the left (the default) or right edge of its display. On an edge shared with another display, auto-hide waits briefly before opening, so crossing between displays does not open it ([#177](https://github.com/prisant-labs/windows-explorer-quickpane/issues/177))
- Clicking a folder navigates the active Explorer window and brings it forward, or opens a new window when none is open

### Open and Save dialog support

QuickPane attaches to file Open and Save common dialogs and honors the same pane mode as real windows. Inside mode shifts every one of the dialog's controls and widens the dialog so nothing overlaps the pane, and beside mode floats a pane against the dialog's edge. Both the classic dialog and the modern common item dialog navigate to the clicked folder.

### Search

A box at the top of the pane filters every row beneath it as you type. Because it narrows what is already on screen rather than searching the disk, it stays instant however many groups are pinned.

- A heading disappears along with the rows beneath it
- A folder stays visible when something inside it matches
- Escape clears the box

### Recent Apps

The programs you have been using, where a row opens its app rather than a folder. An app reaches the list either by being brought to the front or by having its Save or Open dialog used, and a setting chooses which of the two count.

- Browser rows open out into that browser's profiles, which are read from the browser's own profile list
- Chromium-family browsers and Firefox are both read
- Programs carry the name and icon Windows shows for them, which tells side-by-side installs such as Chrome and Chrome Canary apart
- The folder an app's dialog was last sent to is on the row's right-click menu

### Per-app folders

Right-click a folder while an app's Save or Open dialog is open and choose "Always start {app} here". That app's dialogs then offer the folder from an App Folders section at the top of the pane, which appears only inside a dialog.

### Keyboard navigation

An optional shortcut moves focus into the pane, where the arrow keys move between folders and Enter opens one. It stays off until it is switched on under Settings, Keyboard, so no key combination is taken from the rest of the system by default.

### Quick switcher

A second optional shortcut, Ctrl+Alt+Space unless you change it, opens a search box over the window in front. Type part of a pin, a Recent folder, a drive, or a quick action, and press Enter. The Explorer window or Open or Save dialog in front goes to that folder, or the action runs against that Explorer window. With neither in front, the folder opens in a new Explorer window. Escape closes the box. Pins rank above Recent folders when both match equally well. The shortcut also stays off until it is switched on under Settings, Keyboard.

### Profiles

Each profile is an independent workspace with its own groups folder, recents count, and sidebar width.

- Switch profiles from the tray icon's right-click menu or from the profile tab row under the title
- The profile tab row can auto-hide, sliding out when the cursor is over the header
- Manage profiles on the Settings window's Groups & profiles page, with cards for add, rename, and remove
- Move a group to another profile with a destination picker and a "Copy instead of move" checkbox, so one group can live in more than one profile

### Remembered layout

Section headers, groups, and folder subtrees remember whether they are open, keyed by section type, group folder, and tree path. The state survives pane rebuilds, profile switches, window switches, and restarts.

### Collapsible pane and appearance

- A title header with the logo and a chevron collapses the pane to a thin strip, or toggles auto-hide on the desktop dock
- Dark and light themes, applied live
- Adjustable pane width from 160 to 400 px, dragged from the inner edge
- Settings apply live with no Apply button

## Requirements

- **Windows 11 or Windows 10**, 64-bit. This fork is developed and tested on Windows 11, including in Windows Sandbox. Windows 10 is expected to work, but the fork does not test it regularly. See [Compatibility](#compatibility).
- **.NET Framework 4.8** runtime (preinstalled on current Windows)
- To build from source: **Visual Studio 2019/2022** with the .NET Framework 4.8 targeting pack
- To build the installer: **[Inno Setup](https://jrsoftware.org/isinfo.php)** 6+

## Installation

### Option A: Run a release build

1. Download the latest `QuickPane-<version>-x64-setup.exe` from the [Releases page](https://github.com/prisant-labs/windows-explorer-quickpane/releases/latest) (or build it yourself below)
2. Run the installer. It installs per-user with no administrator rights, registers the **Pin to Quick Pane** folder verb, adds a startup entry, and launches QuickPane. Over an existing QuickPane, it upgrades that install in place
3. Open any Explorer window and the pane appears inside it

### Option B: Build from source

```bash
git clone https://github.com/prisant-labs/windows-explorer-quickpane.git
```

**Build the app**

1. Open `QuickPane.sln` in Visual Studio
2. Select the **Release | x64** configuration (x64 only)
3. Build the solution. The executable is produced at `QuickPane\bin\x64\Release\QuickPane.exe`
4. Run `QuickPane.exe`

**Build the installer (optional)**

1. Build the app first, then run `pwsh -File scripts/package.ps1`
2. The single-file `QuickPane-<version>-x64-setup.exe` is produced in `dist\`, beside the zip and `SHA256SUMS.txt`

> `package.ps1` passes the build's folder and version to `Installer.iss`, so it works for `-Configuration Debug` too. Opening `Installer.iss` in Inno Setup and pressing **Compile** packages the `Release\x64` build.

### Recommended: hide Explorer's own navigation pane

QuickPane sits alongside Windows' built-in navigation pane rather than replacing it, so the two will show side by side until you turn the native one off. In any Explorer window, open the **View** tab, click **Navigation pane**, and uncheck **Navigation pane** so QuickPane is the only tree on the left.

![Explorer's View ribbon with the Navigation pane toggle open](screens/hide-nav-pane-windows.jpg)

## Usage

### Pinning folders

- Right-click any folder in Explorer and choose **Pin to Quick Pane**, or right-click inside a folder's empty background, or drag a folder into the pane
- Click a pinned folder to navigate the current Explorer window to it

### Groups and tabs

- Drag a group, tab, or pin to reorder it
- Drag one group onto another to merge their tabs
- Drag a tab into empty space to split it into a new group
- Right-click a group or section header to rename it

### Quick actions and custom commands

Turn on the **Quick actions** section under **Pane sections** in Settings. Then choose its rows on the **Quick actions** page, where you can also reorder them and reset them to their defaults.

A row acts on the folder on screen, unless its name says "selected". Those rows act on the files selected in Explorer, and say "Nothing selected" when there are none.

To add your own row, give a command a name and a template on the same page. These are invented examples:

| Template | What the row does |
|---|---|
| `code {path}` | Opens the selected item in Visual Studio Code, or the folder when nothing is selected |
| `"C:\Program Files\Git\git-bash.exe" --cd={dir}` | Opens Git Bash in the folder on screen |
| `pwsh -File C:\Demo\tidy.ps1 {paths}` | Runs your script with each selected item as its own argument |
| `C:\Demo\stamp.exe --label={name}` | Runs your program with the selected item's name inside one argument |

The placeholders are `{path}`, `{paths}`, `{dir}`, and `{name}`. Write `{{` and `}}` for literal braces. QuickPane quotes each value, so a folder named `R&D notes` stays one argument.

A placeholder is refused for a program that would read a folder name as code: `cmd`, `wsl`, `wt`, a batch file, PowerShell without `-File`, or `bash -c`. Those programs still work with no placeholder. Quoting cannot make every program safe, so use a placeholder only where the program treats it as a file name, and not after something like `python -c`. Commands in a restored backup arrive turned off.

### Choosing a mode

- Open Settings and pick **Inside**, **Beside**, or **Off** under Pane mode
- Enable the **Desktop dock** for a screen-edge bar that works in any mode

### Profiles

- Switch from the tray menu or the profile tab row under the title
- Add, rename, remove, and move groups between profiles in the Settings window

### Settings

- Open Settings from the tray icon or the gear in the pane
- A navigation pane lists the pages, and a search box jumps to a setting
- Adjust mode, theme, pane width, recents count, section visibility and order, the desktop dock and its options, the profile tab row, which apps Recent Apps lists, the keyboard shortcuts, and profiles
- The About page shows the running version and a short summary of what is new in it, and offers a download when a newer release is published
- Changes apply live

## Settings and data

All data is stored locally under your user profile:

```
%APPDATA%\QuickPane\
├── settings.json        # All settings, sections, dock options, and profiles
├── Groups\              # Groups, tabs, and pinned shortcuts on disk
├── uistate.txt          # Remembered open/closed state of sections, groups, and trees
└── debug.log            # Diagnostic log
```

Groups are stored as ordinary folders: a **group** is a folder under `Groups\`, a **tab** is a subfolder, and a **pin** is a `.lnk` shortcut. You can back up, edit, or rearrange the `Groups` tree directly and QuickPane reflects it.

### Privacy

- **No cloud sync**: everything stays on your computer
- **No accounts**: no login or registration
- **No telemetry**: QuickPane sends no usage data, crash reports, or analytics anywhere
- **Readable formats**: a JSON settings file and ordinary Windows shortcuts you can open and back up anywhere

QuickPane makes one network request of its own, an update check:

- **What it sends**: an HTTPS GET to `api.github.com` for this fork's latest release, with the header `User-Agent: QuickPane/<version>`. Like any web request, it shows GitHub your IP address. It sends nothing else.
- **When it runs**: at most once a day, a few minutes after QuickPane starts, and also when you open the About page in Settings or press "Check for updates" there. To stop the daily check, turn off "Check for updates automatically" on the About page. Opening About still checks.
- **What it does with the answer**: when a newer version exists, it shows one notice per version, adds an item to the tray menu, and puts a dot on About in Settings. It never downloads or installs anything. The notice opens the release page in your browser.

Two other features reach the network only when you use them:

- **Links**: the About page's links and the Remote drives page's download buttons open in your default browser when you click them.
- **Remote drives (SSH)**: `sshfs` connects to the servers you enter, when you connect a drive.

### settings.json format

```json
{
  "groupsPath": "%APPDATA%\\QuickPane\\Groups",
  "sections": [
    { "type": "groups",   "visible": true, "order": 0 },
    { "type": "recents",  "visible": true, "order": 1 },
    { "type": "computer", "visible": true, "order": 2 },
    { "type": "network",  "visible": true, "order": 3 },
    { "type": "linux",    "visible": true, "order": 4 }
  ],
  "recentsMaxCount": 15,
  "sidebarWidthPx": 220,
  "mode": "inside",
  "desktopDock": false,
  "desktopDockAutoHide": true,
  "desktopDockAllDesktops": false,
  "profiles": [
    { "name": "Profile 1", "groupsPath": "%APPDATA%\\QuickPane\\Groups", "recentsMaxCount": 15, "sidebarWidthPx": 220 }
  ],
  "activeProfile": 0,
  "showProfileTabs": true,
  "profileTabsAutoHide": false
}
```

## Compatibility

| OS | Supported | Notes |
|----|-----------|-------|
| Windows 11 x64 | ✅ | Developed and tested on, including in Windows Sandbox |
| Windows 10 x64 (incl. LTSC) | ✅ | Expected to work. The original project was developed on Windows 10 LTSC, but this fork does not test Windows 10 regularly. Windows 11-only features, such as tabs, are skipped there. |
| Windows 11 on Arm | ⚠️ | The installer accepts it from the release after 3.12.0.2, and Windows runs the x64 build through emulation. Nobody has run QuickPane on an Arm computer yet. |
| Windows 8.1 and Windows 7 | ❌ | Not supported. Both are past Microsoft's end of support, and the Explorer window classes differ. |
| macOS / Linux | ❌ | Windows-only (Win32 + WPF) |

## Troubleshooting

### The pane is not showing in Explorer windows

Make sure Pane mode is **Inside** or **Beside** rather than **Off** in Settings. A new window shows the pane the moment Windows has presented it and laid out its toolbar, so the pane appears on its own without you resizing the window.

### A Save dialog saves to the wrong place when I click a folder

QuickPane navigates file dialogs through the address bar rather than the File name box, so clicking a folder changes the location without committing a save. If a third-party app runs its own custom dialog that fights the inside layout, QuickPane drops that dialog to the non-invasive beside pane automatically.

### An Explorer window looks shifted after QuickPane closes

Closing QuickPane restores each window's content view to full width. If a window was mid-load when QuickPane exited, open a new window and it will be normal.

### Build errors about C# syntax

The project uses the C# 7.3 language version that is the default for .NET Framework 4.8. Build with the .NET Framework 4.8 toolchain rather than retargeting the project to a newer framework.

## How it works

QuickPane is a tray application built on WPF and documented Win32 interop only; it never injects code into `explorer.exe`, so a fault in QuickPane can never bring Explorer down. The core ideas:

- **SetWinEventHook** out of process discovers Explorer folder windows and tracks them as they are created, shown, moved, activated, and destroyed
- **Inside mode** reparents a WPF `HwndSource` as a `WS_CHILD` of the `CabinetWClass` window and shifts Explorer's content host (`ShellTabWindowClass`) to the right. A readiness gate holds the shift until the window is actually presented and its toolbar has laid out, so the pane never flashes blank or sits over the toolbar
- **A short burst, not a constant poll**, holds the split the moment a window opens or is activated, because those are the only times Explorer resets its content to full width. Resize is held purely from location events, so dragging the window edge stays smooth
- **Beside mode** makes the Explorer window the pane's owner via `GWLP_HWNDPARENT`, so Windows keeps the pane at that window's z-level and on its virtual desktop with no manual tracking
- **File dialogs** are detected as a `#32770` carrying a `ComboBoxEx32` (classic comdlg32) or a `SHELLDLL_DefView` (modern common item dialog). Requiring `#32770` is deliberate, because the desktop itself hosts a `SHELLDLL_DefView` and must never be matched
- **The desktop dock** reserves the screen edge with `SHAppBarMessage` and pins itself to every virtual desktop through `IVirtualDesktopManager`
- **Groups on disk** are folders of `.lnk` shortcuts, read and written through the shell `IShellLink`, so the model is just files you can see
- **Settings** serialize to JSON with `DataContractJsonSerializer` using atomic temp-then-replace writes, with migration that upgrades older files in place; remembered open/closed state lives in `uistate.txt`

## Project structure

```
QuickPane/
├── QuickPane.sln              # Visual Studio solution
├── Installer.iss              # Inno Setup installer script
├── CHANGELOG.md               # Version history
├── screens/                   # Screenshots used in this README
└── QuickPane/
    ├── QuickPane.csproj       # Project (.NET Framework 4.8, x64, WPF)
    ├── app.manifest           # Per-monitor DPI, runs without elevation
    ├── App.xaml(.cs)          # Tray host and service startup
    ├── Interop/
    │   ├── NativeMethods.cs   # All Win32 P/Invoke
    │   ├── WinEventHook.cs    # Out-of-process window-event hooks
    │   ├── ExplorerNavigator.cs # Navigate a real Explorer window
    │   ├── DialogNavigator.cs # Detect and navigate file dialogs
    │   └── VirtualDesktop.cs  # Virtual-desktop queries and pinning
    ├── Explorer/
    │   ├── ExplorerWatcher.cs # Discovers windows, drives layout, burst enforcer
    │   ├── ExplorerWindow.cs  # One embedded (inside) pane and its content shift
    │   ├── ExplorerFollowerWatcher.cs # Beside-mode follower panes
    │   ├── DialogPaneWatcher.cs # Panes for Open/Save dialogs
    │   ├── DialogInsidePane.cs # Inside-mode pane for a dialog
    │   ├── RecentTracker.cs   # Capture real Explorer navigation
    │   ├── AppBarHost.cs      # Desktop dock AppBar
    │   └── MonitorPicker.cs   # Lists and resolves monitors for the dock's Display setting
    ├── Models/
    │   └── Models.cs          # Settings, profiles, and pinned-folder models
    ├── Services/
    │   ├── SettingsStore.cs   # JSON load, save, migrate
    │   ├── GroupStore.cs      # Groups, tabs, and pins on disk
    │   ├── UiState.cs         # Persistent open/closed state
    │   ├── RecentFoldersService.cs # Recent list model
    │   ├── DriveService.cs    # Drive and tree enumeration
    │   ├── KnownFolders.cs    # Known-folder resolution
    │   ├── ShellLink.cs       # Read and write .lnk shortcuts
    │   ├── FileDropOps.cs     # Shell move/copy/shortcut drops
    │   ├── ThemeService.cs    # Dark / light theming
    │   ├── FluentThemeService.cs # Ported Fluent theme, scoped to the Settings window
    │   ├── QuickActionLibrary.cs # The built-in quick actions and their defaults
    │   ├── QuickActionRunner.cs  # Runs a quick action
    │   ├── CustomCommandTemplate.cs # Custom command templates: placeholders, quoting, refusals
    │   ├── UpdateService.cs   # Asks GitHub for the latest release
    │   ├── UpdateNotifier.cs  # The daily check and the one-time notice
    │   ├── WhatsNew.cs        # The version summary on the About page
    │   └── Log.cs
    ├── Themes/Fluent/         # Ported Microsoft WPF Fluent theme (see NOTICE.md below)
    └── UI/
        ├── SidebarControl.xaml(.cs)  # The pane shell
        ├── GroupSection.xaml(.cs)    # A group with its tabs
        ├── ComputerSection.xaml(.cs) # This PC, drives, trees
        ├── RecentsSection.xaml(.cs)  # Recent folders
        ├── ShellRootSection.cs       # Network and Linux roots
        ├── FolderItem.xaml(.cs)      # One folder row
        ├── FolderTreeNode.cs         # A lazy tree node
        ├── LazyContextMenu.cs        # Builds a pinned row's menu when it opens
        ├── Settings/                 # The native Settings window, its cards, and its pages
        ├── GroupPickerWindow.xaml(.cs) # Pick a group when pinning
        ├── TextPrompt.cs             # Top-level prompts for text entry
        ├── DropLineAdorner.cs        # Reorder drop line
        └── DragGhostAdorner.cs       # Drag ghost
```

## Contributing

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Make your changes (keep to documented Win32 and the existing C# 7.3 / .NET Framework 4.8 target)
4. Build and test on Windows 10 or 11
5. Commit your changes (`git commit -m 'Add amazing feature'`)
6. Push to the branch (`git push origin feature/amazing-feature`)
7. Open a Pull Request

## License

MIT, see the [LICENSE](LICENSE) file for details.

### Third-party code

The Settings window's visual style (`QuickPane/Themes/Fluent/`) is ported from Microsoft's
WPF Fluent theme (`dotnet/wpf`, MIT), itself built from the open-source
[WPF-UI](https://github.com/lepoco/wpfui) project. See
[`QuickPane/Themes/Fluent/NOTICE.md`](QuickPane/Themes/Fluent/NOTICE.md) for the source
commit, the ported files, and both copyright notices.

---

**⭐ If QuickPane made Explorer feel like home, please star the repository!**

---

*Made with ❤️ for people who live in File Explorer.*
