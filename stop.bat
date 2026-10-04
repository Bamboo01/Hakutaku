@echo off
rem Stops the Hakutaku stack that start.bat started. The database is kept.
setlocal
cd /d "%~dp0"

docker compose -f compose.dev.yaml down
if errorlevel 1 (
    echo.
    echo Could not stop Hakutaku. Is Docker Desktop running?
    pause
    exit /b 1
)
echo.
echo Hakutaku is stopped. Your data is kept for next time.
pause
