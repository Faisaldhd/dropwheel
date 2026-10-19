@echo off
rem Builds the patched DropWheel (Windows x64, self-contained) with the locally downloaded .NET 10 SDK.
rem The app carries the .NET runtime itself, so no runtime install is needed on the target PC.
rem CI is the supported packager; this script is a local developer shortcut only.
setlocal
set "ROOT=%~dp0"
rem Local SDK lives outside the repo, in DropWheel_1\_local\sdk (never committed).
set "SDK=%ROOT%..\_local\sdk\dotnet-sdk"
set "ZIP=%ROOT%..\_local\sdk\dotnet-sdk.zip"
if not exist "%ZIP%" set "ZIP=%ROOT%..\tools\dotnet-sdk.zip"

if not exist "%SDK%\dotnet.exe" (
    if not exist "%ZIP%" (
        echo [X] dotnet-sdk.zip is missing.
        echo     Download: https://dotnetcli.blob.core.windows.net/dotnet/Sdk/10.0.402/dotnet-sdk-10.0.402-win-x64.zip
        echo     Save it as: "%ROOT%..\_local\sdk\dotnet-sdk.zip"
        exit /b 2
    )
    echo Extracting the SDK ^(one time^)...
    powershell -NoProfile -ExecutionPolicy Bypass -Command "Expand-Archive -LiteralPath '%ZIP%' -DestinationPath '%SDK%' -Force"
    if not exist "%SDK%\dotnet.exe" (
        echo [X] Extraction failed.
        exit /b 3
    )
)

set "PATH=%SDK%;%PATH%"
"%SDK%\dotnet.exe" --version
if errorlevel 1 exit /b 4

echo Building ^(self-contained, win-x64^)...
"%SDK%\dotnet.exe" publish "%ROOT%src\FileAction.App\FileAction.App.csproj" -c Release -r win-x64 --self-contained true -o "%ROOT%publish"
if errorlevel 1 (
    echo [X] Build failed.
    exit /b 5
)
if not exist "%ROOT%publish\DropWheel.exe" (
    echo [X] Build failed: publish did not produce DropWheel.exe.
    exit /b 6
)
echo [OK] %ROOT%publish\DropWheel.exe
echo      Self-contained: no .NET install is needed on the target PC.
exit /b 0

rem Release (self-contained publish + install + zip + setup):  release.cmd
rem Installer only (CI is the supported path):  scripts\build-installer.ps1
