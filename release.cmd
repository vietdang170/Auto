@echo off
rem Prepares a new release: asks for the version, builds and tests Auto, creates release\vX.Y.Z.
cd /d "%~dp0"
set /p VERSION=Nhap so phien ban moi (vi du 1.1.0): 
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0release.ps1" -Version "%VERSION%"
echo.
if errorlevel 1 (echo Chua tao duoc ban phat hanh, xem loi o tren.) else (start "" "%~dp0release\v%VERSION%")
pause
