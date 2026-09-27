@echo off
setlocal
cd /d "%~dp0"
title Tuoi Tho - Khoi dong mot cham

rem Self-elevate once so stale TuoiTho processes can be stopped safely after reboot/power loss.
fltmc >nul 2>&1
if errorlevel 1 (
  echo Dang xin quyen Administrator...
  powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
  exit /b
)

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

echo [0/3] Dang don runtime cu...
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -Command ^
  "$svc=Get-CimInstance Win32_Service -Filter \"Name='TuoiTho.Service'\" -ErrorAction SilentlyContinue; if($svc -and $svc.State -eq 'Running'){ if($svc.PathName -match '(?i)TuoiTho\.Service\.exe'){ Stop-Service -Name 'TuoiTho.Service' -Force -ErrorAction Stop; (Get-Service 'TuoiTho.Service').WaitForStatus('Stopped',[TimeSpan]::FromSeconds(20)) } else { throw 'Registered TuoiTho.Service points to an unexpected executable.' } }"
if errorlevel 1 (
  echo.
  echo [LOI] Khong dung duoc Windows Service Tuoi Tho cu.
  pause
  exit /b 2
)
call "%~dp0scripts\M1-STOP.cmd"
if errorlevel 1 (
  echo.
  echo [LOI] Khong dung duoc runtime Tuoi Tho cu.
  pause
  exit /b 2
)
timeout /t 2 /nobreak >nul

echo [1/3] Dang khoi dong Tuoi Tho...
call "%~dp0scripts\M1-START.cmd"
if errorlevel 1 (
  echo.
  echo [LOI] Khong khoi dong duoc Tuoi Tho.
  pause
  exit /b 3
)

echo.
echo [2/3] Dang cho dich vu san sang...
timeout /t 8 /nobreak >nul

echo [3/3] Dang kiem tra truoc khi ghep noi...
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0M5-REMOTE-CHECK.ps1" -PrePair
set "RC=%ERRORLEVEL%"

echo.
if "%RC%"=="0" (
  echo ==========================================
  echo   TUOI THO REMOTE: SAN SANG GHEP NOI
  echo ==========================================
  echo Trong cua so Tuoi Tho, mo tab DIEU KHIEN TU XA
  echo va bam TAO MA GHEP NOI.
) else (
  echo ==========================================
  echo   TUOI THO REMOTE: CHUA SAN SANG
  echo ==========================================
  echo Hay chup man hinh nay gui de kiem tra.
)

echo.
pause
exit /b %RC%
