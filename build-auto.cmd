@echo off
rem Double-click to build Auto.exe and run the integration checks.
rem Everything the compiler and the checks print is saved to build.log next to this file.
setlocal
cd /d "%~dp0"
set "LOG=%~dp0build.log"
echo Build started %DATE% %TIME% > "%LOG%"
rem A running Auto.exe cannot be overwritten, but Windows lets it be renamed: keep it as Auto.exe.bak.
rem (Not an .exe name, so the previous build is never opened by mistake.)
if exist Auto.exe.bak del /q Auto.exe.bak >> "%LOG%" 2>&1
if exist Auto.exe if not exist Auto.exe.bak move /y Auto.exe Auto.exe.bak >> "%LOG%" 2>&1
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" -Test >> "%LOG%" 2>&1
set "RESULT=%ERRORLEVEL%"
rem Never leave the folder without an Auto.exe when the build did not produce one.
if not exist Auto.exe if exist Auto.exe.bak copy /y Auto.exe.bak Auto.exe >> "%LOG%" 2>&1
echo EXIT=%RESULT% >> "%LOG%"
exit /b %RESULT%
