@echo off
setlocal
cd /d "%~dp0"
title Tuoi Tho - Khoi dong mot cham

echo ==========================================
echo      TUOI THO - KHOI DONG MOT CHAM
echo ==========================================
echo.

if not exist "C:\ProgramData\TuoiTho\RemoteControl\remote-control.json" (
  echo [CAN CAU HINH] Chua co cau hinh dieu khien tu xa.
  echo Hay chay M5-REMOTE-SETUP.cmd mot lan truoc.
  echo.
  pause
  exit /b 2
)

echo [1/3] Dang khoi dong Tuoi Tho...
call "%~dp0scripts\M1-START.cmd"
if errorlevel 1 (
  echo.
  echo [LOI] Khong khoi dong duoc Tuoi Tho.
  pause
  exit /b 3
)

echo.
echo [2/3] Dang cho dich vu ket noi Supabase...
timeout /t 20 /nobreak >nul

echo [3/3] Dang kiem tra dieu khien tu xa...
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0M5-REMOTE-CHECK.ps1"
set "RC=%ERRORLEVEL%"

echo.
if "%RC%"=="0" (
  echo ==========================================
  echo   TUOI THO REMOTE: READY
  echo ==========================================
  echo Mo bang dieu khien tren dien thoai...
  start "" "https://chukysodl.github.io/tuoitho-family/"
) else (
  echo ==========================================
  echo   TUOI THO REMOTE: CHUA SAN SANG
  echo ==========================================
  echo Hay chup man hinh nay gui de kiem tra.
)

echo.
pause
exit /b %RC%
