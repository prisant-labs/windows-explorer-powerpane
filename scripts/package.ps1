# Packages a built PowerPane for release: the installer PowerPane-<version>-x64-setup.exe, built
# from Installer.iss with Inno Setup 6, and the same file as PowerPane-x64-setup.exe; PowerPane-<version>-x64.zip holding the program, its
# license, and THIRD-PARTY-NOTICES.txt; SHA256SUMS.txt for both and for each file in the zip; and notes.md for the release page.
# CI runs it on every build, so packaging is proven before anyone pushes a tag; the release
# workflow runs it on the tagged commit. Run scripts/build.ps1 first.
#
# Usage:  pwsh -File scripts/package.ps1 [-Version 4.1.0] [-Configuration Release] [-OutDir dist] [-NoNotes] [-NoInstaller]
# Output: <OutDir>\PowerPane-<version>-x64-setup.exe and PowerPane-x64-setup.exe unless -NoInstaller, PowerPane-<version>-x64.zip,
#         SHA256SUMS.txt, and notes.md unless -NoNotes
param(
    [string]$Version,
    [ValidateSet('Release', 'Debug')][string]$Configuration = 'Release',
    [string]$OutDir = 'dist',
    [switch]$NoNotes,
    [switch]$NoInstaller
)
$ErrorActionPreference = 'Stop'

# The files every release zip holds, and the copyright lines its notices file must keep. A zip
# that differs is a release that would ship without a licence or a notice MIT requires.
$ZipEntries = @('LICENSE', 'PowerPane.exe', 'PowerPane.exe.config', 'THIRD-PARTY-NOTICES.txt')
$NoticeLines = @(
    'Copyright (c) Leszek Pomianowski and WPF UI Contributors',
    'Copyright (c) Microsoft Corporation',
    'Copyright (c) .NET Foundation and Contributors',
    'Permission is hereby granted, free of charge, to any person obtaining a copy'
)

# Returns $null when the zip holds exactly $ZipEntries and its notices file holds every line in
# $NoticeLines as a whole line, and otherwise a sentence that names what is wrong. Whole lines,
# because "Copyright (c) Microsoft Corporation" also appears inside an attribution sentence, and
# a substring match would pass a file that lost the licence's own copyright line.
function Test-PackageZip([string]$zipPath) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
    try {
        $names = @($archive.Entries | ForEach-Object { $_.FullName } | Sort-Object)
        $missing = @($ZipEntries | Where-Object { $names -notcontains $_ })
        $extra = @($names | Where-Object { $ZipEntries -notcontains $_ })
        if ($missing.Count -gt 0) { return "it lacks $($missing -join ', ')." }
        if ($extra.Count -gt 0) { return "it holds unexpected files: $($extra -join ', ')." }
        $entry = $archive.GetEntry('THIRD-PARTY-NOTICES.txt')
        $reader = New-Object System.IO.StreamReader($entry.Open())
        try { $text = $reader.ReadToEnd() } finally { $reader.Dispose() }
        $lines = @($text -split "`r?`n" | ForEach-Object { $_.Trim() })
        $lost = @($NoticeLines | Where-Object { $lines -notcontains $_ })
        if ($lost.Count -gt 0) { return "THIRD-PARTY-NOTICES.txt lacks the line '$($lost[0])'." }
        return $null
    } finally {
        $archive.Dispose()
    }
}

$repo = Split-Path $PSScriptRoot -Parent
$bin = Join-Path $repo "PowerPane\bin\x64\$Configuration"
$exe = Join-Path $bin 'PowerPane.exe'
$config = Join-Path $bin 'PowerPane.exe.config'
$license = Join-Path $repo 'LICENSE'
$notices = Join-Path $repo 'THIRD-PARTY-NOTICES.txt'
foreach ($f in $exe, $config) {
    if (-not (Test-Path $f)) { throw "Missing $f. Run scripts/build.ps1 -Configuration $Configuration first." }
}
foreach ($f in $license, $notices) {
    if (-not (Test-Path $f)) { throw "Missing $f. Every package carries it." }
}

# The exe's own file version is the source of truth. A tag that disagrees with it means
# AssemblyInfo.cs was not bumped, and the zip would carry a program that misreports its version
# to the in-app update check.
# The release version is spelled as its tag is (4.1.0 from Semantic Versioning on, 4.0.0.1 before),
# and scripts/release-version.ps1 says which file version that requires (4.1.0.0, or 4.0.0.1).
$built = (Get-Item $exe).VersionInfo.FileVersion
if (-not $Version) {
    $parts = $built.Split('.')
    $Version = if ($parts.Count -eq 4 -and $parts[3] -eq '0') { $parts[0..2] -join '.' } else { $built }
}
$expected = ((& (Join-Path $PSScriptRoot 'release-version.ps1') -Version $Version) -match '^fileVersion=') -replace '^fileVersion=', ''
if ($built -ne $expected) { throw "Built version is $built, but release $Version needs $expected. Bump PowerPane\Properties\AssemblyInfo.cs." }

if (-not [System.IO.Path]::IsPathRooted($OutDir)) { $OutDir = Join-Path $repo $OutDir }
if (Test-Path $OutDir) { Remove-Item $OutDir -Recurse -Force }
New-Item -ItemType Directory -Force $OutDir | Out-Null

# LICENSE ships inside the zip because MIT requires the copyright notice to travel with copies.
# THIRD-PARTY-NOTICES.txt ships for the same reason: the Settings window's Fluent styles are MIT
# code with their own copyright holders, and the XAML comments that name them are dropped when
# the XAML is compiled, so the program itself carries none of them (#278).
$zip = Join-Path $OutDir "PowerPane-$Version-x64.zip"
Compress-Archive -Path $exe, $config, $license, $notices -DestinationPath $zip

# Check the zip as built, not the source files, so a change to the list above that drops a
# notice fails here, and in CI, rather than in a published release.
$zipProblem = Test-PackageZip $zip
if ($zipProblem) { throw "The zip is incomplete: $zipProblem" }

$packaged = @($exe, $config, $license, $notices, $zip)
if (-not $NoInstaller) {
    $iscc = (Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source
    if (-not $iscc) {
        $iscc = @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
                  "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
    }
    if (-not $iscc) { throw 'Inno Setup 6 (ISCC.exe) was not found. Install it from https://jrsoftware.org/isinfo.php, or pass -NoInstaller.' }
    # Both values are passed so the installer carries exactly the build checked above, whatever the
    # configuration, rather than whatever Installer.iss would find on its own.
    & $iscc /Qp "/DMyAppVersion=$Version" "/DMyBinDir=$bin" "/O$OutDir" (Join-Path $repo 'packaging\Installer.iss')
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed with exit code $LASTEXITCODE." }
    $setup = Join-Path $OutDir "PowerPane-$Version-x64-setup.exe"
    if (-not (Test-Path $setup)) { throw "Inno Setup did not produce $setup." }
    # The same installer under a name with no version, so the README can link to
    # releases/latest/download/PowerPane-x64-setup.exe and that link always fetches the newest
    # release. The versioned copy stays, because the winget workflow looks for it by name.
    $stableSetup = Join-Path $OutDir 'PowerPane-x64-setup.exe'
    Copy-Item $setup $stableSetup
    $packaged += $setup, $stableSetup
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

Download `PowerPane-<version>-x64-setup.exe` and run it. `PowerPane-x64-setup.exe` is the same file under a name that never changes, for links to the newest release. It installs for the current user, with no administrator rights. Over an existing PowerPane, it closes PowerPane, replaces the program, and updates the same Add/Remove Programs entry. While QuickPane is still installed, it stops and asks you to uninstall QuickPane first. Your settings, groups, and pins in `%APPDATA%\PowerPane` stay where they are.

The zip holds the same program, its licence, and `THIRD-PARTY-NOTICES.txt`, for replacing the files by hand instead:

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
