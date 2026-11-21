<#
    PowerPane installer (no administrator rights required).

    This architecture is a single user-mode executable plus HKCU registry entries, so unlike the old
    COM build there is no regsvr32 and no elevation. The script copies the same four files as the setup
    program (PowerPane.exe, its .config, LICENSE.txt, and THIRD-PARTY-NOTICES.txt) into
    %LOCALAPPDATA%\PowerPane, registers the "Add to PowerPane" right-click verb, adds a startup entry,
    and launches the app.

    It takes the files from a build in this repository, or from the same folder as this script, which
    is where they are after unzipping a release zip and copying this script beside them.

    Run it by right-clicking and choosing "Run with PowerShell", or from a console:
        powershell -ExecutionPolicy Bypass -File .\packaging\Install.ps1

    It runs under Windows PowerShell 5.1 or PowerShell 7.
#>

$ErrorActionPreference = 'Stop'

$InstallDir = Join-Path $env:LOCALAPPDATA 'PowerPane'
$AppData    = Join-Path $env:APPDATA   'PowerPane'
$ExeName    = 'PowerPane.exe'

# Every file the setup program installs, keyed by its name in the install folder. Installer.iss's
# [Files] section is the reference, and scripts/check-rules.ps1 fails when the two lists differ. The
# .config carries the per-monitor DPI switch, so without it a pane keeps the scale of the display the
# app started on. MIT requires LICENSE and the Fluent styles' notices to travel with every copy.
function Find-Payload {
    # $PSScriptRoot, not $MyInvocation.MyCommand.Definition: inside a function the latter is the
    # function's own text, so the old lookup never found the script's folder. This script lives in
    # packaging\, one folder below the repository root that holds the build.
    $here = $PSScriptRoot
    $repo = Split-Path -Parent $here
    $layouts = @(
        @{ Bin = $here; Docs = $here },
        @{ Bin = (Join-Path $repo 'PowerPane\bin\x64\Release'); Docs = $repo },
        @{ Bin = (Join-Path $repo 'PowerPane\bin\x64\Debug'); Docs = $repo }
    )
    foreach ($layout in $layouts) {
        $exe = Join-Path $layout.Bin $ExeName
        if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { continue }
        $payload = [ordered]@{
            'PowerPane.exe'           = $exe
            'PowerPane.exe.config'    = (Join-Path $layout.Bin 'PowerPane.exe.config')
            'LICENSE.txt'             = (Join-Path $layout.Docs 'LICENSE')
            'THIRD-PARTY-NOTICES.txt' = (Join-Path $layout.Docs 'THIRD-PARTY-NOTICES.txt')
        }
        $missing = @($payload.Values | Where-Object { -not (Test-Path -LiteralPath $_ -PathType Leaf) })
        if ($missing.Count -gt 0) {
            throw "Found $exe, but not the files that must ship beside it: $($missing -join ', '). Nothing was changed."
        }
        return $payload
    }
    throw "Could not find $ExeName. Build the solution first (scripts\build.ps1), or place the release zip's files next to this script. Nothing was changed."
}

# Every PowerPane process, the tray app and its watchdog alike. A process whose path cannot be read
# still counts, so a copy is never reported gone while it holds a file open.
function Get-PowerPaneProcesses { @(Get-Process -Name 'PowerPane' -ErrorAction SilentlyContinue) }

# Ask the running copy to close with --exit, as the setup program does, and never kill it. A kill
# reads as a crash to the watchdog, which relaunches the app moments later, and it skips the teardown
# that restores every Explorer window's layout. --exit waits until the tray app and its watchdog are
# both gone, and exits 1 when they are still there after 15 seconds.
function Stop-PowerPane([string]$fallbackExe) {
    if ((Get-PowerPaneProcesses).Count -eq 0) { return }
    $signal = Get-PowerPaneProcesses | Where-Object { $_.Path } | Select-Object -First 1 -ExpandProperty Path
    if (-not $signal -and (Test-Path -LiteralPath $fallbackExe -PathType Leaf)) { $signal = $fallbackExe }
    if (-not $signal) {
        throw 'PowerPane is running, but this script cannot find a PowerPane.exe to ask it to close. Close it from its tray icon, then run this script again.'
    }
    Write-Host "Closing the running copy with --exit."
    $request = Start-Process -FilePath $signal -ArgumentList '--exit' -Wait -PassThru
    $deadline = (Get-Date).AddSeconds(10)
    while ((Get-PowerPaneProcesses).Count -gt 0 -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 250 }
    if ($request.ExitCode -ne 0 -or (Get-PowerPaneProcesses).Count -gt 0) {
        throw 'PowerPane did not close when asked. Close it from its tray icon, then run this script again.'
    }
}

# QuickPane, the name before the rename, runs under its own mutex and exit signal, so both copies would
# attach two panes to every Explorer window. Stop until it is removed, as the setup program does.
$quickPaneRun = Get-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'QuickPane' -ErrorAction SilentlyContinue
$quickPaneKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{A1C3E5F7-2B4D-6E8F-0A1C-3E5F7B9D1A2C}_is1'
if ($quickPaneRun -or (Test-Path $quickPaneKey)) {
    throw 'QuickPane is still installed. Uninstall it from Settings, Apps (or run its Uninstall.ps1), then run this script again. Your pins and settings in %APPDATA%\QuickPane are kept.'
}

Write-Host 'Installing PowerPane...' -ForegroundColor Cyan

# 1. Find every file before touching the running copy, so a missing build stops here with the app
#    still running and nothing changed.
$payload = Find-Payload
$exe = Join-Path $InstallDir $ExeName

# 2. Close any running instance so its files are not locked.
Stop-PowerPane $exe

# 3. Copy the files into the per-user install folder.
New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
foreach ($name in $payload.Keys) {
    Copy-Item -Force -LiteralPath $payload[$name] -Destination (Join-Path $InstallDir $name)
}

# 4. Register the "Add to PowerPane" context menu verb under HKCU (no admin needed). Four keys, all
#    named PowerPanePin: folders, folder backgrounds, files, and drives. Installer.iss writes the same
#    four, Uninstall.ps1 removes them, and scripts/check-rules.ps1 fails when the three disagree. The
#    registry API is used because PowerShell reads the * in the file key as a wildcard.
$verbCmd = "`"$exe`" --pin `"%1`""
$verbCmdBg = "`"$exe`" --pin `"%V`""

function Register-AddVerb([string]$classKey, [string]$command) {
    $verb = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey("Software\Classes\$classKey\shell\PowerPanePin")
    $verb.SetValue('', 'Add to PowerPane')
    $verb.SetValue('Icon', $exe)
    $verb.Close()
    $cmd = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey("Software\Classes\$classKey\shell\PowerPanePin\command")
    $cmd.SetValue('', $command)
    $cmd.Close()
}

Register-AddVerb 'Directory' $verbCmd
Register-AddVerb 'Directory\Background' $verbCmdBg
Register-AddVerb '*' $verbCmd
Register-AddVerb 'Drive' $verbCmd

# 5. Start with Windows.
Set-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'PowerPane' -Value "`"$exe`""

# 6. Make sure the data folder exists. The app writes a default settings.json on first run.
New-Item -ItemType Directory -Force -Path (Join-Path $AppData 'Groups') | Out-Null

# 7. Launch. The app creates its Start menu shortcut at every start (ShortcutRepair).
Start-Process -FilePath $exe

Write-Host ''
Write-Host 'PowerPane installed and running.' -ForegroundColor Green
Write-Host 'A tray icon is now in the notification area. Open any File Explorer window and the sidebar appears on the left.'
Write-Host 'If you still see the built-in navigation pane, turn it off once via View, Navigation pane, Navigation pane. Explorer remembers the choice.'
