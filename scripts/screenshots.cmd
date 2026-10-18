@echo off
rem R-5 — renders the README screenshots from the published app.
rem Run this from a normal desktop session (WPF renders offscreen, but it needs an interactive
rem window station): it writes docs\screenshots\en, docs\screenshots\en-light and docs\screenshots\ar.
setlocal
for %%i in ("%~dp0..") do set "ROOT=%%~fi"
set "EXE=%ROOT%\release\DropWheel\DropWheel.exe"
set "OUT=%ROOT%\docs\screenshots"

if not exist "%EXE%" (
    echo [X] "%EXE%" not found.
    echo     Build it first:  release.cmd
    exit /b 2
)

echo Rendering screenshots into "%OUT%" ...
"%EXE%" --self-test "%OUT%"
if errorlevel 1 (
    echo [X] --self-test failed. See "%OUT%\self-test.txt".
    exit /b 3
)

set "COUNT=0"
for /r "%OUT%" %%F in (*.png) do set /a COUNT+=1
if %COUNT% LSS 5 (
    echo [X] Only %COUNT% PNGs were written - the self-test did not render.
    echo     Check "%OUT%\self-test.txt" and whether a desktop session is available.
    exit /b 4
)

echo [OK] %COUNT% PNGs in "%OUT%" (en, en-light, ar)
echo      Check docs\screenshots\self-test.txt for the per-window log.
exit /b 0
