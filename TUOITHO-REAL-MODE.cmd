@echo off
setlocal
cd /d "%~dp0"
title Quan ly thoi gian - Bat che do that

fltmc >nul 2>&1
if errorlevel 1 (
  echo Dang xin quyen Administrator...
  powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
  exit /b
)

echo ==========================================
echo      QUAN LY THOI GIAN - CHUYEN SANG CHE DO THAT
echo ==========================================
echo.
echo CANH BAO: Che do nay se khoa bang giao dien Quan ly thoi gian toan man hinh.
echo Khong logout/ngat phien Windows. Chi tiep tuc khi da test Remote ONLINE.
echo.
choice /C YN /N /M "Bat CHE DO THAT? [Y/N]: "
if errorlevel 2 exit /b 1

if not exist "C:\ProgramData\TuoiTho" mkdir "C:\ProgramData\TuoiTho"
> "C:\ProgramData\TuoiTho\run-mode.txt" echo REAL

echo.
echo Da luu che do REAL. Dang khoi dong lai Quan ly thoi gian...
call "%~dp0TUOITHO-START.cmd"
if errorlevel 1 (
  echo.
  echo [LOI] Quan ly thoi gian khoi dong lai khong thanh cong.
  pause
  exit /b 2
)

echo.
echo Dang xac minh TestMode da TAT...
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0M5-REMOTE-CHECK.ps1" -ExpectRealMode
if errorlevel 1 (
  echo.
  echo [LOI] CHUA CHUYEN DUOC SANG CHE DO THAT.
  echo Hay chup man hinh nay gui de kiem tra.
  pause
  exit /b 3
)

echo.
echo ==========================================
echo   CHE DO THAT: DA BAT THANH CONG
echo ==========================================
echo Dien thoai se cap nhat trong khoang 15-30 giay.
pause
exit /b 0
