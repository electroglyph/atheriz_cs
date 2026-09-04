# strong_audit — Dual-Runtime Parity Harness

JSON-driven scenario harness: each scenario declares `inputs` + `rolls` queues + `expected` is derived from Python trace (golden). Both runners build real engine objects via `GrottoFixture` / `global_test_env` and emit canonical traces. `compare.py` asserts `py == cs`.

```
strong_audit/
  scenarios/_smoke/*.json
  scenarios/combat/*.json
  py_runner/runner.py + conftest_patch.py
  cs_runner/StrongAudit.Runner/
  traces/{py,cs}/
  compare.py
  ci/strong_audit.sh
  findings.md
```

Run: `./strong_audit/ci/strong_audit.sh` or `python3 strong_audit/compare.py --py traces/py --cs traces/cs --strict`.

## Scenario contract

```json
{
  "scenario": "_smoke/randint",
  "seed": 42,
  "rolls": {"randint":[7,42],"choice":[],"shuffle":[],"uniform":[],"getrandbits":[],"dice":[]},
  "inputs": {"op":"randint","a":1,"b":100},
  "outputs": {}
}
```

Rolls are queues per RNG primitive; underflow → FAIL. Python runner patches every `from random import randint` binding via sys.modules sweep; C# runner swaps `GrottoRandom.NextInt/NextSingle/NextDouble`.

## Fixture

Python: `global_test_env` from `grotto/tests/conftest.py` (clears `ObjectRegistry`, `NodeHandler`, `MapHandler`, `GameTime`, `AsyncTicker`, `SAVE_PATH=tempDir`). C#: `GrottoFixture` mirror.

## Comparison

integers exact, floats `abs<1e-9`, strings byte-exact (ANSI, $You/$conj), flags hex integer exact, known_incompatible `dill vs JSON` bucket.

See `../strong_audit.md` §1-10 for full plan.
