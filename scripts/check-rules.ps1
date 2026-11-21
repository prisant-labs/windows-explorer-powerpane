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

# 1. PowerPane.csproj is a legacy project that lists every file, so a .cs file missing from it is
#    silently left out of the build. The build still succeeds when nothing references the file yet.
$csproj = Get-Content (Join-Path $root 'PowerPane/PowerPane.csproj') -Raw
$listed = @{}
foreach ($m in [regex]::Matches($csproj, '<Compile\s+Include="([^"]+)"')) {
    $listed[$m.Groups[1].Value.Replace('\', '/').ToLowerInvariant()] = $true
}
$appDir = Join-Path $root 'PowerPane'
foreach ($f in Get-SourceFiles 'PowerPane' @('*.cs')) {
    $rel = [System.IO.Path]::GetRelativePath($appDir, $f.FullName).Replace('\', '/')
    if (-not $listed.ContainsKey($rel.ToLowerInvariant())) {
        $failures.Add("PowerPane/$rel is not listed in PowerPane/PowerPane.csproj. Add <Compile Include=""$($rel.Replace('/', '\'))"" />.")
    }
}

# 2. WinEventHook shares one native hook per event range. A direct SetWinEventHook call elsewhere
#    adds a second hook that the shared one cannot see or release.
$hookAllowed = @('PowerPane/Interop/WinEventHook.cs', 'PowerPane/Interop/NativeMethods.cs')
foreach ($f in Get-SourceFiles 'PowerPane' @('*.cs')) {
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
#    without a trace, which once emptied every icon on the Settings Pane page. Files must spell
#    them as escapes: \uE713 in C#, &#xE713; in XAML. Every tracked text file is read, not only
#    code: AGENTS.md once taught this rule with the raw glyph as its example, so the example read
#    as "" to any tool that dropped it (#277). Files are chosen by extension, because the bytes of
#    an icon or an image can decode as private-use characters.
$textExtensions = @('.cs', '.xaml', '.md', '.ps1', '.psm1', '.iss', '.csproj', '.props', '.targets', '.config',
                    '.json', '.yml', '.yaml', '.txt', '.sh', '.py', '.xml', '.resx', '.manifest', '.sln', '.editorconfig')
$tracked = @(git -C $root ls-files 2>$null)
if ($LASTEXITCODE -eq 0 -and $tracked.Count -gt 0) {
    $glyphFiles = foreach ($t in $tracked) {
        $full = Join-Path $root $t
        $name = [System.IO.Path]::GetFileName($t)
        $ext = [System.IO.Path]::GetExtension($t).ToLowerInvariant()
        if (($textExtensions -contains $ext -or $textExtensions -contains $name.ToLowerInvariant()) -and (Test-Path -LiteralPath $full -PathType Leaf)) {
            Get-Item -LiteralPath $full
        }
    }
} else {
    # No git (an exported source tree): fall back to the code files, which is what this rule read
    # before it covered documentation.
    $glyphFiles = @(Get-SourceFiles 'PowerPane' @('*.cs', '*.xaml')) + @(Get-SourceFiles 'tests' @('*.cs'))
    $glyphScope = 'code files only, because git ls-files failed'
}
# Printed with the result, so a log shows which scan ran: a silent fallback would still say clean.
$glyphFiles = @($glyphFiles)
if (-not $glyphScope) { $glyphScope = 'tracked text files' }
$glyphScope = "$($glyphFiles.Count) $glyphScope"
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
#    the real %APPDATA%\PowerPane folder.
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

# 5. The "Add to PowerPane" verb is registered by three files in packaging/ that no compiler links: Installer.iss
#    (the installer), Install.ps1 (the manual install), and Uninstall.ps1 (the manual uninstall). A verb
#    added to one and not the others leaves a menu entry that installs but never uninstalls, or the
#    reverse. Each file must name the same set of PowerPanePin class keys, and none may keep the old
#    "Pin to PowerPane" text.
function Get-VerbKeys([string]$file, [string]$pattern) {
    $text = Get-Content (Join-Path $root "packaging\$file") -Raw
    $keys = @()
    foreach ($m in [regex]::Matches($text, $pattern)) { $keys += $m.Groups[1].Value }
    return @($keys | Sort-Object -Unique)
}
$verbSets = [ordered]@{
    'Installer.iss' = Get-VerbKeys 'Installer.iss' 'Software\\Classes\\(.+?)\\shell\\PowerPanePin"'
    'Install.ps1'   = Get-VerbKeys 'Install.ps1' "Register-AddVerb\s+'([^']+)'"
}
$uninstallText = Get-Content (Join-Path $root 'packaging\Uninstall.ps1') -Raw
$uninstallList = [regex]::Match($uninstallText, '\$classKey\s+in\s+@\(([^)]*)\)')
$uninstallKeys = @()
if ($uninstallList.Success) {
    foreach ($m in [regex]::Matches($uninstallList.Groups[1].Value, "'([^']+)'")) { $uninstallKeys += $m.Groups[1].Value }
}
$verbSets['Uninstall.ps1'] = @($uninstallKeys | Sort-Object -Unique)
$reference = ($verbSets['Installer.iss'] -join ', ')
foreach ($name in $verbSets.Keys) {
    $found = ($verbSets[$name] -join ', ')
    if ($verbSets[$name].Count -eq 0) {
        $failures.Add("$name registers no PowerPanePin verb keys, or check-rules cannot read them.")
    } elseif ($found -ne $reference) {
        $failures.Add("$name names the PowerPanePin keys [$found], but Installer.iss names [$reference]. The three files must register and remove the same keys.")
    }
}
foreach ($name in @('Installer.iss', 'Install.ps1', 'Uninstall.ps1')) {
    $n = 0
    foreach ($line in Get-Content (Join-Path $root "packaging\$name")) {
        $n++
        if ($line -match 'Pin to PowerPane') {
            $failures.Add("${name}:$n still says ""Pin to PowerPane"". The verb is ""Add to PowerPane"".")
        }
    }
}

# 6. Install.ps1 and Uninstall.ps1 install and remove PowerPane by hand, and nothing runs them in CI,
#    so they drifted from the setup program unseen: they force-killed the app, which the watchdog
#    answers with a relaunch, killed it before checking a build existed, and installed only the exe.
#    The scripts are read with the PowerShell parser, so a comment that
#    names a forbidden command does not count and a script that no longer parses fails here.
function Read-PowerShellAst([string]$name) {
    $tokens = $null; $errors = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseFile((Join-Path $root "packaging\$name"), [ref]$tokens, [ref]$errors)
    foreach ($e in $errors) { $failures.Add("packaging/${name}:$($e.Extent.StartLineNumber) does not parse: $($e.Message)") }
    return $ast
}

# A step written in the script body, not inside a function definition, whose position says when it runs.
function Get-BodyCommands($ast, [string[]]$names) {
    @($ast.FindAll({
        param($node)
        if ($node -isnot [System.Management.Automation.Language.CommandAst]) { return $false }
        if ($names -notcontains $node.GetCommandName()) { return $false }
        for ($p = $node.Parent; $p; $p = $p.Parent) {
            if ($p -is [System.Management.Automation.Language.FunctionDefinitionAst]) { return $false }
        }
        return $true
    }, $true) | Sort-Object { $_.Extent.StartOffset })
}

function Get-FirstOffset($commands) {
    if ($commands.Count -eq 0) { return [int]::MaxValue }
    return $commands[0].Extent.StartOffset
}

$manualScripts = [ordered]@{ 'Install.ps1' = (Read-PowerShellAst 'Install.ps1'); 'Uninstall.ps1' = (Read-PowerShellAst 'Uninstall.ps1') }
foreach ($name in $manualScripts.Keys) {
    $ast = $manualScripts[$name]
    # A kill reads as a crash to the watchdog and skips the layout restore. Close the app with --exit.
    $kills = $ast.FindAll({
        param($node)
        $node -is [System.Management.Automation.Language.CommandAst] -and
            @('Stop-Process', 'spps', 'kill', 'taskkill', 'taskkill.exe') -contains $node.GetCommandName()
    }, $true)
    foreach ($k in $kills) {
        $failures.Add("packaging/${name}:$($k.Extent.StartLineNumber) stops PowerPane with $($k.GetCommandName()). Close it with --exit, as Installer.iss does.")
    }
    $exitArgs = $ast.FindAll({
        param($node)
        $node -is [System.Management.Automation.Language.StringConstantExpressionAst] -and $node.Value -eq '--exit'
    }, $true)
    if (@($exitArgs).Count -eq 0) {
        $failures.Add("packaging/$name never passes --exit, so it cannot close a running PowerPane cleanly.")
    }
}

# Install.ps1 must find every file before it closes the app, and close the app before it copies.
$installAst = $manualScripts['Install.ps1']
$findAt = Get-FirstOffset (Get-BodyCommands $installAst @('Find-Payload'))
$stopAt = Get-FirstOffset (Get-BodyCommands $installAst @('Stop-PowerPane'))
$copyAt = Get-FirstOffset (Get-BodyCommands $installAst @('Copy-Item'))
if ($findAt -eq [int]::MaxValue -or $stopAt -eq [int]::MaxValue) {
    $failures.Add('packaging/Install.ps1 must call Find-Payload and then Stop-PowerPane, or check-rules cannot read the order.')
} elseif (-not ($findAt -lt $stopAt -and $stopAt -lt $copyAt)) {
    $failures.Add('packaging/Install.ps1 must find every file (Find-Payload), then close the app (Stop-PowerPane), then copy. A missing build must leave the running copy alone.')
}

# Uninstall.ps1 must close the app before it removes anything.
$uninstallAst = $manualScripts['Uninstall.ps1']
$stopAt = Get-FirstOffset (Get-BodyCommands $uninstallAst @('Stop-PowerPane'))
$removeAt = Get-FirstOffset (Get-BodyCommands $uninstallAst @('Remove-Item', 'Remove-ItemProperty'))
if ($stopAt -eq [int]::MaxValue -or $stopAt -gt $removeAt) {
    $failures.Add('packaging/Uninstall.ps1 must close the app (Stop-PowerPane) before it removes anything.')
}
$deleteVerbAt = $uninstallAst.FindAll({
    param($node)
    $node -is [System.Management.Automation.Language.InvokeMemberExpressionAst] -and $node.Member.Value -eq 'DeleteSubKeyTree'
}, $true) | ForEach-Object { $_.Extent.StartOffset } | Sort-Object | Select-Object -First 1
if ($null -ne $deleteVerbAt -and $stopAt -gt $deleteVerbAt) {
    $failures.Add('packaging/Uninstall.ps1 must close the app (Stop-PowerPane) before it removes the verb keys.')
}

# Install.ps1 must install the same files, under the same names, as Installer.iss's [Files] section.
$issFiles = @()
foreach ($line in Get-Content (Join-Path $root 'packaging\Installer.iss')) {
    if ($line -notmatch '^\s*Source:\s*"([^"]+)"') { continue }
    $source = $Matches[1].Replace('{#MyAppExe}', 'PowerPane.exe')
    if ($line -match 'DestName:\s*"([^"]+)"') { $issFiles += $Matches[1] }
    else { $issFiles += [System.IO.Path]::GetFileName($source.Replace('\', '/')) }
}
$issFiles = @($issFiles | Sort-Object -Unique)
$payloadAssign = $installAst.FindAll({
    param($node)
    $node -is [System.Management.Automation.Language.AssignmentStatementAst] -and
        $node.Left -is [System.Management.Automation.Language.VariableExpressionAst] -and
        $node.Left.VariablePath.UserPath -eq 'payload'
}, $true) | Select-Object -First 1
$scriptFiles = @()
if ($payloadAssign) {
    $table = $payloadAssign.Right.Find({ param($node) $node -is [System.Management.Automation.Language.HashtableAst] }, $true)
    if ($table) {
        foreach ($pair in $table.KeyValuePairs) {
            if ($pair.Item1 -is [System.Management.Automation.Language.StringConstantExpressionAst]) { $scriptFiles += $pair.Item1.Value }
        }
    }
}
$scriptFiles = @($scriptFiles | Sort-Object -Unique)
if ($issFiles.Count -eq 0) {
    $failures.Add('packaging/Installer.iss installs no files, or check-rules cannot read its [Files] section.')
} elseif ($scriptFiles.Count -eq 0) {
    $failures.Add('packaging/Install.ps1 has no $payload table, or check-rules cannot read it.')
} elseif (($scriptFiles -join ', ') -ne ($issFiles -join ', ')) {
    $failures.Add("packaging/Install.ps1 installs [$($scriptFiles -join ', ')], but Installer.iss installs [$($issFiles -join ', ')]. The two must install the same files.")
}

# The Start menu shortcut has one name in three places: ShortcutRepair.LinkName, which the app writes
# at every start, Installer.iss's [Icons] entry, and Uninstall.ps1, which removes it. A mismatch leaves
# two shortcuts, or one that outlives the uninstall.
$repairText = Get-Content (Join-Path $root 'PowerPane/Services/ShortcutRepair.cs') -Raw
$linkMatch = [regex]::Match($repairText, 'LinkName\s*=\s*"([^"]+)"')
$issText = Get-Content (Join-Path $root 'packaging\Installer.iss') -Raw
$appNameMatch = [regex]::Match($issText, '#define\s+MyAppName\s+"([^"]+)"')
if (-not $linkMatch.Success -or -not $appNameMatch.Success) {
    $failures.Add('check-rules cannot read ShortcutRepair.LinkName or Installer.iss''s MyAppName.')
} else {
    $linkName = $linkMatch.Groups[1].Value
    if ("$($appNameMatch.Groups[1].Value).lnk" -ne $linkName) {
        $failures.Add("Installer.iss names the Start menu shortcut ""$($appNameMatch.Groups[1].Value).lnk"", but ShortcutRepair.LinkName is ""$linkName"".")
    }
    $linkInUninstall = $uninstallAst.FindAll({
        param($node)
        $node -is [System.Management.Automation.Language.StringConstantExpressionAst] -and $node.Value -eq $linkName
    }, $true)
    if (@($linkInUninstall).Count -eq 0) {
        $failures.Add("packaging/Uninstall.ps1 does not name the Start menu shortcut ""$linkName"" (ShortcutRepair.LinkName), so the uninstall leaves it behind.")
    }
}

if ($failures.Count -gt 0) {
    Write-Host "check-rules: $($failures.Count) violation(s) of the rules in AGENTS.md"
    $failures | ForEach-Object { Write-Host "  $_" }
    exit 1
}
Write-Host "check-rules: clean (glyph scan read $glyphScope)"
# Explicit, so a caller that reads $LASTEXITCODE sees 0. A script that ends without exit leaves it
# unset in a fresh shell, which test.ps1 would read as a failure.
exit 0
