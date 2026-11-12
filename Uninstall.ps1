<#
    PowerPane uninstaller. Removes the executable, the context menu verb, and the startup entry.
    It deliberately leaves %APPDATA%\PowerPane in place so your groups and settings survive a
    reinstall. Delete that folder by hand if you want a clean wipe.

        powershell -ExecutionPolicy Bypass -File .\Uninstall.ps1
#>

$ErrorActionPreference = 'SilentlyContinue'

$InstallDir = Join-Path $env:LOCALAPPDATA 'PowerPane'

Write-Host 'Uninstalling PowerPane...' -ForegroundColor Cyan

# 1. Stop the running instance. Disposing restores every Explorer window to its normal layout.
Get-Process -Name 'PowerPane' -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 400

# 2. Remove the context menu verb (both folder and folder-background variants).
Remove-Item -Path 'HKCU:\Software\Classes\Directory\shell\PowerPanePin' -Recurse -Force
Remove-Item -Path 'HKCU:\Software\Classes\Directory\Background\shell\PowerPanePin' -Recurse -Force

# 3. Remove the startup entry.
Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'PowerPane' -Force

# 4. Remove the installed files.
Remove-Item -Path $InstallDir -Recurse -Force

Write-Host ''
Write-Host 'PowerPane removed. Your groups and settings in %APPDATA%\PowerPane were kept.' -ForegroundColor Green
Write-Host 'If Explorer still looks shifted, open a new Explorer window or sign out and back in.'
