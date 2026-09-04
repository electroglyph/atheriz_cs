"""
Deterministic RNG patch for strong_audit.

Patches every `from random import randint, choice, ...` binding across already-imported modules,
plus stdlib `random` itself and `atheriz.utils.dice_roll`.

Usage:
    from conftest_patch import DeterministicPatch
    with DeterministicPatch(rolls):
        # run scenario
        ...

rolls: dict with keys randint, choice, shuffle, uniform, getrandbits, dice — each a list queue.
Missing keys default to empty.
Queue underflow raises RuntimeError (treated as FAIL by compare.py).
"""
import sys, random
import contextlib

class DeterministicPatch:
    def __init__(self, rolls):
        self.rolls = {k: list(v) for k, v in (rolls or {}).items()}
        for k in ("randint","choice","shuffle","uniform","getrandbits","dice","randrange","random"):
            self.rolls.setdefault(k, [])
        self._idx = {k: 0 for k in self.rolls}
        self._patches = []

    def _next(self, key):
        lst = self.rolls.get(key, [])
        i = self._idx.get(key, 0)
        if i >= len(lst):
            raise RuntimeError(f"DeterministicPatch queue underflow: {key} need index {i} but len={len(lst)} rolls={self.rolls}")
        self._idx[key] = i + 1
        return lst[i]

    def __enter__(self):
        rolls = self.rolls
        idx_ref = self._idx

        # Save originals to restore
        self._orig = {}
        for name in ("randint","choice","shuffle","uniform","randrange","getrandbits","random"):
            if hasattr(random, name):
                self._orig[f"random.{name}"] = getattr(random, name)

        # Wrap random module
        def fake_randint(a,b):
            return self._next("randint")
        def fake_choice(s):
            v = self._next("choice")
            # If queue entry is an index, interpret; else return verbatim
            if isinstance(v, int) and hasattr(s, '__getitem__'):
                try: return s[v % len(s)] if len(s) else v
                except: return v
            return v
        def fake_shuffle(s):
            # queue entry is list to replace contents, or None to leave
            v = self._next("shuffle")
            if isinstance(v, list):
                s[:] = v
            elif v is None:
                pass
            else:
                # if single int, shuffle deterministically by rotating?
                pass
            return None
        def fake_uniform(a,b):
            return self._next("uniform")
        def fake_getrandbits(k):
            return self._next("getrandbits")
        def fake_random():
            return self._next("random")

        random.randint = fake_randint
        random.choice = fake_choice
        random.shuffle = fake_shuffle
        random.uniform = fake_uniform
        random.getrandbits = fake_getrandbits
        random.random = fake_random
        if hasattr(random, "randrange"):
            random.randrange = lambda *a, **kw: self._next("randrange")

        # Patch every already-imported module that did `from random import X`
        for mod in list(sys.modules.values()):
            if mod is None: continue
            d = getattr(mod, "__dict__", None)
            if d is None: continue
            for name, fake in (("randint", fake_randint), ("choice", fake_choice), ("shuffle", fake_shuffle), ("uniform", fake_uniform), ("getrandbits", fake_getrandbits), ("random", fake_random)):
                if name in d:
                    # Only patch if it looks like the stdlib random binding (function)
                    # Check that existing value is callable and module isn't random itself
                    if mod is random: continue
                    try:
                        # Detect via qualname or module
                        cur = d[name]
                        # Patch if cur came from random (heuristic: has same name)
                        if callable(cur) or isinstance(cur, type(lambda:None)):
                            # Check if cur is original random.* by comparing to saved orig
                            orig = self._orig.get(f"random.{name}")
                            if cur is orig or getattr(cur, "__module__", "") == "random":
                                d[name] = fake
                    except Exception:
                        pass

        # Patch atheriz.utils.dice_roll if present
        try:
            import atheriz.utils as autils
            if hasattr(autils, "dice_roll"):
                self._orig["atheriz.utils.dice_roll"] = autils.dice_roll
                def fake_dice(n, faces):
                    return self._next("dice")
                autils.dice_roll = fake_dice
        except Exception:
            pass

        return self

    def __exit__(self, exc_type, exc, tb):
        # Restore random module
        for k, v in self._orig.items():
            if k.startswith("random."):
                setattr(random, k.split(".",1)[1], v)
            elif k == "atheriz.utils.dice_roll":
                try:
                    import atheriz.utils as autils
                    autils.dice_roll = v
                except: pass
        # Restore per-module bindings: best-effort restore by re-import random?
        # We patched to fake functions; to restore we'd need original per-module values.
        # Simplify: leave patched — next scenario creates fresh patch that re-patches.
        # But restore the modules we patched to original random.* functions
        fake_map = { "randint": random.randint, "choice": random.choice, "shuffle": random.shuffle, "uniform": random.uniform, "getrandbits": random.getrandbits, "random": random.random }
        # Actually above we already restored random.* to originals, so fake_map now is originals
        # Need to restore per-module to originals as well:
        for mod in list(sys.modules.values()):
            if mod is None or mod is random: continue
            d = getattr(mod, "__dict__", None)
            if d is None: continue
            for name in ("randint","choice","shuffle","uniform","getrandbits","random","randrange"):
                if name in d:
                    cur = d[name]
                    # if cur is one of our fakes (closure), replace with original
                    orig = self._orig.get(f"random.{name}")
                    if orig is not None and cur is not orig:
                        # Heuristic: if cur's code object contains DeterministicPatch, it's a fake
                        try:
                            if getattr(cur, "__code__", None) and "DeterministicPatch" in str(cur.__code__.co_consts):
                                d[name] = orig
                        except: pass
        return False

    # Helper for tests: assert all queues consumed (optional)
    def assert_consumed(self):
        pass
