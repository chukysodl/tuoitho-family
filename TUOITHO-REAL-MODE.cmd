@echo off
setlocal
cd /d "%~dp0"
title Tuoi Tho - Bat che do that

fltmc >nul 2>&1
if errorlevel 1 (
  echo Dang xin quyen Administrator...
  powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
  exit /b
)

echo ==========================================
echo      TUOI THO - CHUYEN SANG CHE DO THAT
echo ==========================================
echo.
echo CANH BAO: Che do nay co the khoa/ngat phien Windows cua tai khoan tre.
echo Chi tiep tuc khi ban dang co quyen truy cap may va da test Remote ONLINE.
echo.
choice /C YN /N /M "Bat CHE DO THAT? [Y/N]: "
if errorlevel 2 exit /b 1

if not exist "C:\ProgramData\TuoiTho" mkdir "C:\ProgramData\TuoiTho"
> "C:\ProgramData\TuoiTho\run-mode.txt" echo REAL

echo.
echo Da luu che do REAL. Dang khoi dong lai Tuoi Tho...
call "%~dp0TUOITHO-START.cmd"
exit /b %ERRORLEVEL%
