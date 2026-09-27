@echo off
setlocal EnableExtensions
cd /d "%~dp0"
title Tuoi Tho - Sua Remote mot lan

fltmc >nul 2>&1
if errorlevel 1 (
  echo Dang xin quyen Administrator...
  powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
  exit /b
)

echo ==========================================
echo      TUOI THO - SUA REMOTE MOT LAN
echo ==========================================
echo.

set "PROJECT_REF_FILE=%~dp0infra\supabase\.temp\project-ref"
if not exist "%PROJECT_REF_FILE%" (
  echo [LOI] Chua tim thay Supabase Project Ref da link.
  echo Hay chay M5-REMOTE-SETUP.cmd mot lan.
  echo.
  pause
  exit /b 2
)

set /p PROJECT_REF=<"%PROJECT_REF_FILE%"
if "%PROJECT_REF%"=="" (
  echo [LOI] Project Ref rong.
  pause
  exit /b 2
)

where npx.cmd >nul 2>&1
if errorlevel 1 (
  echo [LOI] Khong tim thay npx.cmd / Node.js.
  pause
  exit /b 3
)

echo [1/2] Dang cap nhat device-gateway tren Supabase...
pushd "%~dp0infra\supabase"
call npx.cmd --yes supabase@latest functions deploy device-gateway --project-ref "%PROJECT_REF%"
set "RC=%ERRORLEVEL%"
popd
if not "%RC%"=="0" (
  echo.
  echo [LOI] Khong deploy duoc device-gateway.
  echo Neu Supabase CLI yeu cau dang nhap, chay: npx.cmd supabase login
  pause
  exit /b %RC%
)

echo.
echo [2/3] Dang migrate danh tinh thiet bi...
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -Command ^
  "$svc=Get-Service -Name 'TuoiTho.Service' -ErrorAction SilentlyContinue; if($svc){ if($svc.Status -eq 'Running'){Stop-Service 'TuoiTho.Service' -Force}; Start-Service 'TuoiTho.Service'; Start-Sleep -Seconds 8; Stop-Service 'TuoiTho.Service' -Force; exit 0 } else { exit 0 }"
if errorlevel 1 (
  echo [CANH BAO] Khong migrate qua Windows Service; se thu bang runtime hien tai.
)

echo.
echo [3/3] Dang khoi dong lai Tuoi Tho...
call "%~dp0TUOITHO-START.cmd"
exit /b %ERRORLEVEL%
