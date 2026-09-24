@echo off
REM Port of atheriz = "atheriz.atheriz:main" — C# Windows analogue
REM Mirrors atheriz/atheriz.py:1559 CLI: start|stop|restart|reload|reset|create|new|test
REM Usage: atheriz.cmd [--help] [start|new|create|...]
REM Engine requires webclient — run build.cmd first on fresh clone
setlocal EnableDelayedExpansion

set "SCRIPT_DIR=%~dp0"
REM strip trailing backslash
if "%SCRIPT_DIR:~-1%"=="\" set "SCRIPT_DIR=%SCRIPT_DIR:~0,-1%"
set "PROJECT_ROOT=%SCRIPT_DIR%"
set "SERVER_PROJ=%PROJECT_ROOT%\src\Atheriz.Server\Atheriz.Server.csproj"
  set "SERVER_DLL_DEBUG=%PROJECT_ROOT%\src\Atheriz.Server\bin\Debug\net10.0\Atheriz.Server.dll"
  set "SERVER_DLL_RELEASE=%PROJECT_ROOT%\src\Atheriz.Server\bin\Release\net10.0\Atheriz.Server.dll"
set "PUBLISH_DLL=%PROJECT_ROOT%\publish\Atheriz.Server.dll"

where dotnet >nul 2>nul
if %errorlevel% neq 0 (
  echo error: dotnet 10.0.100+ required (see global.json) 1>&2
  exit /b 1
)

if "%~1"=="" (
  if exist "%SERVER_DLL_RELEASE%" (
    dotnet "%SERVER_DLL_RELEASE%" --help
    exit /b %errorlevel%
  )
  if exist "%SERVER_DLL_DEBUG%" (
    dotnet "%SERVER_DLL_DEBUG%" --help
    exit /b %errorlevel%
  )
  dotnet run --project "%SERVER_PROJ%" -- --help
  exit /b %errorlevel%
)

REM Prefer DLL to preserve CWD (game folder)
if exist "%SERVER_DLL_RELEASE%" goto :dispatch
if exist "%SERVER_DLL_DEBUG%" goto :dispatch
if exist "%PUBLISH_DLL%" goto :dispatch
goto :no_dll

:dispatch
REM --- webclient staleness check (warnings only, never blocks startup) ---
REM L1: webclient/src newer than staged server copy means run build.cmd
REM L2: this game's staged copy (CWD) differs from server copy means run deploy.py
REM Entry HTML files embed hashed asset names, so any rebuild changes them.
set "WEBCLIENT_DIR=%PROJECT_ROOT%\webclient"
set "WWWROOT=%PROJECT_ROOT%\src\Atheriz.Server\wwwroot"
set "SRC_HASH_FILE=%WWWROOT%\.webclient-hash"
set "DO_WEB_CHECK="
if /i "%~1"=="start" set "DO_WEB_CHECK=game"
if /i "%~1"=="restart" set "DO_WEB_CHECK=game"
if /i "%~1"=="reload" set "DO_WEB_CHECK=game"
if /i "%~1"=="new" set "DO_WEB_CHECK=src"
if /i "%~1"=="create" set "DO_WEB_CHECK=src"
if not defined DO_WEB_CHECK goto :run_dll
REM L1: same hash inputs as build.cmd
set "SRC_HASH="
for /f "delims=" %%h in ('powershell -NoProfile -Command "$hash=''; $files=@(Get-ChildItem -Recurse -File '%WEBCLIENT_DIR%\src', '%WEBCLIENT_DIR%\vite.config.ts', '%WEBCLIENT_DIR%\package.json' -ErrorAction SilentlyContinue | Sort-Object FullName); $sha=[System.Security.Cryptography.SHA256]::Create(); foreach($f in $files){ $bytes=[System.IO.File]::ReadAllBytes($f.FullName); $null=$sha.TransformBlock($bytes,0,$bytes.Length,$null,$null)}; $sha.TransformFinalBlock([byte[]]::new(0),0,0) | Out-Null; [System.BitConverter]::ToString($sha.Hash).Replace('-','').ToLower()"') do set "SRC_HASH=%%h"
if "%SRC_HASH%"=="" goto :web_l1_done
set "STORED_HASH="
if exist "%SRC_HASH_FILE%" set /p STORED_HASH=<"%SRC_HASH_FILE%"
if "%STORED_HASH%"=="" goto :web_l1_stale
if "%STORED_HASH%"=="%SRC_HASH%" goto :web_l1_done
REM tolerate legacy hash files with trailing space (build.cmd used to echo one)
if "%STORED_HASH%"=="%SRC_HASH% " goto :web_l1_done
:web_l1_stale
echo WARNING: webclient sources are newer than the staged server copy. 1>&2
echo   Rebuild with: %PROJECT_ROOT%\build.cmd 1>&2
:web_l1_done
if "%DO_WEB_CHECK%"=="src" goto :run_dll
set "GAME_ASSETS_STALE=0"
call :check_staged_entry "web\static\atheriz_draw\index.html" "%WWWROOT%\atheriz_draw\index.html" draw
call :check_staged_entry "web\static\webclient\index.html" "%WWWROOT%\webclient\index.html" webclient
if "%GAME_ASSETS_STALE%"=="1" echo   Refresh this game's copy with: python "%WEBCLIENT_DIR%\deploy.py" game --web-root "%CD%\web" 1>&2
goto :run_dll

:check_staged_entry
if not exist "%~2" exit /b 0
if not exist "%~1" (
  if not exist "web" exit /b 0
  echo WARNING: this game has no staged %~3 entry (%~1 missing). 1>&2
  set "GAME_ASSETS_STALE=1"
  exit /b 0
)
fc /b "%~1" "%~2" >nul 2>nul
if errorlevel 1 (
  echo WARNING: this game's staged %~3 differs from the server copy. 1>&2
  set "GAME_ASSETS_STALE=1"
)
exit /b 0

:run_dll
if exist "%SERVER_DLL_RELEASE%" (
  dotnet "%SERVER_DLL_RELEASE%" %*
  exit /b %errorlevel%
)
if exist "%SERVER_DLL_DEBUG%" (
  dotnet "%SERVER_DLL_DEBUG%" %*
  exit /b %errorlevel%
)
if exist "%PUBLISH_DLL%" (
  dotnet "%PUBLISH_DLL%" %*
  exit /b %errorlevel%
)

:no_dll
echo note: no built Atheriz.Server.dll found — building via dotnet run --project (will be slower) 1>&2
echo hint: run build.cmd to pre-build webclient + engine 1>&2
dotnet run --project "%SERVER_PROJ%" -- %*
exit /b %errorlevel%
