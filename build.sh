#!/usr/bin/env bash
# Build webclient (only if webclient/src changed) + .NET engine
# Mirrors webclient/README.md: npm run build / python deploy.py package + dotnet build
# Engine requires webclient — hash of webclient/src decides if vite rebuild needed
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
WEBCLIENT_DIR="$SCRIPT_DIR/webclient"
DEST_WWWROOT="$SCRIPT_DIR/src/Atheriz.Server/wwwroot"
DEST_WEB_TMPL="$SCRIPT_DIR/src/Atheriz.Server/web/templates"
SRC_HASH_FILE="$DEST_WWWROOT/.webclient-hash"
FORCE=0

usage() {
  echo "Usage: $0 [--force] [--help]"
  echo "  --force  force rebuild of webclient even if unchanged"
  echo "  --help   show this help"
  exit 0
}

for arg in "$@"; do
  case "$arg" in
    --force|-f) FORCE=1 ;;
    --help|-h) usage ;;
    *) echo "unknown arg: $arg" >&2; usage ;;
  esac
done

if [ ! -f "$WEBCLIENT_DIR/package.json" ]; then
  echo "error: webclient/package.json not found at $WEBCLIENT_DIR" >&2
  echo "hint: ensure webclient/ is vendored (rsync -a ../atheriz/webclient/ ./webclient/ --exclude node_modules --exclude dist)" >&2
  exit 1
fi

if ! command -v node >/dev/null 2>&1; then
  echo "error: node >=18 required (see webclient/package.json engines)" >&2
  exit 1
fi
if ! command -v npm >/dev/null 2>&1; then
  echo "error: npm required" >&2
  exit 1
fi
if ! command -v dotnet >/dev/null 2>&1; then
  echo "error: dotnet 8.0.130+ required (see global.json)" >&2
  exit 1
fi

# --- hash webclient/src + config (yes to webclient/src per request) ---
compute_src_hash() {
  # Hash all files under webclient/src plus vite/package config — sorted, like vite.config.ts webclientHash
  # Excludes gfonts (output, not input) and dist/node_modules
  find "$WEBCLIENT_DIR/src" "$WEBCLIENT_DIR/vite.config.ts" "$WEBCLIENT_DIR/package.json" "$WEBCLIENT_DIR/package-lock.json" "$WEBCLIENT_DIR/tsconfig.json" \
    -type f -print0 2>/dev/null | sort -z | xargs -0 sha256sum 2>/dev/null | sha256sum | cut -d' ' -f1
}

SRC_HASH=""
if [ -d "$WEBCLIENT_DIR/src" ]; then
  SRC_HASH=$(compute_src_hash)
else
  echo "error: $WEBCLIENT_DIR/src not found" >&2
  exit 1
fi

if [ -z "$SRC_HASH" ]; then
  echo "error: failed to compute webclient/src hash" >&2
  exit 1
fi

NEED_WEB_BUILD=1
# Proper glob handling — [ -f "webclient-"*.js ] does not expand inside test; use nullglob
shopt -s nullglob
_www_assets=( "$DEST_WWWROOT"/assets/webclient-*.js )
_has_assets=0
if [ ${#_www_assets[@]} -gt 0 ]; then _has_assets=1; fi
shopt -u nullglob
if [ "$FORCE" -eq 0 ] && [ -f "$SRC_HASH_FILE" ] && [ "$_has_assets" -eq 1 ] && [ -f "$DEST_WWWROOT/webclient/index.html" ] && [ -f "$DEST_WWWROOT/atheriz_draw/index.html" ]; then
  if [ "$(cat "$SRC_HASH_FILE" 2>/dev/null)" = "$SRC_HASH" ]; then
    NEED_WEB_BUILD=0
  fi
fi
# Force rebuild if any required output is missing (hash file, html, or any webclient asset)
if [ ! -f "$SRC_HASH_FILE" ] || [ ! -f "$DEST_WWWROOT/webclient/index.html" ] || [ ! -f "$DEST_WWWROOT/atheriz_draw/index.html" ] || [ "$_has_assets" -eq 0 ]; then
  NEED_WEB_BUILD=1
fi
if [ "$FORCE" -eq 1 ]; then
  NEED_WEB_BUILD=1
fi

if [ "$NEED_WEB_BUILD" -eq 0 ]; then
  echo "Webclient unchanged ($SRC_HASH) — skipping vite build"
else
  echo "Webclient changed ($SRC_HASH) — rebuilding..."
  echo "  webclient/src hash: $SRC_HASH"
  if [ "$FORCE" -eq 1 ]; then echo "  (forced rebuild)"; fi
  # npm ci (fast if lock unchanged) then vite build
  (cd "$WEBCLIENT_DIR" && npm ci --silent || npm install)
  (cd "$WEBCLIENT_DIR" && npm run build)

  SRC_DIST="$WEBCLIENT_DIR/dist"
  if [ ! -d "$SRC_DIST" ]; then
    echo "error: vite build did not produce $SRC_DIST" >&2
    exit 1
  fi

  mkdir -p "$DEST_WWWROOT" "$DEST_WEB_TMPL"
  # Clean old built assets — must remove entire assets dir so stale hashed webclient-*.js do not accumulate
  # (previous buggy glob test left multiple hashes; rm -rf ensures single current hash)
  rm -rf "$DEST_WWWROOT/assets" "$DEST_WWWROOT/atheriz_draw" "$DEST_WWWROOT/webclient"
  # Remove nested artifacts from prior bad rsync (wwwroot/assets/assets, wwwroot/webclient/webclient)
  rm -rf "$DEST_WWWROOT/assets/assets" "$DEST_WWWROOT/webclient/webclient"
  # Do NOT rm gfonts/chafa on every build — they are large and vite may not emit gfonts every time (dist/gfonts only if public/gfonts present)
  # Clean gfonts/chafa only if dist contains them
  if [ -d "$SRC_DIST/gfonts" ]; then rm -rf "$DEST_WWWROOT/gfonts"; fi
  if [ -f "$SRC_DIST/chafa.wasm" ] || ls "$SRC_DIST/assets/chafa-"*.wasm >/dev/null 2>&1; then rm -f "$DEST_WWWROOT/chafa.wasm"; fi

  # Stage dist → wwwroot (mirrors webclient/deploy.py:87-127)
  rsync -a "$SRC_DIST/assets/" "$DEST_WWWROOT/assets/"
  # Defensive: remove accidental nested copy if it appeared
  rm -rf "$DEST_WWWROOT/assets/assets"
  # fonts from webclient/fonts (Fira_Custom etc.)
  if [ -d "$WEBCLIENT_DIR/fonts" ]; then
    rsync -a "$WEBCLIENT_DIR/fonts/" "$DEST_WWWROOT/fonts/"
  fi
  mkdir -p "$DEST_WWWROOT/webclient"
  cp -f "$SRC_DIST/webclient/index.html" "$DEST_WWWROOT/webclient/index.html"
  mkdir -p "$DEST_WWWROOT/atheriz_draw"
  cp -f "$SRC_DIST/index.html" "$DEST_WWWROOT/atheriz_draw/index.html"
  if [ -f "$SRC_DIST/chafa.wasm" ]; then
    cp -f "$SRC_DIST/chafa.wasm" "$DEST_WWWROOT/chafa.wasm"
  elif ls "$SRC_DIST/assets/chafa-"*.wasm >/dev/null 2>&1; then
    cp -f "$SRC_DIST/assets/chafa-"*.wasm "$DEST_WWWROOT/chafa.wasm" 2>/dev/null || true
    # keep hashed copy in assets as well (vite already did)
  fi
  if [ -d "$SRC_DIST/gfonts" ]; then
    rsync -a "$SRC_DIST/gfonts/" "$DEST_WWWROOT/gfonts/"
  fi

  # Record hash so next run can skip
  echo "$SRC_HASH" > "$SRC_HASH_FILE"
  echo "Webclient deployed to $DEST_WWWROOT ($(du -sh "$DEST_WWWROOT" 2>/dev/null | cut -f1))"
  # Sync .webclient-revision.json hash for banner (optional, vite already writes it)
  if [ -f "$WEBCLIENT_DIR/.webclient-revision.json" ]; then
    echo "  revision: $(cat "$WEBCLIENT_DIR/.webclient-revision.json" 2>/dev/null | head -c 120)"
  fi
fi

# Always build .NET (cheap, ~3s)
echo "Building .NET..."
dotnet build "$SCRIPT_DIR/Atheriz.sln" -c Release

echo "Build complete — webclient $([ "$NEED_WEB_BUILD" -eq 0 ] && echo "unchanged" || echo "rebuilt") + engine built"
echo "Run ./atheriz.sh --help  (or atheriz.cmd on Windows)"
