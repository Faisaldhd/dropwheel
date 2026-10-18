@echo off
rem Builds the final DropWheel app into ..\release\DropWheel and zips it.
setlocal
set "ROOT=%~dp0"
set "SDK=%ROOT%tools\dotnet-sdk"
set "OUT=%ROOT%..\release\DropWheel"
set "ZIP=%ROOT%..\release\DropWheel-win-x64.zip"

set "DOTNET_CLI_HOME=%ROOT%.dotnet-cli"
set "NUGET_PACKAGES=%ROOT%.nuget"
set "DOTNET_NOLOGO=1"
set "DOTNET_CLI_TELEMETRY_OPTOUT=1"
set "APPDATA=%ROOT%.appdata\Roaming"
set "LOCALAPPDATA=%ROOT%.appdata\Local"
set "TMP=%ROOT%.tmp"
set "TEMP=%ROOT%.tmp"

if not exist "%SDK%\dotnet.exe" (
    echo [X] SDK missing: "%SDK%\dotnet.exe"
    exit /b 2
)
if exist "%OUT%" rmdir /s /q "%OUT%"

"%SDK%\dotnet.exe" publish "%ROOT%src\FileAction.App\FileAction.App.csproj" ^
    -c Release -r win-x64 --self-contained -o "%OUT%" --no-restore --artifacts-path "%ROOT%artifacts\release"
if errorlevel 1 (
    echo [X] publish failed
    exit /b 3
)

if exist "%ZIP%" del /q "%ZIP%"
powershell -NoProfile -ExecutionPolicy Bypass -Command "Compress-Archive -Path '%OUT%\*' -DestinationPath '%ZIP%' -Force"
if errorlevel 1 (
    echo [X] zip failed
    exit /b 4
)


rem Drop the files the runtime does not need (kept out of every release).
for %%F in (createdump.exe Microsoft.DiaSymReader.Native.amd64.dll mscordaccore.dll) do (
    if exist "%OUT%\%%F" del /q "%OUT%\%%F"
)
for %%F in ("%OUT%\*.pdb") do if exist "%%~F" del /q "%%~F"

set "APPVERSION=2.0.0"
set "ISCC="
for %%P in ("%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" "%ProgramFiles%\Inno Setup 6\ISCC.exe" "%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe") do (
    if not defined ISCC if exist %%P set "ISCC=%%~P"
)
if defined ISCC (
    echo Building installer...
    "%ISCC%" /DAppVersion=%APPVERSION% "installer\DropWheel.iss"
    if errorlevel 1 echo [!] installer failed
) else (
    echo [i] Inno Setup ISCC.exe not found - skipped DropWheel-Setup-%APPVERSION%.exe
)

echo [OK] %OUT%\DropWheel.exe
echo [OK] %ZIP%
exit /b 0
