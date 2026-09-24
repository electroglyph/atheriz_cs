> **⚠️ WORK IN PROGRESS — EXPERIMENT: this project is still a work in progress and an experiment, and everything here is 100% clanker generated.**

# AtheriZ — C# Port

C# port of `atheriz` (Python MUD server, v0.9.0) on **.NET 10** (C# 14, `net10.0`). Core engine is in `src/Atheriz.Core`, the server in `src/Atheriz.Server`, and game templates in `src/Atheriz.GameTemplate`. The webclient (terminal + drawing editor) is included.

## Prereqs

- **.NET 10 SDK** (`10.0.100` or newer, see `global.json`):
  ```bash
  dotnet --version
  ```
- **Node 18+** and **npm** — only needed to build the webclient:
  ```bash
  node --version
  npm --version
  ```
- **Python 3** — only needed to redeploy the webclient into a game folder (`deploy.py` uses the standard library only).
- **rsync** — used by `build.sh` to stage the webclient (`sudo pacman -S rsync` / `apt install rsync`).
  On Arch: `sudo pacman -S dotnet-sdk aspnet-targeting-pack nodejs npm`
  (`aspnet-targeting-pack` is required: without it the `Atheriz.Server` web
  project fails at restore with `NETSDK1226: Prune Package data not found`.)

## Build

From the repo root (where `Atheriz.sln` is):

```bash
./build.sh          # builds the webclient and the server
./build.sh --force  # rebuild webclient even if nothing changed
# Windows: build.cmd / build.cmd --force
```

The first run takes a bit (downloads webclient dependencies and copies fonts). The next run is fast if `webclient/src` hasn't changed.

You can also build with plain `dotnet`:

```bash
dotnet build Atheriz.sln -c Release
```

## Run

All commands work through the launch scripts:

```bash
./atheriz.sh --help
./atheriz.sh new /tmp/MyGame          # create a new game folder
./atheriz.sh new /tmp/MyGame --overwrite

# Windows: atheriz.cmd --help / atheriz.cmd new MyGame
```

That creates `MyGame.csproj`, `GameSettings.cs`, `CustomObject.cs`, etc., plus `save/`, `secret/` and `web/` (with the webclient) — and its own `atheriz.sh` / `atheriz.cmd` launchers plus `build.sh` / `build.cmd` build scripts (see below).

For a non-interactive `new` (scripts, CI), pass the superuser credentials via the environment instead of the prompts:

```bash
ATHERIZ_SUPERUSER_USERNAME=admin ATHERIZ_SUPERUSER_PASSWORD=admin1234 ./atheriz.sh new /tmp/MyGame --overwrite
```

From inside your game folder — every new game gets its own `atheriz.sh` /
`atheriz.cmd`, so you never need to go back to the repo root (they forward
to the engine; override its location with `ATHERIZ_ROOT` if it moved):

```bash
cd /tmp/MyGame
./atheriz.sh create myaccount MyChar s3cretPass
./atheriz.sh start --foreground                 # runs on 0.0.0.0:9999 (web) + 0.0.0.0:4444 (telnet)
./atheriz.sh start --foreground --port 9999 --host 0.0.0.0
./atheriz.sh stop --port 9999
./atheriz.sh reload
./atheriz.sh reset --yes
# Windows: atheriz.cmd create ... / atheriz.cmd start --foreground
```

## Build a game

Game folders also get their own `build.sh` / `build.cmd`. From inside the game folder:

```bash
./build.sh               # rebuild the game plugin (Release) + redeploy the webclient into the game
./build.sh --no-web      # plugin only
./build.sh --web         # webclient redeploy only
./build.sh --reload      # build, then hot-load the running server (any combo: e.g. --no-web --reload)
# Windows: build.cmd [--no-web] [--web] [--reload]
```

The plugin build is what `reload` picks up (reload never compiles — it skips stale sources with "run `dotnet build` first"). The web step is equivalent to running this from the repo root:

```bash
python webclient/deploy.py game --web-root "/tmp/MyGame/web"
```

If you prefer `dotnet` directly:

```bash
dotnet run --project src/Atheriz.Server -- --help
# game-folder commands need to keep your current directory as the game folder,
# so use the built dll:
dotnet build src/Atheriz.Server
dotnet src/Atheriz.Server/bin/Debug/net10.0/Atheriz.Server.dll create myaccount MyChar pass
```

The server also supports `restart` and `test` (`test [core] [args...]` forwards to the test runner).

`/health` is liveness; `/ready` returns `ok` only after startup completes (503 while starting).

## Test

From the repo root:

```bash
dotnet test Atheriz.sln -c Release              # full suite (~5,000 tests; required after server changes)
dotnet test tests/Atheriz.Core.Tests/Atheriz.Core.Tests.csproj -c Release --filter FullyQualifiedName~PortedAccountTests
```

During iteration, use `--filter` for focused tests; run the full suite once at the end.

## Configuration

Ports and paths are in `src/Atheriz.Server/appsettings.json` (`Atheriz:` section). Defaults: `save` / `secret` in the game folder, `ServerName AtheriZ`, web `0.0.0.0:9999`, telnet `0.0.0.0:4444`. The telnet server (via `telnet_cs`) sends `DO TTYPE` on connect, then `WILL SGA` / `WILL BINARY` / `DO NAWS` once negotiation advances; charset negotiation stays off and text is UTF-8 both ways.

You can override with `appsettings.Development.json` or `ATHERIZ_` environment variables (e.g. `ATHERIZ_SSL_CERTFILE` for TLS, `ATHERIZ_SUPERUSER_USERNAME` / `ATHERIZ_SUPERUSER_PASSWORD` for the initial superuser).

Game folders require `GameSettings.cs` + `*.csproj` (created by `new`). Running a game-folder command outside a game folder will fail with `Cannot determine database path` — create a game folder first.

### telnet_cs pin

`telnet_cs` is pre-1.0 and ships breaking changes between minor versions (its README calls this out), so the
package stays exact-pinned in `Directory.Packages.props` — float only deliberately. The Atheriz telnet adapter
(`TelnetCsWriter` / `TelnetProtocol`) targets the 0.19.0 contract: the ECHO API (`ServerSession.SetEchoAsync` /
`WriteWithEchoAsync`, new in 0.13.0), the clean-close guarantee (peer FIN / TLS close_notify unwind the
session), the latched handshake-deadline mapping (`TimeoutException`, never raw OCE), the end-of-stream drain
(no CR LF truncation), and the single verdict filter (`AcceptFilter` takes `AcceptDecision`; the bool form is
gone). 0.17.0–0.19.0 add nothing the adapter uses: 0.17 adds a client-side `CreateAsync` factory and latches
the TLS flag at session construction (MCCP correctly refused over TLS); 0.18 renames only client-side/relocated
types (`BaseClient` to `TelnetSessionBase`, `MccpWriteFilter` to `telnet_cs.IO`, `DuplexEnd` top-level) and folds
`ServerSession.SetTimeout` into the settable `Timeout` property (the adapter never calls it); 0.19 removes the
blocking `Client` constructors (`CreateAsync`-only; the adapter never constructs a client). No wire, preset, or
default changes.

## Project Layout

```
Atheriz.sln
src/
  Atheriz.Core/          # engine
  Atheriz.Server/        # host (wwwroot/, web/)
  Atheriz.GameTemplate/  # template for `new`
webclient/               # webclient source (vite, xterm.js, drawing editor)
tests/
  Atheriz.Core.Tests/    # engine + server tests
atheriz.sh / atheriz.cmd # launch wrappers
build.sh / build.cmd     # build webclient + server
```
