# Maps a release tag, or a version, to the two spellings a release uses. PowerPane is versioned as
# Semantic Versioning from 4.1.0: the tag is v<major>.<minor>.<patch>, and the program's own file
# version, which Windows requires to have four parts, is the same three numbers followed by .0. Tags
# from before the switch had four parts (v4.0.0.1) and still map to themselves, so a winget or release
# retry for an old tag keeps working.
#
# Usage:  pwsh -File scripts/release-version.ps1 -Tag v4.1.0       prints version=4.1.0 and fileVersion=4.1.0.0
#         pwsh -File scripts/release-version.ps1 -Version 4.1.0    the same, from a version without the v
#         pwsh -File scripts/release-version.ps1 -SelfTest         checks the mapping against sample tags
# The release and winget workflows append its output to $GITHUB_OUTPUT, and scripts/test.ps1 runs the
# self-test, so a broken mapping fails locally before it can fail a release.
param([string]$Tag, [string]$Version, [switch]$SelfTest)
$ErrorActionPreference = 'Stop'

function ConvertTo-ReleaseVersion([string]$text, [switch]$IsTag) {
    $pattern = if ($IsTag) { '^v(\d+)\.(\d+)\.(\d+)(?:\.(\d+))?$' } else { '^(\d+)\.(\d+)\.(\d+)(?:\.(\d+))?$' }
    if ($text -notmatch $pattern) {
        $example = if ($IsTag) { 'v4.1.0' } else { '4.1.0' }
        throw "'$text' is not a release $(if ($IsTag) { 'tag' } else { 'version' }) such as $example."
    }
    $three = "$($Matches[1]).$($Matches[2]).$($Matches[3])"
    if ($Matches[4]) {
        # A four-part tag from before Semantic Versioning names its program version exactly.
        $four = "$three.$($Matches[4])"
        return [pscustomobject]@{ Version = $four; FileVersion = $four }
    }
    [pscustomobject]@{ Version = $three; FileVersion = "$three.0" }
}

if ($SelfTest) {
    $cases = @(
        @('v4.1.0', '4.1.0', '4.1.0.0'),
        @('v10.20.30', '10.20.30', '10.20.30.0'),
        @('v4.0.0.1', '4.0.0.1', '4.0.0.1'),
        @('v3.10.0.3', '3.10.0.3', '3.10.0.3')
    )
    $bad = @('4.1.0', 'v4.1', 'v4.1.0-beta', 'v4.1.0.0.0', 'release-4.1.0', 'v4.1.0 ')
    $fails = 0
    foreach ($c in $cases) {
        $r = ConvertTo-ReleaseVersion $c[0] -IsTag
        if ($r.Version -ne $c[1] -or $r.FileVersion -ne $c[2]) { "FAIL $($c[0]) gave $($r.Version) / $($r.FileVersion)"; $fails++ }
    }
    foreach ($b in $bad) {
        $threw = $false
        try { [void](ConvertTo-ReleaseVersion $b -IsTag) } catch { $threw = $true }
        if (-not $threw) { "FAIL '$b' was accepted as a tag"; $fails++ }
    }
    if ((ConvertTo-ReleaseVersion '4.1.0').FileVersion -ne '4.1.0.0') { 'FAIL version 4.1.0 did not map to 4.1.0.0'; $fails++ }
    if ($fails) { throw "release-version.ps1 self-test: $fails failure(s)." }
    'release-version.ps1 self-test passed.'
    return
}

$r = if ($Tag) { ConvertTo-ReleaseVersion $Tag -IsTag } elseif ($Version) { ConvertTo-ReleaseVersion $Version } else { throw 'Pass -Tag, -Version, or -SelfTest.' }
"version=$($r.Version)"
"fileVersion=$($r.FileVersion)"
