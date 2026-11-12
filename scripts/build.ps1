# Builds PowerPane.exe (x64) with MSBuild. Works with or without the .NET Framework 4.8 targeting
# pack installed: when the pack is missing, MSBuild is pointed at the reference assemblies from the
# Microsoft.NETFramework.ReferenceAssemblies.net48 NuGet package instead, which needs no admin rights.
#
# It builds PowerPane.csproj rather than PowerPane.sln. Visual Studio 2019's MSBuild cannot load the
# SDK-style test project, so the test project is kept out of the solution and built by `dotnet test`
# (see scripts/test.ps1).
#
# Usage:  pwsh -File scripts/build.ps1 [-Configuration Release|Debug]
# Output: PowerPane\bin\x64\<Configuration>\PowerPane.exe
param([ValidateSet('Release', 'Debug')][string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent

# MSBuild: whatever vswhere reports (Visual Studio or Build Tools, 2019 or later).
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$msbuild = $null
if (Test-Path $vswhere) {
    $msbuild = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
}
if (-not $msbuild -or -not (Test-Path $msbuild)) { throw 'MSBuild not found. Install Visual Studio or the Visual Studio Build Tools.' }

$props = @("/p:Configuration=$Configuration", '/p:Platform=x64')

# Reference assemblies: the installed targeting pack if present, otherwise the NuGet package.
$native = Join-Path ${env:ProgramFiles(x86)} 'Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8'
if (-not (Test-Path (Join-Path $native 'mscorlib.dll'))) {
    $cache = Join-Path $env:LOCALAPPDATA 'PowerPaneBuild'
    $extract = Join-Path $cache 'refasm-net48'
    if (-not (Test-Path (Join-Path $extract 'build\.NETFramework\v4.8\mscorlib.dll'))) {
        New-Item -ItemType Directory -Force $cache | Out-Null
        $nupkg = Join-Path $cache 'microsoft.netframework.referenceassemblies.net48.1.0.3.nupkg'
        if (-not (Test-Path $nupkg)) {
            Invoke-WebRequest -Uri 'https://api.nuget.org/v3-flatcontainer/microsoft.netframework.referenceassemblies.net48/1.0.3/microsoft.netframework.referenceassemblies.net48.1.0.3.nupkg' -OutFile $nupkg
        }
        # The package is Microsoft-signed; refuse to use it if the signature does not verify.
        if (Get-Command dotnet -ErrorAction SilentlyContinue) {
            dotnet nuget verify --all $nupkg | Out-Null
            if ($LASTEXITCODE -ne 0) { throw "Signature check failed for $nupkg" }
        }
        Expand-Archive -Path $nupkg -DestinationPath $extract -Force
    }
    $props += '/p:TargetFrameworkRootPath=' + (Join-Path $extract 'build') + '\'
    'Targeting pack not installed; using the reference-assembly package.'
}

$csproj = Join-Path $repo 'PowerPane\PowerPane.csproj'
& $msbuild $csproj /restore @props /m /nologo /v:minimal /clp:Summary
if ($LASTEXITCODE -ne 0) { throw "MSBuild failed with exit code $LASTEXITCODE" }

$exe = Join-Path $repo "PowerPane\bin\x64\$Configuration\PowerPane.exe"
"Built: $exe"
"Version: $((Get-Item $exe).VersionInfo.FileVersion)"
"SHA-256: $((Get-FileHash $exe -Algorithm SHA256).Hash)"
