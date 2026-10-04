@echo off
rem Starts the whole Hakutaku stack in Docker: server, database, admin UI and wiki.
rem Double-click it, or run it from a terminal. stop.bat stops it again.
setlocal
cd /d "%~dp0"

set "URL=http://localhost:8090"

where docker >nul 2>&1
if errorlevel 1 (
    echo Docker is not installed.
    echo Install Docker Desktop from https://www.docker.com/products/docker-desktop/
    echo then run this again.
    goto :fail
)

docker info >nul 2>&1
if not errorlevel 1 goto :docker_ready

rem Installed but not running, so open Docker Desktop ourselves. It installs
rem either for all users or for the current user only, so look in both places.
set "DOCKER_DESKTOP="
if exist "%ProgramFiles%\Docker\Docker\Docker Desktop.exe" set "DOCKER_DESKTOP=%ProgramFiles%\Docker\Docker\Docker Desktop.exe"
if exist "%LOCALAPPDATA%\Programs\DockerDesktop\Docker Desktop.exe" set "DOCKER_DESKTOP=%LOCALAPPDATA%\Programs\DockerDesktop\Docker Desktop.exe"
if not defined DOCKER_DESKTOP (
    echo Docker is not running. Open Docker Desktop, wait until it says it is running,
    echo then run this again.
    goto :fail
)
echo Starting Docker Desktop. This can take a minute...
start "" "%DOCKER_DESKTOP%"
set /a TRIES=0
:wait_docker
docker info >nul 2>&1
if not errorlevel 1 goto :docker_ready
set /a TRIES+=1
if %TRIES% geq 90 (
    echo Docker Desktop did not start within 3 minutes. Open it yourself, wait until
    echo it says it is running, then run this again.
    goto :fail
)
rem A 2 second sleep. ping rather than timeout, which fails instantly when the
rem script is run with its input redirected.
ping -n 3 127.0.0.1 >nul
goto :wait_docker

:docker_ready
echo Building and starting Hakutaku. The first time takes a few minutes...
docker compose -f compose.dev.yaml up -d --build
if errorlevel 1 (
    echo.
    echo Docker could not start Hakutaku, see the error above. If it mentions a port
    echo that is already allocated, another program is using it: see the readme.
    goto :fail
)

echo Waiting for the server to answer...
set /a TRIES=0
:wait_app
curl -sf %URL%/Health >nul 2>&1
if not errorlevel 1 goto :app_ready
set /a TRIES+=1
if %TRIES% geq 60 (
    echo The server did not come up within 2 minutes. Its last log lines:
    docker compose -f compose.dev.yaml logs --tail 50 app
    goto :fail
)
ping -n 3 127.0.0.1 >nul
goto :wait_app

:app_ready
echo.
echo   Hakutaku is running.
echo.
echo     Admin UI:  %URL%
echo     Username:  admin
echo     Password:  hakutaku
echo     Wiki:      http://localhost:5020
echo.
echo   To stop it, double-click stop.bat.
echo.
start "" "%URL%"
pause
exit /b 0

:fail
echo.
pause
exit /b 1
