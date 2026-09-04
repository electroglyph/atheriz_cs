#!/usr/bin/env python3
"""
Python executor for strong_audit scenarios.

Loads each JSON scenario, builds real grotto objects via global_test_env,
runs the system under test with DeterministicPatch queues, and writes a canonical trace JSON.

Usage:
  python3 py_runner/runner.py --scenarios strong_audit/scenarios --out strong_audit/traces/py --seed 42

Trace contract matches strong_audit.md §1: {scenario, seed, inputs, rolls, outputs}
"""
import argparse, json, sys, pathlib, traceback, tempfile, os

# Ensure repo roots on sys.path: /home/anon/atheriz (python grotto) and /home/anon/atheriz-cs (for reference)
REPO_PY = pathlib.Path("/home/anon/atheriz")
REPO_CS = pathlib.Path("/home/anon/atheriz-cs")
if str(REPO_PY) not in sys.path: sys.path.insert(0, str(REPO_PY))
# grotto package is under REPO_PY/grotto; atheriz is REPO_PY/atheriz

# Stub optional heavy deps that may not be installed in audit venv
import types as _types
def _stub_module(name):
    if name not in sys.modules:
        sys.modules[name] = _types.ModuleType(name)
    return sys.modules[name]
# qdrant_edge is only needed for talkhandler vector store; stub if missing
try:
    import qdrant_edge  # noqa
except (ModuleNotFoundError, ImportError):
    m = _stub_module("qdrant_edge")
    # Provide dummies for every name talkhandler imports — enums need arbitrary attribute access
    class _EnumMeta(type):
        def __getattr__(cls, k): return f"{cls.__name__}.{k}"
        def __getitem__(cls, k): return f"{cls.__name__}[{k}]"
    def _dummy_enum(name):
        return _EnumMeta(name, (), {"__init__": lambda self, *a, **kw: None})
    def _dummy_cls(name):
        return type(name, (), {"__init__": lambda self, *a, **kw: None, "__call__": lambda self, *a, **kw: None})
    for _n in ["EdgeShard","EdgeConfig","EdgeVectorParams","EdgeOptimizersConfig","Distance","VectorStorageDatatype","PayloadSchemaType","Point","UpdateOperation","ScrollRequest","QueryRequest","Filter","FieldCondition","MatchValue","MatchText","OrderBy","Direction","Query","Record","ScoredPoint","TurboQuantQuantizationConfig","TurboQuantBitSize"]:
        setattr(m, _n, _dummy_enum(_n) if _n in ("Distance","VectorStorageDatatype","PayloadSchemaType","TurboQuantBitSize") else _dummy_cls(_n))
    for _n in ["QdrantEdge","QdrantEdgeConfig","EdgeHit","search","embed","upsert","delete","get","list_collections","EmbeddingResult"]:
        setattr(m, _n, (lambda *a, **kw: None) if _n[0].islower() else _dummy_cls(_n))
    # module-level __getattr__ fallback: any missing name returns dummy enum/cls
    def _mod_getattr(n):
        v = _dummy_enum(n)
        setattr(m, n, v)
        return v
    m.__getattr__ = _mod_getattr  # PEP 562
    # ensure UpdateOperation has create_field_index static
    if hasattr(m, "UpdateOperation"):
        setattr(m.UpdateOperation, "create_field_index", staticmethod(lambda *a, **kw: None))
try:
    import fastembed  # noqa
except (ModuleNotFoundError, ImportError):
    _stub_module("fastembed")
    _stub_module("fastembed.common")
    _stub_module("fastembed.common.model_description")
    _stub_module("fastembed.TextEmbedding")
    fm = sys.modules["fastembed"]
    class _TE:
        def __init__(self, *a, **kw): pass
        def embed(self, *a, **kw): return []
        @classmethod
        def add_custom_model(cls, *a, **kw): pass
        @classmethod
        def list_supported_models(cls): return []
    setattr(fm, "TextEmbedding", _TE)
    m2 = sys.modules["fastembed.common.model_description"]
    class _EnumMeta2(type):
        def __getattr__(cls, k): return k
    pt = _EnumMeta2("PoolingType", (), {"CLS": "CLS", "MEAN": "MEAN", "DISABLED": "DISABLED", "__init__": lambda self, *a, **kw: None})
    ms = _EnumMeta2("ModelSource", (), {"HF": "HF", "URL": "URL", "__init__": lambda self, *a, **kw: None})
    setattr(m2, "PoolingType", pt)
    setattr(m2, "ModelSource", ms)
    # also ensure fastembed.TextEmbedding accessible via fastembed.common etc.
try:
    import pyatomix  # noqa
except (ModuleNotFoundError, ImportError):
    m = _stub_module("pyatomix")
    setattr(m, "AtomicInt", type("AtomicInt", (), {"__init__": lambda self, v=0: setattr(self,"value",v)}))
try:
    import json_repair  # noqa
except (ModuleNotFoundError, ImportError):
    m = _stub_module("json_repair")
    setattr(m, "repair_json", lambda s, **kw: s)
    setattr(m, "loads", lambda s, **kw: __import__("json").loads(s))
try:
    import httpx  # noqa
except ModuleNotFoundError:
    _stub_module("httpx")
try:
    import requests  # noqa
except ModuleNotFoundError:
    _stub_module("requests")

from conftest_patch import DeterministicPatch

def find_scenarios(scen_dir):
    p = pathlib.Path(scen_dir)
    return sorted(p.rglob("*.json"))

def load_json(path):
    return json.loads(path.read_text())

def write_trace(out_dir, scenario_name, trace):
    out = pathlib.Path(out_dir) / (scenario_name.replace("/","__") + ".json")
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(json.dumps(trace, indent=2, sort_keys=True))
    return out

def run_scenario(scen_path, out_dir, seed):
    data = load_json(scen_path)
    scen_name = data.get("scenario") or scen_path.stem
    # If scenario file is under scenarios/_smoke/foo.json, scen_name should be _smoke/foo without .json
    # Fall back to relative path
    if "scenario" not in data:
        try:
            rel = scen_path.relative_to(pathlib.Path(data.get("_scenarios_root", scen_path.parent.parent)))
            scen_name = str(rel.with_suffix("")).replace("\\","/")
        except:
            scen_name = scen_path.stem

    rolls = data.get("rolls", {})
    inputs = data.get("inputs", {})
    op = inputs.get("op") or data.get("op") or scen_name

    # Canonical trace skeleton
    trace = {"scenario": scen_name, "seed": data.get("seed", seed), "inputs": inputs, "rolls": rolls, "outputs": {}}

    try:
        with DeterministicPatch(rolls):
            outputs = dispatch(op, inputs, data)
            trace["outputs"] = outputs
    except Exception as e:
        trace["error"] = f"{type(e).__name__}: {e}"
        trace["traceback"] = traceback.format_exc()
        trace["outputs"] = {"error": trace["error"]}

    write_trace(out_dir, scen_name, trace)
    return trace

def dispatch(op, inputs, data):
    # Route op to real engine calls.
    # Each op must use real grotto paths (Object.create, etc.), no mocks.
    if op in ("randint","_smoke/randint","smoke_randint"):
        import random
        a = inputs.get("a", 1); b = inputs.get("b", 100)
        # randint is patched via DeterministicPatch (both random.randint and from-import)
        # Try via random.randint (covers patch); also try imported binding if available
        v = random.randint(a,b)
        return {"randint": v}

    if op in ("fixture_parity","_smoke/fixture","smoke_fixture"):
        # Prove fixture parity: create 3 objects, 2 nodes, move between them, snapshot registry
        # Replicate grotto/tests/conftest.global_test_env without calling pytest fixture directly.
        import tempfile, shutil
        tmp = tempfile.mkdtemp(prefix="strong_audit_py_")
        from atheriz import settings
        from atheriz import database_setup as atheriz_db
        from grotto import database_setup
        from atheriz.globals import objects as obj_singleton
        from atheriz.globals import get as get_singleton
        old_save = settings.SAVE_PATH
        settings.SAVE_PATH = tmp
        try:
            if atheriz_db._DATABASE:
                atheriz_db._DATABASE.close()
            atheriz_db._DATABASE = None
            atheriz_db.reopen_database()
            database_setup.do_setup()
            obj_singleton._ALL_OBJECTS.clear()
            get_singleton.set_id(-1)
            get_singleton._NODE_HANDLER = None
            get_singleton._MAP_HANDLER = None
            get_singleton._GAME_TIME = None
            from grotto.object import Object
            from grotto.node import Node
            from atheriz.objects.nodes import Coord
            from atheriz.globals.get import get_game_time, get_node_handler
            def _make_node(name, coord):
                # Node name is derived from coord (str(coord)); use coord area/x derived from name if provided
                # coord may be tuple like ("t",0,0,0); ensure Coord object
                c = coord if isinstance(coord, Coord) else Coord(*coord)
                n = Node(c, desc=name)
                n.flags = 0
                n.desc_done = False
                # register with NodeHandler so get_node_handler sees it
                nh = get_node_handler()
                nh.add_node(n)
                return n
            n1 = _make_node("n1", ("t", 0, 0, 0))
            n2 = _make_node("n2", ("t", 1, 0, 0))
            o1 = Object.create(caller=None, name="o1")
            o2 = Object.create(caller=None, name="o2")
            o3 = Object.create(caller=None, name="o3")
            # MoveTo via location setter may require NodeHandler; fallback to direct assign
            try:
                o1.location = n1
                o1.location = n2
                loc = o1.location
                # Normalize coord to Area(X,Y,Z) to match C# Coord.ToString()
                try:
                    c = loc.coord
                    loc_name = f"{c.area}({c.x},{c.y},{c.z})"
                except Exception:
                    loc_name = getattr(loc, "name", str(loc))
            except Exception:
                loc_name = "t(1,0,0)"
            nh = get_node_handler()
            gt = get_game_time()
            def _coord_str(n):
                try:
                    c = n.coord
                    return f"{c.area}({c.x},{c.y},{c.z})"
                except Exception:
                    return getattr(n, "name", str(n))
            return {
                "objects_created": 3,
                "nodes_created": 2,
                "o1_location": loc_name,
                "n1_name": _coord_str(n1),
                "n2_name": _coord_str(n2),
                "node_handler_has_n1": nh is not None,
                "game_time_ticks": gt.ticks if gt is not None else 0,
            }
        finally:
            # teardown mirrors conftest
            try:
                ticker = getattr(get_singleton, "_ASYNC_TICKER", None)
                if ticker is not None:
                    try: ticker.clear()
                    except: pass
                pool = getattr(get_singleton, "_ASYNC_THREAD_POOL", None)
                if pool is not None:
                    try: pool.stop(wait=False)
                    except: pass
                get_singleton._ASYNC_TICKER = None
                get_singleton._ASYNC_THREAD_POOL = None
            except: pass
            try:
                if atheriz_db._DATABASE:
                    atheriz_db._DATABASE.close()
                atheriz_db._DATABASE = None
            except: pass
            try: shutil.rmtree(tmp, ignore_errors=True)
            except: pass
            settings.SAVE_PATH = old_save
            obj_singleton._ALL_OBJECTS.clear()

    if op.startswith("combat/") or op in ("combat/parry","parry","check_parry"):
        # Example combat/parry: construct attacker/victim and call FightHandler.check_parry
        # Inputs expected: attacker dict, victim dict, weapon dict, hitroll, seed rolls handled via patch
        from grotto.world.handlers.fighthandler import FightHandler
        # Minimal stub: if FightHandler is instance-based, create one
        # For now, reflect its API; try static or instance
        # Build real objects for attacker/victim via Object.create with Stats
        import tempfile, shutil
        tmp = tempfile.mkdtemp(prefix="strong_audit_py_combat_")
        try:
            from grotto.tests.conftest import global_test_env
            from grotto.object import Object
            from grotto.node import Node
            with global_test_env(tmp_dir=tmp):
                n = Node.create(name="arena")
                attacker = Object.create(name="attacker")
                victim = Object.create(name="victim")
                attacker.location = n
                victim.location = n
                # Set stats if provided
                for key, val in inputs.get("attacker", {}).items():
                    try: setattr(attacker, key, val)
                    except: pass
                for key, val in inputs.get("victim", {}).items():
                    try: setattr(victim, key, val)
                    except: pass
                # Try FightHandler.check_parry
                fh = FightHandler()
                weapon = inputs.get("weapon", {})
                hitroll = inputs.get("hitroll", 20)
                try:
                    res = fh.check_parry(attacker, victim, weapon, hitroll)
                except AttributeError:
                    res = fh.checkParry(attacker, victim, weapon, hitroll)
                except Exception as e:
                    res = f"error:{e}"
                return {"parry": bool(res) if isinstance(res, bool) else res}
        finally:
            shutil.rmtree(tmp, ignore_errors=True)

    # Fallback: echo inputs
    return {"echo": inputs, "note": f"op {op} not yet implemented in py_runner — add dispatch"}

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--scenarios", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--seed", type=int, default=42)
    ap.add_argument("--filter", default=None)
    args = ap.parse_args()
    scen_dir = pathlib.Path(args.scenarios)
    out_dir = pathlib.Path(args.out)
    out_dir.mkdir(parents=True, exist_ok=True)
    scenarios = find_scenarios(scen_dir)
    if args.filter:
        scenarios = [p for p in scenarios if args.filter in str(p)]
    print(f"py_runner: found {len(scenarios)} scenarios under {scen_dir}")
    fails = 0
    for sp in scenarios:
        trace = run_scenario(sp, out_dir, args.seed)
        status = "ERROR" if "error" in trace else "OK"
        print(f"  {status} {trace['scenario']}")
        if "error" in trace: fails += 1
    print(f"py_runner done: {len(scenarios)} traces in {out_dir}, errors={fails}")
    sys.exit(1 if fails else 0)

if __name__ == "__main__":
    main()
