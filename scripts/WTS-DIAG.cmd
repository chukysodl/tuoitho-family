@echo off
setlocal
cd /d "%~dp0.."

echo Building WTS diagnostic...
dotnet build tools\TuoiTho.WtsDiagnostic\TuoiTho.WtsDiagnostic.csproj --configuration Release --nologo
if errorlevel 1 goto :failed

echo.
echo Running WTS diagnostic for this interactive Windows session...
dotnet run --project tools\TuoiTho.WtsDiagnostic\TuoiTho.WtsDiagnostic.csproj --configuration Release --no-build
set "RC=%ERRORLEVEL%"
echo.
if not "%RC%"=="0" (
  echo WTS DIAGNOSTIC REPORTED FAILURE. See values above.
) else (
  echo WTS DIAGNOSTIC PASS.
)
pause
exit /b %RC%

:failed
echo.
echo WTS DIAGNOSTIC BUILD FAILED.
pause
exit /b 1