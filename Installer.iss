; PowerPane one-click installer script for Inno Setup (https://jrsoftware.org/isinfo.php).
;
; This produces a single PowerPane-<version>-x64-setup.exe, listed as "Explorer PowerPane". Because PowerPane is a user-mode app that
; only writes HKCU keys, the installer runs without administrator rights (PrivilegesRequired=lowest).
; It installs the executable, creates a Start menu shortcut and an optional desktop shortcut, registers
; the "Pin to PowerPane" folder verb, adds a startup entry, and launches the app. Uninstall preserves
; the user's groups and settings under %APPDATA%\PowerPane.
;
; To build: run scripts/build.ps1, then scripts/package.ps1, which compiles this script and puts the
; installer in dist\. Opening this file in Inno Setup and pressing Compile also works once the Release
; build exists; the installer then lands in Output\.

; The version is read from the built program, so AssemblyInfo.cs stays the one place to set it and the
; installer can never disagree with the exe it carries. package.ps1 passes both values explicitly.
#ifndef MyBinDir
  #define MyBinDir AddBackslash(SourcePath) + "PowerPane\bin\x64\Release"
#endif
#ifndef MyAppVersion
  #define MyAppVersion GetVersionNumbersString(MyBinDir + "\PowerPane.exe")
#endif
; Two names, because MyAppName is what people see and MyAppFolder is where files go (rename decision D9):
; the Add/Remove Programs entry and the Start menu shortcut say "Explorer PowerPane", while the program
; folder, the setup file, and every other internal name stay plain "PowerPane".
#define MyAppName "Explorer PowerPane"
#define MyAppFolder "PowerPane"
#define MyAppPublisher "Prisant Labs"
#define MyAppURL "https://github.com/prisant-labs/windows-explorer-powerpane"
#define MyAppExe "PowerPane.exe"

[Setup]
; A new AppId for the rename to PowerPane (rename decision D5). QuickPane's installers, this fork's and
; the original project's, used {A1C3E5F7-2B4D-6E8F-0A1C-3E5F7B9D1A2C} and a different program folder,
; so PowerPane is a separate product to Windows. PrepareToInstall below refuses to install while
; QuickPane is still there, because the two would attach two panes to every Explorer window.
AppId={{6EFF7253-4671-4EAB-AACF-28AAE07F0CA3}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
; Without this, Inno Setup names the Add/Remove Programs entry "Explorer PowerPane version <version>".
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases
VersionInfoVersion={#MyAppVersion}
UninstallDisplayIcon={app}\{#MyAppExe}
DefaultDirName={localappdata}\{#MyAppFolder}
DisableProgramGroupPage=yes
DisableDirPage=yes
PrivilegesRequired=lowest
OutputBaseFilename={#MyAppFolder}-{#MyAppVersion}-x64-setup
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
; PowerPane spends its life in the tray, so without a launcher here the only way back in after the app
; is closed is to run the installer again. The Start menu entry goes straight into {userprograms}
; rather than a program group, which is the same path the app repairs at every start, so the installer
; and the app maintain one shortcut between them instead of two competing ones. Its name, MyAppName,
; must match ShortcutRepair.LinkName ("Explorer PowerPane.lnk").
Name: "{userprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"; Comment: "PowerPane sidebar for File Explorer"
Name: "{userdesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"; Comment: "PowerPane sidebar for File Explorer"; Tasks: desktopicon

[Registry]
; "Pin to PowerPane" on any folder.
Root: HKCU; Subkey: "Software\Classes\Directory\shell\PowerPanePin"; ValueType: string; ValueName: ""; ValueData: "Pin to PowerPane"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\Directory\shell\PowerPanePin"; ValueType: string; ValueName: "Icon"; ValueData: "{app}\{#MyAppExe}"
Root: HKCU; Subkey: "Software\Classes\Directory\shell\PowerPanePin\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExe}"" --pin ""%1"""

; "Pin to PowerPane" on the folder background (empty area inside a folder).
Root: HKCU; Subkey: "Software\Classes\Directory\Background\shell\PowerPanePin"; ValueType: string; ValueName: ""; ValueData: "Pin to PowerPane"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\Directory\Background\shell\PowerPanePin\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExe}"" --pin ""%V"""

; Start with Windows.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "PowerPane"; ValueData: """{app}\{#MyAppExe}"""; Flags: uninsdeletevalue

[Dirs]
Name: "{userappdata}\PowerPane\Groups"

[Run]
Filename: "{app}\{#MyAppExe}"; Description: "Launch Explorer PowerPane"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Ask the app to close rather than killing it. A force kill reads as a crash to the watchdog, which
; answers by relaunching PowerPane in the middle of the uninstall, and it also skips the teardown that
; restores every Explorer window's layout. The verb blocks until the app and its watchdog are both gone.
Filename: "{app}\{#MyAppExe}"; Parameters: "--exit"; Flags: runhidden waituntilterminated; RunOnceId: "StopPowerPane"

[Code]
{ Same reasoning on the way in: an upgrade over a running copy has to stop it before the executable can
  be replaced, and Inno's own force-close would look like a crash to the watchdog. }

{ QuickPane, the name before the rename, installed under its own AppId, folder, mutex, and exit signal,
  so PowerPane cannot stop it or replace it. Both running would attach two panes to one Explorer window,
  so setup stops until QuickPane is uninstalled. The uninstall key covers both installers that used the
  old AppId; the Run value covers a copy installed by the zip's Install.ps1, which writes no uninstall key. }
function QuickPaneInstalled: Boolean;
var
  Key: String;
begin
  Key := 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{A1C3E5F7-2B4D-6E8F-0A1C-3E5F7B9D1A2C}_is1';
  Result := RegKeyExists(HKCU, Key) or RegKeyExists(HKLM, Key) or
    RegValueExists(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'QuickPane');
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
  Exe: String;
begin
  if QuickPaneInstalled then
  begin
    Result := 'QuickPane is still installed. Explorer PowerPane replaces it, and the two cannot run ' +
      'side by side. Uninstall QuickPane from Settings, Apps, then run this setup again. Uninstalling ' +
      'QuickPane keeps your pins and settings in %APPDATA%\QuickPane.';
    exit;
  end;
  Result := '';
  Exe := ExpandConstant('{app}\{#MyAppExe}');
  if FileExists(Exe) then
    Exec(Exe, '--exit', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;
