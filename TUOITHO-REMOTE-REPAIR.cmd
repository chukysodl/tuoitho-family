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

echo [1/5] Dang dung runtime cu...
call "%~dp0scripts\M1-STOP.cmd"
sc.exe stop "TuoiTho.Service" >nul 2>&1
timeout /t 2 /nobreak >nul

echo [2/5] Dang build ban sua moi nhat...
dotnet build "%~dp0TuoiTho.sln" --configuration Release
if errorlevel 1 (
  echo [LOI] Build that bai.
  pause
  exit /b 3
)

where npx.cmd >nul 2>&1
if errorlevel 1 (
  echo [LOI] Khong tim thay npx.cmd / Node.js.
  pause
  exit /b 4
)

echo [3/5] Dang cap nhat device-gateway tren Supabase...
pushd "%~dp0infra\supabase"
call npx.cmd --yes supabase@latest functions deploy device-gateway --project-ref "%PROJECT_REF%"
set "RC=%ERRORLEVEL%"
popd
if not "%RC%"=="0" (
  echo [LOI] Khong deploy duoc device-gateway.
  pause
  exit /b %RC%
)

echo [4/5] Dang thu migrate credential cu bang SYSTEM...
set "MIGRATION_SERVICE=TuoiTho.RemoteMigration"
set "SERVICE_EXE=%~dp0src\TuoiTho.Service\bin\Release\net10.0-windows\TuoiTho.Service.exe"
sc.exe stop "%MIGRATION_SERVICE%" >nul 2>&1
sc.exe delete "%MIGRATION_SERVICE%" >nul 2>&1
sc.exe create "%MIGRATION_SERVICE%" binPath= "\"%SERVICE_EXE%\"" start= demand obj= LocalSystem >nul
if errorlevel 1 (
  echo [CANH BAO] Khong tao duoc migration service. Se thu tiep bang tai khoan hien tai.
) else (
  sc.exe start "%MIGRATION_SERVICE%" >nul 2>&1
  timeout /t 8 /nobreak >nul
  sc.exe stop "%MIGRATION_SERVICE%" >nul 2>&1
  timeout /t 2 /nobreak >nul
  sc.exe delete "%MIGRATION_SERVICE%" >nul 2>&1
)

echo [5/5] Dang khoi dong lai Tuoi Tho va kiem tra...
call "%~dp0TUOITHO-START.cmd"
exit /b %ERRORLEVEL%
