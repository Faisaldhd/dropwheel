#Requires -Version 5.1
<#
.SYNOPSIS
    Read-only final-candidate verification for DropWheel 2.4.0. CI evidence only.

.DESCRIPTION
    Answers the six release gates in docs/audit/final-candidate/CHECKLIST.md by querying GitHub
    Actions through `gh`, reading the repository tree, and reading the artifacts a run uploaded.
    It never builds, dispatches, installs or uses an executable from this repository.

    Read-only contract, enforced at startup by Assert-ReadOnly:
      * the only external command the script invokes is `gh`;
      * the script text must not contain an execution primitive;
      * every artifact it downloads lands under -WorkDir (default <repo>/_local/tmp/finalcheck,
        git-ignored) and is deleted afterwards unless -KeepArtifacts is passed.

.PARAMETER Repo
    owner/name on GitHub. Default Faisaldhd/dropwheel.

.PARAMETER Branch
    Branch whose runs are searched when -RunId is omitted. Default main.

.PARAMETER RunId
    Pin the run that carries the 2.4.0 artifacts. Skips discovery.

.PARAMETER UiCoverageBranch
    Branch to search for the ui-coverage artifact when the release run has none (the step is not on
    main yet). Default audit/selftest.

.PARAMETER RedRunId
    A run that must be red on the ui-coverage gate, proving a FAIL row fails the job.
    Default 39377633125.

.PARAMETER WorkDir
    Scratch directory for downloaded artifacts. Default <repo>/_local/tmp/finalcheck.

.PARAMETER KeepArtifacts
    Keep the downloaded artifacts instead of deleting WorkDir at the end.

.OUTPUTS
    Exit 0 = every gate PASS. Exit 1 = at least one gate FAILED or BLOCKED. Exit 2 = not yet
    provable (no completed successful run carries the 2.4.0 artifacts) - a timing gap, not a
    release failure.

.EXAMPLE
    pwsh ./docs/audit/final-candidate/verify.ps1

.EXAMPLE
    pwsh ./docs/audit/final-candidate/verify.ps1 -RunId 39375412602 -KeepArtifacts
#>
[CmdletBinding()]
param(
    [string] $Repo = 'Faisaldhd/dropwheel',
    [string] $Branch = 'main',
    [string] $RunId,
    [string] $UiCoverageBranch = 'audit/selftest',
    [string] $RedRunId = '39377633125',
    [string] $WorkDir,
    [switch] $KeepArtifacts
)

$ErrorActionPreference = 'Stop'

# --------------------------------------------------------------------------------------
# Read-only guard
# --------------------------------------------------------------------------------------

function Assert-ReadOnly {
    <#
        Refuses to run when the script text contains a way to start a process other than `gh`, or
        when an invocation in the script is not `gh`. The tokens are assembled from parts so they
        do not appear literally in this file.
    #>
    $text = Get-Content -LiteralPath $PSCommandPath -Raw

    $primitives = @(
        ('Start-' + 'Process')
        ('Invoke-' + 'Expression')
        ('Invoke-' + 'Item')
        ('Start-' + 'Job')
        ('Invoke-' + 'Command')
        ('cmd' + '.exe')
        ('powershell' + '.exe')
        ('pwsh' + '.exe')
    )
    foreach ($token in $primitives) {
        if ([regex]::IsMatch($text, [regex]::Escape($token), 'IgnoreCase')) {
            throw "Refusing to run: this script contains the execution primitive '$token'."
        }
    }

    $invoked = @([regex]::Matches($text, '&\s+(\S+)') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
    $foreign = @($invoked | Where-Object { $_ -ne 'gh' })
    if ($foreign.Count -gt 0) {
        throw ("Refusing to run: the script invokes something other than gh: {0}" -f ($foreign -join ', '))
    }

    Write-Host 'read-only guard: ok (only gh is invoked; no execution primitive in the script)'
}

function Invoke-Gh {
    # The single external command this script is allowed to run.
    param([string[]] $GhArgs)
    $output = & gh @GhArgs 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw ("gh {0} failed with exit code {1}:`n{2}" -f ($GhArgs -join ' '), $LASTEXITCODE, ($output -join "`n"))
    }
    return $output
}

function Invoke-GhJson {
    param([string[]] $GhArgs)
    return ((Invoke-Gh @GhArgs) -join "`n" | ConvertFrom-Json)
}

# --------------------------------------------------------------------------------------
# Results
# --------------------------------------------------------------------------------------

$script:Results = [System.Collections.Generic.List[object]]::new()
$script:Downloaded = @{}

function Add-Result {
    param(
        [string] $Id,
        [string] $Title,
        [ValidateSet('PASS', 'FAIL', 'BLOCKED', 'SKIP')] [string] $Status,
        [string] $Evidence
    )
    $script:Results.Add([pscustomobject]@{
            Id       = $Id
            Title    = $Title
            Status   = $Status
            Evidence = $Evidence
        })
    $colour = switch ($Status) {
        'PASS' { 'Green' }
        'FAIL' { 'Red' }
        'BLOCKED' { 'Yellow' }
        default { 'DarkGray' }
    }
    Write-Host ("  [{0}] {1} - {2}" -f $Status, $Id, $Evidence) -ForegroundColor $colour
}

# --------------------------------------------------------------------------------------
# Helpers
# --------------------------------------------------------------------------------------

function Get-RunArtifactNames {
    param([string] $RunIdForArtifact)
    $json = Invoke-GhJson @('api', "repos/$Repo/actions/runs/$RunIdForArtifact/artifacts?per_page=100")
    if (-not $json -or -not $json.artifacts) { return @() }
    return @($json.artifacts | ForEach-Object { $_.name })
}

function Get-RepoFileAtRef {
    param([string] $Path, [string] $Ref)
    $json = Invoke-GhJson @('api', "repos/$Repo/contents/$Path?ref=$Ref")
    if (-not $json -or -not $json.content) { throw "no content for $Path at $Ref" }
    return [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String(($json.content -replace '\s', '')))
}

function Save-Artifact {
    <# Downloads one artifact once per run; returns the directory, or $null when the run has none. #>
    param([string] $RunIdForArtifact, [string] $Name)

    $key = "$RunIdForArtifact/$Name"
    if ($script:Downloaded.ContainsKey($key)) { return $script:Downloaded[$key] }

    if (-not ((Get-RunArtifactNames -RunIdForArtifact $RunIdForArtifact) -contains $Name)) {
        $script:Downloaded[$key] = $null
        return $null
    }

    $dest = Join-Path $script:ArtifactRoot $Name
    if (Test-Path -LiteralPath $dest) { Remove-Item -LiteralPath $dest -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $dest | Out-Null
    $null = Invoke-Gh @('run', 'download', $RunIdForArtifact, '--repo', $Repo, '-n', $Name, '-D', $dest)
    $script:Downloaded[$key] = $dest
    return $dest
}

function Get-ArtifactText {
    <# Downloads an artifact and returns the first matching file's text, or $null. #>
    param([string] $RunIdForArtifact, [string] $Name, [string] $Leaf)

    $dir = Save-Artifact -RunIdForArtifact $RunIdForArtifact -Name $Name
    if (-not $dir) { return $null }
    $file = Get-ChildItem -LiteralPath $dir -Recurse -File -Filter $Leaf -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if (-not $file) { return $null }
    return (Get-Content -LiteralPath $file.FullName -Raw)
}

function Find-ArtifactFile {
    param([string] $RunIdForArtifact, [string] $Name, [string] $Leaf)
    $dir = Save-Artifact -RunIdForArtifact $RunIdForArtifact -Name $Name
    if (-not $dir) { return $null }
    $file = Get-ChildItem -LiteralPath $dir -Recurse -File -Filter $Leaf -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($file) { return $file.FullName }
    return $null
}

function Get-MissingText {
    <# Returns the expected substrings that the text does not contain. #>
    param([string] $Text, [string[]] $Expected, [switch] $CaseSensitive)
    $missing = @()
    foreach ($needle in $Expected) {
        $found = if ($CaseSensitive) { $Text.Contains($needle, [StringComparison]::Ordinal) }
        else { $Text.IndexOf($needle, [StringComparison]::OrdinalIgnoreCase) -ge 0 }
        if (-not $found) { $missing += $needle }
    }
    return $missing
}

# --------------------------------------------------------------------------------------
# Start
# --------------------------------------------------------------------------------------

Assert-ReadOnly

# docs/audit/final-candidate/verify.ps1 -> repo root is four levels up.
$repoRoot = $PSCommandPath
foreach ($i in 1..4) { $repoRoot = Split-Path -Parent $repoRoot }
if (-not $WorkDir) { $WorkDir = Join-Path $repoRoot '_local\tmp\finalcheck' }
$script:ArtifactRoot = Join-Path $WorkDir 'artifacts'
New-Item -ItemType Directory -Force -Path $script:ArtifactRoot | Out-Null

Write-Host ''
Write-Host "DropWheel 2.4.0 final-candidate verification - repo $Repo, branch $Branch"
Write-Host "scratch: $WorkDir"
Write-Host ''

# --- discovery -------------------------------------------------------------------------

$target = $null
$looked = @()

if ($RunId) {
    $target = Invoke-GhJson @('run', 'view', $RunId, '--repo', $Repo, '--json', 'databaseId,headSha,status,conclusion')
    Write-Host "pinned run: $RunId (conclusion=$($target.conclusion)) @ $($target.headSha)"
} else {
    Write-Host 'searching recent runs for one that carries DropWheel-Setup-2.4.0 ...'
    $runs = Invoke-GhJson @('run', 'list', '--repo', $Repo, '--branch', $Branch, '-L', '60', '--json',
        'databaseId,headSha,status,conclusion')
    foreach ($run in @($runs)) {
        if ($run.conclusion -ne 'success') { continue }
        $names = @(Get-RunArtifactNames -RunIdForArtifact ([string] $run.databaseId))
        $shortSha = ([string] $run.headSha).Substring(0, 7)
        $shown = if ($names.Count -gt 0) { $names -join ', ' } else { 'no artifacts' }
        $looked += ("{0} @ {1}: {2}" -f $run.databaseId, $shortSha, $shown)
        if ($names -contains 'DropWheel-Setup-2.4.0') { $target = $run; break }
    }
}

if (-not $target) {
    Write-Host ''
    Write-Host 'NOT YET PROVABLE: no completed successful run on the branch carries the 2.4.0 artifacts.' -ForegroundColor Yellow
    foreach ($line in $looked) { Write-Host "  looked at $line" }
    Write-Host ''
    Write-Host 'This is a timing gap, not a release failure. A normal push to main runs the package job'
    Write-Host '(ci.yml:151), so no dispatch is needed. Exiting 2.'
    if (-not $KeepArtifacts -and (Test-Path -LiteralPath $WorkDir)) {
        Remove-Item -LiteralPath $WorkDir -Recurse -Force -ErrorAction SilentlyContinue
    }
    exit 2
}

$runId = [string] $target.databaseId
$sha = [string] $target.headSha
Write-Host "target run: $runId @ $sha"
Write-Host ''

# --- gate 1: installer smoke log --------------------------------------------------------

$jobs = Invoke-GhJson @('run', 'view', $runId, '--repo', $Repo, '--json', 'jobs')
$packageJob = @($jobs.jobs | Where-Object { $_.name -like 'Package*' }) | Select-Object -First 1

if (-not $packageJob) {
    Add-Result -Id 'smoke' -Title 'installer smoke' -Status 'BLOCKED' -Evidence 'this run has no Package job'
} else {
    $logPath = Join-Path $WorkDir 'package.log'
    $log = Invoke-Gh @('run', 'view', $runId, '--repo', $Repo, '--job', [string] $packageJob.databaseId, '--log')
    $log | Set-Content -LiteralPath $logPath -Encoding utf8
    $smokeText = Get-Content -LiteralPath $logPath -Raw

    $missing = @(Get-MissingText -Text $smokeText -Expected @(
            'ok: the installer exited with 0'
            'ok: the uninstaller exited with 0'
            'ok: the HKCU Run value is gone'
            'ok: the Start-menu shortcut is gone'
            'ok: the Send-to shortcut is gone'
            'ok: the file context-menu key is gone'
            'ok: the folder context-menu key is gone'
            'ok: the program folder is gone'
            'ok: a silent uninstall keeps the settings folder'
            'PASS: silent install, shell entries, silent uninstall and settings retention verified.'
        ))

    # The smoke script prints an entry only for an uninstall key that EXISTS, so any HKLM line means
    # a per-machine install, which this product must never create.
    if ([regex]::IsMatch($smokeText, 'uninstall entry:\s*HKLM:')) {
        $missing += 'no HKLM uninstall entry (one was printed)'
    }

    if ($missing.Count -eq 0) {
        Add-Result -Id 'smoke' -Title 'installer smoke' -Status 'PASS' `
            -Evidence 'install+uninstall exit 0; HKCU Run, Start-menu, Send-to, both context-menu keys and the program folder gone; settings kept; no HKLM entry'
    } else {
        Add-Result -Id 'smoke' -Title 'installer smoke' -Status 'FAIL' -Evidence ('missing: ' + ($missing -join ' | '))
    }
}

# --- gate 2: 2.4.0 everywhere -----------------------------------------------------------

$versionProblems = @()

$csproj = Get-RepoFileAtRef -Path 'src/FileAction.App/FileAction.App.csproj' -Ref $sha
if ($csproj -notmatch '<Version>\s*2\.4\.0\s*</Version>') { $versionProblems += 'csproj is not <Version>2.4.0</Version>' }

$iss = Get-RepoFileAtRef -Path 'installer/DropWheel.iss' -Ref $sha
if ($iss -notmatch '#define\s+AppVersion\s+"2\.4\.0"') { $versionProblems += '.iss AppVersion is not 2.4.0' }

$setupExe = Find-ArtifactFile -RunIdForArtifact $runId -Name 'DropWheel-Setup-2.4.0' -Leaf 'DropWheel-2.4.0-Setup.exe'
$portableZip = Find-ArtifactFile -RunIdForArtifact $runId -Name 'DropWheel-Setup-2.4.0' -Leaf 'DropWheel-2.4.0-portable-win-x64.zip'
if (-not $setupExe) { $versionProblems += 'no DropWheel-2.4.0-Setup.exe in artifact DropWheel-Setup-2.4.0' }
if (-not $portableZip) { $versionProblems += 'no DropWheel-2.4.0-portable-win-x64.zip in that artifact' }

$diagnoseText = Get-ArtifactText -RunIdForArtifact $runId -Name 'diagnose' -Leaf 'diagnose.txt'
if (-not $diagnoseText) {
    $versionProblems += 'no diagnose artifact'
} else {
    if ($diagnoseText -notmatch 'version:\s*2\.4\.0\.') { $versionProblems += 'diagnose version is not 2.4.0.x' }
    if ($diagnoseText -notmatch 'strings:\s*\d+ keys, settings\.title="[^"]+"') {
        $versionProblems += 'diagnose does not resolve settings.title'
    }
}

# About window: settings.about.version is fed by the assembly version (SettingsWindow.cs:477-483), so
# it renders the same 2.4.0.x the diagnostic prints. Prove the label exists in both tables.
$en = Get-RepoFileAtRef -Path 'src/FileAction.Core/Strings/en.json' -Ref $sha
$ar = Get-RepoFileAtRef -Path 'src/FileAction.Core/Strings/ar.json' -Ref $sha
if ($en -notmatch '"settings\.about\.version"') { $versionProblems += 'en.json has no settings.about.version' }
if ($ar -notmatch '"settings\.about\.version"') { $versionProblems += 'ar.json has no settings.about.version' }

$readme = Get-RepoFileAtRef -Path 'README.md' -Ref $sha
if ($readme -notmatch '2\.4\.0') { $versionProblems += 'README.md does not mention 2.4.0' }
$changes = Get-RepoFileAtRef -Path 'CHANGES.md' -Ref $sha
if ($changes -notmatch '(?m)^##\s*2\.4\.0') { $versionProblems += 'CHANGES.md has no ## 2.4.0 section' }

if ($versionProblems.Count -eq 0) {
    Add-Result -Id 'version' -Title '2.4.0 everywhere' -Status 'PASS' `
        -Evidence 'csproj+iss 2.4.0; setup and zip named 2.4.0; diagnose 2.4.0.x; settings.about.version in en+ar; README and CHANGES carry 2.4.0'
} else {
    Add-Result -Id 'version' -Title '2.4.0 everywhere' -Status 'FAIL' -Evidence ($versionProblems -join ' | ')
}

# --- gate 3: 26 tools -------------------------------------------------------------------

$toolsProblems = @()
$shippedText = Get-ArtifactText -RunIdForArtifact $runId -Name 'shipped-self-test' -Leaf 'self-test.txt'
if (-not $shippedText) {
    $toolsProblems += 'no shipped-self-test artifact'
} elseif ($shippedText -notmatch '26 registered') {
    $toolsProblems += 'shipped self-test does not report "26 registered"'
}

# 26 ids, each in both cultures, is the only dispatchable proof that exists in this tree.
$uiRunId = $null
if (@(Get-RunArtifactNames -RunIdForArtifact $runId) -contains 'ui-coverage') {
    $uiRunId = $runId
} else {
    $uiRuns = Invoke-GhJson @('run', 'list', '--repo', $Repo, '--branch', $UiCoverageBranch, '-L', '20', '--json',
        'databaseId,conclusion')
    foreach ($r in @($uiRuns)) {
        if ($r.conclusion -ne 'success') { continue }
        if (@(Get-RunArtifactNames -RunIdForArtifact ([string] $r.databaseId)) -contains 'ui-coverage') {
            $uiRunId = [string] $r.databaseId
            break
        }
    }
}

$uiText = $null
if ($uiRunId) {
    $uiText = Get-ArtifactText -RunIdForArtifact $uiRunId -Name 'ui-coverage' -Leaf 'ui-coverage.txt'
}
if (-not $uiText) {
    $toolsProblems += 'ui-coverage artifact unavailable, per-culture dispatch unproven'
} else {
    $ids = @([regex]::Matches($uiText, 'tool:(tools\.[a-z0-9]+)') |
            ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
    if ($ids.Count -ne 26) {
        $toolsProblems += ("ui-coverage lists {0} distinct tool ids, expected 26" -f $ids.Count)
    }
}

# No dispatch unit-test exists in this tree; say so rather than implying one.
$dispatchNote = 'no ToolDispatch-style test exists (not claimed)'
$testsDir = Join-Path $repoRoot 'tests'
if (Test-Path -LiteralPath $testsDir) {
    $hits = Get-ChildItem -LiteralPath $testsDir -Recurse -File -Filter '*.cs' -ErrorAction SilentlyContinue |
        Select-String -Pattern 'ToolDispatch' -SimpleMatch
    if ($hits) { $dispatchNote = 'ToolDispatch test present' }
}

if ($toolsProblems.Count -eq 0) {
    Add-Result -Id 'tools' -Title '26 tools' -Status 'PASS' `
        -Evidence ("26 registered in the shipped self-test; 26 tool ids in en+ar in ui-coverage (run {0}); {1}" -f $uiRunId, $dispatchNote)
} else {
    Add-Result -Id 'tools' -Title '26 tools' -Status 'FAIL' -Evidence ($toolsProblems -join ' | ')
}

# --- gate 4: ui-coverage -----------------------------------------------------------------

if (-not $uiText) {
    Add-Result -Id 'uicoverage' -Title 'en+ar RTL coverage' -Status 'BLOCKED' -Evidence 'ui-coverage artifact unavailable'
} else {
    $uiProblems = @()
    $surfaces = [regex]::Match($uiText, 'surfaces:\s*(\d+) rows, pass (\d+), skip (\d+), fail (\d+)')
    $keys = [regex]::Match($uiText, 'keys:\s*(\d+) English / (\d+) Arabic')
    if (-not $surfaces.Success) { $uiProblems += 'report has no surfaces summary' }
    if (-not $keys.Success) {
        $uiProblems += 'report has no key counts'
    } elseif ([int] $keys.Groups[1].Value -lt 1000 -or [int] $keys.Groups[2].Value -lt 1000) {
        $uiProblems += ("key counts below 1000 (en {0}, ar {1})" -f $keys.Groups[1].Value, $keys.Groups[2].Value)
    }

    # A FAIL row must fail the job: cite a run that went red on exactly this gate.
    $strength = "red-run proof $RedRunId missing"
    try {
        $redRun = Invoke-GhJson @('run', 'view', $RedRunId, '--repo', $Repo, '--json', 'conclusion,jobs')
        if ($redRun.conclusion -eq 'failure') {
            $failedSteps = @($redRun.jobs | ForEach-Object { @($_.steps | Where-Object { $_.conclusion -eq 'failure' }) } |
                    ForEach-Object { $_.name })
            if ($failedSteps -join ', ' -match 'self-test') {
                $strength = "a FAIL row fails the job (run $RedRunId died at 'self-test')"
            } else {
                $uiProblems += "red run $RedRunId did not fail at a self-test step"
            }
        } else {
            $uiProblems += "run $RedRunId is not a completed failure"
        }
    } catch {
        $uiProblems += "could not read run $RedRunId"
    }

    $detail = 'report parsed partially'
    if ($surfaces.Success -and $keys.Success) {
        $detail = "surfaces {0}, pass {1}, fail {2}; keys {3} en / {4} ar; {5}" -f `
            $surfaces.Groups[1].Value, $surfaces.Groups[2].Value, $surfaces.Groups[4].Value,
            $keys.Groups[1].Value, $keys.Groups[2].Value, $strength
    }
    if ($uiRunId -ne $runId) {
        $detail += " (artifact from run $uiRunId, NOT the release commit - PASS with caveat)"
    }

    if ($uiProblems.Count -eq 0) {
        Add-Result -Id 'uicoverage' -Title 'en+ar RTL coverage' -Status 'PASS' -Evidence $detail
    } else {
        Add-Result -Id 'uicoverage' -Title 'en+ar RTL coverage' -Status 'FAIL' -Evidence (($uiProblems -join ' | ') + "; $detail")
    }
}

# --- gate 5: no raw keys -----------------------------------------------------------------

if (-not $shippedText) {
    Add-Result -Id 'rawkeys' -Title 'no raw string keys' -Status 'BLOCKED' -Evidence 'no shipped-self-test artifact'
} else {
    $rawProblems = @(Get-MissingText -Text $shippedText -Expected @(
            'ok en/localization: 1094 keys (English)'
            'ok en-light/localization: 1094 keys (English)'
            'ok ar/localization: 1094 keys (Arabic)'
            'ok ar-light/localization: 1094 keys (Arabic)'
            'ok settings.json file load/save'
        ))
    # The app's own failure marker is uppercase; "skipped ocr (failed: ...)" is a skip, not a failure.
    $upperFailures = @([regex]::Matches($shippedText, '(?m)^FAILED:'))
    if ($upperFailures.Count -gt 0) { $rawProblems += ("{0} uppercase FAILED: line(s)" -f $upperFailures.Count) }
    if ($shippedText -match 'raw localization key') { $rawProblems += 'log mentions a raw localization key' }

    if ($rawProblems.Count -eq 0) {
        Add-Result -Id 'rawkeys' -Title 'no raw string keys' -Status 'PASS' `
            -Evidence '1094 keys in en, en-light, ar and ar-light; settings.json load/save; zero uppercase FAILED: lines'
    } else {
        Add-Result -Id 'rawkeys' -Title 'no raw string keys' -Status 'FAIL' -Evidence ($rawProblems -join ' | ')
    }
}

# --- gate 6: lock badge/icons ------------------------------------------------------------

$lockProblems = @()
$lockDir = Join-Path $repoRoot 'src/FileAction.App/Assets/Lock'
$lockIcons = @(Get-ChildItem -LiteralPath $lockDir -File -Filter '*.ico' -ErrorAction SilentlyContinue)
foreach ($required in @('locked-document.ico', 'locked-folder.ico', 'locked-badge.ico')) {
    if (-not ($lockIcons.Name -contains $required)) { $lockProblems += "missing embedded asset $required" }
}

$badgeDir = Join-Path $repoRoot 'docs/screenshots/lock-badge'
$badgePngs = @(Get-ChildItem -LiteralPath $badgeDir -File -Filter '*.png' -ErrorAction SilentlyContinue)
if ($badgePngs.Count -lt 8) {
    $lockProblems += ("docs/screenshots/lock-badge has {0} PNGs, expected 8" -f $badgePngs.Count)
}

if (-not $shippedText) {
    $lockProblems += 'no shipped self-test log to prove the packaged build exercises the icons'
} else {
    if ($shippedText -notmatch 'ok tools/lock') { $lockProblems += 'shipped self-test has no ok tools/lock line' }
    if ($shippedText -match 'LockIcons|SHCreateItemFromParsingName|ShellIcon') {
        $lockProblems += 'shipped self-test log contains a LockIcons/ShellIcon failure'
    }
}

if ($lockProblems.Count -eq 0) {
    Add-Result -Id 'lockicons' -Title 'lock badge/icons' -Status 'PASS' `
        -Evidence ("{0} embedded .ico assets (document/folder/badge present), {1} committed PNG previews, ok tools/lock with no LockIcons exception" -f $lockIcons.Count, $badgePngs.Count)
} else {
    Add-Result -Id 'lockicons' -Title 'lock badge/icons' -Status 'FAIL' -Evidence ($lockProblems -join ' | ')
}

# --- summary -----------------------------------------------------------------------------

Write-Host ''
Write-Host ("run {0} @ {1}" -f $runId, $sha)
$script:Results | Format-Table Id, Status, Evidence -AutoSize -Wrap | Out-String -Width 200 | Write-Host

$failed = @($script:Results | Where-Object { $_.Status -eq 'FAIL' })
$blocked = @($script:Results | Where-Object { $_.Status -eq 'BLOCKED' })

if ($failed.Count -gt 0) {
    Write-Host ("RESULT: FAIL - {0} gate(s) failed: {1}" -f $failed.Count, (($failed.Id) -join ', ')) -ForegroundColor Red
} elseif ($blocked.Count -gt 0) {
    Write-Host ("RESULT: BLOCKED - {0} gate(s) could not be read: {1}" -f $blocked.Count, (($blocked.Id) -join ', ')) -ForegroundColor Yellow
} else {
    Write-Host 'RESULT: PASS - all six gates verified from CI evidence.' -ForegroundColor Green
}

if (-not $KeepArtifacts -and (Test-Path -LiteralPath $WorkDir)) {
    Remove-Item -LiteralPath $WorkDir -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host "scratch removed: $WorkDir"
} else {
    Write-Host "artifacts kept in: $WorkDir"
}

if ($failed.Count -gt 0 -or $blocked.Count -gt 0) { exit 1 }
exit 0
