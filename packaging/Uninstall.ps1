<#
    PowerPane uninstaller. Closes the app, then removes the installed files, the context menu verb,
    the startup entry, and the Start menu shortcut. It deliberately leaves %APPDATA%\PowerPane in
    place so your groups and settings survive a reinstall. Delete that folder by hand if you want a
    clean wipe.

        powershell -ExecutionPolicy Bypass -File .\packaging\Uninstall.ps1

    It runs under Windows PowerShell 5.1 or PowerShell 7.
#>

# Closing the app is the one step that must succeed, so it runs with errors as failures. Removing
# what is already gone is not an error, so the steps after it ignore them.
$ErrorActionPreference = 'Stop'

$InstallDir = Join-Path $env:LOCALAPPDATA 'PowerPane'
$InstalledExe = Join-Path $InstallDir 'PowerPane.exe'
# The name ShortcutRepair.LinkName gives the Start menu shortcut, and the name Installer.iss's [Icons]
# entry uses. scripts/check-rules.ps1 fails when this file and ShortcutRepair disagree.
$LinkName = 'Explorer PowerPane.lnk'

# Every PowerPane process, the tray app and its watchdog alike. A process whose path cannot be read
# still counts, so a copy is never reported gone while it holds a file open.
function Get-PowerPaneProcesses { @(Get-Process -Name 'PowerPane' -ErrorAction SilentlyContinue) }

# Ask the running copy to close with --exit, as the setup program's uninstaller does, and never kill
# it. A kill reads as a crash to the watchdog, which relaunches the app moments later, and it skips the
# teardown that restores every Explorer window's layout. --exit waits until the tray app and its
# watchdog are both gone, and exits 1 when they are still there after 15 seconds.
function Stop-PowerPane([string]$fallbackExe) {
    if ((Get-PowerPaneProcesses).Count -eq 0) { return }
    $signal = Get-PowerPaneProcesses | Where-Object { $_.Path } | Select-Object -First 1 -ExpandProperty Path
    if (-not $signal -and (Test-Path -LiteralPath $fallbackExe -PathType Leaf)) { $signal = $fallbackExe }
    if (-not $signal) {
        throw 'PowerPane is running, but this script cannot find a PowerPane.exe to ask it to close. Close it from its tray icon, then run this script again. Nothing was removed.'
    }
    Write-Host "Closing the running copy with --exit."
    $request = Start-Process -FilePath $signal -ArgumentList '--exit' -Wait -PassThru
    $deadline = (Get-Date).AddSeconds(10)
    while ((Get-PowerPaneProcesses).Count -gt 0 -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 250 }
    if ($request.ExitCode -ne 0 -or (Get-PowerPaneProcesses).Count -gt 0) {
        throw 'PowerPane did not close when asked. Close it from its tray icon, then run this script again. Nothing was removed.'
    }
}

# A shortcut's target, or $null when it cannot be read.
function Get-LinkTarget([string]$path) {
    try { (New-Object -ComObject WScript.Shell).CreateShortcut($path).TargetPath } catch { $null }
}

Write-Host 'Uninstalling PowerPane...' -ForegroundColor Cyan

# 1. Close the running instance. Its own shutdown restores every Explorer window to its normal layout.
Stop-PowerPane $InstalledExe

$ErrorActionPreference = 'SilentlyContinue'

# 2. Remove the "Add to PowerPane" verb: the folder, folder-background, file, and drive keys. The
#    registry API is used because PowerShell reads the * in the file key as a wildcard.
foreach ($classKey in @('Directory', 'Directory\Background', '*', 'Drive')) {
    [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKeyTree("Software\Classes\$classKey\shell\PowerPanePin", $false)
}

# 3. Remove the startup entry.
Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'PowerPane' -Force

# 4. Remove the shortcuts. The app recreates the Start menu shortcut at every start, so it is removed
#    whatever it points at, as the setup program's uninstaller does. A desktop shortcut exists only if
#    you asked for one, so it is removed only when it points into the install folder being deleted.
Remove-Item -LiteralPath (Join-Path ([Environment]::GetFolderPath('Programs')) $LinkName) -Force
$desktopLink = Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) $LinkName
if (Test-Path -LiteralPath $desktopLink) {
    $target = Get-LinkTarget $desktopLink
    if ($target -and $target.StartsWith($InstallDir + '\', [StringComparison]::OrdinalIgnoreCase)) {
        Remove-Item -LiteralPath $desktopLink -Force
    }
}

# 5. Remove the installed files, including the PinQueue folder the verb writes into.
Remove-Item -LiteralPath $InstallDir -Recurse -Force

Write-Host ''
Write-Host 'PowerPane removed. Your groups and settings in %APPDATA%\PowerPane were kept.' -ForegroundColor Green
Write-Host 'If Explorer still looks shifted, open a new Explorer window or sign out and back in.'
