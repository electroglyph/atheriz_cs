#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
AUDIT="$ROOT/strong_audit"
PY="$AUDIT/traces/py"
CS="$AUDIT/traces/cs"

echo "== strong_audit: build grotto =="
dotnet build "$ROOT/grotto/grotto.csproj" -c Release --no-incremental

echo "== strong_audit: ensure venv =="
if [ ! -d "$AUDIT/.venv" ]; then
  python3 -m venv "$AUDIT/.venv"
  "$AUDIT/.venv/bin/pip" install -q -r "$AUDIT/py_runner/requirements.txt" || true
fi
# also need grotto python deps inside venv if not installed; try pip install from atheriz if requirements exists
if [ -f /home/anon/atheriz/requirements.txt ]; then
  "$AUDIT/.venv/bin/pip" install -q -r /home/anon/atheriz/requirements.txt 2>&1 | tail -5 || true
fi

echo "== strong_audit: build cs_runner =="
dotnet build "$AUDIT/cs_runner/StrongAudit.Runner" -c Release

echo "== strong_audit: run py_runner =="
"$AUDIT/.venv/bin/python" "$AUDIT/py_runner/runner.py" --scenarios "$AUDIT/scenarios" --out "$PY" --seed 42

echo "== strong_audit: run cs_runner =="
dotnet run --project "$AUDIT/cs_runner/StrongAudit.Runner" -c Release -- --scenarios "$AUDIT/scenarios" --out "$CS" --seed 42

echo "== strong_audit: compare =="
python3 "$AUDIT/compare.py" --py "$PY" --cs "$CS" --scenarios "$AUDIT/scenarios" --strict ${ALLOW_KNOWN:+--allow-known-incompatible}

echo "strong_audit PASS"
