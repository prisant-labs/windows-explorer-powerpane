# Checks the AGENTS.md hard rules that a build and the tests cannot see, so a pull request that
# breaks one fails CI instead of waiting for a reviewer to notice. Each rule here has been broken
# at least once by an edit that compiled and passed every test.
#   pwsh -File scripts/check-rules.ps1
# Exits 1 and lists every violation when a rule fails.
param()
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$failures = New-Object System.Collections.Generic.List[string]

function Get-SourceFiles([string]$dir, [string[]]$patterns) {
    Get-ChildItem -Path (Join-Path $root $dir) -Recurse -File -Include $patterns |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' }
}

function Get-RelativePath([System.IO.FileInfo]$file) {
    [System.IO.Path]::GetRelativePath($root, $file.FullName).Replace('\', '/')
}

# 1. QuickPane.csproj is a legacy project that lists every file, so a .cs file missing from it is
#    silently left out of the build. The build still succeeds when nothing references the file yet.
$csproj = Get-Content (Join-Path $root 'QuickPane/QuickPane.csproj') -Raw
$listed = @{}
foreach ($m in [regex]::Matches($csproj, '<Compile\s+Include="([^"]+)"')) {
    $listed[$m.Groups[1].Value.Replace('\', '/').ToLowerInvariant()] = $true
}
$appDir = Join-Path $root 'QuickPane'
foreach ($f in Get-SourceFiles 'QuickPane' @('*.cs')) {
    $rel = [System.IO.Path]::GetRelativePath($appDir, $f.FullName).Replace('\', '/')
    if (-not $listed.ContainsKey($rel.ToLowerInvariant())) {
        $failures.Add("QuickPane/$rel is not listed in QuickPane/QuickPane.csproj. Add <Compile Include=""$($rel.Replace('/', '\'))"" />.")
    }
}

# 2. WinEventHook shares one native hook per event range. A direct SetWinEventHook call elsewhere
#    adds a second hook that the shared one cannot see or release.
$hookAllowed = @('QuickPane/Interop/WinEventHook.cs', 'QuickPane/Interop/NativeMethods.cs')
foreach ($f in Get-SourceFiles 'QuickPane' @('*.cs')) {
    $rel = Get-RelativePath $f
    if ($hookAllowed -contains $rel) { continue }
    $n = 0
    foreach ($line in Get-Content $f.FullName) {
        $n++
        $code = ($line -split '//', 2)[0]
        if ($code -match '\bSetWinEventHook\s*\(') {
            $failures.Add("${rel}:$n calls SetWinEventHook directly. Use WinEventHook instead.")
        }
    }
}

# 3. Icon glyphs are private-use characters (U+E000 to U+F8FF). Some editors and tools drop them
#    without a trace, which once emptied every icon on the Settings Pane page. Source files must
#    spell them as escapes:  in C#, &#xE713; in XAML.
$glyphFiles = @(Get-SourceFiles 'QuickPane' @('*.cs', '*.xaml')) + @(Get-SourceFiles 'tests' @('*.cs'))
foreach ($f in $glyphFiles) {
    $rel = Get-RelativePath $f
    $n = 0
    foreach ($line in [System.IO.File]::ReadAllLines($f.FullName)) {
        $n++
        foreach ($ch in $line.ToCharArray()) {
            if ([int]$ch -ge 0xE000 -and [int]$ch -le 0xF8FF) {
                $failures.Add(('{0}:{1} holds the private-use character U+{2:X4}. Write it as an escape.' -f $rel, $n, [int]$ch))
                break
            }
        }
    }
}

# 4. Tests touch only their own temporary directory. SettingsStore's default constructor points at
#    the real %APPDATA%\QuickPane folder.
foreach ($f in Get-SourceFiles 'tests' @('*.cs')) {
    $rel = Get-RelativePath $f
    $n = 0
    foreach ($line in Get-Content $f.FullName) {
        $n++
        $code = ($line -split '//', 2)[0]
        if ($code -match 'new\s+SettingsStore\s*\(\s*\)') {
            $failures.Add("${rel}:$n uses SettingsStore's default constructor, which reads and writes the real settings folder. Pass a temporary directory.")
        }
    }
}

if ($failures.Count -gt 0) {
    Write-Host "check-rules: $($failures.Count) violation(s) of the rules in AGENTS.md"
    $failures | ForEach-Object { Write-Host "  $_" }
    exit 1
}
Write-Host 'check-rules: clean'
# Explicit, so a caller that reads $LASTEXITCODE sees 0. A script that ends without exit leaves it
# unset in a fresh shell, which test.ps1 would read as a failure.
exit 0
