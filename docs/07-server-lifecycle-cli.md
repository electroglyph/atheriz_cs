# Running the server

> Covers: `src/Atheriz.Server/Program.cs`, `src/Atheriz.Server/Cli/`, `src/Atheriz.Server/Infrastructure/`, `atheriz.sh`, `atheriz.cmd`, `build.sh`, `build.cmd`. You learn how to run, stop, restart, reload, wipe, and populate a game from the command line, which files track the live server, and how to triage a server that will not start. Game-code authoring details live in [Custom code and hot reload](11-custom-code-plugins.md); hosting and settings live in [Hosting and configuration](08-hosting-configuration.md).

Game-folder commands need your current directory to be the game folder, so run them through the game folder's own launcher (`./atheriz.sh ...`). The engine is version 0.24.0.0 at the time of writing; see `../CHANGELOG.md` for history.

## 1. CLI overview

Every invocation starts at `Program.cs`, which hands off to `AtherizCli.InvokeAsync` (System.CommandLine 2.0.0). There are 8 commands:

```bash
./atheriz.sh start --help
./atheriz.sh stop --help
./atheriz.sh restart --help
./atheriz.sh reload --help
./atheriz.sh reset --help
./atheriz.sh create --help
./atheriz.sh new --help
./atheriz.sh test --help
```

Or with a built DLL directly (same arguments after `--` with `dotnet run`):

```bash
dotnet run --project src/Atheriz.Server -- --help
dotnet src/Atheriz.Server/bin/Release/net10.0/Atheriz.Server.dll start --foreground
```

Global flags (accepted by the commands that need them):

| Flag | Meaning |
|---|---|
| `--port/-p <n>` | Web port override. Glued forms `-p1234` and `-p=1234` are normalized to the same value. |
| `--telnet-port <n>` | Telnet port override. Env `ATHERIZ_TELNET_PORT` (plus the `Atheriz__TelnetPort` alias) sets the same value without a flag. |
| `--host <ip>` | Bind address. Must parse as an IP address, otherwise the command exits with code 2. |
| `--foreground/-f` | Stay in-process. `start`, `new`, `reset`, and `restart` all default to background operation; pass `--foreground` (or `-f`) to keep the server in your terminal instead. |

Background is the default everywhere it applies. Foreground is the opt-in for debugging, containers, and watching log output live.

## 2. `start`: foreground vs background

```bash
cd mygame
./atheriz.sh start --foreground   # stay attached
./atheriz.sh start                 # background (default)
```

Foreground runs `ServerHost.RunForegroundAsync`: Kestrel plus the telnet hosted service in-process, log output on your terminal, Ctrl+C shuts down cleanly.

Background spawns a detached child (`start --foreground` with `ATHERIZ_DAEMON=1` in its environment) and then polls readiness for up to 15 seconds before reporting success. The readiness check requires both halves to agree:

- the pid claim in `save/server.pid` equals the child pid just spawned, and
- the configured web port is listening.

Only then does the parent print its started banner. If the poll times out, the parent reports failure — check `save/server.log` (see §12).

```
              ┌─────────────────────┐
              │ ./atheriz.sh start  │
              └─────────┬───────────┘
                        │
              --foreground? ──yes──▶ RunForegroundAsync in-process
                        │
                        no
                        ▼
           spawn detached child (start --foreground + ATHERIZ_DAEMON=1)
                        │
                        ▼
           15 s readiness poll: pid claim == child pid AND port listening?
                  ┌──────┴──────┐
                 yes            no ──▶ report failure, read save/server.log
                  ▼
           print started banner
```

An unsafe `--host` value is refused before anything binds. If you need a public bind, set an explicit IP address via `--host` (must parse as IP, else exit 2) and confirm your firewall and TLS story first (see [Hosting and configuration](08-hosting-configuration.md)).

## 3. `stop`: graceful shutdown first, verified kill second

```bash
cd mygame
./atheriz.sh stop
```

Stop is two-phase, and the phases run in order:

```
  ./atheriz.sh stop
        │
        ▼
  (a) POST /_internal/shutdown via ShutdownClient (TLS-flip retry)
        │
        ├── Accepted ──▶ exit 0, server saves and exits on its own
        │
        ├── AuthRejected ──▶ abort, do NOT kill (token mismatch — fix secret/admin.token)
        │
        └── unreachable / refused ──▶ phase (b)
                                      │
        (b) verified kill: PidFile.TryReadPid + IsServerProcess
            (+ test-host exclusion) + IsProcessListeningOnPort
            + terminal re-verify, then Kill → wait 3 s → Kill
            (SIGTERM on Unix; there is no separate SIGKILL step)
```

Phase (a) is graceful: `ShutdownClient` posts to `/_internal/shutdown`, retrying with the TLS setting flipped once (plain vs TLS) so a cert mismatch does not look like a dead server. A clean accept returns exit code 0. An auth rejection aborts the whole stop — never escalate to kill when the token disagrees, because you would be killing a server you cannot prove is yours.

Phase (b) is the verified kill. It reads the pid claim, confirms the pid really is this server process (stale claims from recycled pids are rejected), confirms something is actually listening on the game ports, then signals, waits 3 seconds, and signals again if still alive. On Unix the signal is `SIGTERM`; there is no separate `SIGKILL` step.

Two refusal cases protect you from shooting the wrong process:

- No pid file but something is listening: stop refuses. Reconcile the stray listener by hand instead of letting the tool guess.
- Never run a stray `kill` without these checks. `kill $(cat save/server.pid)` is the last-resort escape hatch in the triage checklist (§12), not the normal path.

## 4. `restart`: stop, wait for the ports, start again

```bash
cd mygame
./atheriz.sh restart
./atheriz.sh restart --foreground
```

`restart` runs `StopAsync`, invalidates the settings cache so edited `appsettings.json` values are picked up, waits for the claimed pid to exit (escalating to `Kill` after re-verification if it lingers), then waits up to 10 seconds for BOTH the web and telnet ports to go free via `WaitForPortFreeAsync` before spawning the replacement (`SpawnStart`) or running in-process (`RunForeground` with `--foreground`).

Reach for `restart` when you changed settings, ports, or the bind address — `reload` does not re-read those (see §5).

## 5. `reload`: hot-load built code, never compiles

```bash
cd mygame
./atheriz.sh reload
```

`reload` posts to `/_internal/hot_reload` (request body cap 4096 bytes). On the server side, `AdminRoutes` wraps `PluginReloader.ReloadGameLogicAsync` plus `ServerLifecycle.DoReload` in a 60-second watchdog. If the work finishes in time you get the result; if the watchdog fires you get a `200` with `{status:error}` while the reload keeps running in the background — check `save/server.log` for the outcome instead of firing a second reload on top.

The one rule that bites newcomers:

> `reload` never compiles. If your sources are newer than your built plugin, the message from `PluginReloader` tells you to run `dotnet build` first.

The intended loop is edit → rebuild → reload. The per-game launcher shortens it:

```bash
cd mygame
./build.sh --reload   # build, then hot-load into the live server
```

`--reload` means build-then-hot-load; plain `reload` means hot-load only. When to use what: `reload` after a plugin rebuild; `restart` after settings/port changes; `reset` when you want to start the world over (§6). Full mechanics (assembly unload, live-object patching) live in [Custom code and hot reload](11-custom-code-plugins.md).

## 6. `reset`: wipe the world and reseed (always asks)

```bash
cd mygame
./atheriz.sh reset
```

`reset` always prompts `[y/N]` and there is no `--force` or `--yes` flag by design — wiping is destructive and the prompt is the guardrail. Answer anything but `y`/`Y` and nothing happens.

With confirmation, the sequence is:

1. Port-listener guard over the override, configured, and telnet ports.
2. Stop the live server if one is claimed.
3. Take `.wipe-lock` so concurrent savers stay out.
4. Park background savers through `DbWriteGate` (30-second hold).
5. `CloseDatabase`, then `GuardWipePath` verifies the wipe target is really inside this game folder's `save/` tree — a guard failure aborts the reset rather than deleting the wrong directory.
6. Recursive `save/` delete (a file-sweep variant on Windows, where memory-mapped files behave differently), then re-create the directory and `ReopenDatabase`.
7. `InitialSetup.RunSetup(Prompt:false)` reseeds superuser credentials (from `ATHERIZ_SUPERUSER_*` when set, else stored/prompted defaults), areas, and the `limbo` start room.
8. Start the server again (background by default, `--foreground` if you passed it).

What this means for you: `reset` destroys players, rooms, builds, and history, then gives you a fresh world with the same game code. Back up `save/` first if there is anything you want to keep — the backup procedure is in [Saves and backups](10-persistence-saves.md).

## 7. `create`: add an account (live server first)

```bash
cd mygame
./atheriz.sh create myaccount MyChar s3cretPass --port 9999
```

Arguments are `create <account> <character> <password>`, with optional `--port` pointing at the live server when it is not on the default port.

Live-server first: the handler posts to `/_internal/create_account` (request cap 64 KB). The server validates the names and password, then runs `ServerEvents.AtCharCreate` under the creation lock, inside a 60-second watchdog in `AdminRoutes` — the same watchdog shape as `reload`. Too-fast repeat creation from the same source is rejected by the creation throttle; wait and retry.

Offline creation happens only if the live server is unreachable AND no live pid claim exists. The double liveness check (probe, then re-check around the database load) prevents creating the same character twice — once in memory, once on disk. Offline creation opens the database (`EnsureCreated` + `LoadObjects`), runs `AtCharCreate` against the loaded world, and saves.

Name and password rules (letters, length, uniqueness) are enforced the same way in both paths; a rejection message tells you which rule failed. What the new player sees on first login is covered in [Connecting: webclient and telnet](09-connecting.md).

## 8. `new`: scaffold a game folder, then start it

```bash
./atheriz.sh new /path/to/MyGame
./atheriz.sh new /path/to/MyGame --overwrite
ATHERIZ_SUPERUSER_USERNAME=admin ATHERIZ_SUPERUSER_PASSWORD='s3cret!' ./atheriz.sh new /path/to/MyGame --overwrite
```

Accepted flags: `--overwrite`/`--force`, `--port`, `--host`, `--telnet-port`, `--foreground` (background start is still the default unless you pass `--foreground`).

Folder-name rules: the last path segment becomes the game identifier and must be a valid identifier — C# and Python keywords are both rejected (so `class`, `pass`, `event`, and friends are out). Refusals to know:

- `DenyRoot`: you cannot scaffold directly onto a filesystem root.
- A nested-folder warning when the target sits inside another game folder or repo checkout.
- Without `--overwrite`, an existing non-empty target aborts. With `--overwrite`, the handler holds the wipe, probes for a live server on the target's ports, wipes `save/**` plus any stale `admin.token`, then scaffolds.

Credentials: the command prompts for a superuser name/password unless `ATHERIZ_SUPERUSER_*` is set in the environment. Those variables are also honored by `InitialSetup.ResolveCredentials` (used by `reset` and first boot), so they are not `new`-only. Non-interactive `--overwrite` requires the env vars — there is nobody to answer the prompt in a pipeline.

What scaffolding writes: the `<Game>.csproj` project file, `GameSettings.cs`, `README.md`, per-game `atheriz.sh`/`atheriz.cmd` forwarders, `build.sh`/`build.cmd`, and the `web/` client overrides, plus `save/` and `secret/` directories created with `0700` permissions and a `.gitignore` covering `save/`, `secret/`, `bin/`, and `obj/`. `secret/` gets `0700`; its contents are covered in §10. After scaffolding, `InitialSetup.RunSetup` seeds the world (best-effort plugin load — a game-code compile error is reported, not hidden), the process changes into the new folder, and the server starts. Full scaffold mechanics live in [Custom code and hot reload](11-custom-code-plugins.md); start with [Hosting and configuration](08-hosting-configuration.md) for settings you will want right after scaffolding.

## 9. `test`: run the engine checks

`test` forwards to `dotnet test`, stripping a leading `core` argument first (`./atheriz.sh test core ...` means the core project). That is all it does.

## 10. Files that track the server

All paths are inside the game folder:

| Path | Permissions | Purpose |
|---|---|---|
| `save/server.pid` | `0600` | Pid claim for the live server. Read by `stop`/`restart`/liveness probes; stale claims are rejected by process verification. |
| `secret/admin.token` | `0600` | Shared secret for `/_internal/*` admin routes, sent as `X-Admin-Token`. A zero-byte file is treated as poisoned and regenerated on next boot. |
| `save/server.log` | — | The server log. Read FIRST on any issue, before curl or `ss`. |
| `save/.wipe-lock` (`.wipe-lock`) | — | Taken during `reset` wipes so savers and starters serialize against the delete. |
| `wwwroot/.webclient-hash` | — | Hash of the shipped webclient; the sync checker compares the game folder's `web/` tree against it at startup. |

`save/` and `secret/` directories themselves are created `0700`. The database (`save/database.sqlite3`) and the rest of the `save/` layout are covered in [Saves and backups](10-persistence-saves.md).

## 11. Launchers: root scripts, per-game forwarders, builds

Root `atheriz.sh` / `atheriz.cmd` locate the server DLL in this order and run the first one found:

1. `Release` build output,
2. `Debug` build output,
3. `publish` output,
4. `dotnet run` against `src/Atheriz.Server` as a last resort.

The current directory is preserved (game-folder commands depend on it). For `start`/`restart`/`reload` (and `new`/`create` without the game copy) the script also compares the webclient sources against the staged server copy and prints a rebuild hint when sources are newer — warnings only, startup is never blocked. When no built DLL exists at all it says so and falls back to `dotnet run --project` (slower); run `./build.sh` to pre-build.

Per-game forwarders (scaffolded into each game folder by `new`) resolve the engine back to this repo:

- `atheriz.sh`: honor `ATHERIZ_ROOT` if set, else search upward from the game folder for the engine checkout, else fall back to the path baked in at scaffold time.
- `atheriz.cmd`: environment variable or baked path only — there is no upward-search loop on Windows. The forwarder never changes directory on your behalf.

`build.sh` / `build.cmd` flags:

| Flag | Effect |
|---|---|
| `--no-web` | Skip the webclient build (fast plugin-only iteration). |
| `--web` | Web redeploy only into `web/` (skip the plugin build). |
| `--reload` | Build, then hot-load into the live server. |
| `--no-engine` | Skip rebuilding the engine itself. |

Root `build.sh` builds the engine `Release` and `Debug`; `build.cmd` builds `Release` only. Either may also rebuild the engine first when the game plugin DLL is stale relative to the engine sources.

## 12. Failure triage: the ordered checklist

Work this list top to bottom; stop at the first step that explains the failure.

1. Read `save/server.log` FIRST. `DoStartup completed` means boot finished; a log stuck at `Starting` means boot hung — the lines above the hang name the subsystem.
2. `curl --max-time 5 http://localhost:9999/health` must return `{"status":"ok",...}`. Connection refused means nothing is listening; a `200` with an error body means the host is up but the app layer failed.
3. `ss -lptn 'sport = :9999'` must show `LISTEN`. No listener, no server — compare against the pid claim in `save/server.pid`.
4. Connect the webclient and run `look`: a fresh game answers with `limbo`. `You are nowhere.` means the character has no location (reseed or repuppet; see [Connecting: webclient and telnet](09-connecting.md)).
5. Check the pid claim: does the pid in `save/server.pid` exist and is it this server? Stale claims after a crash are normal — clear them by starting fresh, not by hand-editing the database.
6. Kill leftovers only as a last resort: `kill $(cat save/server.pid)`, then re-verify with steps 1–3. Never leave scratch servers running once triage is done.

## 2.1 Background daemon details

The detached child redirects stdout/stderr to `save/server.log` and writes crashes through `DaemonCrashLog`, so a child that dies during `DoStartup` still leaves a readable trail — the parent's readiness poll simply times out and points at the log. `DaemonDetach` owns the platform split (process-group detach on Unix, creation-flags detach on Windows) and `DaemonSpawner` owns the argument forwarding: every flag you passed to `start` (port, host, telnet-port) is forwarded to the child verbatim, which is why `--port 1234` on the parent produces a child listening on 1234 and a readiness poll against 1234.

```bash
cd mygame
./atheriz.sh start --port 1234 &
echo "parent returned, polling..."
sleep 16
curl --max-time 5 http://localhost:1234/health
```

Expected: the parent returns within the 15-second poll with a started banner naming the pid; the curl answers `{"status":"ok",...}`. If the parent instead reports failure, the child pid in the message is the process to look up in `save/server.log` — do not guess at other pids.

## 3.1 Stop transcripts

Clean stop of a live server:

```bash
cd mygame
./atheriz.sh stop
echo "exit=$?"
```

Expected: `exit=0`, the log ends with the shutdown save completing, and `save/server.pid` is released. A second `stop` immediately after reports no live server rather than killing anything — the claim is gone, so there is nothing to verify against.

Auth rejection (token mismatch after a manual `secret/` copy, for example):

```bash
cd mygame
./atheriz.sh stop
echo "exit=$?"
```

Expected: nonzero exit, a message naming the token rejection, and the server still running. Fix the token (delete `secret/admin.token` only while stopped, then start to regenerate — see [Hosting and configuration](08-hosting-configuration.md)), never escalate to a manual kill while the server is healthy.

## 5.1 Reload transcripts

Happy path after rebuilding the plugin:

```bash
cd mygame
./build.sh --reload
./atheriz.sh reload
```

Expected: build succeeds, the reload posts to `/_internal/hot_reload`, and the response reports success with the new game logic live — walk to a room your change touches and confirm the new behavior in-client.

Stale sources (you edited game code but forgot to build):

```bash
cd mygame
./atheriz.sh reload
```

Expected: a message from `PluginReloader` telling you to run `dotnet build` first. The live server keeps running the old logic untouched. Build, then reload again — or use `./build.sh --reload` to do both steps in one command.

Watchdog timeout (a very large world where rewire exceeds 60 seconds):

Expected: HTTP `200` with `{status:error}` naming the watchdog, while the reload continues in the background. Wait, watch `save/server.log` for the completion line, and do NOT fire a second reload on top — a reload already in progress makes the next one report `Reload already in progress; skipping.` instead of queueing.

## 8.1 `new` transcripts and identifier rules

Happy path:

```bash
./atheriz.sh new /path/to/MyGame
cd /path/to/MyGame
./atheriz.sh start --foreground
```

Expected: scaffold banner listing the written files, `save/` + `secret/` present with `0700` modes, then startup banners ending in `DoStartup completed`. Open the webclient, `look` → `limbo`.

Non-interactive (pipelines, containers):

```bash
ATHERIZ_SUPERUSER_USERNAME=admin ATHERIZ_SUPERUSER_PASSWORD='s3cret!' ./atheriz.sh new /path/to/MyGame
```

Expected: no prompts, credentials taken from the environment. Without the variables and without a terminal, `new` aborts rather than inventing credentials.

Identifier rules (last path segment becomes the game name):

| Name | Accepted? | Why |
|---|---|---|
| `MyGame` | Yes | Plain identifier. |
| `my_game2` | Yes | Underscores and digits (not leading) are fine. |
| `class` | No | C# keyword. |
| `pass` | No | Python keyword — both languages are checked. |
| `my-game` | No | Dashes are not identifier characters. |
| `` (empty) | No | Missing name. |

`--overwrite` on an occupied target:

```bash
./atheriz.sh new /path/to/MyGame --overwrite
```

Expected: live-server probe first (a running game on the target's ports aborts the wipe — stop it, then retry), then `save/**` plus any stale `admin.token` removed, then a clean scaffold. The probe is what makes `--overwrite` safe to retry: it never deletes a world out from under a running server.

## 11.1 Build-flag recipes

```bash
./build.sh              # full: engine (when stale) + plugin + web redeploy into web/
./build.sh --no-web     # fast iteration: plugin only, skip the client rebuild
./build.sh --web        # web redeploy only into web/ (skip the plugin build)
./build.sh --reload     # build, then hot-load into the live server
./build.sh --no-engine  # skip the engine rebuild (plugin + web only)
```

Staleness warnings from `atheriz.sh` name the direction: webclient sources newer than the staged copy mean rebuild the client; a game plugin newer than the engine sources can trigger an engine rebuild first (per-game `--no-engine` skips that freshness check). Either warning clears with a successful build.

## 12.1 Triage session transcript

A healthy game folder answers every step:

```bash
cd mygame
tail -5 save/server.log
curl --max-time 5 http://localhost:9999/health
ss -lptn 'sport = :9999'
cat save/server.pid
```

Expected: the log tail shows `DoStartup completed`; the curl prints `{"status":"ok",...}`; `ss` shows a `LISTEN` row for the server pid; the pid file matches that pid. In the webclient, `look` describes `limbo`.

A hung boot instead shows: log stuck at `Starting` with no `DoStartup completed`, `/health` answering (host up, world not loaded), `/ready` at `503 starting`. Read upward from the hang line — the subsystem named there is the one to investigate, and its settings are in [Hosting and configuration](08-hosting-configuration.md).

Under the hood: CLI parsing lives in `src/Atheriz.Server/Cli/AtherizCli.cs` with one handler file per command (`NewHandler`, `CreateHandler`, `ResetHandler`, `ReloadHandler`, `StopHandler`, `RestartHandler`, `TestHandler`, plus `DaemonSpawner`, `DaemonDetach`, `DaemonCrashLog`, `ShutdownClient`, `ProcessHelper`). Process identity checks (`IsServerProcess`), port-liveness probes (`IsProcessListeningOnPort`, `WaitForPortFreeAsync`), pid-claim management (`Infrastructure/PidFile.cs`), admin-token persistence (`Infrastructure/AdminToken.cs`), and the template scaffolder (`Infrastructure/GameTemplateGenerator.cs`, wired through `NewHandler`) back every behavior above. Boot and shutdown sequencing (`Infrastructure/ServerLifecycle.cs`: `DoStartup` / `DoShutdown` / `DoReload`) and the admin-route watchdogs (`Hosting/AdminRoutes.cs`) are detailed in [Hosting and configuration](08-hosting-configuration.md).

---
Next: [Hosting and configuration](08-hosting-configuration.md)
