#!/usr/bin/env bash
# Port of atheriz = "atheriz.atheriz:main" (pyproject.toml:99) — C# analogue
# Mirrors atheriz/atheriz.py:1559 CLI: start|stop|restart|reload|reset|create|new|test
# Usage: ./atheriz.sh [--help] [start|new|create|...]
# Engine requires webclient — run ./build.sh first on fresh clone
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$SCRIPT_DIR"
SERVER_PROJ="$PROJECT_ROOT/src/Atheriz.Server/Atheriz.Server.csproj"
SERVER_DLL_DEBUG="$PROJECT_ROOT/src/Atheriz.Server/bin/Debug/net10.0/Atheriz.Server.dll"
SERVER_DLL_RELEASE="$PROJECT_ROOT/src/Atheriz.Server/bin/Release/net10.0/Atheriz.Server.dll"
PUBLISH_DIR="$PROJECT_ROOT/publish"
PUBLISH_DLL="$PUBLISH_DIR/Atheriz.Server.dll"

if ! command -v dotnet >/dev/null 2>&1; then
  echo "error: dotnet SDK 10.0.100+ required (see global.json, dotnet --version)" >&2
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

# --- webclient staleness handling ---
# Two levels, mirroring the build/deploy pipeline:
#   L1: webclient/src newer than the staged server copy (wwwroot).
#       Serve commands (start/restart/reload) warn only and never block
#       startup; scaffold commands (new/create) rebuild first via
#       ./build.sh, since scaffolding would otherwise bake the stale
#       stage into the new game folder.
#   L2: this game's staged copy (CWD/web/static) differs from wwwroot
#       → dotnet <server.dll> deploy game --web-root "<game>/web"
# L2 compares the entry HTML files: they embed the hashed asset names, so any
# rebuild changes them. Only runs for serve/scaffold commands.
WEBCLIENT_DIR="$PROJECT_ROOT/webclient"
WWWROOT="$PROJECT_ROOT/src/Atheriz.Server/wwwroot"
SRC_HASH_FILE="$WWWROOT/.webclient-hash"

web_src_stale() {
  # 0 (stale) when sources differ from the last staged build. Compares
  # the same content hash build.sh records (not mtimes, which copies and
  # checkouts can fake), so the two can never disagree about staleness.
  [ -d "$WEBCLIENT_DIR/src" ] || return 1
  [ -f "$SRC_HASH_FILE" ] || return 0
  [ -f "$WWWROOT/atheriz_draw/index.html" ] || return 0
  local current stored
  current=$(find "$WEBCLIENT_DIR/src" "$WEBCLIENT_DIR/vite.config.ts" "$WEBCLIENT_DIR/package.json" \
    "$WEBCLIENT_DIR/package-lock.json" "$WEBCLIENT_DIR/tsconfig.json" \
    -type f -print0 2>/dev/null | sort -z | xargs -0 sha256sum 2>/dev/null | sha256sum | cut -d' ' -f1)
  stored=$(tr -d '[:space:]' <"$SRC_HASH_FILE")
  # An unreadable hash is stale (rebuild, the safe direction); the legacy
  # trailing-space hash files compare clean after whitespace stripping.
  [ -z "$current" ] || [ "$current" != "$stored" ]
}

check_staged_entry() {
  # $1 = game entry html, $2 = wwwroot reference html, $3 = label
  # 0 (stale/missing) when the game copy needs refreshing.
  local game="$1" ref="$2"
  [ -f "$ref" ] || return 1
  if [ ! -f "$game" ]; then
    [ -d "./web" ] || return 1
    echo "WARNING: this game has no staged $3 entry ($game missing)." >&2
    return 0
  fi
  if command -v cmp >/dev/null 2>&1 && ! cmp -s "$game" "$ref"; then
    echo "WARNING: this game's staged $3 differs from the server copy." >&2
    return 0
  fi
  return 1
}

check_webclient_sync() {
  # $1 = "game" to also compare this game's staged copy (CWD); omit for
  # other serve commands. (new/create never call this — they rebuild when
  # stale instead of warning, and the game does not exist yet, so CWD may
  # incidentally be another game folder that must never be judged.)
  if web_src_stale; then
    echo "WARNING: webclient sources are newer than the staged server copy." >&2
    echo "  Rebuild with: $PROJECT_ROOT/build.sh  (build.cmd on Windows)" >&2
  fi
  if [ "${1:-}" = "game" ]; then
    local game_assets_stale=0
    if check_staged_entry "./web/static/atheriz_draw/index.html" "$WWWROOT/atheriz_draw/index.html" "draw"; then
      game_assets_stale=1
    fi
    if check_staged_entry "./web/static/webclient/index.html" "$WWWROOT/webclient/index.html" "webclient"; then
      game_assets_stale=1
    fi
    if [ "$game_assets_stale" -eq 1 ]; then
      # Print a concrete runnable command: first built DLL wins, same
      # Release → Debug → publish order run_via_dll executes.
      for _dll in "$SERVER_DLL_RELEASE" "$SERVER_DLL_DEBUG" "$PUBLISH_DLL"; do
        if [ -f "$_dll" ]; then
          echo "  Refresh this game's copy with: dotnet \"$_dll\" deploy game --web-root \"$(pwd)/web\"" >&2
          break
        fi
      done
    fi
  fi
  return 0
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
case "${1:-}" in
  start|restart|reload)
    check_webclient_sync game
    ;;
  new|create)
    # Scaffolding copies wwwroot into the new game folder: a stale stage
    # would bake old assets into the game, so rebuild first instead of
    # warning. A failed rebuild aborts (set -e) rather than scaffolding
    # from a stale copy.
    if web_src_stale; then
      echo "webclient sources are newer than the staged server copy — rebuilding first..." >&2
      "$PROJECT_ROOT/build.sh"
    fi
    ;;
esac
if have_dll; then
  run_via_dll "$@"
  exit $?
fi
echo "note: no built Atheriz.Server.dll found — building via dotnet run --project (will be slower)" >&2
echo "hint: run ./build.sh to pre-build webclient + engine" >&2
run_via_project "$@"
