@echo off
setlocal
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0M5-REMOTE-SETUP.ps1"
set "RC=%ERRORLEVEL%"
if not "%RC%"=="0" (
  echo.
  echo M5 remote setup did not complete. Follow the repair message above.
  pause
)
exit /b %RC%
