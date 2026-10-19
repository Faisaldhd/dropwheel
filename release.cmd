@echo off
rem Local convenience wrapper. CI (.github/workflows/ci.yml) remains the only supported packager:
rem it builds the self-contained publish, the setup, the portable zip and the signatures on a clean runner.
rem
rem For a local developer build, run the same script CI uses:
rem   powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\build-installer.ps1"
rem It needs the .NET 10 SDK and Inno Setup 6.3+ on PATH and asserts the setup is at most 70 MB.
setlocal
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\build-installer.ps1"
exit /b %ERRORLEVEL%
