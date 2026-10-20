<#
.SYNOPSIS
    Imports a downloaded DropWheel self-test artifact into the website's capture assets.

.DESCRIPTION
    Teams A/B publish the app's own offscreen captures as CI artifacts. This script takes the
    *downloaded and extracted* artifact directory, validates the files, and copies them into
    docs/site/assets with the deterministic website names described in docs/website/CAPTURES.md.

    It never converts formats, never deletes anything, and is safe to re-run: identical files are
    reported as unchanged, differing files are replaced, new files are added. It writes
    docs/site/assets/tools/manifest.json describing which files are present per tool id.

    Expected artifact layout (any depth; a `motion` directory is discovered recursively):

        motion/wheel-dark.webp                 motion/wheel-light.webp
        motion/wheel-dark-poster.png|webp      motion/wheel-light-poster.png|webp
        motion/tool-<id>-dark.webp             motion/tool-<id>-light.webp
        motion/tool-<id>-dark-poster.png|webp   motion/tool-<id>-light-poster.png|webp
        self-test.txt                          (optional, used for diagnostics)

.PARAMETER ArtifactDir
    Directory that a `gh run download` produced (or the unzipped artifact folder).

.PARAMETER AssetsRoot
    Destination assets root. Defaults to <repo>/docs/site/assets. Useful for a dry run into a
    scratch copy; the real website run uses the default.

.EXAMPLE
    pwsh -File scripts/site/import-captures.ps1 -ArtifactDir C:\work\captures

.EXAMPLE
    pwsh -File scripts/site/import-captures.ps1 -ArtifactDir _local\tmp\site\fake-artifact `
        -AssetsRoot _local\tmp\site\proof-assets
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $ArtifactDir,

    [string] $AssetsRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $AssetsRoot) {
    # Normal use is `pwsh -File scripts/site/import-captures.ps1`; the fallback keeps the script
    # usable when its text is invoked in memory (tests), where $PSScriptRoot is empty.
    $scriptDirectory = if ($PSScriptRoot) { $PSScriptRoot } else { (Get-Location).ProviderPath }
    $repoRoot = Split-Path -Parent (Split-Path -Parent $scriptDirectory)
    $AssetsRoot = Join-Path $repoRoot 'docs/site/assets'
}

function Resolve-FullPath([string] $Path) {
    $item = Get-Item -LiteralPath $Path -ErrorAction Stop
    return $item.FullName
}

function Get-Hash([string] $Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
}

function Test-WebP([string] $Path) {
    $bytes = [System.IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -lt 12) { return $false }
    $riff = [System.Text.Encoding]::ASCII.GetString($bytes, 0, 4)
    $webp = [System.Text.Encoding]::ASCII.GetString($bytes, 8, 4)
    return ($riff -eq 'RIFF' -and $webp -eq 'WEBP')
}

function Test-Png([string] $Path) {
    $bytes = [System.IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -lt 8) { return $false }
    return ($bytes[0] -eq 0x89 -and $bytes[1] -eq 0x50 -and $bytes[2] -eq 0x4E -and $bytes[3] -eq 0x47)
}

# ---------------------------------------------------------------- discovery

$artifactRoot = Resolve-FullPath $ArtifactDir
if (-not (Test-Path -LiteralPath $artifactRoot -PathType Container)) {
    throw "artifact path is not a directory: $artifactRoot"
}

$motionDirs = New-Object System.Collections.Generic.List[string]
$direct = Join-Path $artifactRoot 'motion'
if (Test-Path -LiteralPath $direct -PathType Container) { $motionDirs.Add((Resolve-FullPath $direct)) }
foreach ($dir in Get-ChildItem -LiteralPath $artifactRoot -Recurse -Directory -Filter 'motion' -ErrorAction SilentlyContinue) {
    $full = $dir.FullName
    if (-not $motionDirs.Contains($full)) { $motionDirs.Add($full) }
}
# A flat artifact whose root *is* the motion folder.
if ($motionDirs.Count -eq 0 -and (Get-ChildItem -LiteralPath $artifactRoot -Filter 'wheel-*.webp' -File -ErrorAction SilentlyContinue)) {
    $motionDirs.Add($artifactRoot)
}
if ($motionDirs.Count -eq 0) {
    throw "no motion/ directory (or wheel-*.webp files) under $artifactRoot - is this a self-test artifact?"
}

$sources = @{}
foreach ($dir in $motionDirs) {
    foreach ($file in Get-ChildItem -LiteralPath $dir -File -ErrorAction SilentlyContinue) {
        if ($sources.ContainsKey($file.Name)) {
            $existing = $sources[$file.Name]
            if ((Get-Hash $existing) -ne (Get-Hash $file.FullName)) {
                throw "two different files named '$($file.Name)' in the artifact: '$existing' and '$($file.FullName)'"
            }
            continue
        }
        $sources[$file.Name] = $file.FullName
    }
}

# ---------------------------------------------------------------- validation

$wheelClip = '^wheel-(?<theme>dark|light)\.webp$'
$wheelPoster = '^wheel-(?<theme>dark|light)-poster\.(?<ext>png|webp)$'
$toolClip = '^tool-(?<id>.+?)-(?<theme>dark|light)\.webp$'
$toolPoster = '^tool-(?<id>.+?)-(?<theme>dark|light)-poster\.(?<ext>png|webp)$'

$plan = New-Object System.Collections.Generic.List[object]
$pngOnlyPosters = New-Object System.Collections.Generic.List[string]
$invalid = New-Object System.Collections.Generic.List[string]

function Add-Plan([string] $kind, [string] $theme, [string] $source, [string] $destination, [string] $format) {
    $plan.Add([pscustomobject]@{
            Kind        = $kind
            Theme       = $theme
            SourceName  = (Split-Path -Leaf $source)
            SourcePath  = $source
            Destination = $destination
            Format      = $format
        })
}

foreach ($name in ($sources.Keys | Sort-Object)) {
    $path = $sources[$name]
    $length = (Get-Item -LiteralPath $path).Length
    if ($length -eq 0) { $invalid.Add("$name (empty file)"); continue }

    if ($name -match $wheelClip) {
        if (-not (Test-WebP $path)) { $invalid.Add("$name (not a WebP file)"); continue }
        Add-Plan 'wheel-clip' $Matches.theme $path "motion/$name" 'webp'
    }
    elseif ($name -match $wheelPoster) {
        $ext = $Matches.ext
        $ok = if ($ext -eq 'webp') { Test-WebP $path } else { Test-Png $path }
        if (-not $ok) { $invalid.Add("$name (not a valid $ext file)"); continue }
        if ($ext -eq 'png') { $pngOnlyPosters.Add("wheel-$($Matches.theme)") }
        Add-Plan 'wheel-poster' $Matches.theme $path "motion/wheel-$($Matches.theme)-poster.$ext" $ext
    }
    elseif ($name -match $toolPoster) {
        $id = $Matches.id
        $theme = $Matches.theme
        $ext = $Matches.ext
        $ok = if ($ext -eq 'webp') { Test-WebP $path } else { Test-Png $path }
        if (-not $ok) { $invalid.Add("$name (not a valid $ext file)"); continue }
        if ($ext -eq 'png') { $pngOnlyPosters.Add("tool-$id-$theme") }
        Add-Plan 'tool-poster' $theme $path "tools/$id-$theme-poster.$ext" $ext
    }
    elseif ($name -match $toolClip) {
        if (-not (Test-WebP $path)) { $invalid.Add("$name (not a WebP file)"); continue }
        $id = $Matches.id
        Add-Plan 'tool-clip' $Matches.theme $path "tools/$id-$($Matches.theme)-clip.webp" 'webp'
    }
}

if ($invalid.Count -gt 0) {
    throw ("the artifact contains invalid capture files:`n  " + ($invalid -join "`n  "))
}
if ($plan.Count -eq 0) {
    throw "no wheel or tool capture files matched the naming contract under $artifactRoot"
}
$wheelCount = @($plan | Where-Object Kind -like 'wheel-*').Count
if ($wheelCount -eq 0) { throw "the artifact has no wheel-<theme>.webp clip; refusing a partial import" }

# ---------------------------------------------------------------- copy

$added = 0; $replaced = 0; $unchanged = 0
$rows = New-Object System.Collections.Generic.List[object]

foreach ($entry in ($plan | Sort-Object Destination)) {
    $destinationPath = Join-Path $AssetsRoot $entry.Destination
    $directory = Split-Path -Parent $destinationPath
    if (-not (Test-Path -LiteralPath $directory)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }

    $status = 'added'
    if (Test-Path -LiteralPath $destinationPath -PathType Leaf) {
        if ((Get-Hash $destinationPath) -eq (Get-Hash $entry.SourcePath)) {
            $status = 'unchanged'; $unchanged++
        }
        else {
            $status = 'replaced'; $replaced++
        }
    }
    else {
        $added++
    }

    if ($status -ne 'unchanged') {
        Copy-Item -LiteralPath $entry.SourcePath -Destination $destinationPath -Force
        if ((Get-Hash $destinationPath) -ne (Get-Hash $entry.SourcePath)) {
            throw "copy verification failed for $destinationPath"
        }
    }

    $rows.Add([pscustomobject]@{
            Status      = $status
            Kind        = $entry.Kind
            Theme       = $entry.Theme
            Destination = $entry.Destination
            KB          = [math]::Round((Get-Item -LiteralPath $destinationPath).Length / 1KB, 1)
        })
}

# ---------------------------------------------------------------- manifest

$toolIds = @($plan | Where-Object { $_.Kind -in @('tool-clip', 'tool-poster') } | ForEach-Object {
        $_.Destination -replace '^tools/', '' -replace '-(dark|light)(-clip|-poster)?\.(webp|png)$', ''
    } | Sort-Object -Unique)

$themes = @('dark', 'light')
$wheelNode = [ordered]@{}
foreach ($theme in $themes) {
    $files = @($plan | Where-Object { $_.Kind -like 'wheel-*' -and $_.Theme -eq $theme } |
            ForEach-Object Destination | Sort-Object)
    $wheelNode[$theme] = $files
}

$toolsNode = [ordered]@{}
foreach ($id in $toolIds) {
    $node = [ordered]@{}
    foreach ($theme in $themes) {
        $files = @($plan | Where-Object {
                $_.Theme -eq $theme -and $_.Kind -like 'tool-*' -and
                $_.Destination -match "^tools/$([regex]::Escape($id))-(dark|light)(-clip|-poster)?\."
            } | ForEach-Object Destination | Sort-Object -Unique)
        $node[$theme] = $files
    }
    $toolsNode[$id] = $node
}

$manifest = [ordered]@{
    contract = 'site-tool-captures/1'
    note     = 'Files present under docs/site/assets for each id, relative to the assets root. Generated by scripts/site/import-captures.ps1; do not hand-edit.'
    wheel    = $wheelNode
    tools    = $toolsNode
}
$manifestPath = Join-Path $AssetsRoot 'tools/manifest.json'
$manifestDirectory = Split-Path -Parent $manifestPath
if (-not (Test-Path -LiteralPath $manifestDirectory)) {
    New-Item -ItemType Directory -Path $manifestDirectory -Force | Out-Null
}
$utf8 = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 8) + "`n", $utf8)

# ---------------------------------------------------------------- report

Write-Host ''
Write-Host "Artifact : $artifactRoot"
Write-Host "Assets   : $AssetsRoot"
Write-Host "Motion   : $($motionDirs.Count) source folder(s)"
Write-Host ''
$rows | Format-Table Status, Kind, Theme, Destination, KB -AutoSize | Out-String -Width 200 | Write-Host

Write-Host ("Added {0}, replaced {1}, unchanged {2}. Tool ids in this import: {3}." -f $added, $replaced, $unchanged, $toolIds.Count)
Write-Host "Manifest: $manifestPath"

if ($pngOnlyPosters.Count -gt 0) {
    Write-Warning ("PNG-only posters (no WebP poster in the artifact, nothing converted): " + ($pngOnlyPosters -join ', '))
    Write-Warning "The website's current poster slots (assets/motion/*-poster.webp and assets/tools/<id>-<theme>.webp) were left untouched; new cards should point at the -poster.png files until Team A publishes WebP posters."
}

$logFile = Get-ChildItem -LiteralPath $artifactRoot -Recurse -Filter 'self-test.txt' -File -ErrorAction SilentlyContinue |
    Select-Object -First 1
if ($logFile) {
    $skipped = @(Get-Content -LiteralPath $logFile.FullName | Where-Object { $_ -match 'skipped motion/' })
    Write-Host "self-test.txt: $($logFile.FullName)"
    if ($skipped.Count -gt 0) {
        Write-Warning "$($skipped.Count) tool/wheel captures were skipped by the app:"
        $skipped | ForEach-Object { Write-Warning "  $_" }
    }
}
else {
    Write-Warning "no self-test.txt in the artifact - cannot confirm the render log"
}

$missing = @($toolIds | Where-Object {
        $id = $_
        foreach ($theme in $themes) {
            if (-not ($plan | Where-Object { $_.Kind -eq 'tool-clip' -and $_.Theme -eq $theme -and $_.Destination -eq "tools/$id-$theme-clip.webp" })) { return $true }
        }
        return $false
    })
if ($missing.Count -gt 0) {
    Write-Warning ("tools missing a dark or light clip: " + ($missing -join ', '))
}

exit 0
