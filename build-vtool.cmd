@echo off
rem Double-click to build V-TOOL.exe and run the integration checks.
rem Everything the compiler and the checks print is saved to build.log next to this file.
setlocal
cd /d "%~dp0"
set "LOG=%~dp0build.log"
echo Build started %DATE% %TIME% > "%LOG%"
rem A running V-TOOL.exe cannot be overwritten, but Windows lets it be renamed: keep it as V-TOOL.exe.bak.
rem (Not an .exe name, so the previous build is never opened by mistake.)
if exist V-TOOL.exe.bak del /q V-TOOL.exe.bak >> "%LOG%" 2>&1
if exist V-TOOL.exe if not exist V-TOOL.exe.bak move /y V-TOOL.exe V-TOOL.exe.bak >> "%LOG%" 2>&1
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" -Test >> "%LOG%" 2>&1
set "RESULT=%ERRORLEVEL%"
rem Never leave the folder without a V-TOOL.exe when the build did not produce one.
if not exist V-TOOL.exe if exist V-TOOL.exe.bak copy /y V-TOOL.exe.bak V-TOOL.exe >> "%LOG%" 2>&1
echo EXIT=%RESULT% >> "%LOG%"
exit /b %RESULT%
