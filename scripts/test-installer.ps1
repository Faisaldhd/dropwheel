<#
.SYNOPSIS
    CI-only end-to-end smoke test for the DropWheel setup.

.DESCRIPTION
    Proves the parts of the installer contract that can be observed on a clean Windows machine:

      1. a silent per-user install into a temporary folder
         (/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CURRENTUSER /DIR=...) exits with 0;
      2. DropWheel.exe, LICENSE, THIRD-PARTY-NOTICES.md and licenses\ are installed;
      3. the HKCU Run value DropWheel = "<app>\DropWheel.exe" --background exists;
      4. the Start-menu shortcut exists and no desktop shortcut was created (the task is
         unchecked by default);
      5. after seeding the two Explorer context-menu keys and the Send-to shortcut (the app
         creates them only when the user opts in), a silent uninstall removes them, the Run
         value, the program folder and the Start-menu shortcut;
      6. a silent uninstall keeps the settings folder and the downloaded helper programs.

    Run it only in CI, after scripts\build-installer.ps1 (the "Package" job). Everything is
    removed again, so the test can be repeated on the same machine. Any failure exits non-zero.

.PARAMETER SetupPath
    The setup to test. Default: the newest release\DropWheel-<version>-Setup.exe.

.PARAMETER InstallDirectory
    Empty folder to install into. Default: a fresh folder under %RUNNER_TEMP%.

.PARAMETER TimeoutSeconds
    How long the installer or uninstaller may take. Default 300.
#>
[CmdletBinding()]
param(
    [string] $SetupPath,
    [string] $InstallDirectory,
    [int] $TimeoutSeconds = 300
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$appId = '{1AB9CDBE-C50D-484B-96ED-5D575D0806D2}'
$runSubKey = 'Software\Microsoft\Windows\CurrentVersion\Run'
$runValueName = 'DropWheel'
$shellKeys = @(
    'Software\Classes\*\shell\DropWheel',
    'Software\Classes\Directory\shell\DropWheel'
)
$uninstallKeyNames = @(
    ("HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{0}_is1" -f $appId),
    ("HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{0}_is1" -f $appId)
)

$tempRoot = $env:RUNNER_TEMP
if (-not $tempRoot) { $tempRoot = [System.IO.Path]::GetTempPath() }
$tempRoot = $tempRoot.TrimEnd('\')

$appDataFolder = Join-Path $env:APPDATA 'DropWheel'
$localAppDataFolder = Join-Path $env:LOCALAPPDATA 'DropWheel'
$sendToFolder = Join-Path $env:APPDATA 'Microsoft\Windows\SendTo'
$sendToShortcut = Join-Path $sendToFolder 'DropWheel.lnk'
$startMenuShortcut = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\DropWheel.lnk'
$desktopShortcut = Join-Path ([Environment]::GetFolderPath('Desktop')) 'DropWheel.lnk'
$settingsMarker = Join-Path $appDataFolder 'installer-smoke-test.txt'
$toolsMarker = Join-Path $localAppDataFolder 'tools\installer-smoke-test.txt'
$installLog = Join-Path $tempRoot 'dropwheel-install.log'
$uninstallLog = Join-Path $tempRoot 'dropwheel-uninstall.log'

$installPath = $null
$appDataExistedBefore = $false
$localAppDataExistedBefore = $false
$failed = $false
$failureMessage = ''

# --------------------------------------------------------------------------------------
# Assertions and small helpers
# --------------------------------------------------------------------------------------

function Write-Step {
    param([string] $Text)
    Write-Host ''
    Write-Host "== $Text" -ForegroundColor Cyan
}

function Assert-That {
    param([bool] $Condition, [string] $Message)
    if (-not $Condition) { throw "Assertion failed: $Message" }
    Write-Host "  ok: $Message"
}

function Get-RunValue {
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($runSubKey)
    if (-not $key) { return $null }
    try {
        return $key.GetValue($runValueName, $null)
    } finally {
        $key.Dispose()
    }
}

function Test-UserKeyExists {
    param([string] $SubKey)
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($SubKey)
    if (-not $key) { return $false }
    $key.Dispose()
    return $true
}

function Stop-DropWheelProcesses {
    foreach ($process in @(Get-Process -Name 'DropWheel' -ErrorAction SilentlyContinue)) {
        try { $process.Kill() } catch { }
        try { $process.Dispose() } catch { }
    }
}

function Wait-Until {
    param([scriptblock] $Condition, [int] $Seconds, [string] $What)
    $deadline = (Get-Date).AddSeconds($Seconds)
    while ((Get-Date) -lt $deadline) {
        if (& $Condition) { return }
        Start-Sleep -Milliseconds 250
    }
    throw "Timed out after $Seconds s waiting for $What."
}

function Invoke-SetupProcess {
    param([string] $FilePath, [string[]] $Arguments, [string] $What)

    Write-Host ("  {0} {1}" -f $FilePath, ($Arguments -join ' '))
    $process = Start-Process -FilePath $FilePath -ArgumentList $Arguments -PassThru -WindowStyle Hidden
    try {
        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
            # A hung setup must fail the job quickly instead of leaving a 6-hour build.
            try { $process.Kill($true) } catch { try { $process.Kill() } catch { } }
            Stop-DropWheelProcesses
            throw "$What did not finish within $TimeoutSeconds s."
        }
        return $process.ExitCode
    } finally {
        try { $process.Dispose() } catch { }
    }
}

# --------------------------------------------------------------------------------------
# The test
# --------------------------------------------------------------------------------------

function Invoke-SmokeTest {
    Write-Step 'Checking that the machine is clean'
    $dirty = @()
    if (Get-RunValue) { $dirty += "the HKCU Run value $runValueName" }
    foreach ($shellKey in $shellKeys) {
        if (Test-UserKeyExists $shellKey) { $dirty += "the registry key $shellKey" }
    }
    if (Test-Path -LiteralPath $sendToShortcut) { $dirty += "the Send-to shortcut $sendToShortcut" }
    foreach ($uninstallKey in $uninstallKeyNames) {
        if (Test-Path -LiteralPath $uninstallKey) { $dirty += "the uninstall entry $uninstallKey" }
    }
    if (Test-Path -LiteralPath $appDataFolder) { $dirty += "the settings folder $appDataFolder" }
    if (Test-Path -LiteralPath $localAppDataFolder) { $dirty += "the local app data folder $localAppDataFolder" }
    if ($dirty.Count -gt 0) {
        throw ("The smoke test needs a clean machine, but it found: {0}" -f ($dirty -join '; '))
    }
    $script:appDataExistedBefore = $false
    $script:localAppDataExistedBefore = $false
    Write-Host '  ok: no DropWheel install, settings, Run value or shell entries'

    Write-Step 'Resolving the setup'
    if (-not $SetupPath) {
        $releaseDir = Join-Path $repoRoot 'release'
        $candidate = Get-ChildItem -LiteralPath $releaseDir -Filter 'DropWheel-*-Setup.exe' -File -ErrorAction SilentlyContinue |
            Sort-Object -Property LastWriteTime -Descending |
            Select-Object -First 1
        if (-not $candidate) {
            throw "No release\DropWheel-*-Setup.exe; run scripts\build-installer.ps1 first."
        }
        $script:SetupPath = $candidate.FullName
    }
    if (-not (Test-Path -LiteralPath $SetupPath)) { throw "Setup not found: $SetupPath" }
    $setup = Get-Item -LiteralPath $SetupPath
    Assert-That ($setup.Name -match '^DropWheel-\d+\.\d+\.\d+-Setup\.exe$') "setup name matches the update contract ($($setup.Name))"
    Write-Host ("  setup: {0} ({1:N2} MB)" -f $setup.FullName, ($setup.Length / 1MB))

    Write-Step 'Installing silently, per user, into a temporary folder'
    if (-not $InstallDirectory) {
        $script:InstallDirectory = Join-Path $tempRoot ('dropwheel-installer-test-' + [guid]::NewGuid().ToString('N'))
    }
    if (Test-Path -LiteralPath $InstallDirectory) { throw "The install directory already exists: $InstallDirectory" }
    $script:installPath = $InstallDirectory

    Remove-Item -LiteralPath $installLog -Force -ErrorAction SilentlyContinue
    $installArguments = @(
        '/VERYSILENT'
        '/SUPPRESSMSGBOXES'
        '/NORESTART'
        '/CURRENTUSER'
        ('/DIR="{0}"' -f $InstallDirectory)
        ('/LOG="{0}"' -f $installLog)
    )
    $exitCode = Invoke-SetupProcess -FilePath $SetupPath -Arguments $installArguments -What 'The installer'
    Assert-That ($exitCode -eq 0) "the installer exited with 0 (got $exitCode)"

    Write-Step 'Checking what the installer put on the machine'
    $installedExe = Join-Path $InstallDirectory 'DropWheel.exe'
    Assert-That (Test-Path -LiteralPath $installedExe) 'DropWheel.exe is installed'
    Assert-That (Test-Path -LiteralPath (Join-Path $InstallDirectory 'LICENSE')) 'LICENSE is installed'
    Assert-That (Test-Path -LiteralPath (Join-Path $InstallDirectory 'THIRD-PARTY-NOTICES.md')) 'THIRD-PARTY-NOTICES.md is installed'
    $licenseFiles = @(Get-ChildItem -LiteralPath (Join-Path $InstallDirectory 'licenses') -File -ErrorAction SilentlyContinue)
    Assert-That ($licenseFiles.Count -ge 5) "licenses\ is installed ($($licenseFiles.Count) files)"

    $runValue = Get-RunValue
    Assert-That ($null -ne $runValue -and ([string] $runValue).Length -gt 0) 'the HKCU Run value DropWheel exists'
    Assert-That (([string] $runValue) -like '*DropWheel.exe*' -and ([string] $runValue) -like '*--background*') "Run value starts DropWheel --background ($runValue)"

    Assert-That (Test-Path -LiteralPath $startMenuShortcut) 'the Start-menu shortcut exists'
    Assert-That (-not (Test-Path -LiteralPath $desktopShortcut)) 'no desktop shortcut was created (task is unchecked by default)'
    foreach ($uninstallKey in $uninstallKeyNames) {
        if (Test-Path -LiteralPath $uninstallKey) { Write-Host "  uninstall entry: $uninstallKey" }
    }

    Write-Step 'Seeding the app-created shell entries and the user data a silent uninstall must keep'
    $registry = [Microsoft.Win32.Registry]::CurrentUser
    foreach ($shellKey in $shellKeys) {
        $key = $registry.CreateSubKey($shellKey)
        try {
            $key.SetValue('', 'DropWheel')
            $key.SetValue('Icon', ('"{0}",0' -f $installedExe))
            $command = $key.CreateSubKey('command')
            try { $command.SetValue('', ('"{0}" --wheel "%1"' -f $installedExe)) } finally { $command.Dispose() }
        } finally {
            $key.Dispose()
        }
    }
    New-Item -ItemType Directory -Path $sendToFolder -Force | Out-Null
    $shell = $null
    try {
        $shell = New-Object -ComObject WScript.Shell
        $shortcut = $shell.CreateShortcut($sendToShortcut)
        $shortcut.TargetPath = $installedExe
        $shortcut.Arguments = '--wheel'
        $shortcut.Save()
    } catch {
        # The uninstaller only deletes the file; a plain file is enough if the shell COM object
        # is unavailable on this runner.
        Set-Content -LiteralPath $sendToShortcut -Value 'DropWheel'
    } finally {
        if ($shell) {
            try { [void] [System.Runtime.InteropServices.Marshal]::ReleaseComObject($shell) } catch { }
        }
    }
    Assert-That (Test-UserKeyExists $shellKeys[0]) 'the file context-menu key is seeded'
    Assert-That (Test-UserKeyExists $shellKeys[1]) 'the folder context-menu key is seeded'
    Assert-That (Test-Path -LiteralPath $sendToShortcut) 'the Send-to shortcut is seeded'

    if (-not (Test-Path -LiteralPath $appDataFolder)) {
        New-Item -ItemType Directory -Path $appDataFolder -Force | Out-Null
    }
    Set-Content -LiteralPath $settingsMarker -Value 'settings must survive a silent uninstall'
    New-Item -ItemType Directory -Path (Split-Path -Parent $toolsMarker) -Force | Out-Null
    Set-Content -LiteralPath $toolsMarker -Value 'downloaded helper programs must survive a silent uninstall'

    Write-Step 'Uninstalling silently'
    $uninstaller = Join-Path $InstallDirectory 'unins000.exe'
    Assert-That (Test-Path -LiteralPath $uninstaller) 'the uninstaller exists'
    Remove-Item -LiteralPath $uninstallLog -Force -ErrorAction SilentlyContinue
    $uninstallArguments = @(
        '/VERYSILENT'
        '/SUPPRESSMSGBOXES'
        '/NORESTART'
        ('/LOG="{0}"' -f $uninstallLog)
    )
    $uninstallExitCode = Invoke-SetupProcess -FilePath $uninstaller -Arguments $uninstallArguments -What 'The uninstaller'
    Assert-That ($uninstallExitCode -eq 0 -or $uninstallExitCode -eq 3010) "the uninstaller exited with 0 (got $uninstallExitCode)"

    # The uninstaller relaunches itself from %TEMP% and the first process can return before the
    # files are gone, so wait for the observable result rather than trusting the exit code.
    Wait-Until -Condition { -not (Test-Path -LiteralPath $InstallDirectory) } -Seconds 120 -What 'the program folder to disappear'
    Wait-Until -Condition { -not (Test-Path -LiteralPath $uninstallKeyNames[0]) } -Seconds 60 -What 'the per-user uninstall entry to disappear'

    Write-Step 'Checking what the uninstall removed'
    Assert-That (-not (Test-Path -LiteralPath $InstallDirectory)) 'the program folder is gone'
    Assert-That ($null -eq (Get-RunValue)) 'the HKCU Run value is gone'
    Assert-That (-not (Test-UserKeyExists $shellKeys[0])) 'the file context-menu key is gone'
    Assert-That (-not (Test-UserKeyExists $shellKeys[1])) 'the folder context-menu key is gone'
    Assert-That (-not (Test-Path -LiteralPath $sendToShortcut)) 'the Send-to shortcut is gone'
    Assert-That (-not (Test-Path -LiteralPath $startMenuShortcut)) 'the Start-menu shortcut is gone'

    Write-Step 'Checking what the uninstall kept'
    Assert-That (Test-Path -LiteralPath $settingsMarker) 'a silent uninstall keeps the settings folder'
    Assert-That (Test-Path -LiteralPath $toolsMarker) 'a silent uninstall keeps the downloaded helper programs'
}

# --------------------------------------------------------------------------------------
# Run, clean up, report
# --------------------------------------------------------------------------------------

function Invoke-Cleanup {
    # Best effort only: a failed test must leave the logs for the CI job, but the machine must be
    # usable for another run.
    Stop-DropWheelProcesses
    if ($installPath -and (Test-Path -LiteralPath $installPath)) {
        Remove-Item -LiteralPath $installPath -Recurse -Force -ErrorAction SilentlyContinue
    }
    if (-not $appDataExistedBefore -and (Test-Path -LiteralPath $appDataFolder)) {
        Remove-Item -LiteralPath $appDataFolder -Recurse -Force -ErrorAction SilentlyContinue
    }
    if (-not $localAppDataExistedBefore -and (Test-Path -LiteralPath $localAppDataFolder)) {
        Remove-Item -LiteralPath $localAppDataFolder -Recurse -Force -ErrorAction SilentlyContinue
    }
    if (Test-Path -LiteralPath $sendToShortcut) {
        Remove-Item -LiteralPath $sendToShortcut -Force -ErrorAction SilentlyContinue
    }
    foreach ($shellKey in $shellKeys) {
        try { [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKeyTree($shellKey, $false) } catch { }
    }
    if (Get-RunValue) {
        try {
            $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($runSubKey, $true)
            if ($key) {
                try { $key.DeleteValue($runValueName, $false) } finally { $key.Dispose() }
            }
        } catch { }
    }
}

try {
    Invoke-SmokeTest
    Write-Host ''
    Write-Host 'PASS: silent install, shell entries, silent uninstall and settings retention verified.' -ForegroundColor Green
} catch {
    $failed = $true
    $failureMessage = $_.Exception.Message
    Write-Host ''
    Write-Host "FAIL: $failureMessage" -ForegroundColor Red
    Write-Host "  setup:            $SetupPath"
    Write-Host "  install dir:      $InstallDirectory"
    Write-Host "  install log:      $installLog"
    Write-Host "  uninstall log:    $uninstallLog"
    if (Test-Path -LiteralPath $installLog) {
        Write-Host ''
        Write-Host '--- last 40 lines of the install log ---'
        Get-Content -LiteralPath $installLog -Tail 40 | ForEach-Object { Write-Host $_ }
    }
} finally {
    Invoke-Cleanup
}

if ($failed) { exit 1 }
exit 0
