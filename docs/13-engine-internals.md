# Engine internals: threads, locks, utils

> Covers: `src/Atheriz.Core/Concurrency/` (`AsyncThreadPool`, `AsyncTicker`), `src/Atheriz.Core/Utils/` (`Pathfind`/`PathNode`, `GameUtils`, `StringDistance`, `GameClock`/`ITimeProvider`/`SystemTimeProvider`, `CryptoRandom`, `FsUtil`/`PathGuards`, `TlsCertLoader`), `src/Atheriz.Core/Logger.cs`, `src/Atheriz.Core/ServerEvents.cs`, `src/Atheriz.Core/Globals/` (`GlobalServices`, `StartStop`, `Autosave`), `src/Atheriz.Server/Infrastructure/ServerLifecycle.cs`. The one doc allowed to lead with internals: what runs where, what the locking rules are, and what helpers exist. Game-code rules that follow from these internals live in [Custom code and hot reload](11-custom-code-plugins.md); save mechanics live in [Saves and backups](10-persistence-saves.md).

## 1. Threading model in one page

Three kinds of work, three places it runs:

- Pool threads (`AsyncThreadPool`, §2) run game logic: commands, hook
  handlers, input functions. Anything a player types lands here.
- The ticker (`AsyncTicker`, §3) runs repeating work: `AtTick` methods on
  ticking objects (the tutorial torch), alarms, autosave scheduling.
- The database gate serializes writes (→ [Saves and backups](10-persistence-saves.md)).
  The world lives in memory; the database is written only on save.

Two consequences shape every other section. First, world mutation happens
under documented locks (§4) — game code touches the world through
`GameObject` and command APIs that take the right locks, never by
reaching around them. Second, there are no sleeps or polling loops in
game code: handlers run on shared threads, so a blocking handler stalls
everyone. Queue work (pathfind-then-move, §6) instead of waiting for it.

Which thread runs a given piece of game code: command `Run` bodies and
hook handlers run on pool threads (one per queued item, relief when
saturated); `AtTick` bodies, alarms, and registered coroutines run on the
ticker; `Delay` callbacks return to the pool. None of these is the
network thread and none is stable across calls — the same torch may tick
on different ticker turns — so per-call state lives in arguments and
object fields under locks, never in thread-locals or "the current
thread".

## 2. AsyncThreadPool: bounded workers, relief, watchdog

`AsyncThreadPool` (`src/Atheriz.Core/Concurrency/AsyncThreadPool.cs:8`)
is a bounded worker pool with relief threads and a watchdog:

- Sizing: construct with worker, relief, and watchdog options. The relief
  limit defaults to the processor count
  (`AsyncThreadPool.cs:55-57`); relief spawns are spaced by a cooldown of
  1.0 second (`ReliefSpawnCooldownSeconds`, `AsyncThreadPool.cs:10`), so a
  burst of slow handlers adds help without fork-bombing the host.
- Relief rule: when every worker is busy, the pool spawns a relief worker
  (`MaybeSpawnReliefWorker`, `AsyncThreadPool.cs:292`) to keep the queue
  draining. A relief worker with no work exits after its idle bound —
  relief is elastic in both directions, and losing relief threads to
  idleness is normal, not an error.
- Watchdog: a `Timer` (not a dedicated thread) ticks on an interval and
  flags work items that overrun a threshold (30-second threshold and
  5-second interval by default). An `Interlocked` guard keeps slow ticks
  from overlapping each other (`AsyncThreadPool.cs:40`). The watchdog
  reports; it never kills game code. When a flag names your handler,
  treat it as a bearing, not a verdict: confirm whether the handler is
  slow (needs splitting, with follow-up work queued back to the pool) or
  merely unlucky (a burst chained several slow items onto one worker).
  Repeated flags on the same handler mean slow; scattered flags across
  handlers mean load.
- Clock: ages and thresholds read the monotonic clock (`GameClock`, §5),
  so wall-clock jumps never promote or demote a worker decision.
- Double-dispose guard: disposal is idempotent (`_disposed`,
  `AsyncThreadPool.cs:41,623`) — stopping twice is safe, and shutdown
  paths rely on that.
- Scheduling extras: `Delay` overloads (`AsyncThreadPool.cs:509-532`)
  run an `Action` or `Func<Task>` after a `TimeSpan` or a number of
  seconds; `Stop(wait, timeout)` (`:545`) drains or abandons the queue on
  shutdown.

One-shot game work belongs on `Delay`, repeating work on the ticker (§3).
`Delay` with a short span replaces every hand-rolled "wait then act"
pattern — and because the pool owns the wait, game code never blocks a
thread holding one. `Stop` is called once by the shutdown path; the
double-dispose guard makes a repeated call during overlapping shutdown
signals a no-op rather than an exception.

Sizing guidance: the defaults suit a small live game (bounded workers for
steady command load, relief up to processor count for bursts, watchdog at
30 seconds over a 5-second interval). Raise worker counts only when the
watchdog repeatedly flags healthy-but-slow handlers — and read the flags
as a symptom first: a handler that always overruns usually needs
splitting (queue follow-up work back to the pool) rather than a bigger
pool around it.

Tune the pool through settings (threadpool entries in the settings table,
→ [Hosting and configuration](08-hosting-configuration.md)), not by
constructing pools in game code. There is exactly one pool; game code
borrows it.

## 3. AsyncTicker: repeating work and coroutine slots

`AsyncTicker` (`src/Atheriz.Core/Concurrency/AsyncTicker.cs:7`) is the
repeating scheduler: alarms, tick callbacks, and registered coroutines.

- `AddCoro` / `RemoveCoro` come in matching overloads
  (`AsyncTicker.cs:36-39,80-91`): the callback may be a `Func<Task>`, a
  plain `Action`, or a `Func<CancellationToken, Task>`; the interval may
  be seconds (`double`) or a `TimeSpan`. Removal takes the same pair, so
  keep the delegate and the interval you registered with:

  ```csharp
  Func<Task> bell = RingBellAsync;
  ticker.AddCoro(bell, TimeSpan.FromMinutes(1));
  // ...
  ticker.RemoveCoro(bell, TimeSpan.FromMinutes(1));
  ```

- Ticking objects plug in here: `GameObject.Create` and
  `ResolveRelations` register each object's `AtTick` with the ticker
  (`src/Atheriz.Core/Objects/GameObject.cs:692,877`), and `Node` overrides
  `AtTick` for room-side behavior. Hot reload re-registers ticks after
  patching, so ticking objects survive reload
  (→ [Custom code and hot reload](11-custom-code-plugins.md)).
- Registration is by (callback, interval) slots: the ticker groups
  callbacks per interval, so registering the same callback at two
  intervals creates two independent slots, and removing one interval
  leaves the other running. Always remove with the exact pair used at
  registration — a mismatched interval removes nothing and the callback
  keeps firing on its original slot.
- Tickable-node interplay (which nodes tick, how often, what a tick may
  touch) is covered from the user's side in [World and maps](04-world-maps.md)
  and [Hooks, scripts, and text](05-hooks-scripts-text.md); this section
  is the mechanism those docs stand on.

## 4. Locking rules

The public lock surface never changes visibility. Lock *types* behind the
surface may change where absence of reentrancy is proven, but three lines
never move: `Monitor.Wait`/`Pulse` sites never become `Lock`,
`ReaderWriterLockSlim` `SyncRoot`s never convert, and no public lock
becomes private or internal. External game code depends on the public
lock API, so visibility is the contract.

`LockScope` (`src/Atheriz.Core/LockScope.cs:7`) is an internal sealed
`IDisposable` guard the engine uses to pair lock acquisition with release
(`ReadScope`/`WriteScope` on doors, for example). It is engine-only:
game code never names it, never constructs it, never wraps locks in its
own scoped APIs. Reach objects through the documented APIs and the scopes
take care of themselves.

The public lock surface, in one place: object `SyncRoot`s, node
`NodeLock`s, door `SyncRoot`/`Lock` (one shared recursive lock behind
both names), channel listener guards, session `Lock`. These names and
their visibility are the contract — game code may rely on them, the
engine may change what sits behind them only where absence of reentrancy
is proven, and no upgrade (including the .NET 10 / C# 14 one) ever narrows
their visibility, wraps them in scoped APIs, converts `Monitor.Wait` /
`Pulse` sites, or converts `ReaderWriterLockSlim` `SyncRoot`s.

Lock order — always nest in this direction, never the reverse:

| Outer → inner | Rule |
|---|---|
| Registry → object | Look up under the registry lock, release it, run caller predicates outside the lock (re-entrant predicates must not deadlock), then lock the target object to mutate. |
| Door → handler | Door state locks first; handler bookkeeping (door-modified marks) happens after the door lock releases. `RemapDoors` takes the handler lock toward doors one way only — never the reverse edge, or the pair deadlocks. |
| Channel peers | Snapshot listener ids before taking the channel lock; use the lock-free delete-guard read while holding a peer lock instead of nesting the channel lock inside one. |

Enumeration rule: `area.Grids` and `grid.Nodes` are enumerated from a
copy taken under the read lock (the shared handler-to-areas-to-grids
read-lock snapshot walk), never by holding the lock across caller code.
The same snapshot-then-release shape governs registry scans: predicates
run outside the lock, first match in snapshot order wins, and no `Exists`
check ever holds the lock across its scan.

When in doubt, do less under a lock: copy, release, compute, re-lock,
apply. Locking call sites for commands live in [Commands](06-commands.md);
per-object gates in [Hooks, scripts, and text](05-hooks-scripts-text.md).

The door/handler pair shows the discipline in full. Door state locks
first and handler bookkeeping (door-modified marks on the node handler)
happens after the door lock releases — the order is always door toward
handler. The reverse edge exists exactly once (`RemapDoors` takes the
handler lock toward doors), and the two never nest: per-setter marks are
suppressed for bodies that already hold the door write lock, precisely so
no path can hold both edges at once. That is the template for every new
lock pair in the engine: pick one direction, enforce it at every call
site, and suppress the automatic mark/side-effect on paths that already
hold the outer lock.

Checklist before introducing any new lock or lock edge in engine code:

1. Name the direction (outer → inner) and confirm no existing path takes
   the reverse edge — one ABBA pair is one deadlock.
2. Keep the critical section free of callbacks, predicates, and I/O; copy
   out, release, compute, re-lock, apply.
3. Prefer the existing surface (`SyncRoot`, `NodeLock`, scopes) over a new
   lock; a second lock per object doubles the ordering proof.
4. Never convert a `ReaderWriterLockSlim` `SyncRoot`, never turn a
   `Monitor.Wait`/`Pulse` site into `Lock`, never narrow visibility.

## 5. Clocks and time

- `GameClock` (`src/Atheriz.Core/Utils/GameClock.cs:15`) is the static
  monotonic source: `MonotonicSeconds` (`:23`), `MonotonicMilliseconds`
  (`:33`), and `Now` (`:36`, monotonic seconds). Pool aging, watchdog
  thresholds, and cooldowns read this clock so NTP steps and daylight
  changes cannot confuse them.
- `ITimeProvider` (`src/Atheriz.Core/Utils/ITimeProvider.cs:7`) abstracts
  time for code that must be drivable; `SystemTimeProvider`
  (`src/Atheriz.Core/Utils/SystemTimeProvider.cs:5`) is the production
  implementation. Game-visible calendar and tick scheduling sit on top
  (→ [World and maps](04-world-maps.md)); this section is the mechanism.
- Rule of thumb: durations, cooldowns, ages, and rate limits read
  `GameClock`; wall-clock display (timestamps in logs, calendar dates)
  reads the provider. Mixing them — a cooldown compared against a wall
  timestamp — breaks across clock steps, which is exactly why the two
  sources are separate types instead of one convenience clock.

## 6. Pathfinding: A* over link snapshots

`Pathfind` (`src/Atheriz.Core/Utils/Pathfind.cs:5`) runs A* over node
links with snapshot isolation and iteration caps:

- `AStar(start, end, caller, handler, maxIterationsOverride)`
  (`Pathfind.cs:83`) returns `(Found, Path, ClosedSet)`: whether the goal
  was reached, the node path, and the closed set for diagnostics. A
  not-found result with a large closed set means the search exhausted its
  room (cap or sealed area); a tiny closed set means the start had almost
  nowhere to go (missing links, or every neighbor door-filtered for the
  caller). Read the tuple before retrying with a bigger cap.
- `FindPath` overloads (`Pathfind.cs:157-161`) take `(start, goal,
  handler)` plus an optional caller and iteration cap, and return the
  coordinate route (or null when none exists). `GetNeighbors(c, handler,
  caller)` (`:179`) lists walkable neighbors of a coordinate.
- Snapshot: neighbors resolve from a copy of the node's links taken under
  the node's read lock, so concurrent link edits never tear a search
  mid-path. Door filtering stays on the caller path — the variants with a
  caller skip locked/closed doors, the ones without do not. Pick the
  caller-less overloads for map tooling and generation (walls are data),
  the caller overloads for anything a character will walk (doors are
  permission checks). Mixing them up shows as wanderers walking through
  locked doors or builders unable to route across an open one.
- Caps: an explicit override wins; otherwise
  `AtherizSettings.Global.MaxAstarIterations` applies; when settings are
  unreachable the floor is 50000. Searches that exhaust the cap report
  not-found instead of hanging the pool thread.
- Handoff: queue a pathfind, then move along the returned route through
  the normal move path — with no delay between the two. Production code
  queues the search and applies the walk; game code must not sleep,
  poll, or block waiting for either half. When the walk fails midway (a
  door locked after the search, a link removed), re-queue one fresh search
  from the current position rather than retrying the stale route: the
  world moved, so the answer changed, and a second search is cheaper than
  arguing with a locked door.

Example — route a wanderer from Town Square toward the end of North Road:

```csharp
List<Coord>? route = Pathfind.FindPath(squareCoord, roadEndCoord, nodeHandler);
if (route is not null)
{
    // Walk route one node at a time through the normal move path.
}
```

(`Coord`, areas, and grids from the user's side live in
[World and maps](04-world-maps.md).)

Path reconstruction walks parent links: each `PathNode` carries its
position and its parent, and the result is built by walking parents back
to the start and reversing (`Pathfind.cs:7-18`). The walk is allocation
proportional to path length — one list, one reverse — so even a long
North Road amble costs the pool thread almost nothing beyond the search
itself. Keep routes before moving (snapshot the list, then walk it)
rather than re-querying per step; the world may change between steps, and
per-step searches multiply cap-burning searches across one stroll.

## 7. Helpers: text, match, random, files, TLS

Small, dependency-free, and safe to call from any game thread:

- `GameUtils` (`src/Atheriz.Core/Utils/GameUtils.cs:9`): escape stripping
  — `StripAnsi` (`:23`) removes ANSI sequences, `StripTerminalEscapes`
  (`:25`) removes the wider terminal-escape set (source-generated
  matchers, no runtime regex compilation on the hot path). Strip before
  matching or measuring player input; render after.
- `StringDistance` (`src/Atheriz.Core/Utils/StringDistance.cs`): fuzzy
  matching — `Levenshtein(a, b)` (`:22`) scores two strings,
  `BestMatch(query, candidates)` (`:61`) returns the closest candidate or
  null when there is nothing to match against. Use it for "did you mean"
  suggestions, never as the primary lookup (exact match first, fuzzy as
  the fallback message).
- `CryptoRandom` (`src/Atheriz.Core/Utils/CryptoRandom.cs`):
  `UInt64String` (`:16`), `UrlSafeToken(bytes = 32)` (`:27`), and
  `HexToken(bytes = 32)` (`:36`) for tokens and nonces. Random-facing,
  never `System.Random` for secrets.
- `FsUtil` (`src/Atheriz.Core/Utils/FsUtil.cs:11-31`): managed permission
  setters (`TryChmod0600`/`TryChmod0700`/`TryChmod0755` and the
  `TrySet*` aliases) used for `save/`+`secret/` dirs, pid files, and
  tokens — managed APIs with OS guards plus a working fallback, per the
  cross-platform rule.
- `PathGuards` (`src/Atheriz.Core/Utils/PathGuards.cs`): traversal and
  placement guards — `GuardSavePath`/`GuardSecretPath` (`:14-19`) pin the
  database and secret paths, `DenyRoot` (`:82`) refuses filesystem roots,
  `GuardWipePath` (`:111`) constrains destructive wipes,
  `EnsureSaveDirectory`/`EnsureSecretDirectory` (`:33-35`) create with
  guards applied.
- `TlsCertLoader` (`src/Atheriz.Core/Utils/TlsCertLoader.cs:13`):
  `Load(certFile, keyFile)` preloads one certificate per handshake with no
  filesystem reads on the select path (certificate fields and the
  insecure-fallback warning live in [Hosting and configuration](08-hosting-configuration.md)).
- Files and paths round out the set: the `FsUtil` permission setters back
  every private file and directory the server creates (pid files and
  tokens at 0600, `save/` and `secret/` at 0700), while `PathGuards`
  stands at each dangerous call site — save/secret placement, root
  refusal, wipe confinement, directory creation. When a file operation
  fails, read the guard message first: it names the refused path and the
  rule that refused it, which is usually faster than re-reading this
  section.

## 8. Logging and events

`AtherizLogger` (`src/Atheriz.Core/Logger.cs:299-320`) is the logging
facade: `LogInformation`, `LogWarning`, `LogError` (plain and with
`Exception`), `LogDebug`, and `LogCritical` route categorized messages to
the configured sinks, while `LogRobust` (plus `LogErrorRobust` /
`LogInformationRobust`) survives logger-outage paths such as early boot
and shutdown. Categories default sanely; pass one when the message
belongs to a subsystem an operator may filter on. Severity guide:
`Debug` for per-object chatter an operator never enables in production,
`Information` for lifecycle milestones (startup, reload, save),
`Warning` for drift the operator should fix (sync-check mismatch, stale
dll), `Error` for failed operations with a logged cause, `Critical` for
conditions that stop the server. Reach for the `Robust` variants only
where the normal sink may not exist yet (boot before logging attaches) or
anymore (shutdown teardown) — everywhere else they hide category routing
for no benefit.

`ServerEvents.AtCharCreate`
(`src/Atheriz.Core/ServerEvents.cs:57`) runs under the creation lock —
and the lock is narrow on purpose. It guards only the check-then-insert
sequence (reserve the name, commit the character) so two concurrent
signups serialize instead of double-claiming. Everything around it stays
outside: home-room resolution runs before the lock, password verification
runs before the lock (hashing cost must never serialize signups), and
console output plus hook calls run after it releases. No console I/O
happens under the creation lock. The ordered shape of one signup:

1. Resolve the home room (no lock; handler reads are lock-safe).
2. Verify the password hash (no lock; hashing cost stays off the
   serialized path, with only a hash rotation re-hashing inside).
3. Take the creation lock: re-check the name, insert the account and the
   character, release.
4. After release: print the success output, fire the creation hooks.

Steps 1–2 may run concurrently for any number of signups; only step 3
serializes, and it holds nothing but the check-then-insert. A slow signup
storm therefore queues at step 3 for milliseconds, not for hashes or
room lookups — which is the entire point of the narrowing.

The 60-second create and reload watchdogs live in `AdminRoutes`, not in
`ServerEvents` — events fan out; routes bound time. (Reload's watchdog is
covered in [Custom code and hot reload](11-custom-code-plugins.md).)

## 9. Boot order: services, startup, autosave, lifecycle

```bash
GlobalServices → StartStop.DoStartup → InitialSetup → Autosave → banner
reload: PluginReloader + ServerLifecycle.DoReload
shutdown: ServerLifecycle.DoShutdown → StartStop.DoShutdown → save
```

- `GlobalServices` (`src/Atheriz.Core/Globals/GlobalServices.cs:7`) is the
  locator behind the engine singletons: pool, ticker, handlers, settings.
  Call sites read through it; nothing constructs a second pool or ticker.
- `StartStop.DoStartup` (`src/Atheriz.Core/Globals/StartStop.cs:36`)
  runs engine startup and registers each loaded object's `AtTick`
  through typed dispatch — every tickable is a `GameObject` (`Node`
  overrides `AtTick`), and tick faults are caught and logged per object
  (`StartStop.cs:375-381`), so one bad tick never kills the ticker.
  `StartStop.DoShutdown` (`:186`) reverses it. The `Started` flag guards
  re-entry.
- Startup and shutdown are asymmetric on purpose. Startup registers:
  every loaded object gets its tick slot, the pool and ticker start
  accepting work, and only then do listeners open — no command runs
  before its object's tick is registered. Shutdown unregisters in the
  opposite order: listeners close first (no new work), queued work drains,
  tick slots release, and the final save lands. The typed tick dispatch
  (an `is`-based walk over `GameObject` subtypes, never a
  `GetMethod("AtTick")` lookup) keeps registration free of reflection on
  the hot path, matching the no-reflection rule game code follows
  (→ [Custom code and hot reload](11-custom-code-plugins.md)).
- `ServerLifecycle` (`src/Atheriz.Server/Infrastructure/ServerLifecycle.cs`)
  owns the server side: `DoStartup` (`:33`), `DoShutdown` (`:75`), and
  `DoReload` (`:100`). Each is guarded and idempotent — starting twice,
  stopping twice, or reloading into the same state is safe. `DoStartup`
  loads the world once into memory (the load-once discipline,
  → [Saves and backups](10-persistence-saves.md)), wires the pool, ticker,
  and listeners, then logs completion; `DoShutdown` reverses the wiring
  and lands a final save so shutdown never loses committed play.
  A healthy boot ends with `DoStartup completed` in `save/server.log`; a
  hang before that line means startup never finished (triage order in
  [Running the server](07-server-lifecycle-cli.md)).
- `Autosave` (`src/Atheriz.Core/Globals/Autosave.cs:11`) schedules
  checkpoints on the ticker: `StartAutosave(ticker, settings)` (`:99`,
  settings-only overload at `:126`) begins the schedule,
  `StopAutosave()` (`:140`, ticker overload at `:151`) ends it. Timing and
  guarantees live in [Saves and backups](10-persistence-saves.md).
  Scheduling on the ticker (rather than a private timer) keeps saves on
  the same scheduler as ticks, so lifecycle ordering stays simple:
  stopping the ticker during shutdown stops the save schedule with it,
  and no stray checkpoint fires after the final save.

---
Next: [Table of contents](README.md)
