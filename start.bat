@echo off
setlocal
cd /d "%~dp0"

echo ==========================================================
echo   CPM Hardware-Check (Next.js + C# LibreHardwareMonitor)
echo ==========================================================

echo [1/2] Baue Next.js Frontend...
cd frontend
call npm.cmd run build
if %ERRORLEVEL% neq 0 (
    echo [FEHLER] Frontend-Build fehlgeschlagen!
    pause
    exit /b %ERRORLEVEL%
)

echo.
echo [2/2] Starte C# ASP.NET Core Server...
echo       -^> Web-Dashboard: http://localhost:8085
echo       -^> LHM API Baum:  http://localhost:8085/data.json
cd ..\backend
dotnet run

