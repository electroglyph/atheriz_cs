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
set "SERVER_DLL_DEBUG=%PROJECT_ROOT%\src\Atheriz.Server\bin\Debug\net8.0\Atheriz.Server.dll"
set "SERVER_DLL_RELEASE=%PROJECT_ROOT%\src\Atheriz.Server\bin\Release\net8.0\Atheriz.Server.dll"
set "PUBLISH_DLL=%PROJECT_ROOT%\publish\Atheriz.Server.dll"

where dotnet >nul 2>nul
if %errorlevel% neq 0 (
  echo error: dotnet 8.0.130+ required (see global.json) 1>&2
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

echo note: no built Atheriz.Server.dll found — building via dotnet run --project (will be slower) 1>&2
echo hint: run build.cmd to pre-build webclient + engine 1>&2
dotnet run --project "%SERVER_PROJ%" -- %*
exit /b %errorlevel%
