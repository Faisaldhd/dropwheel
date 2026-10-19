<#
.SYNOPSIS
    Builds the DropWheel setup from a self-contained win-x64 publish.

.DESCRIPTION
    DropWheel 2.2.x ships self-contained: the publish carries .NET and every dependency, so the
    setup installs no runtime, downloads nothing and checks nothing about the target machine.

    This script:
      1. reads <Version> from src\FileAction.App\FileAction.App.csproj;
      2. publishes with
             dotnet publish src\FileAction.App\FileAction.App.csproj
                 -c Release -r win-x64 --self-contained true
                 -o release\selfcontained
      3. removes *.pdb and the runtime's createdump.exe from the publish;
      4. copies LICENSE, THIRD-PARTY-NOTICES.md and licenses\ next to DropWheel.exe, so the
         installed folder (and the portable layout) is self-describing;
      5. generates the violet wizard images (164x314 and 55x55 BMP) from
         branding\DropWheel-1024.png with System.Drawing. If System.Drawing or the logo is not
         available the Inno Setup defaults are used instead (the build then prints a warning);
      6. compiles installer\DropWheel.iss with ISCC /DAppVersion=<version>;
      7. asserts release\DropWheel-<version>-Setup.exe exists and is at most 70 MB, then prints
         the publish and setup sizes and file counts.

    The setup is not signed here (docs\signing.md covers signing). CI only: nothing in this
    repository is built or run on the owner's PC.

.PARAMETER MaxSetupMB
    Size budget for the setup executable. Default 70.

.PARAMETER IsccPath
    Full path to ISCC.exe. By default the usual Inno Setup 6 locations are searched, including
    the Chocolatey install used by CI.

.PARAMETER SkipPublish
    Reuse an existing release\selfcontained (developer convenience; CI never passes this).

.EXAMPLE
    pwsh scripts\build-installer.ps1
#>
[CmdletBinding()]
param(
    [string] $Configuration = 'Release',
    [int] $MaxSetupMB = 70,
    [string] $IsccPath,
    [switch] $SkipPublish
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'src\FileAction.App\FileAction.App.csproj'
$releaseDir = Join-Path $repoRoot 'release'
$publishDir = Join-Path $releaseDir 'selfcontained'
$wizardDir = Join-Path $releaseDir 'wizard'
$issPath = Join-Path $repoRoot 'installer\DropWheel.iss'
$logoPath = Join-Path $repoRoot 'branding\DropWheel-1024.png'
$licensesSource = Join-Path $repoRoot 'licenses'

# --------------------------------------------------------------------------------------
# Small helpers
# --------------------------------------------------------------------------------------

function Get-AppVersion {
    param([string] $ProjectPath)

    if (-not (Test-Path -LiteralPath $ProjectPath)) {
        throw "Project file not found: $ProjectPath"
    }

    [xml] $projectXml = Get-Content -LiteralPath $ProjectPath -Raw
    $versionNode = @($projectXml.Project.PropertyGroup) |
        Where-Object { $_ -and $_.Version } |
        Select-Object -First 1

    if (-not $versionNode -or -not $versionNode.Version) {
        throw "No <Version> element in $ProjectPath"
    }

    $version = ([string] $versionNode.Version).Trim()
    if ($version -notmatch '^\d+(\.\d+){1,3}$') {
        throw "Unsupported <Version> value '$version' in $ProjectPath"
    }
    return $version
}

function Find-Iscc {
    param([string] $ExplicitPath)

    if ($ExplicitPath) {
        if (-not (Test-Path -LiteralPath $ExplicitPath)) {
            throw "ISCC.exe not found at '$ExplicitPath'."
        }
        return (Resolve-Path -LiteralPath $ExplicitPath).Path
    }

    $candidates = @()
    foreach ($root in @(${env:ProgramFiles(x86)}, $env:ProgramFiles)) {
        if ($root) { $candidates += (Join-Path $root 'Inno Setup 6\ISCC.exe') }
    }
    if ($env:LOCALAPPDATA) {
        $candidates += (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')
    }
    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }

    # Chocolatey (the CI image) may put Inno Setup somewhere else; fall back to a search.
    if (Test-Path -LiteralPath 'C:\ProgramData\chocolatey') {
        $found = Get-ChildItem -LiteralPath 'C:\ProgramData\chocolatey' -Filter 'ISCC.exe' -Recurse -ErrorAction SilentlyContinue |
            Select-Object -First 1
        if ($found) { return $found.FullName }
    }

    throw @'
ISCC.exe (Inno Setup 6) was not found.
Install it with:  choco install innosetup --no-progress -y
or from https://jrsoftware.org/isdl.php and pass -IsccPath <path to ISCC.exe>.
'@
}

function Get-InnoSetupVersion {
    param([string] $Iscc)

    # ISCC.exe carries no version resource; the message file header names the release the
    # bundled messages come from ("; *** Inno Setup version 6.5.0+ English messages ***").
    # It is a lower bound on the compiler version, which is all the checks below need.
    $innoDir = Split-Path -Parent $Iscc
    $defaultIsl = Join-Path $innoDir 'Default.isl'
    if (-not (Test-Path -LiteralPath $defaultIsl)) { return $null }

    $firstLine = Get-Content -LiteralPath $defaultIsl -TotalCount 1
    if ($firstLine -match 'Inno Setup version (\d+)\.(\d+)\.(\d+)') {
        return (New-Object System.Version ([int] $Matches[1]), ([int] $Matches[2]), ([int] $Matches[3]))
    }
    return $null
}

# --------------------------------------------------------------------------------------
# Publish
# --------------------------------------------------------------------------------------

function Invoke-Publish {
    param([string] $ProjectPath, [string] $OutputDirectory, [string] $BuildConfiguration)

    if (Test-Path -LiteralPath $OutputDirectory) {
        Write-Host "Cleaning $OutputDirectory"
        Remove-Item -LiteralPath $OutputDirectory -Recurse -Force
    }
    New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $dotnet) {
        throw "dotnet was not found on PATH; install the .NET 10 SDK."
    }

    $arguments = @(
        'publish', $ProjectPath
        '-c', $BuildConfiguration
        '-r', 'win-x64'
        '--self-contained', 'true'
        '-o', $OutputDirectory
    )

    Write-Host "dotnet $($arguments -join ' ')"
    & $dotnet.Source @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE"
    }
}

function Remove-DebugArtifacts {
    param([string] $Directory)

    # <DebugType>none</DebugType> is set on the app, but a publish can still copy *.pdb from a
    # project reference and a self-contained runtime brings createdump.exe. Neither belongs in
    # the setup (and SolidCompression would only shrink what should not be there at all).
    $isJunk = { param($file) $file.Extension -eq '.pdb' -or $file.Name -eq 'createdump.exe' }

    $junk = @(Get-ChildItem -LiteralPath $Directory -Recurse -File -Force | Where-Object { & $isJunk $_ })
    foreach ($file in $junk) {
        Write-Host ("removing {0}" -f $file.FullName.Substring($Directory.Length).TrimStart('\'))
        Remove-Item -LiteralPath $file.FullName -Force
    }

    $left = @(Get-ChildItem -LiteralPath $Directory -Recurse -File -Force | Where-Object { & $isJunk $_ })
    if ($left.Count -gt 0) {
        throw "debug files remain in ${Directory}: $($left.Name -join ', ')"
    }
}

function Copy-LegalFiles {
    param([string] $Directory, [string] $RepositoryRoot, [string] $LicensesDirectory)

    foreach ($file in @('LICENSE', 'THIRD-PARTY-NOTICES.md')) {
        $source = Join-Path $RepositoryRoot $file
        if (-not (Test-Path -LiteralPath $source)) { throw "Missing $source" }
        Copy-Item -LiteralPath $source -Destination $Directory -Force
    }

    if (-not (Test-Path -LiteralPath $LicensesDirectory)) {
        throw "Missing licenses folder: $LicensesDirectory"
    }
    $target = Join-Path $Directory 'licenses'
    New-Item -ItemType Directory -Path $target -Force | Out-Null
    Copy-Item -Path (Join-Path $LicensesDirectory '*') -Destination $target -Recurse -Force

    # The installer ships these next to the app and the About dialog opens them; a publish that
    # lost one of them must fail the build instead of shipping an app with no licence texts.
    foreach ($required in @('DropWheel.exe', 'LICENSE', 'THIRD-PARTY-NOTICES.md', 'licenses')) {
        if (-not (Test-Path -LiteralPath (Join-Path $Directory $required))) {
            throw "The publish is missing $required"
        }
    }
    $licenseFiles = @(Get-ChildItem -LiteralPath $target -File)
    if ($licenseFiles.Count -lt 5) {
        throw "Only $($licenseFiles.Count) files in the publish's licenses folder"
    }
    Write-Host ("legal files: LICENSE, THIRD-PARTY-NOTICES.md and {0} licence texts" -f $licenseFiles.Count)
}

# --------------------------------------------------------------------------------------
# Wizard images (violet, from branding\DropWheel-1024.png)
# --------------------------------------------------------------------------------------

function Initialize-SystemDrawing {
    # Windows PowerShell 5.1 resolves "System.Drawing"; PowerShell 7 ships System.Drawing.Common.
    foreach ($assembly in @('System.Drawing.Common', 'System.Drawing')) {
        try {
            Add-Type -AssemblyName $assembly -ErrorAction Stop
            [void] [System.Drawing.Bitmap]
            return $true
        } catch {
            # Try the next name.
        }
    }
    foreach ($file in @('System.Drawing.Common.dll', 'System.Drawing.dll')) {
        $candidate = Join-Path $PSHOME $file
        if (Test-Path -LiteralPath $candidate) {
            try {
                [void] [System.Reflection.Assembly]::LoadFrom($candidate)
                [void] [System.Drawing.Bitmap]
                return $true
            } catch {
                # Try the next file.
            }
        }
    }
    return $false
}

function New-BrandedBitmap {
    param(
        [int] $Width,
        [int] $Height,
        [int] $LogoSize,
        $Logo
    )

    $top = [System.Drawing.ColorTranslator]::FromHtml('#8B2FE0')
    $bottom = [System.Drawing.ColorTranslator]::FromHtml('#4B0E86')
    $glowEdge = [System.Drawing.Color]::FromArgb(0, 255, 255, 255)
    $glowCenter = [System.Drawing.Color]::FromArgb(60, 255, 255, 255)

    $bitmap = New-Object System.Drawing.Bitmap $Width, $Height, ([System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
    try {
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
            $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality

            $bounds = New-Object System.Drawing.Rectangle 0, 0, $Width, $Height
            $gradient = New-Object System.Drawing.Drawing2D.LinearGradientBrush $bounds, $top, $bottom, ([System.Drawing.Drawing2D.LinearGradientMode]::Vertical)
            try {
                $graphics.FillRectangle($gradient, $bounds)
            } finally {
                $gradient.Dispose()
            }

            # A soft light halo so the dark logo does not sit flat on the gradient.
            $centerX = [int] ($Width / 2)
            $centerY = [int] ($Height / 2)
            $radius = [int] ($LogoSize * 0.82)
            $haloPath = New-Object System.Drawing.Drawing2D.GraphicsPath
            try {
                [void] $haloPath.AddEllipse($centerX - $radius, $centerY - $radius, 2 * $radius, 2 * $radius)
                $halo = New-Object System.Drawing.Drawing2D.PathGradientBrush $haloPath
                try {
                    $halo.CenterColor = $glowCenter
                    $halo.SurroundColors = [System.Drawing.Color[]] @($glowEdge)
                    $graphics.FillPath($halo, $haloPath)
                } finally {
                    $halo.Dispose()
                }
            } finally {
                $haloPath.Dispose()
            }

            $logoX = [int] (($Width - $LogoSize) / 2)
            $logoY = [int] (($Height - $LogoSize) / 2)
            $graphics.DrawImage($Logo, $logoX, $logoY, $LogoSize, $LogoSize)
        } finally {
            $graphics.Dispose()
        }
        return $bitmap
    } catch {
        $bitmap.Dispose()
        throw
    }
}

function New-WizardImages {
    param([string] $SourceLogo, [string] $OutputDirectory)

    if (-not (Initialize-SystemDrawing)) {
        Write-Warning 'System.Drawing is unavailable in this PowerShell; using the built-in Inno Setup wizard images.'
        return $null
    }
    if (-not (Test-Path -LiteralPath $SourceLogo)) {
        Write-Warning "Wizard image source not found: $SourceLogo; using the built-in Inno Setup wizard images."
        return $null
    }

    if (Test-Path -LiteralPath $OutputDirectory) {
        Remove-Item -LiteralPath $OutputDirectory -Recurse -Force
    }
    New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

    $largePath = Join-Path $OutputDirectory 'wizard-large.bmp'
    $smallPath = Join-Path $OutputDirectory 'wizard-small.bmp'
    $logo = [System.Drawing.Image]::FromFile($SourceLogo)
    try {
        # Inno Setup 6.5 and older display a 164x314 image at 100% DPI (6.6+ scales it, keeping
        # the aspect ratio); the small image is 55x55.
        $large = New-BrandedBitmap -Width 164 -Height 314 -LogoSize 116 -Logo $logo
        try {
            $large.Save($largePath, [System.Drawing.Imaging.ImageFormat]::Bmp)
        } finally {
            $large.Dispose()
        }
        $small = New-BrandedBitmap -Width 55 -Height 55 -LogoSize 41 -Logo $logo
        try {
            $small.Save($smallPath, [System.Drawing.Imaging.ImageFormat]::Bmp)
        } finally {
            $small.Dispose()
        }
    } finally {
        $logo.Dispose()
    }

    foreach ($file in @($largePath, $smallPath)) {
        if (-not (Test-Path -LiteralPath $file)) { throw "Wizard image was not written: $file" }
        if ((Get-Item -LiteralPath $file).Length -lt 1000) { throw "Wizard image looks empty: $file" }
    }
    return @{ Large = $largePath; Small = $smallPath }
}

# --------------------------------------------------------------------------------------
# Build
# --------------------------------------------------------------------------------------

$version = Get-AppVersion -ProjectPath $project
Write-Host "DropWheel version: $version"

New-Item -ItemType Directory -Path $releaseDir -Force | Out-Null

if (-not $SkipPublish) {
    Invoke-Publish -ProjectPath $project -OutputDirectory $publishDir -BuildConfiguration $Configuration
} elseif (-not (Test-Path -LiteralPath (Join-Path $publishDir 'DropWheel.exe'))) {
    throw "-SkipPublish was used but $publishDir has no DropWheel.exe"
}

Remove-DebugArtifacts -Directory $publishDir
Copy-LegalFiles -Directory $publishDir -RepositoryRoot $repoRoot -LicensesDirectory $licensesSource

$wizard = $null
try {
    $wizard = New-WizardImages -SourceLogo $logoPath -OutputDirectory $wizardDir
} catch {
    Write-Warning "Wizard image generation failed ($($_.Exception.Message)); using the built-in Inno Setup wizard images."
    $wizard = $null
}

$iscc = Find-Iscc -ExplicitPath $IsccPath
Write-Host "ISCC: $iscc"
$innoVersion = Get-InnoSetupVersion -Iscc $iscc
if ($innoVersion) {
    Write-Host "Inno Setup message files: $innoVersion"
    if ($innoVersion -lt (New-Object System.Version 6, 3)) {
        throw "Inno Setup 6.3 or newer is required (ArchitecturesAllowed=x64compatible); found $innoVersion."
    }
    if ($innoVersion -lt (New-Object System.Version 6, 5)) {
        Write-Warning "installer\Arabic.isl comes from Inno Setup 6.5; an older compiler may reject newer messages."
    }
} else {
    Write-Warning 'Could not determine the Inno Setup version; continuing.'
}

$isccArguments = @("/DAppVersion=$version")
if ($wizard) {
    # Paths are relative to the .iss file (installer\), which Inno Setup uses as source dir.
    $isccArguments += '/DCustomWizardImages'
    $isccArguments += '/DWizardImageFile=..\release\wizard\wizard-large.bmp'
    $isccArguments += '/DWizardSmallImageFile=..\release\wizard\wizard-small.bmp'
    Write-Host 'Wizard images: generated violet 164x314 and 55x55 BMPs'
} else {
    Write-Host 'Wizard images: Inno Setup defaults'
}
$isccArguments += $issPath

Write-Host "ISCC $($isccArguments -join ' ')"
& $iscc @isccArguments
if ($LASTEXITCODE -ne 0) {
    throw "ISCC failed with exit code $LASTEXITCODE"
}

# --------------------------------------------------------------------------------------
# Verify what was built
# --------------------------------------------------------------------------------------

$setupPath = Join-Path $releaseDir ("DropWheel-{0}-Setup.exe" -f $version)
if (-not (Test-Path -LiteralPath $setupPath)) {
    throw "The setup was not produced: $setupPath"
}

$publishFiles = @(Get-ChildItem -LiteralPath $publishDir -Recurse -File)
$publishBytes = ($publishFiles | Measure-Object -Property Length -Sum).Sum
$publishMB = [math]::Round($publishBytes / 1MB, 1)

$setup = Get-Item -LiteralPath $setupPath
$setupMB = [math]::Round($setup.Length / 1MB, 2)
$setupLimitBytes = $MaxSetupMB * 1MB

Write-Host ''
Write-Host '--------------------------------------------------------------'
Write-Host ("publish (self-contained, untrimmed win-x64): {0} MB / {1} files" -f $publishMB, $publishFiles.Count)
Write-Host ("setup exe:                                  {0} MB" -f $setupMB)
Write-Host ("setup path:                                 {0}" -f $setupPath)
Write-Host '--------------------------------------------------------------'

if ($env:GITHUB_STEP_SUMMARY) {
    $summary = @(
        "### DropWheel $version installer",
        '',
        '| item | size | files |',
        '| --- | --- | --- |',
        ("| publish (self-contained, untrimmed, win-x64) | $publishMB MB | $($publishFiles.Count) |"),
        ("| setup exe | $setupMB MB | 1 |"),
        ''
    ) -join "`n"
    Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Value $summary
}

if ($setup.Length -gt $setupLimitBytes) {
    throw ("The setup is {0} MB, above the {1} MB budget. Reduce the publish contents or check what it pulls in." -f $setupMB, $MaxSetupMB)
}

Write-Host ("OK: {0} ({1} MB, budget {2} MB)" -f $setup.Name, $setupMB, $MaxSetupMB)
