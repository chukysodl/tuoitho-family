@echo off
setlocal
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0M5-REMOTE-CHECK.ps1"
set "RC=%ERRORLEVEL%"
echo.
pause
exit /b %RC%
