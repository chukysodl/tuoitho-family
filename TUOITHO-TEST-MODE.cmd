@echo off
setlocal
cd /d "%~dp0"
title Quan ly thoi gian - Bat che do thu nghiem

fltmc >nul 2>&1
if errorlevel 1 (
  echo Dang xin quyen Administrator...
  powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
  exit /b
)

if not exist "C:\ProgramData\TuoiTho" mkdir "C:\ProgramData\TuoiTho"
> "C:\ProgramData\TuoiTho\run-mode.txt" echo TEST

echo ==========================================
echo      QUAN LY THOI GIAN - CHE DO THU NGHIEM
echo ==========================================
echo Da luu che do TEST. Dang khoi dong lai Quan ly thoi gian...
call "%~dp0TUOITHO-START.cmd"
exit /b %ERRORLEVEL%
