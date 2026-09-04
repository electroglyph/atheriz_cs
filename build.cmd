@echo off
REM Build webclient (only if webclient/src changed) + .NET engine
REM Mirrors build.sh incremental logic — standalone, webclient required
setlocal EnableDelayedExpansion

set "SCRIPT_DIR=%~dp0"
if "%SCRIPT_DIR:~-1%"=="\" set "SCRIPT_DIR=%SCRIPT_DIR:~0,-1%"
set "WEBCLIENT_DIR=%SCRIPT_DIR%\webclient"
set "DEST_WWWROOT=%SCRIPT_DIR%\src\Atheriz.Server\wwwroot"
set "SRC_HASH_FILE=%DEST_WWWROOT%\.webclient-hash"
set "FORCE=0"

for %%a in (%*) do (
  if "%%a"=="--force" set "FORCE=1"
  if "%%a"=="-f" set "FORCE=1"
  if "%%a"=="--help" goto :usage
  if "%%a"=="-h" goto :usage
)
goto :main

:usage
echo Usage: %~nx0 [--force] [--help]
echo   --force  force rebuild of webclient even if unchanged
echo   --help   show this help
exit /b 0

:main
if not exist "%WEBCLIENT_DIR%\package.json" (
  echo error: webclient\package.json not found at %WEBCLIENT_DIR% 1>&2
  exit /b 1
)
where node >nul 2>nul
if %errorlevel% neq 0 (
  echo error: node ^>=18 required 1>&2
  exit /b 1
)
where npm >nul 2>nul
if %errorlevel% neq 0 (
  echo error: npm required 1>&2
  exit /b 1
)
where dotnet >nul 2>nul
if %errorlevel% neq 0 (
  echo error: dotnet 8.0.130+ required 1>&2
  exit /b 1
)

REM --- compute hash of webclient/src + config ---
set "SRC_HASH="
for /f "delims=" %%h in ('powershell -NoProfile -Command "$hash=''; $files=@(Get-ChildItem -Recurse -File '%WEBCLIENT_DIR%\src', '%WEBCLIENT_DIR%\vite.config.ts', '%WEBCLIENT_DIR%\package.json' -ErrorAction SilentlyContinue | Sort-Object FullName); $sha=[System.Security.Cryptography.SHA256]::Create(); foreach($f in $files){ $bytes=[System.IO.File]::ReadAllBytes($f.FullName); $null=$sha.TransformBlock($bytes,0,$bytes.Length,$null,$null)}; $sha.TransformFinalBlock([byte[]]::new(0),0,0) | Out-Null; [System.BitConverter]::ToString($sha.Hash).Replace('-','').ToLower()"') do set "SRC_HASH=%%h"

if "%SRC_HASH%"=="" (
  echo error: failed to compute webclient/src hash 1>&2
  exit /b 1
)

set "NEED_WEB_BUILD=1"
if "%FORCE%"=="0" if exist "%SRC_HASH_FILE%" (
  set /p STORED_HASH=<"%SRC_HASH_FILE%"
  if "!STORED_HASH!"=="%SRC_HASH%" if exist "%DEST_WWWROOT%\webclient\index.html" if exist "%DEST_WWWROOT%\atheriz_draw\index.html" (
    REM check hashed asset exists
    dir /b "%DEST_WWWROOT%\assets\webclient-*.js" >nul 2>nul
    if !errorlevel! equ 0 set "NEED_WEB_BUILD=0"
  )
)
if "%FORCE%"=="1" set "NEED_WEB_BUILD=1"
if not exist "%SRC_HASH_FILE%" set "NEED_WEB_BUILD=1"
if not exist "%DEST_WWWROOT%\webclient\index.html" set "NEED_WEB_BUILD=1"
dir /b "%DEST_WWWROOT%\assets\webclient-*.js" >nul 2>nul
if %errorlevel% neq 0 set "NEED_WEB_BUILD=1"

if "%NEED_WEB_BUILD%"=="0" (
  echo Webclient unchanged (%SRC_HASH%) — skipping vite build
) else (
  echo Webclient changed (%SRC_HASH%) — rebuilding...
  pushd "%WEBCLIENT_DIR%"
  call npm ci --silent
  if !errorlevel! neq 0 call npm install
  call npm run build
  if !errorlevel! neq 0 (
    echo error: vite build failed 1>&2
    popd
    exit /b 1
  )
  popd

  set "SRC_DIST=%WEBCLIENT_DIR%\dist"
  if not exist "%SRC_DIST%" (
    echo error: vite build did not produce %SRC_DIST% 1>&2
    exit /b 1
  )
  if not exist "%DEST_WWWROOT%" mkdir "%DEST_WWWROOT%"
  REM clean old
  if exist "%DEST_WWWROOT%\assets" rmdir /s /q "%DEST_WWWROOT%\assets%"
  if exist "%DEST_WWWROOT%\atheriz_draw" rmdir /s /q "%DEST_WWWROOT%\atheriz_draw"
  if exist "%SRC_DIST%\gfonts" if exist "%DEST_WWWROOT%\gfonts" rmdir /s /q "%DEST_WWWROOT%\gfonts%"
  if exist "%SRC_DIST%\chafa.wasm" del /q "%DEST_WWWROOT%\chafa.wasm" 2>nul

  xcopy /E /Y /I "%SRC_DIST%\assets" "%DEST_WWWROOT%\assets\" >nul
  if exist "%WEBCLIENT_DIR%\fonts" xcopy /E /Y /I "%WEBCLIENT_DIR%\fonts" "%DEST_WWWROOT%\fonts\" >nul
  if not exist "%DEST_WWWROOT%\webclient" mkdir "%DEST_WWWROOT%\webclient"
  copy /Y "%SRC_DIST%\webclient\index.html" "%DEST_WWWROOT%\webclient\index.html" >nul
  if not exist "%DEST_WWWROOT%\atheriz_draw" mkdir "%DEST_WWWROOT%\atheriz_draw"
  copy /Y "%SRC_DIST%\index.html" "%DEST_WWWROOT%\atheriz_draw\index.html" >nul
  if exist "%SRC_DIST%\chafa.wasm" (
    copy /Y "%SRC_DIST%\chafa.wasm" "%DEST_WWWROOT%\chafa.wasm" >nul
  ) else (
    for %%f in ("%SRC_DIST%\assets\chafa-*.wasm") do copy /Y "%%f" "%DEST_WWWROOT%\chafa.wasm" >nul 2>nul
  )
  if exist "%SRC_DIST%\gfonts" xcopy /E /Y /I "%SRC_DIST%\gfonts" "%DEST_WWWROOT%\gfonts\" >nul

  echo %SRC_HASH% > "%SRC_HASH_FILE%"
  echo Webclient deployed to %DEST_WWWROOT%
)

echo Building .NET...
dotnet build "%SCRIPT_DIR%\Atheriz.sln" -c Release
if %errorlevel% neq 0 exit /b %errorlevel%

if "%NEED_WEB_BUILD%"=="0" (
  echo Build complete — webclient unchanged + engine built
) else (
  echo Build complete — webclient rebuilt + engine built
)
echo Run atheriz.cmd --help
exit /b 0
