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
      6. a silent uninstall keeps the settings folder and the downloaded helper programs;
      7. the installed DropWheel.exe passes --self-test and --diagnose with at least 300 localized
         keys and no raw key in the output (2.2.0 shipped an installed build whose string tables
         were empty, so this gate must run on the installed copy, not on the publish folder).

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
$selfTestDir = $null
$selfTestLog = $null
$diagnosePath = $null
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

function Invoke-AppProcess {
    # Same contract as Invoke-SetupProcess, but for the installed app: a GUI-subsystem exe does not
    # set $LASTEXITCODE under &, and a hung self-test must fail the job instead of hanging it.
    param([string] $FilePath, [string[]] $Arguments, [string] $What, [int] $Seconds = 600)

    Write-Host ("  {0} {1}" -f $FilePath, ($Arguments -join ' '))
    $process = Start-Process -FilePath $FilePath -ArgumentList $Arguments -PassThru
    try {
        if (-not $process.WaitForExit($Seconds * 1000)) {
            # A hung app must fail the job quickly instead of leaving a 6-hour build.
            try { $process.Kill($true) } catch { try { $process.Kill() } catch { } }
            Stop-DropWheelProcesses
            throw "$What did not finish within $Seconds s."
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

    # The installed copy is what users run. 2.2.0 shipped an installed build whose embedded string
    # tables were stripped by trimming; CI had only exercised a framework-dependent publish, so
    # every label became a raw key and nothing failed until the app was installed. Gate the exact
    # installed bytes here, before the shell-entry seeding and the uninstall.
    Write-Step 'Running the installed app: --self-test and --diagnose'
    $script:selfTestDir = Join-Path $tempRoot ('dropwheel-installed-selftest-' + [guid]::NewGuid().ToString('N'))
    $script:selfTestLog = Join-Path $selfTestDir 'self-test.txt'
    $selfTestExit = Invoke-AppProcess -FilePath $installedExe `
        -Arguments @('--self-test', ('"{0}"' -f $selfTestDir)) `
        -What 'The installed --self-test' -Seconds 900
    Assert-That ($selfTestExit -eq 0) "the installed --self-test exited with 0 (got $selfTestExit)"
    Assert-That (Test-Path -LiteralPath $selfTestLog) 'the installed --self-test wrote self-test.txt'

    $selfTestText = if (Test-Path -LiteralPath $selfTestLog) { Get-Content -LiteralPath $selfTestLog -Raw } else { '' }
    # -cmatch on purpose: -match is case-insensitive and would also match the benign
    # "skipped ocr (failed: ...)" line. Only the app's own uppercase FAILED: marker is fatal.
    Assert-That ($selfTestText -cnotmatch '(?m)^FAILED:') 'the installed --self-test reported no failed check'
    Assert-That ($selfTestText -notmatch 'raw localization key') 'the installed self-test rendered no raw localization key'
    Assert-That ($selfTestText -notmatch 'settings\.title') 'the installed self-test output contains no raw "settings.title" key'

    $keyCounts = @([regex]::Matches($selfTestText, 'localization:\s+(\d+) keys') | ForEach-Object { [int] $_.Groups[1].Value })
    Assert-That ($keyCounts.Count -ge 1) 'the installed self-test reported its Loc key count'
    Assert-That ($keyCounts.Count -ge 4) "the installed self-test checked all four language/theme passes ($($keyCounts.Count) reported)"
    foreach ($keyCount in $keyCounts) {
        Assert-That ($keyCount -ge 300) "the installed app loaded $keyCount localized keys (minimum 300)"
    }

    $script:diagnosePath = Join-Path $tempRoot 'dropwheel-installed-diagnose.txt'
    Remove-Item -LiteralPath $diagnosePath -Force -ErrorAction SilentlyContinue
    $diagnoseExit = Invoke-AppProcess -FilePath $installedExe `
        -Arguments @('--diagnose', ('"{0}"' -f $diagnosePath)) `
        -What 'The installed --diagnose' -Seconds 180
    Assert-That ($diagnoseExit -eq 0) "the installed --diagnose exited with 0 (got $diagnoseExit)"
    Assert-That (Test-Path -LiteralPath $diagnosePath) 'the installed --diagnose wrote its report'

    $diagnoseText = if (Test-Path -LiteralPath $diagnosePath) { Get-Content -LiteralPath $diagnosePath -Raw } else { '' }
    $localeMatch = [regex]::Match($diagnoseText, 'localization: English=(\d+) keys, Arabic=(\d+) keys')
    Assert-That ($localeMatch.Success) 'the installed --diagnose reported both Loc table counts'
    if ($localeMatch.Success) {
        $englishKeys = [int] $localeMatch.Groups[1].Value
        $arabicKeys = [int] $localeMatch.Groups[2].Value
        Assert-That ($englishKeys -ge 300) "the installed English table has $englishKeys keys (minimum 300)"
        Assert-That ($arabicKeys -ge 300) "the installed Arabic table has $arabicKeys keys (minimum 300)"
    }

    $activeMatch = [regex]::Match($diagnoseText, 'strings:\s+(\d+) keys')
    Assert-That ($activeMatch.Success) 'the installed --diagnose reported the active Loc key count'
    if ($activeMatch.Success) {
        $activeKeys = [int] $activeMatch.Groups[1].Value
        Assert-That ($activeKeys -ge 300) "the installed app has $activeKeys active localized keys (minimum 300)"
    }

    Assert-That ($diagnoseText -notmatch 'localization:.*ERROR') 'the installed --diagnose reports no localization error'
    Assert-That ($diagnoseText -notmatch 'settings\.title="settings\.title"') 'the installed --diagnose did not resolve settings.title to its raw key'
    # Any quoted dotted identifier is a key that reached the output instead of its translation.
    Assert-That ($diagnoseText -notmatch '="[a-z][a-z0-9]*(\.[a-z0-9]+)+"') 'the installed --diagnose shows no raw dotted key as a value'

    Write-Host ("  self-test key counts: {0}" -f ($keyCounts -join ', '))
    if ($localeMatch.Success) {
        Write-Host ("  diagnose: English={0}, Arabic={1}, active={2}" -f `
            $localeMatch.Groups[1].Value, $localeMatch.Groups[2].Value, $activeMatch.Groups[1].Value)
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
    if ($selfTestDir -and (Test-Path -LiteralPath $selfTestDir)) {
        Remove-Item -LiteralPath $selfTestDir -Recurse -Force -ErrorAction SilentlyContinue
    }
    if ($diagnosePath -and (Test-Path -LiteralPath $diagnosePath)) {
        Remove-Item -LiteralPath $diagnosePath -Force -ErrorAction SilentlyContinue
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
    if ($selfTestLog -and (Test-Path -LiteralPath $selfTestLog)) {
        Write-Host ''
        Write-Host '--- last 40 lines of the installed self-test ---'
        Get-Content -LiteralPath $selfTestLog -Tail 40 | ForEach-Object { Write-Host $_ }
    }
    if ($diagnosePath -and (Test-Path -LiteralPath $diagnosePath)) {
        Write-Host ''
        Write-Host '--- installed diagnose report (first 40 lines) ---'
        Get-Content -LiteralPath $diagnosePath -Head 40 | ForEach-Object { Write-Host $_ }
    }
} finally {
    Invoke-Cleanup
}

if ($failed) { exit 1 }
exit 0
