#!/usr/bin/env bash
# Port of atheriz = "atheriz.atheriz:main" (pyproject.toml:99) — C# analogue
# Mirrors atheriz/atheriz.py:1559 CLI: start|stop|restart|reload|reset|create|new|test
# Usage: ./atheriz.sh [--help] [start|new|create|...]
# Engine requires webclient — run ./build.sh first on fresh clone
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$SCRIPT_DIR"
SERVER_PROJ="$PROJECT_ROOT/src/Atheriz.Server/Atheriz.Server.csproj"
SERVER_DLL_DEBUG="$PROJECT_ROOT/src/Atheriz.Server/bin/Debug/net8.0/Atheriz.Server.dll"
SERVER_DLL_RELEASE="$PROJECT_ROOT/src/Atheriz.Server/bin/Release/net8.0/Atheriz.Server.dll"
PUBLISH_DIR="$PROJECT_ROOT/publish"
PUBLISH_DLL="$PUBLISH_DIR/Atheriz.Server.dll"

if ! command -v dotnet >/dev/null 2>&1; then
  echo "error: dotnet SDK 8.0.100+ required (see global.json, dotnet --version)" >&2
  exit 1
fi

# Prefer built DLL to preserve CWD (game folder) — dotnet run --project changes CWD to project dir
# See README.md Game-folder commands note
have_dll() {
  for dll in "$SERVER_DLL_RELEASE" "$SERVER_DLL_DEBUG" "$PUBLISH_DLL"; do
    if [ -f "$dll" ]; then
      return 0
    fi
  done
  return 1
}

run_via_dll() {
  for dll in "$SERVER_DLL_RELEASE" "$SERVER_DLL_DEBUG" "$PUBLISH_DLL"; do
    if [ -f "$dll" ]; then
      exec dotnet "$dll" "$@"
    fi
  done
  return 1
}

run_via_project() {
  exec dotnet run --project "$SERVER_PROJ" -- "$@"
}

if [ $# -eq 0 ]; then
  # No args → help (mirrors atheriz --help)
  if have_dll; then
    # DLL exists: run it directly so its stderr surfaces (no silent fallback)
    run_via_dll --help
    exit $?
  fi
  run_via_project --help
  exit $?
fi

# A built DLL exists → run it directly so server stderr surfaces.
# Fall back to dotnet-run only when nothing is built yet.
if have_dll; then
  run_via_dll "$@"
  exit $?
fi
echo "note: no built Atheriz.Server.dll found — building via dotnet run --project (will be slower)" >&2
echo "hint: run ./build.sh to pre-build webclient + engine" >&2
run_via_project "$@"
