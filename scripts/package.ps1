# Packages a built PowerPane for release: the installer PowerPane-<version>-x64-setup.exe, built
# from Installer.iss with Inno Setup 6; PowerPane-<version>-x64.zip holding the program and its
# license; SHA256SUMS.txt for both and for each file in the zip; and notes.md for the release page.
# CI runs it on every build, so packaging is proven before anyone pushes a tag; the release
# workflow runs it on the tagged commit. Run scripts/build.ps1 first.
#
# Usage:  pwsh -File scripts/package.ps1 [-Version 3.10.0.3] [-Configuration Release] [-OutDir dist] [-NoNotes] [-NoInstaller]
# Output: <OutDir>\PowerPane-<version>-x64-setup.exe unless -NoInstaller, PowerPane-<version>-x64.zip,
#         SHA256SUMS.txt, and notes.md unless -NoNotes
param(
    [string]$Version,
    [ValidateSet('Release', 'Debug')][string]$Configuration = 'Release',
    [string]$OutDir = 'dist',
    [switch]$NoNotes,
    [switch]$NoInstaller
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$bin = Join-Path $repo "PowerPane\bin\x64\$Configuration"
$exe = Join-Path $bin 'PowerPane.exe'
$config = Join-Path $bin 'PowerPane.exe.config'
$license = Join-Path $repo 'LICENSE'
foreach ($f in $exe, $config) {
    if (-not (Test-Path $f)) { throw "Missing $f. Run scripts/build.ps1 -Configuration $Configuration first." }
}

# The exe's own file version is the source of truth. A tag that disagrees with it means
# AssemblyInfo.cs was not bumped, and the zip would carry a program that misreports its version
# to the in-app update check.
$built = (Get-Item $exe).VersionInfo.FileVersion
if (-not $Version) { $Version = $built }
if ($built -ne $Version) { throw "Built version is $built, expected $Version. Bump PowerPane\Properties\AssemblyInfo.cs." }

if (-not [System.IO.Path]::IsPathRooted($OutDir)) { $OutDir = Join-Path $repo $OutDir }
if (Test-Path $OutDir) { Remove-Item $OutDir -Recurse -Force }
New-Item -ItemType Directory -Force $OutDir | Out-Null

# LICENSE ships inside the zip because MIT requires the copyright notice to travel with copies.
$zip = Join-Path $OutDir "PowerPane-$Version-x64.zip"
Compress-Archive -Path $exe, $config, $license -DestinationPath $zip

$packaged = @($exe, $config, $license, $zip)
if (-not $NoInstaller) {
    $iscc = (Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source
    if (-not $iscc) {
        $iscc = @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
                  "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
    }
    if (-not $iscc) { throw 'Inno Setup 6 (ISCC.exe) was not found. Install it from https://jrsoftware.org/isinfo.php, or pass -NoInstaller.' }
    # Both values are passed so the installer carries exactly the build checked above, whatever the
    # configuration, rather than whatever Installer.iss would find on its own.
    & $iscc /Qp "/DMyAppVersion=$Version" "/DMyBinDir=$bin" "/O$OutDir" (Join-Path $repo 'Installer.iss')
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed with exit code $LASTEXITCODE." }
    $setup = Join-Path $OutDir "PowerPane-$Version-x64-setup.exe"
    if (-not (Test-Path $setup)) { throw "Inno Setup did not produce $setup." }
    $packaged += $setup
}

$sums = Join-Path $OutDir 'SHA256SUMS.txt'
@(
    foreach ($f in $packaged) {
        '{0}  {1}' -f (Get-FileHash $f -Algorithm SHA256).Hash.ToLowerInvariant(), (Split-Path $f -Leaf)
    }
) | Set-Content $sums -Encoding ascii

if (-not $NoNotes) {
    # A full release document in docs/releases/<version>.md is used as is, minus its "# ..." title,
    # which would repeat the release title. Otherwise the notes are this version's changelog section
    # plus the generic install steps below.
    $notes = Join-Path $OutDir 'notes.md'
    $doc = Join-Path $repo "docs\releases\$Version.md"
    if (Test-Path $doc) {
        (Get-Content $doc | Select-Object -Skip 1 | Out-String).Trim() | Set-Content $notes -Encoding utf8
    } else {
        $cl = Get-Content (Join-Path $repo 'CHANGELOG.md') -Raw
        $m = [regex]::Match($cl, "(?ms)^## \[$([regex]::Escape($Version))\][^\n]*\n(.*?)(?=^## \[)")
        if (-not $m.Success) { throw "No docs\releases\$Version.md and no CHANGELOG.md section for $Version." }
        $installSteps = @'
## Install

Download `PowerPane-<version>-x64-setup.exe` and run it. It installs for the current user, with no administrator rights. Over an existing PowerPane, including one installed by the original project's installer, it closes PowerPane, replaces the program, and updates the same Add/Remove Programs entry. Your settings, groups, and pins in `%APPDATA%\PowerPane` stay where they are.

The zip holds the same program, for replacing the files by hand instead:

1. Close PowerPane, and wait for it to finish: `Start-Process "$env:LOCALAPPDATA\PowerPane\PowerPane.exe" -ArgumentList '--exit' -Wait`
2. Keep a copy of the current `PowerPane.exe` in `%LOCALAPPDATA%\PowerPane`, so you can go back.
3. Unzip the download, and copy `PowerPane.exe` and `PowerPane.exe.config` into `%LOCALAPPDATA%\PowerPane`, replacing the files there.
4. Start `%LOCALAPPDATA%\PowerPane\PowerPane.exe` again.

`SHA256SUMS.txt` lists the SHA-256 hash of the installer, the zip, and each file inside the zip. The files are not code-signed, so Windows SmartScreen may warn before the installer first runs. This release also carries a GitHub build provenance attestation, which `gh attestation verify <file> -R prisant-labs/windows-explorer-powerpane` checks.
'@
        $installSteps = $installSteps.Replace('<version>', $Version)
        ($m.Groups[1].Value.Trim() + "`n`n" + $installSteps) | Set-Content $notes -Encoding utf8
    }
    # Release notes are posted publicly, so stop on anything that looks like a local path.
    $hits = Select-String -Path $notes -Pattern '(?i)\b[a-z]:\\(users|projects)\\|onedrive'
    if ($hits) { $hits; throw 'Possible personal data in the release notes.' }
}

Get-Content $sums
"Packaged $Version into $OutDir"
