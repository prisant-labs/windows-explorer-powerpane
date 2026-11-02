# Developing QuickPane

This guide covers building, testing, and releasing this fork. The README describes what QuickPane does and how its parts fit together. This file covers how to work on it safely.

## Requirements

- Windows 10 or 11, x64.
- Visual Studio or the Visual Studio Build Tools, 2019 or later, with MSBuild and the .NET desktop build tools.
- The .NET 8 SDK, which runs the unit tests.
- The .NET Framework 4.8 targeting pack is optional. `scripts/build.ps1` downloads Microsoft's signed reference-assembly package when the pack is missing.
- Inno Setup 6, which `scripts/package.ps1` uses to build the installer. CI pins 6.7.1. Without it, pass `-NoInstaller` to package only the zip.

## Build

```powershell
pwsh -File scripts/build.ps1                      # Release, x64
pwsh -File scripts/build.ps1 -Configuration Debug
```

The script finds MSBuild through `vswhere`, builds `QuickPane/QuickPane.csproj`, and prints the output path, version, and SHA-256. The exe lands in `QuickPane\bin\x64\<Configuration>\QuickPane.exe`.

The script builds the app project, not `QuickPane.sln`. Visual Studio 2019's MSBuild cannot load the SDK-style test project, so the test project stays out of the solution. With Visual Studio 2022 you may still open the solution as usual.

## Test

```powershell
pwsh -File scripts/test.ps1            # builds, then runs the tests
pwsh -File scripts/test.ps1 -NoBuild   # tests only
```

The tests live in `tests/QuickPane.Tests`, an SDK-style `net48` NUnit project. It references the built `QuickPane.exe` through a `HintPath` rather than a project reference, because the .NET SDK cannot build the legacy WPF project. Build the app first with the same configuration.

`[assembly: InternalsVisibleTo("QuickPane.Tests")]` lets tests reach `internal` code. Logic worth testing should be `internal`, not `private`.

### Rules for tests

- A test must only touch a temporary directory that it creates and deletes. `TempDir.cs` provides one.
- Never use `SettingsStore`'s default constructor in a test. It reads and writes the real `%APPDATA%\QuickPane`. Use `new SettingsStore(tempDir)`, and point `groupsPath` inside the temporary directory too.
- Never call `GroupStore.Recycle` or `DeleteTab` on real folders. Either would put test folders in the developer's Recycle Bin.
- Never start `QuickPane.exe` from a test, and never show a window or dialog.
- Keep expected failures out of the log. Before `Log.Init` runs, `Log` writes to `quickpane.log` in the system temp folder, which is outside the test's own directory. `WorkLane` and `WinEventHub` take an `onError` handler for this reason. A test that makes either fail on purpose passes its own handler.
- Prefer pulling a decision out into a pure `internal static` method and testing that. `DialogPaneWatcher.ResolveMode` is an example.

## CI

`.github/workflows/ci.yml` runs on every push to `main` and on every pull request. It calls the same scripts on `windows-latest`: build, test, and `scripts/package.ps1`, which makes the installer, the release zip, `SHA256SUMS.txt`, and the release notes. It keeps that packaged output as a workflow artifact. Packaging runs on every build so that a broken release step fails a pull request rather than a tag. It also fails when the version in `AssemblyInfo.cs` has no release document and no changelog section yet. CI cannot exercise the app itself: Explorer windows, window hooks, and file dialogs need a real desktop.

`.github/workflows/release.yml` runs when a `v*` tag is pushed. It is described under [Releasing the fork](#releasing-the-fork).

`main` is protected. A change reaches it only through a pull request whose `build-and-test` check passed, and the rule applies to administrators too. No approving review is required, because GitHub does not let authors approve their own pull requests. Force-pushes to `main` and deleting `main` are blocked, and merged branches are deleted automatically. The rule lives in the repository's Settings, under Branches.

## Running a development build

Only one copy of QuickPane runs at a time, because a single-instance mutex guards it. A development build therefore replaces the installed copy while it runs.

`scripts/dev-run.ps1` does the steps below for you. It builds, backs up your settings to `%LOCALAPPDATA%\QuickPane-dev-backups`, closes the running copy with `--exit`, and runs the build. When you close the build from its tray icon, it starts the installed copy again. It waits through a watchdog relaunch, so a crash during the test does not bring the installed copy back early. If you stop the script before then, run it with `-RestoreInstalled`.

```powershell
pwsh -File scripts/dev-run.ps1              # build Release, then run it
pwsh -File scripts/dev-run.ps1 -NoBuild     # run the existing build
pwsh -File scripts/dev-run.ps1 -RestoreInstalled
```

A build run from `bin\` lasts until you sign out. Windows starts the installed copy at sign-in, because the Run key points at `%LOCALAPPDATA%\QuickPane`.

The manual steps, if you prefer them:

1. Back up `%APPDATA%\QuickPane`. A development build reads and writes the same settings and groups.
2. Close the installed copy: `& "$env:LOCALAPPDATA\QuickPane\QuickPane.exe" --exit`. Do not kill the process. A kill skips the layout restore, and the watchdog process restarts the app.
3. Run `QuickPane\bin\x64\Release\QuickPane.exe`.
4. When finished, close it with `--exit` and start the installed copy again. `ShortcutRepair` points the Start menu shortcut at whichever copy ran last, so restarting the installed copy repairs it.

Windows Sandbox makes a safe test bed for UI checks. It is a disposable desktop with no personal files, where scripted clicks cannot disturb your own windows.

## Code conventions

- **C# 7.3 and .NET Framework 4.8 only.** That rules out switch expressions, `is not` patterns, nullable reference types, `using` declarations, ranges, records, and target-typed `new()`.
- **The app project lists every file.** A new `.cs` file needs a `<Compile Include="..." />` line in `QuickPane/QuickPane.csproj`, or it will not compile.
- **Comments explain why.** The codebase documents the reason behind each non-obvious choice, often a past bug. Keep that style.
- **One change per commit.** Each fix should be one commit that can be cherry-picked onto a branch cut from upstream.
- **No personal data.** Use invented paths such as `C:\Demo\Projects` in tests, comments, issues, and commit messages.
- **The pane matches Explorer's own color by default.** A Settings pane color other than Default (the swatches on the Pane page) is the person's own opt-in; never default a theme, profile, or release to a color choice they didn't make.

## Traps in the code

| Area | What to know |
|---|---|
| Inside mode | `ExplorerWindow` hosts the pane as a child window of Explorer's `CabinetWClass` and shifts the tab content right with `SetWindowPos`. |
| Window resizes | Explorer puts every tab back at x=0 and full width on every resize, a programmatic `SetWindowPos` included, and QuickPane shifts it back within milliseconds. During a border drag that correction is visible as flicker, so `ExplorerWatcher` leaves the window to Explorer between `EVENT_SYSTEM_MOVESIZESTART` and `EVENT_SYSTEM_MOVESIZEEND` and hides the pane while it does. `ResizeBorder.CursorOn` tells a resize from a title-bar move, which keeps the inside pane visible (a beside pane hides for either kind of drag; see #147). A 30-second cap releases a window whose end event is lost. See #99 and #102. |
| Windows 11 tabs | Each Explorer tab has its own `ShellTabWindowClass` child. All of them stay visible, with the active one on top, and Explorer resets every tab to full width when a tab opens or closes. `ExplorerWindow` therefore walks all of them live on every layout pass. Never cache one tab's handle, because tabs open and close while the window lives. See #4. |
| Keyboard focus | The embedded pane is a child of another process's window, so it cannot take keyboard focus normally. Typing happens in separate top-level windows such as `TextPrompt`. See lso2/QuickPane#4. |
| File dialogs | `DialogPaneWatcher` attaches panes to standard `#32770` dialogs, inside or beside. An app whose dialog moves its controls back over the pane at an unchanged width is moved to beside for the rest of the session. A width change never counts against the app. The common Save dialog applies its remembered size just after the pane attaches, so `DialogInsidePane` fits the pane inside the width the dialog chose. Never widen the dialog at that point: the shell records the size before the restore on hide runs, so each Save As would reopen one pane wider than the last. A user's border drag hides the pane until release. QuickPane's own pickers receive a pane too. See #103. A beside pane is its own top-level window, so it only moves after a `LOCATIONCHANGE` notification reaches us, which trails a fast drag of the dialog (title bar included, not just the border) by a frame or more. `FollowerPane.BeginDrag`/`EndDrag`, driven by `OnMoveSize` in both `DialogPaneWatcher` and `ExplorerFollowerWatcher`, hide the beside pane for the length of any move or resize and place it once from the final rect on `MOVESIZEEND`, so no frame shows it away from the window's edge. See #147. |
| Folder picker | WinForms `FolderBrowserDialog` on .NET Framework 4.8 is the legacy tree dialog. Use `UI/FolderPicker.cs`, which wraps the modern `IFileOpenDialog`. |
| Deleting | `GroupStore.Recycle` sends a folder to the Recycle Bin. Use it, never `Directory.Delete`, for anything that holds a user's pins. |
| Settings structure | `QuickPane/UI/Settings/` holds `SettingsWindow.cs` (the shell: navigation pane, search, breadcrumb, Mica), `SettingsCards.cs` (shared, theme-live card builders), `SettingsSearch.cs` (the search index, built with no `SettingsStore` and no `GroupStore`), and `Pages/*.cs`, one file per page. Every page implements `SettingsPage` (`Id`, `Title`, `Glyph`, an optional `SearchEntries`, and `Build(SettingsPageContext)`). A page's constructor takes no arguments, because the search index is built once before any page opens. `Build` must do no disk, network, or COM work itself; post that through `WorkQueue` first, as the rest of the app does. Every control sets `AutomationProperties.Name`, so screen readers and UI-test tools can reach it. `SettingsPageBuildTests` builds every page from `SettingsWindow.BuildPages()` against a fake `ISettingsShell` and fails on any unnamed control, so a new page is tested without extra work. If a page's `Build` starts background work that would reach the network or the real disk, give it a seam the test can replace, as `AboutPage.CheckForUpdates` and `SshPage.SetInstallState` do. See #90, #96, and #120. |
| Fluent styles stay window-scoped | `QuickPane/Themes/Fluent/` is a port of Microsoft's WPF Fluent theme (see its `NOTICE.md`). `FluentThemeService` merges it into the Settings window's own `Resources` only, and must never merge a Fluent dictionary into `Application.Current.Resources`: every pane inside Explorer reads that level, so a style merged there would restyle the pane too. `FluentThemeServiceTests.AppliedDictionaries_NeverReachApplicationLevelResources` checks this holds. See #89. |
| Updating the Fluent port | To pick up a newer `dotnet/wpf` commit, read `QuickPane/Themes/Fluent/NOTICE.md` first: it names the ported folders and why `ListView`, `DataGrid`, and `Window.xaml` were left out. Then read the #89 commit message for every change QuickPane made to build the port on C# 7.3 and .NET Framework 4.8, such as the `xmlns:system` fix, `StaticResource` references changed to `DynamicResource`, the one C# 9 pattern rewritten, and the placeholder `SystemAccentColor*` keys `FluentThemeService` overwrites at load time. Re-apply the same changes to the new source, update the source commit in `NOTICE.md`, and run `FluentThemeServiceTests` plus a sandbox visit to every page before committing. |
| Versions | `UpdateService.Current` reads the assembly version. `UpdateService.Display` spells any version the way its tag does, with four parts when the fourth is not zero. Use it, never `Version.ToString(3)`, which drops a fork build's fourth part. |
| Update check | `UpdateService` asks GitHub for this fork's `releases/latest`, which skips drafts and pre-releases. A 404 means no published release yet and reads as "Up to date". |
| Slow work and threads | In inside mode the UI thread shares Explorer's input queue, so one blocking call on it stalls Explorer too. Disk, network, and cross-process COM work goes to `WorkQueue`, which has two background STA lanes. `WorkQueue.PostNavigation` is only for driving an Explorer window or a file dialog to a folder the user clicked. Everything else uses `WorkQueue.Post`. A click used to wait behind Recent polls, copies, and network probes on a single lane. See #38. |
| Checks the user waits for | When the UI must know an answer before it can continue, such as a picker's start folder, use `PathStatus.FolderExistsWithin`. It gives up after `InteractiveProbeMs` and reports a timeout separately from "missing". A bare `Directory.Exists` on an unreachable share waits for the network timeout. See #41. |
| Window event hooks | Create hooks with `WinEventHook`, never with `SetWinEventHook` directly. `WinEventHub` shares one native hook among every `WinEventHook` for the same event range on the same thread, so each event crosses processes once. Handlers run on the UI thread for events from the whole desktop. Reject unrelated events in the first line. See #43. Dispose a hook on the thread that created it: Windows removes a hook only there, and `WinEventHub` logs a detach from another thread. See #73. |
| Recent tracking | `RecentTracker` queries Explorer through COM when a window is activated. Its 2-second poll queries only when the front window, its front tab, or its title changed, or every 30 seconds otherwise. Anything that makes the poll query more often multiplies cross-process traffic. See #39. The title shows only the folder's own name, so a move from `C:\Demo\Alpha\Reports` to `C:\Demo\Beta\Reports` changes nothing the poll compares, and a visit that ends within 30 seconds is not recorded. This is an accepted trade-off. A navigation-complete signal from Explorer would remove it. See #74. |
| Profiles and groups | A profile switch changes `GroupStore.Root` at once, while panes can still hold groups from the old profile. The store records which folder its list was read from. `Reload` drops the list at once when that folder changed, and keeps it for a reload of the same folder. A scan publishes only if the root is unchanged when it finishes, and every edit method refuses a group, tab, or pin outside the current root. A new edit method must call `StaleGroup` for a group and `Stale` for a tab or pin first, and an edit made outside the store must check `IsCurrent`. A pane's UI handlers never call a mutator directly; they post it through `GroupStore.PostEdit`, which captures the root on the caller's thread and refuses the posted edit the same way if a profile switch changed it before the worker runs it. See #68 and #75. |
| Pane lifetime | A pane hosted in an `HwndSource` never raises `Unloaded`, so every host calls `SidebarControl.Detach`, which detaches each section. A section's `Detach` is final: `Wire`, `Build`, and every callback it queues on the dispatcher check it, because a rebuild queued before the pane closed used to subscribe the section to static events again. A new section with subscriptions needs the same flag and a line in `DetachSections`. See #69. |
| Logging | `Log` keeps one handle per log file open through `AppendOnlyFile`, with append-only rights, and checks every 30 seconds that the name still points at the same file, because every QuickPane process rolls the logs when it starts. Write logs only through `Log`. See #46. |
| Caches | A cache keyed by path should be an `LruCache` with a cap, guarded by the owner's lock. QuickPane runs for days, so an uncapped dictionary only grows. See #47 and #48. |
| Quick actions | The built-in action library, path-format helpers, the quoting helper, and the unique-naming helpers all live in `QuickPane/Services/QuickActionLibrary.cs`; `QuickActionRunner` is what actually runs one against a folder. Every action that starts a process goes through `ProcessStartInfo` with its own argument list and `QuickActionLibrary.QuoteArgument`, never through `cmd /c`, because cmd.exe's own metacharacters (`&`, `^`, `%`) are not what that quoting guards against. VS Code's PATH entry is `code.cmd`, a batch file CreateProcess cannot launch directly, so `QuickActionLibrary.FindCodeExe` walks from it to the real `Code.exe` beside its install root instead of invoking the shim at all. The PowerShell fallback for "Open in Terminal" needs a second, different quoting rule of its own (`QuickActionLibrary.TerminalArguments`): PowerShell's `-Command` parses the rest of the line as script text, so a folder name containing `$` needs PowerShell's single-quoted string literal, not `QuoteArgument`'s CommandLineToArgvW-style quoting. The pane's Quick actions section resolves "the folder the host Explorer window shows" fresh at the moment of each click, through `ExplorerNavigator.GetCurrentPath(SidebarControl.HostWindow)` posted to `WorkQueue`, rather than caching it at build time, because the pane does not rebuild on an Explorer navigation. The section is hidden entirely (not merely built empty) inside a file dialog or on the desktop dock, neither of which has an answer for that. |

## Working with upstream

The `upstream` remote points at `lso2/QuickPane`, and `origin` points at this fork.

- **Sync:** `git fetch upstream`, then create a branch from `main`, run `git merge upstream/main` on it, and open a pull request. `main` accepts no direct pushes. Upstream ships large monthly commits, so expect conflicts where the fork has changed the same lines.
- **Report first:** a bug that affects upstream gets an upstream issue before or alongside the fork's fix.
- **Upstream pull requests:** cut a branch from `upstream/main` and cherry-pick only the commit for that change. Never offer the fork's `main`, which carries fork-only commits such as CI, the scripts, and these docs.
- **Fork-only commits:** the commit that points the update check at this fork must never go upstream. When merging upstream, keep the fork's repository in `UpdateService`'s two URLs.

## Releasing the fork

1. Set the version in `QuickPane/Properties/AssemblyInfo.cs` (both attributes). `Installer.iss` reads it from the built program. Fork versions add a fourth number to the upstream base, for example 3.10.0.1.
2. Add a `CHANGELOG.md` entry with Added, Changed, and Fixed sections, linking upstream issues where they exist. When a release needs more than its changelog section, write a full release document in `docs/releases/<version>.md`, as for 3.10.0.2, the first published fork release.
3. When the release changes what a user sees, add an item to "What this fork adds" at the top of `README.md`. Link it to the release that will publish it, and to its issues. A version that is never published gets a line under its `CHANGELOG.md` heading naming the release that first shipped its changes.
4. Merge to `main` through a pull request with green CI.
5. Tag the merged commit on `main` and push the tag: `git tag -a v<version> -m "QuickPane <version>"`, then `git push origin v<version>`.
6. The `release` workflow then runs on that exact commit. It refuses a tag that is not four-part, a tagged commit that is not on `main`, and an exe whose version differs from the tag. It builds, tests, packages, and attests the files' provenance. Then it creates a **draft** release for the tag with the installer, the zip, and `SHA256SUMS.txt`, or refreshes the draft that already exists for that tag. It never touches a published release. Its run summary prints the publish command.
7. Download the installer from the draft, run it over an existing install on a real desktop, and test it. Then publish the draft: `gh release edit v<version> --draft=false -R prisant-labs/windows-explorer-quickpane`. Publish it as a full release, not a pre-release. The in-app update check reads `releases/latest`, which never returns drafts or pre-releases.
8. Publishing starts the `winget` workflow, which opens a pull request in `microsoft/winget-pkgs` for the new version of `PrisantLabs.QuickPane`, using Microsoft's `wingetcreate` from a pinned release whose hash it checks. It needs the repository secret `WINGET_TOKEN`, a personal access token with `public_repo` scope, and skips with a notice without it. A winget maintainer merges the pull request, usually within a day or two. To retry one version, run the workflow by hand with its tag.

If the desktop test fails, fix the bug on `main`. Then bump the version, and tag again. Do not move a pushed tag. To try the workflow without releasing anything, run it by hand from the Actions tab: a run that no tag started builds, tests, and packages, keeps the files as an artifact, and creates no release.

Anyone can check that a download was built by this workflow from this repository: `gh attestation verify QuickPane-<version>-x64.zip -R prisant-labs/windows-explorer-quickpane`.

The binaries are not code-signed. The installer reuses the original project's AppId on purpose, so it upgrades an existing install and its Add/Remove Programs entry in place; `Installer.iss` explains why a new AppId would be worse. To try a build without the installer, close QuickPane with `--exit` and replace `QuickPane.exe` in `%LOCALAPPDATA%\QuickPane`. Add/Remove Programs then keeps showing the previously installed version.
