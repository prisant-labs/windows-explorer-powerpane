; QuickPane one-click installer script for Inno Setup (https://jrsoftware.org/isinfo.php).
;
; This produces a single QuickPane-<version>-x64-setup.exe. Because QuickPane is a user-mode app that
; only writes HKCU keys, the installer runs without administrator rights (PrivilegesRequired=lowest).
; It installs the executable, creates a Start menu shortcut and an optional desktop shortcut, registers
; the "Pin to Quick Pane" folder verb, adds a startup entry, and launches the app. Uninstall preserves
; the user's groups and settings under %APPDATA%\QuickPane.
;
; To build: run scripts/build.ps1, then scripts/package.ps1, which compiles this script and puts the
; installer in dist\. Opening this file in Inno Setup and pressing Compile also works once the Release
; build exists; the installer then lands in Output\.

; The version is read from the built program, so AssemblyInfo.cs stays the one place to set it and the
; installer can never disagree with the exe it carries. package.ps1 passes both values explicitly.
#ifndef MyBinDir
  #define MyBinDir AddBackslash(SourcePath) + "QuickPane\bin\x64\Release"
#endif
#ifndef MyAppVersion
  #define MyAppVersion GetVersionNumbersString(MyBinDir + "\QuickPane.exe")
#endif
#define MyAppName "QuickPane"
#define MyAppPublisher "Prisant Labs"
#define MyAppURL "https://github.com/prisant-labs/windows-explorer-quickpane"
#define MyAppExe "QuickPane.exe"

[Setup]
; Deliberately the same AppId as the original project's installer. Both install into the same folder
; and write the same registry keys, so they are one product to Windows: running this installer over an
; existing QuickPane upgrades that install and its Add/Remove Programs entry in place. A new AppId
; would add a second entry, and uninstalling either one would remove the files both rely on.
AppId={{A1C3E5F7-2B4D-6E8F-0A1C-3E5F7B9D1A2C}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
; Without this, Inno Setup names the Add/Remove Programs entry "QuickPane version <version>".
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases
VersionInfoVersion={#MyAppVersion}
UninstallDisplayIcon={app}\{#MyAppExe}
DefaultDirName={localappdata}\{#MyAppName}
DisableProgramGroupPage=yes
DisableDirPage=yes
PrivilegesRequired=lowest
OutputBaseFilename={#MyAppName}-{#MyAppVersion}-x64-setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
; x64compatible matches x64 Windows and Windows 11 on Arm, which runs x64 programs through emulation.
; The older x64 identifier matches x64 Windows only, so it refused Arm computers outright (#215).
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
; Build Release|x64 first so this path exists, or pass /DMyBinDir=<folder> for another build.
Source: "{#MyBinDir}\{#MyAppExe}"; DestDir: "{app}"; Flags: ignoreversion
; The .config carries the per-monitor DPI switch, so it ships beside the executable.
Source: "{#MyBinDir}\{#MyAppExe}.config"; DestDir: "{app}"; Flags: ignoreversion
; MIT requires the copyright notice to travel with every copy, the installed one included.
Source: "LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion

[Icons]
; QuickPane spends its life in the tray, so without a launcher here the only way back in after the app
; is closed is to run the installer again. The Start menu entry goes straight into {userprograms}
; rather than a program group, which is the same path the app repairs at every start, so the installer
; and the app maintain one shortcut between them instead of two competing ones.
Name: "{userprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"; Comment: "QuickPane sidebar for File Explorer"
Name: "{userdesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"; Comment: "QuickPane sidebar for File Explorer"; Tasks: desktopicon

[Registry]
; "Pin to Quick Pane" on any folder.
Root: HKCU; Subkey: "Software\Classes\Directory\shell\QuickPanePin"; ValueType: string; ValueName: ""; ValueData: "Pin to Quick Pane"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\Directory\shell\QuickPanePin"; ValueType: string; ValueName: "Icon"; ValueData: "{app}\{#MyAppExe}"
Root: HKCU; Subkey: "Software\Classes\Directory\shell\QuickPanePin\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExe}"" --pin ""%1"""

; "Pin to Quick Pane" on the folder background (empty area inside a folder).
Root: HKCU; Subkey: "Software\Classes\Directory\Background\shell\QuickPanePin"; ValueType: string; ValueName: ""; ValueData: "Pin to Quick Pane"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\Directory\Background\shell\QuickPanePin\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExe}"" --pin ""%V"""

; Start with Windows.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "QuickPane"; ValueData: """{app}\{#MyAppExe}"""; Flags: uninsdeletevalue

[Dirs]
Name: "{userappdata}\QuickPane\Groups"

[Run]
Filename: "{app}\{#MyAppExe}"; Description: "Launch QuickPane"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Ask the app to close rather than killing it. A force kill reads as a crash to the watchdog, which
; answers by relaunching QuickPane in the middle of the uninstall, and it also skips the teardown that
; restores every Explorer window's layout. The verb blocks until the app and its watchdog are both gone.
Filename: "{app}\{#MyAppExe}"; Parameters: "--exit"; Flags: runhidden waituntilterminated; RunOnceId: "StopQuickPane"

[Code]
{ Same reasoning on the way in: an upgrade over a running copy has to stop it before the executable can
  be replaced, and Inno's own force-close would look like a crash to the watchdog. }
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
  Exe: String;
begin
  Result := '';
  Exe := ExpandConstant('{app}\{#MyAppExe}');
  if FileExists(Exe) then
    Exec(Exe, '--exit', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;
