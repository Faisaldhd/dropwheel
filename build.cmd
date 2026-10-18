@echo off
rem Builds the patched DropWheel (Windows x64) with the locally downloaded .NET 10 SDK.
setlocal
set "ROOT=%~dp0"
set "SDK=%ROOT%tools\dotnet-sdk"
set "ZIP=%ROOT%tools\dotnet-sdk.zip"
if not exist "%ZIP%" set "ZIP=%ROOT%..\tools\dotnet-sdk.zip"

if not exist "%SDK%\dotnet.exe" (
    if not exist "%ZIP%" (
        echo [X] dotnet-sdk.zip is missing.
        echo     Download: https://dotnetcli.blob.core.windows.net/dotnet/Sdk/10.0.402/dotnet-sdk-10.0.402-win-x64.zip
        echo     Save it as: "%ROOT%tools\dotnet-sdk.zip"
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

echo Building...
"%SDK%\dotnet.exe" publish "%ROOT%src\FileAction.App\FileAction.App.csproj" -c Release -r win-x64 --self-contained -o "%ROOT%publish"
if errorlevel 1 (
    echo [X] Build failed.
    exit /b 5
)
echo [OK] %ROOT%publish\DropWheel.exe
exit /b 0

rem Installer: "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" /DAppVersion=2.0.0 installer\DropWheel.iss
