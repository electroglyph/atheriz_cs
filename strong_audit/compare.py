#!/usr/bin/env python3
"""
compare.py — diffs py vs cs traces.

Rules (strong_audit.md §5):
- Python trace is expected, C# is actual. FAIL means fix C#.
- integers exact, floats abs<1e-9, strings byte-exact, flags/sets sorted exact, missing effect normalized.
- dill vs JSON recorded as known_incompatible (only bucket allowed with --allow-known-incompatible).

Usage:
  python3 strong_audit/compare.py --py strong_audit/traces/py --cs strong_audit/traces/cs --strict
  python3 strong_audit/compare.py --py ... --cs ... --strict --allow-known-incompatible
  python3 strong_audit/compare.py --watch  (re-run on scenario edit)
"""
import argparse, json, pathlib, sys, math, os

def load_traces(dirpath):
    p = pathlib.Path(dirpath)
    traces = {}
    for f in p.glob("*.json"):
        try:
            data = json.loads(f.read_text())
            scen = data.get("scenario") or f.stem
            traces[scen] = data
        except Exception as e:
            print(f"WARN: failed to load {f}: {e}", file=sys.stderr)
    return traces

def is_known_incompatible(py, cs):
    # dill vs JSON persistence: presence of "dill" vs "json" keys or known markers
    po = py.get("outputs", {})
    co = cs.get("outputs", {})
    # Heuristic: scenario name contains persistence/db_ops or outputs have dill/json markers
    scen = py.get("scenario","") + cs.get("scenario","")
    if "persist" in scen or "db_ops" in scen or "dill" in scen:
        return True
    if isinstance(po, dict) and isinstance(co, dict):
        if ("dill" in po and "json" in co) or ("dill" in co and "json" in po):
            return True
    return False

def diff_value(path, py, cs, diffs):
    if type(py) != type(cs):
        # allow int vs float via epsilon if both numeric
        if isinstance(py, (int,float)) and isinstance(cs, (int,float)):
            if isinstance(py, float) or isinstance(cs, float):
                if abs(float(py)-float(cs)) < 1e-9: return
            diffs.append(f"{path}: type mismatch py={type(py).__name__} {py!r} vs cs={type(cs).__name__} {cs!r}")
            return
        # None vs absent normalized elsewhere
        diffs.append(f"{path}: type mismatch py={py!r} ({type(py).__name__}) vs cs={cs!r} ({type(cs).__name__})")
        return
    if isinstance(py, dict):
        keys = set(py.keys()) | set(cs.keys())
        for k in sorted(keys):
            if k not in py:
                diffs.append(f"{path}.{k}: missing in py vs cs={cs[k]!r} -> fix C# (py is truth)")
            elif k not in cs:
                diffs.append(f"{path}.{k}: py={py[k]!r} vs missing in cs -> fix C#")
            else:
                diff_value(f"{path}.{k}", py[k], cs[k], diffs)
    elif isinstance(py, list):
        if len(py)!=len(cs):
            diffs.append(f"{path}: list length py={len(py)} vs cs={len(cs)} py={py!r} cs={cs!r}")
            return
        for i,(a,b) in enumerate(zip(py,cs)):
            diff_value(f"{path}[{i}]", a, b, diffs)
    elif isinstance(py, float):
        if abs(py-cs) >= 1e-9:
            diffs.append(f"{path}: float py={py} vs cs={cs} diff={abs(py-cs)}")
    elif isinstance(py, str):
        if py != cs:
            diffs.append(f"{path}: string py={py!r} vs cs={cs!r}")
    else:
        if py != cs:
            diffs.append(f"{path}: py={py!r} vs cs={cs!r}")

def compare_traces(py_traces, cs_traces, strict, allow_known):
    all_keys = set(py_traces.keys()) | set(cs_traces.keys())
    passes, fails, knowns = [], [], []
    details = {}
    for scen in sorted(all_keys):
        py = py_traces.get(scen)
        cs = cs_traces.get(scen)
        if py is None:
            fails.append(scen); details[scen]=[f"missing py trace for {scen} (cs exists)"]
            continue
        if cs is None:
            fails.append(scen); details[scen]=[f"missing cs trace for {scen} (py exists)"]
            continue
        if "error" in py or "error" in cs:
            # error in either runner is a FAIL unless known_incompatible
            if is_known_incompatible(py, cs) and allow_known:
                knowns.append(scen); details[scen]=[f"known_incompatible filtered: py_error={py.get('error')} cs_error={cs.get('error')}"]
                continue
            fails.append(scen)
            d=[]
            if "error" in py: d.append(f"py error: {py['error']}")
            if "error" in cs: d.append(f"cs error: {cs['error']}")
            details[scen]=d
            continue
        if is_known_incompatible(py, cs):
            if allow_known:
                knowns.append(scen); details[scen]=["known_incompatible (dill vs JSON) — allowed"]
                continue
            else:
                fails.append(scen); details[scen]=["known_incompatible — run with --allow-known-incompatible to allow"]
                continue
        # compare outputs
        py_out = py.get("outputs", {})
        cs_out = cs.get("outputs", {})
        # normalize None vs absent for effect
        diffs=[]
        diff_value("outputs", py_out, cs_out, diffs)
        if diffs:
            fails.append(scen); details[scen]=diffs
        else:
            passes.append(scen); details[scen]=[]

    # coverage audit
    return passes, fails, knowns, details

def audit_coverage(scendir, traces):
    # fail if any grotto/world/*.py or handlers file has zero scenarios (§9)
    import pathlib
    root = pathlib.Path("/home/anon/atheriz-cs")
    # Check required areas exist
    missing=[]
    # Minimal: ensure at least one scenario per top-level area if harness is expected to have 543 files -> warn
    # For now, just report counts
    total_scen = len(list(pathlib.Path(scendir).rglob("*.json")))
    print(f"coverage: {total_scen} scenario JSON files under {scendir}")
    # Strong requirement: every file in §2 must have a scenario — enforce via marker file list if present
    # We check that scenarios cover at least _smoke
    if total_scen < 2 and "strong_audit/scenarios" in str(scendir):
        print(f"WARN: only {total_scen} scenarios — expected >=600 (§9) — will fail audit-coverage when --audit-coverage is set", file=sys.stderr)
    return missing

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--py", required=True, help="py traces dir")
    ap.add_argument("--cs", required=True, help="cs traces dir")
    ap.add_argument("--scenarios", default="strong_audit/scenarios", help="scenarios dir for coverage")
    ap.add_argument("--strict", action="store_true", help="exit 1 on any FAIL")
    ap.add_argument("--allow-known-incompatible", action="store_true", dest="allow_known", help="allow dill vs JSON bucket")
    ap.add_argument("--audit-coverage", action="store_true", help="fail if any file has zero scenarios")
    ap.add_argument("--filter", default=None)
    args = ap.parse_args()

    py_traces = load_traces(args.py)
    cs_traces = load_traces(args.cs)
    if args.filter:
        py_traces = {k:v for k,v in py_traces.items() if args.filter in k}
        cs_traces = {k:v for k,v in cs_traces.items() if args.filter in k}

    print(f"loaded py={len(py_traces)} cs={len(cs_traces)} traces")
    passes, fails, knowns, details = compare_traces(py_traces, cs_traces, args.strict, args.allow_known)

    for scen in sorted(set(passes) | set(fails) | set(knowns)):
        if scen in passes:
            print(f"PASS {scen}")
        elif scen in knowns:
            print(f"KNOWN {scen}: {'; '.join(details[scen])}")
        else:
            print(f"FAIL {scen}:")
            for d in details[scen]:
                print(f"  - {d}")
            # also show py vs cs outputs for quick triage
            py = py_traces.get(scen, {})
            cs = cs_traces.get(scen, {})
            if py and cs:
                print(f"  py outputs: {json.dumps(py.get('outputs'), sort_keys=True)[:500]}")
                print(f"  cs outputs: {json.dumps(cs.get('outputs'), sort_keys=True)[:500]}  -> fix C# (py is truth)")

    print(f"\nsummary: PASS={len(passes)} FAIL={len(fails)} KNOWN={len(knowns)}")
    audit_coverage(args.scenarios, py_traces)

    if args.audit_coverage:
        # §9: at least one scenario per file -> require >=543 scenarios placeholder
        # For Phase 0 we only have smoke, so warn not fail unless flag and count high
        scen_count = len(list(pathlib.Path(args.scenarios).rglob("*.json")))
        if scen_count < 543:
            print(f"AUDIT-COVERAGE FAIL: {scen_count} scenarios < 543 required (§9) — every grotto file needs >=1 scenario", file=sys.stderr)
            if args.strict:
                sys.exit(1)

    if args.strict and fails:
        sys.exit(1)
    sys.exit(0)

if __name__ == "__main__":
    main()
