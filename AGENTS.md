# Instructions for coding agents

This file is for AI coding agents working in this repository, such as Claude Code or Codex. People should start with [README.md](README.md) and [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md), which hold the same rules with more background.

## What this repository is

QuickPane is a Windows tray app in C# 7.3, .NET Framework 4.8, and WPF, built for x64 only. It embeds a pinned-folders pane inside File Explorer windows and Open/Save dialogs. This repository is a fork of `lso2/QuickPane`: the `upstream` remote is the original, and `origin` is the fork.

## Commands

| Task | Command |
|---|---|
| Build | `pwsh -File scripts/build.ps1` |
| Build and test | `pwsh -File scripts/test.ps1` |
| Test only | `pwsh -File scripts/test.ps1 -NoBuild` |
| Rules check only | `pwsh -File scripts/check-rules.ps1` |

A change is not done until the build succeeds with no errors and every test passes. `scripts/test.ps1` runs `scripts/check-rules.ps1` first, which fails on the rules below that a compiler cannot see: a `.cs` file missing from the csproj, a direct `SetWinEventHook` call, a raw private-use icon character, and `SettingsStore`'s default constructor in a test.

## Hard rules

- **Never run `QuickPane.exe`** on a developer's desktop unless the developer asks. A single-instance mutex means a second copy, or `--exit`, acts on the developer's own running copy. Use an isolated environment such as Windows Sandbox for UI checks. For the same reason, never run `scripts/dev-run.ps1`: it is for people trying a build on their own desktop.
- **C# 7.3 only.** No switch expressions, `is not` patterns, nullable reference types, `using` declarations, ranges or indices, records, `init`, or target-typed `new()`.
- **Keep slow work off the UI thread.** In inside mode the UI thread shares Explorer's input queue, so a blocking call there stalls Explorer too. Post disk, network, and cross-process COM work with `WorkQueue.Post`. Post a folder navigation the user clicked with `WorkQueue.PostNavigation`, and put nothing else on that lane. When the UI must wait for an existence check, use `PathStatus.FolderExistsWithin`.
- **Flush settings before reading the file.** `SettingsStore.Save()` returns before `settings.json` is written. Call `Flush()` before anything reads `settings.json` from disk, never call it from a work-lane job, and never post the write yourself.
- **Hook window events through `WinEventHook`.** Never call `SetWinEventHook` directly, because `WinEventHook` shares one native hook per event range. Cap any cache keyed by path with `LruCache`, and write logs only through `Log`.
- **The pane and Settings follow different rules.** The pane matches Explorer's own navigation pane and takes no third-party UI code. Settings may follow the Windows 11 Settings app, including the ported Fluent styles under `QuickPane/Themes/Fluent/`, which stay in the Settings window's own resources and must never reach `Application.Current.Resources`. The pane matches Explorer's own background color by default; a pane color other than Default, picked from the Pane page's swatches, is the user's own opt-in, never a default a theme, profile, or release chooses for them.
- **A new quick action starts off.** Pass `defaultEnabled: false` in `QuickActionLibrary`, so an update never adds a row to someone's pane. The original ten stay on.
- **Never give a custom command's values to a shell.** `CustomCommandTemplate` splits the template first, substitutes per token, quotes each value with `QuickActionLibrary.QuoteArgument`, and starts the program directly. A new way to run something must keep that order. A program that reads an argument as code belongs in `ShellRefusal`, and quoting does not make it safe.
- **A failed read is not an empty answer.** A custom command falls back to the folder when nothing is selected, so `ExplorerNavigator.GetSelectedPaths` reports failure as `SelectionReadStatus.Failed`. Never turn that into an empty list.
- **The update check is QuickPane's only network request of its own.** A change to what it sends, when it runs, or a new request needs the README's privacy section changed in the same pull request.
- **Register new files.** `QuickPane/QuickPane.csproj` is a legacy project that lists every file. A new `.cs` file needs a `<Compile Include="..." />` line.
- **Write icon glyphs as escapes.** Segoe icon glyphs are private-use characters, which some tools drop without a trace. Write `""` in C# and `&#xE713;` in XAML, never the raw character.
- **Keep the test project out of `QuickPane.sln`.** Visual Studio 2019's MSBuild cannot load it.
- **Tests touch only their own temporary directory.** Never use `SettingsStore`'s default constructor, never call `GroupStore.Recycle` or `DeleteTab` on real folders, never show a window, and pass an `onError` handler to `WorkLane` or `WinEventHub` when a test makes one fail, so the failure stays out of the log in the system temp folder.
- **No personal data** in code, comments, tests, commit messages, issues, or pull requests. Use invented examples such as `C:\Demo\Projects`. Never commit screenshots of a real desktop.
- **Never bypass git hooks** with `--no-verify`. If a hook blocks a commit, fix the content.
- **Never weaken branch protection.** `main` accepts changes only through a pull request whose `build-and-test` check passed. Work on a branch, and never change or remove that rule.
- **No new NuGet packages in the app project** without the maintainer's agreement.
- **Do not push, open pull requests, or file issues** unless the maintainer asks.

## How to make a change

1. Read the code around the change, and match its naming, idioms, and comment style. Comments explain why, often citing the bug that forced a choice.
2. Keep one change per commit, so it can be cherry-picked onto a branch cut from `upstream/main`.
3. Put testable decisions in `internal static` methods, and cover them in `tests/QuickPane.Tests`.
4. Run `scripts/test.ps1` before each commit.
5. Record user-visible changes in `CHANGELOG.md` under the next fork version.

## Where things are

| Folder | Contents |
|---|---|
| `QuickPane/Explorer/` | Attaching the pane to Explorer windows and file dialogs, and tracking recent folders. |
| `QuickPane/Interop/` | Win32 declarations, window-event hooks, and dialog and Explorer navigation. |
| `QuickPane/Services/` | Settings, groups and pins on disk, recent items, updates, hotkeys, logging, and SSH mounts. |
| `QuickPane/UI/` | The pane's sections, prompts, and the folder picker. |
| `QuickPane/UI/Settings/` | The native Settings window: its shell, shared card builders, search index, and one page per settings area. |
| `QuickPane/Themes/Fluent/` | A ported Microsoft Fluent theme, scoped to the Settings window only. See its `NOTICE.md`. |
| `QuickPane/Models/Models.cs` | The settings and group data model. |
| `tests/QuickPane.Tests/` | Unit tests. |
| `scripts/` | Build, test, and packaging scripts, and `dev-run.ps1` for people trying a build on their own desktop. |

The "Traps in the code" table in [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md) lists the behaviors that have caused bugs, such as Windows 11 tabs, keyboard focus, file dialogs, profile switches, and pane lifetime. Read it before changing anything in `Explorer/`, `Interop/`, a pane section, `GroupStore`, or the Settings window.

## Local-only notes

A developer may keep an untracked `CLAUDE.md` with machine-specific notes. It imports this file. Never commit it.
