@echo off
setlocal EnableExtensions
cd /d "%~dp0"
title CREMS Windows Installer and Launcher

echo.
echo ============================================================
echo   CREMS Windows Installer and Launcher
echo ============================================================
echo.
echo This installer prepares a local CREMS evaluation environment.
echo It installs missing .NET and Node.js dependencies with WinGet,
echo then configures the database and starts CREMS.
echo.

if not exist "src\CREMS.Api\CREMS.Api.csproj" goto :wrong_folder
if not exist "src\crems-web\package.json" goto :wrong_folder
if not exist "scripts\start-windows.ps1" goto :wrong_folder

set "PATH=%PATH%;%ProgramFiles%\dotnet;%ProgramFiles%\nodejs;%ProgramFiles%\Docker\Docker\resources\bin;%LOCALAPPDATA%\Programs\Docker\Docker\resources\bin"

echo [1/4] Checking the .NET 10 SDK...
dotnet --list-sdks 2>nul | findstr /R /B "10\." >nul
if not errorlevel 1 goto :dotnet_ready
echo Installing the .NET 10 SDK. Windows may request administrator approval...
call :require_winget
winget install --id Microsoft.DotNet.SDK.10 --exact --source winget --accept-source-agreements --accept-package-agreements
if errorlevel 1 goto :install_failed
set "PATH=%PATH%;%ProgramFiles%\dotnet"
dotnet --list-sdks 2>nul | findstr /R /B "10\." >nul
if errorlevel 1 goto :restart_required
:dotnet_ready
for /f "delims=" %%V in ('dotnet --version 2^>nul') do set "DOTNET_VERSION=%%V"
echo       Ready: .NET %DOTNET_VERSION%

echo [2/4] Checking Node.js and npm...
powershell -NoProfile -Command "$c = Get-Command node -ErrorAction SilentlyContinue; if (-not $c) { exit 1 }; $v = [int]((& node --version).TrimStart('v').Split('.')[0]); if ($v -lt 22) { exit 1 }"
if not errorlevel 1 goto :node_ready
echo Installing the current Node.js LTS release...
call :require_winget
winget install --id OpenJS.NodeJS.LTS --exact --source winget --accept-source-agreements --accept-package-agreements
if errorlevel 1 goto :install_failed
set "PATH=%PATH%;%ProgramFiles%\nodejs"
powershell -NoProfile -Command "$c = Get-Command node -ErrorAction SilentlyContinue; if (-not $c) { exit 1 }; $v = [int]((& node --version).TrimStart('v').Split('.')[0]); if ($v -lt 22) { exit 1 }"
if errorlevel 1 goto :restart_required
:node_ready
for /f "delims=" %%V in ('node --version 2^>nul') do set "NODE_VERSION=%%V"
echo       Ready: Node.js %NODE_VERSION%

echo [3/4] Selecting the database...
if exist ".env.windows.json" goto :existing_settings
echo.
echo CREMS can use either:
echo   [D] Docker Desktop with the supplied SQL Server container
echo   [E] An existing SQL Server or SQL Server Express instance
echo.
choice /C DE /N /M "Choose D or E: "
if errorlevel 2 goto :use_existing_sql

set "CREMS_DATABASE_MODE=docker"
where docker.exe >nul 2>nul
if not errorlevel 1 goto :docker_ready
echo.
echo Docker Desktop is not installed. Installing it now...
echo Docker's licence terms and your organization's software policy apply.
call :require_winget
winget install --id Docker.DockerDesktop --exact --source winget --accept-source-agreements --accept-package-agreements
if errorlevel 1 goto :install_failed
set "PATH=%PATH%;%ProgramFiles%\Docker\Docker\resources\bin;%LOCALAPPDATA%\Programs\Docker\Docker\resources\bin"
where docker.exe >nul 2>nul
if errorlevel 1 goto :restart_required
:docker_ready
echo       Ready: Docker database option selected.
goto :start_crems_first_time

:use_existing_sql
set "CREMS_DATABASE_MODE=existing"
echo       Ready: existing SQL Server option selected. Docker is not required.
goto :start_crems_first_time

:existing_settings
echo       Reusing the private settings already configured on this laptop.

:start_crems
echo [4/4] Restoring, compiling, checking and starting CREMS...
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File ".\scripts\start-windows.ps1"
set "CREMS_EXIT=%ERRORLEVEL%"
if not "%CREMS_EXIT%"=="0" goto :launcher_failed
exit /b 0

:start_crems_first_time
echo [4/4] Collecting private settings, restoring, compiling and starting CREMS...
echo Passwords are hidden while you type and are never committed to Git.
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File ".\scripts\start-windows.ps1" -Setup -DatabaseMode "%CREMS_DATABASE_MODE%"
set "CREMS_EXIT=%ERRORLEVEL%"
if not "%CREMS_EXIT%"=="0" goto :launcher_failed
exit /b 0

:wrong_folder
echo ERROR: This file is not in the CREMS repository root.
echo Keep INSTALL_CREMS.bat beside README.md, compose.yaml, src and scripts.
goto :failed

:require_winget
where winget.exe >nul 2>nul
if errorlevel 1 goto :no_winget
exit /b 0

:no_winget
echo ERROR: Windows Package Manager (WinGet) was not found.
echo Install or update "App Installer" from Microsoft Store, then run this file again.
goto :failed

:install_failed
echo.
echo ERROR: A prerequisite could not be installed.
echo Review the message above, confirm internet access and administrator approval,
echo then run INSTALL_CREMS.bat again.
goto :failed

:restart_required
echo.
echo A prerequisite was installed but is not available in this terminal yet.
echo Restart Windows, then run INSTALL_CREMS.bat again.
goto :failed

:launcher_failed
echo.
echo CREMS stopped with an error. Read the message above for the exact cause.
echo Correct it, then run INSTALL_CREMS.bat again; completed downloads are reused.
goto :failed

:failed
echo.
pause
exit /b 1
