# CI-only test for the portable `--apply-update` helper.
#
# The project rules forbid running built exes on the owner's PC; this script is meant for the
# windows-latest runner only. It needs a published/build DropWheel folder (or it publishes one with the
# installed .NET 10 SDK). It never downloads anything.
#
# Usage:
#   pwsh ./scripts/test-update-apply.ps1
#   pwsh ./scripts/test-update-apply.ps1 -ExePath src/FileAction.App/bin/Release/net10.0-windows/DropWheel.exe
#
# What it covers:
#   1. success: marker + exe replaced, settings.json/portable.txt kept, app relaunched (logged), exit 0;
#   2. corrupt zip: helper fails cleanly and nothing on disk changes;
#   3. failure after replacement (invalid new exe): the backup is restored.

[CmdletBinding()]
param(
    [string]$ExePath,
    [string]$WorkRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) {
        throw "ASSERTION FAILED: $Message"
    }
}

function Copy-Directory {
    param([string]$Source, [string]$Destination)
    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    Copy-Item -Path (Join-Path $Source '*') -Destination $Destination -Recurse -Force
}

function New-FakeInstall {
    param(
        [string]$SourceDirectory,
        [string]$Destination,
        [string]$Marker
    )
    Copy-Directory -Source $SourceDirectory -Destination $Destination
    Set-Content -Path (Join-Path $Destination 'portable.txt') -Value 'DropWheel portable build' -NoNewline
    Set-Content -Path (Join-Path $Destination 'settings.json') -Value '{"Language":"English","CheckForUpdates":true}' -NoNewline
    Set-Content -Path (Join-Path $Destination 'marker.txt') -Value $Marker -NoNewline
    Set-Content -Path (Join-Path $Destination 'log.txt') -Value 'original log' -NoNewline
}

function Invoke-ApplyUpdate {
    param(
        [string]$InstallDirectory,
        [string]$Archive
    )

    $exe = Join-Path $InstallDirectory 'DropWheel.exe'
    $arguments = @('--apply-update', ('"{0}"' -f $Archive), '9.9.9')
    $process = Start-Process -FilePath $exe -ArgumentList $arguments -WorkingDirectory $InstallDirectory -PassThru
    if (-not $process.WaitForExit(60000)) {
        try { $process.Kill($true) } catch { }
        throw "the update launcher did not exit within 60 s (pid $($process.Id))"
    }
    return $process.ExitCode
}

function Wait-UpdateResult {
    param(
        [string]$LogPath,
        [string]$Pattern,
        [int]$TimeoutSeconds = 120
    )

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        if (Test-Path -LiteralPath $LogPath) {
            $text = $null
            try {
                $text = Get-Content -LiteralPath $LogPath -Raw -ErrorAction Stop
            } catch {
                # The worker appends while we read; try again on the next tick.
            }
            if ($null -ne $text -and ($text -match 'update apply: success' -or $text -match 'update apply: failed')) {
                if ($text -match $Pattern) {
                    return $text
                }
                throw "the update helper reported an unexpected result:`n$text"
            }
        }
        Start-Sleep -Milliseconds 250
    }
    throw "timed out after $TimeoutSeconds s waiting for '$Pattern' in $LogPath"
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$work = if ($WorkRoot) { $WorkRoot } else { Join-Path ([IO.Path]::GetTempPath()) ('DropWheel-update-test-' + [guid]::NewGuid().ToString('N')) }
$createdWork = -not (Test-Path -LiteralPath $work)
if ($createdWork) {
    New-Item -ItemType Directory -Path $work -Force | Out-Null
}

try {
    # 1. Find a runnable app folder. The app is published as a folder (apphost + DLLs), not single-file, so
    #    the helper test must give the fake install a complete folder. CI builds into bin\Release first.
    if ($ExePath) {
        $resolvedExe = (Resolve-Path -LiteralPath $ExePath).Path
        $sourceDir = Split-Path -Parent $resolvedExe
    } else {
        $builtExe = Join-Path $repoRoot 'src\FileAction.App\bin\Release\net10.0-windows\DropWheel.exe'
        if (Test-Path -LiteralPath $builtExe) {
            $sourceDir = Split-Path -Parent $builtExe
        } else {
            Write-Host 'No Release build output found; publishing a framework-dependent folder with dotnet publish.'
            $publishDir = Join-Path $work 'publish'
            & dotnet publish (Join-Path $repoRoot 'src\FileAction.App\FileAction.App.csproj') `
                -c Release -r win-x64 --self-contained false -o $publishDir
            if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with $LASTEXITCODE" }
            $sourceDir = $publishDir
        }
    }

    $sourceExe = Join-Path $sourceDir 'DropWheel.exe'
    Assert-True (Test-Path -LiteralPath $sourceExe) "no DropWheel.exe in $sourceDir"
    $sourceExeHash = (Get-FileHash -LiteralPath $sourceExe -Algorithm SHA256).Hash

    $fakeExe = Join-Path $env:SystemRoot 'System32\where.exe'
    Assert-True (Test-Path -LiteralPath $fakeExe) "where.exe not found at $fakeExe"
    $fakeExeHash = (Get-FileHash -LiteralPath $fakeExe -Algorithm SHA256).Hash

    # 2. Success case: a real archive that replaces the marker and the exe.
    $successInstall = Join-Path $work 'install-success'
    New-FakeInstall -SourceDirectory $sourceDir -Destination $successInstall -Marker 'old-marker'

    $successZipSource = Join-Path $work 'zip-success'
    New-Item -ItemType Directory -Path $successZipSource -Force | Out-Null
    Copy-Item -LiteralPath $fakeExe -Destination (Join-Path $successZipSource 'DropWheel.exe') -Force
    Set-Content -Path (Join-Path $successZipSource 'marker.txt') -Value 'new-marker' -NoNewline
    $successZip = Join-Path $work 'update-success.zip'
    Compress-Archive -Path (Join-Path $successZipSource '*') -DestinationPath $successZip -Force

    $exitCode = Invoke-ApplyUpdate -InstallDirectory $successInstall -Archive $successZip
    Assert-True ($exitCode -eq 0) "the update launcher exited with $exitCode"

    $successLog = Wait-UpdateResult -LogPath (Join-Path $successInstall 'log.txt') -Pattern 'update apply: success'
    Assert-True ($successLog -match 'relaunched') 'the helper did not log the relaunch'
    Assert-True ((Get-Content -LiteralPath (Join-Path $successInstall 'marker.txt') -Raw) -eq 'new-marker') \
        'marker.txt was not replaced'
    Assert-True ((Get-Content -LiteralPath (Join-Path $successInstall 'settings.json') -Raw) `
        -eq '{"Language":"English","CheckForUpdates":true}') 'settings.json was changed by the update'
    Assert-True ((Get-Content -LiteralPath (Join-Path $successInstall 'portable.txt') -Raw) `
        -eq 'DropWheel portable build') 'portable.txt was changed by the update'
    Assert-True ((Get-FileHash -LiteralPath (Join-Path $successInstall 'DropWheel.exe') -Algorithm SHA256).Hash `
        -eq $fakeExeHash) 'DropWheel.exe was not replaced by the update'
    Assert-True ((Get-Content -LiteralPath (Join-Path $successInstall 'log.txt') -Raw) -match 'original log') \
        'the original log.txt content was lost'
    Write-Host 'PASS: portable update replaced the app and kept user data.'

    # 3. Corrupt zip: a failed extraction must leave the install untouched.
    $brokenInstall = Join-Path $work 'install-broken'
    New-FakeInstall -SourceDirectory $sourceDir -Destination $brokenInstall -Marker 'old-marker'
    $brokenZip = Join-Path $work 'update-broken.zip'
    Set-Content -Path $brokenZip -Value 'this is not a zip archive' -NoNewline

    $exitCode = Invoke-ApplyUpdate -InstallDirectory $brokenInstall -Archive $brokenZip
    Assert-True ($exitCode -eq 0) "the update launcher for the corrupt zip exited with $exitCode"
    $brokenLog = Wait-UpdateResult -LogPath (Join-Path $brokenInstall 'log.txt') -Pattern 'update apply: failed'
    Assert-True ($brokenLog -match 'failed') 'the corrupt zip was not reported as a failure'
    Assert-True ((Get-Content -LiteralPath (Join-Path $brokenInstall 'marker.txt') -Raw) -eq 'old-marker') \
        'marker.txt changed after a corrupt zip'
    Assert-True ((Get-Content -LiteralPath (Join-Path $brokenInstall 'settings.json') -Raw) `
        -eq '{"Language":"English","CheckForUpdates":true}') 'settings.json changed after a corrupt zip'
    Assert-True ((Get-FileHash -LiteralPath (Join-Path $brokenInstall 'DropWheel.exe') -Algorithm SHA256).Hash `
        -eq $sourceExeHash) 'DropWheel.exe changed after a corrupt zip'
    Write-Host 'PASS: corrupt zip changed nothing.'

    # 4. Rollback: the archive is valid but its DropWheel.exe is not runnable, so the failure happens after
    #    the other files were already replaced. The helper must restore the backup.
    $rollbackInstall = Join-Path $work 'install-rollback'
    New-FakeInstall -SourceDirectory $sourceDir -Destination $rollbackInstall -Marker 'old-marker'

    $rollbackZipSource = Join-Path $work 'zip-rollback'
    New-Item -ItemType Directory -Path $rollbackZipSource -Force | Out-Null
    Set-Content -Path (Join-Path $rollbackZipSource 'DropWheel.exe') -Value 'this is not a Windows executable' -NoNewline
    Set-Content -Path (Join-Path $rollbackZipSource 'marker.txt') -Value 'new-marker' -NoNewline
    Set-Content -Path (Join-Path $rollbackZipSource 'extra.txt') -Value 'new-file' -NoNewline
    $rollbackZip = Join-Path $work 'update-rollback.zip'
    Compress-Archive -Path (Join-Path $rollbackZipSource '*') -DestinationPath $rollbackZip -Force

    $exitCode = Invoke-ApplyUpdate -InstallDirectory $rollbackInstall -Archive $rollbackZip
    Assert-True ($exitCode -eq 0) "the update launcher for the rollback case exited with $exitCode"
    $rollbackLog = Wait-UpdateResult -LogPath (Join-Path $rollbackInstall 'log.txt') `
        -Pattern 'previous version was restored'
    Assert-True ($rollbackLog -match 'failed') 'the rollback case did not report a failure'
    Assert-True ((Get-Content -LiteralPath (Join-Path $rollbackInstall 'marker.txt') -Raw) -eq 'old-marker') \
        'marker.txt was not restored by the rollback'
    Assert-True ((Get-Content -LiteralPath (Join-Path $rollbackInstall 'settings.json') -Raw) `
        -eq '{"Language":"English","CheckForUpdates":true}') 'settings.json changed in the rollback case'
    Assert-True ((Get-Content -LiteralPath (Join-Path $rollbackInstall 'portable.txt') -Raw) `
        -eq 'DropWheel portable build') 'portable.txt changed in the rollback case'
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $rollbackInstall 'extra.txt'))) \
        'the new file from the failed update was not removed by the rollback'
    Assert-True ((Get-FileHash -LiteralPath (Join-Path $rollbackInstall 'DropWheel.exe') -Algorithm SHA256).Hash `
        -eq $sourceExeHash) 'DropWheel.exe was not restored by the rollback'
    Write-Host 'PASS: a failure after replacement restored the previous version.'
} finally {
    if ($createdWork) {
        Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
    }
}
