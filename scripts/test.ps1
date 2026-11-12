# Builds the app, then runs the unit tests in tests/PowerPane.Tests with `dotnet test`.
# The test project references the built PowerPane.exe, so the app must be built first with the
# same configuration. The tests never start PowerPane and never touch the real settings folder.
# It runs check-rules.ps1 first, so a local run fails on the same rules CI does.
#
# Usage:  pwsh -File scripts/test.ps1 [-Configuration Release|Debug] [-NoBuild]
param([ValidateSet('Release', 'Debug')][string]$Configuration = 'Release', [switch]$NoBuild)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent

& (Join-Path $PSScriptRoot 'check-rules.ps1')
if ($LASTEXITCODE -ne 0) { throw 'Repository rules failed; see the list above.' }

if (-not $NoBuild) { & (Join-Path $PSScriptRoot 'build.ps1') -Configuration $Configuration }

$proj = Join-Path $repo 'tests\PowerPane.Tests\PowerPane.Tests.csproj'
dotnet test $proj -c $Configuration --nologo
if ($LASTEXITCODE -ne 0) { throw "Tests failed with exit code $LASTEXITCODE" }
