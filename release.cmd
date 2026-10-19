@echo off
rem Builds the final DropWheel app into release\DropWheel (framework-dependent), zips it and builds
rem the installer from installer\DropWheel.iss. The installer copies from ..\release\DropWheel and adds
rem the .NET 10 Desktop Runtime when the target PC does not have it.
setlocal
set "ROOT=%~dp0"
rem Local SDK lives outside the repo, in DropWheel_1\_local\sdk (never committed).
set "SDK=%ROOT%..\_local\sdk\dotnet-sdk"
set "OUT=%ROOT%release\DropWheel"
set "ZIP=%ROOT%release\DropWheel-win-x64.zip"
rem Keep the real profile folder: it is where a per-user Inno Setup lives.
set "REAL_LOCALAPPDATA=%LOCALAPPDATA%"

set "DOTNET_CLI_HOME=%ROOT%.dotnet-cli"
set "NUGET_PACKAGES=%ROOT%.nuget"
set "DOTNET_NOLOGO=1"
set "DOTNET_CLI_TELEMETRY_OPTOUT=1"
set "APPDATA=%ROOT%.appdata\Roaming"
set "LOCALAPPDATA=%ROOT%.appdata\Local"
set "TMP=%ROOT%.tmp"
set "TEMP=%ROOT%.tmp"
if not exist "%TMP%" mkdir "%TMP%"

if not exist "%SDK%\dotnet.exe" (
    echo [X] SDK missing: "%SDK%\dotnet.exe"
    exit /b 2
)
if exist "%OUT%" rmdir /s /q "%OUT%"

"%SDK%\dotnet.exe" publish "%ROOT%src\FileAction.App\FileAction.App.csproj" ^
    -c Release -r win-x64 --self-contained false -o "%OUT%" --artifacts-path "%ROOT%artifacts\release"
if errorlevel 1 (
    echo [X] publish failed
    exit /b 3
)
if not exist "%OUT%\DropWheel.exe" (
    echo [X] publish failed: "%OUT%\DropWheel.exe" was not produced
    exit /b 3
)

rem Framework-dependent publish: the .NET runtime is installed by the setup, not shipped here.
rem DropWheel.exe is built with DebugType=none, so normally there is nothing to prune; drop stray
rem symbols only, so they never end up in the zip or in the setup.
for %%F in ("%OUT%\*.pdb") do if exist "%%~F" del /q "%%~F"

if exist "%ZIP%" del /q "%ZIP%"
powershell -NoProfile -ExecutionPolicy Bypass -Command "Compress-Archive -Path '%OUT%\*' -DestinationPath '%ZIP%' -Force"
if errorlevel 1 (
    echo [X] zip failed
    exit /b 4
)

set "APPVERSION=2.0.0"
set "ISCC="
for %%P in ("%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" "%ProgramFiles%\Inno Setup 6\ISCC.exe" "%REAL_LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe") do (
    if not defined ISCC if exist %%P set "ISCC=%%~P"
)
if defined ISCC (
    echo Building installer...
    "%ISCC%" /DAppVersion=%APPVERSION% "%ROOT%installer\DropWheel.iss"
    if errorlevel 1 (
        echo [!] installer failed
        exit /b 5
    )
    echo [OK] %ROOT%..\release\DropWheel-Setup-%APPVERSION%.exe
) else (
    echo [i] Inno Setup ISCC.exe not found - skipped DropWheel-Setup-%APPVERSION%.exe
)

echo [OK] %OUT%\DropWheel.exe
echo [OK] %ZIP%
exit /b 0
