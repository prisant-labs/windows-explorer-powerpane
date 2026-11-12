# Tries a development build on your own desktop, then puts the installed copy back.
#
# Only one copy of PowerPane runs at a time, so a development build has to replace the installed copy
# while it runs. This script does the four manual steps in docs/DEVELOPMENT.md in order:
#   1. Builds PowerPane.exe, unless -NoBuild is given.
#   2. Backs up %APPDATA%\PowerPane, because a development build reads and writes the same settings.
#   3. Closes whatever copy is running with --exit, and starts the development build.
#   4. Waits until the development build has exited, then starts the installed copy again if it was
#      running before. Starting it also points the Start menu shortcut back at it.
#
# Close the development build from its tray icon when you are done. It never kills a copy: a kill skips
# the layout restore, and the watchdog would restart the app. The watchdog also relaunches a crashed
# build, so the script waits until no copy from the build folder has run for a few seconds.
#
# If the script is interrupted, run it again with -RestoreInstalled to close the development build and
# start the installed copy.
#
# This script acts on the copy running on your desktop. It is for people, not for coding agents.
# It runs under Windows PowerShell 5.1 or PowerShell 7.
#
# Usage:  pwsh -File scripts/dev-run.ps1 [-Configuration Release|Debug] [-NoBuild] [-Exe <path>]
#         pwsh -File scripts/dev-run.ps1 -RestoreInstalled
param(
    [ValidateSet('Release', 'Debug')][string]$Configuration = 'Release',
    [switch]$NoBuild,
    [string]$Exe,
    [switch]$RestoreInstalled
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$installed = Join-Path $env:LOCALAPPDATA 'PowerPane\PowerPane.exe'
$dataDir = Join-Path $env:APPDATA 'PowerPane'
$backupRoot = Join-Path $env:LOCALAPPDATA 'PowerPane-dev-backups'
$keepBackups = 10
# The watchdog relaunches a crashed copy 2.5 seconds after it exits, so a copy counts as gone only
# after this long with no process from its folder.
$goneAfterSec = 6

function Get-FullPath([string]$path) { [IO.Path]::GetFullPath($path) }

# Every running PowerPane process, the tray app and its watchdog alike, with a readable path.
function Get-Copies { @(Get-Process PowerPane -ErrorAction SilentlyContinue | Where-Object { $_.Path }) }

function Get-CopiesOf([string]$path) {
    $full = Get-FullPath $path
    @(Get-Copies | Where-Object { (Get-FullPath $_.Path) -ieq $full })
}

function Close-Running {
    $running = Get-Copies
    if ($running.Count -eq 0) { return }
    $path = $running[0].Path
    Write-Host "Closing the running copy ($path) with --exit."
    # --exit asks the copy holding the single-instance mutex to close, and it waits until the tray app
    # and its watchdog have both gone.
    Start-Process -FilePath $path -ArgumentList '--exit' -Wait
    $deadline = (Get-Date).AddSeconds(15)
    while ((Get-Copies).Count -gt 0 -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 250 }
    if ((Get-Copies).Count -gt 0) {
        throw 'PowerPane is still running after --exit. Close it from its tray icon, then run this again.'
    }
}

function Start-Installed {
    if (-not (Test-Path $installed)) { return }
    Start-Process -FilePath $installed
    Write-Host "Started the installed copy again: $installed"
}

if ($RestoreInstalled) {
    Close-Running
    if (Test-Path $installed) { Start-Installed } else { Write-Host "No installed copy at $installed." }
    return
}

if (-not $Exe) {
    if (-not $NoBuild) { & (Join-Path $PSScriptRoot 'build.ps1') -Configuration $Configuration }
    $Exe = Join-Path $repo "PowerPane\bin\x64\$Configuration\PowerPane.exe"
}
if (-not (Test-Path $Exe)) { throw "No build at $Exe. Build it first, or drop -NoBuild." }
$Exe = Get-FullPath $Exe
if ((Test-Path $installed) -and ($Exe -ieq (Get-FullPath $installed))) {
    throw "$Exe is the installed copy. Point -Exe at a development build instead."
}

$installedWasRunning = (Test-Path $installed) -and (Get-CopiesOf $installed).Count -gt 0

if (Test-Path $dataDir) {
    $backup = Join-Path $backupRoot (Get-Date -Format 'yyyyMMdd-HHmmss')
    New-Item -ItemType Directory -Force $backup | Out-Null
    Copy-Item $dataDir (Join-Path $backup 'PowerPane') -Recurse
    Write-Host "Backed up your settings and groups to $backup"
    Get-ChildItem $backupRoot -Directory | Sort-Object Name -Descending | Select-Object -Skip $keepBackups |
        Remove-Item -Recurse -Force
}

Close-Running

$version = (Get-Item $Exe).VersionInfo.FileVersion
$hash = (Get-FileHash -Algorithm SHA256 $Exe).Hash.Substring(0, 12).ToLowerInvariant()
Start-Process -FilePath $Exe
Write-Host "Running the development build $version (sha256 $hash...) from $Exe"
Write-Host 'Close it from its tray icon when you are done. If you stop this script early, run it with -RestoreInstalled.'

$goneSince = $null
while ($true) {
    Start-Sleep -Milliseconds 500
    if ((Get-CopiesOf $Exe).Count -gt 0) { $goneSince = $null; continue }
    if (-not $goneSince) { $goneSince = Get-Date; continue }
    if (((Get-Date) - $goneSince).TotalSeconds -ge $goneAfterSec) { break }
}
Write-Host 'The development build has exited.'

if (-not $installedWasRunning) {
    if (Test-Path $installed) {
        Write-Host 'The installed copy was not running before, so it was not started. Start it once to point the Start menu shortcut back at it.'
    }
} elseif ((Get-Copies).Count -gt 0) {
    Write-Host 'Another copy of PowerPane is running now, so the installed copy was not started.'
} else {
    Start-Installed
}
