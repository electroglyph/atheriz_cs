# Getting started

> Covers: [`build.sh`](../build.sh), [`build.cmd`](../build.cmd), [`atheriz.sh`](../atheriz.sh), `src/Atheriz.Server/appsettings.json`, `webclient/`. What the reader learns: install prerequisites, build the engine, create a game folder, connect a first client, and check the server is healthy. Configuration detail lives in [Hosting and configuration](08-hosting-configuration.md); client detail in [Webclient: terminal client](12-webclient.md).

## 1. Overview

Follow this page top to bottom and you end with a running game: the server listens on `0.0.0.0:9999` (web) and `0.0.0.0:4444` (telnet), the webclient loads in a browser, and `look` shows `limbo` on a fresh game. The full tutorial continues in [Creating a game](02-creating-a-game.md).

## 2. Prerequisites

| Need | Version | Check | Notes |
|---|---|---|---|
| .NET SDK | 10.0.100 or newer (`global.json` pins `10.0.100`, `latestPatch`) | `dotnet --version` | Required for everything. |
| Node + npm | Node 18+ | `node --version`, `npm --version` | Only needed to build the webclient. |
| Python 3 | any recent 3.x | `python3 --version` | Only needed to redeploy the webclient into a game folder (`deploy.py` uses the standard library only). |
| rsync | any recent | `rsync --version` | Used by `build.sh` to stage the webclient. |

On Arch: `sudo pacman -S dotnet-sdk aspnet-targeting-pack nodejs npm` (`aspnet-targeting-pack` is required: without it the `Atheriz.Server` web project fails at restore with `NETSDK1226: Prune Package data not found`).

Verify the toolchain before building:

```bash
dotnet --version   # expect 10.0.100 or newer
node --version     # expect 18+
python3 --version  # any recent 3.x
```

Windows notes: use `build.cmd` instead of `build.sh` and `atheriz.cmd` instead of `atheriz.sh`. `build.cmd` builds Release only; `build.sh` builds Release and Debug. The commands below show the `.sh` form; the `.cmd` form takes the same arguments.

## 3. Build

From the repo root (where `Atheriz.sln` is):

```bash
./build.sh          # builds the webclient and the server
./build.sh --force  # rebuild webclient even if nothing changed
# Windows: build.cmd / build.cmd --force
```

The first run takes a bit (downloads webclient dependencies and copies fonts). The next run is fast if `webclient/src` has not changed. Root `build.sh` takes only `--force` and `--help`. `build.sh` builds both Release and Debug configurations; `build.cmd` builds Release only.

What gets built: the webclient bundle (staged into the host `wwwroot/`), then the solution — engine library plus server host. The launch scripts prefer the Release server DLL and fall back to Debug.

You can also build with plain `dotnet`:

```bash
dotnet build Atheriz.sln -c Release
```

Key packages (exact pins in `Directory.Packages.props`): `System.CommandLine` 2.0.0 for the CLI, `telnet_cs` 0.19.0 for the telnet server. Run `./atheriz.sh --help` to see every CLI command and its flags.

## 4. Your first game

All commands work through the launch scripts. Game-folder commands need the current directory to be the game folder, so `cd` into it first. All of `start` / `new` / `reset` / `restart` default to background; pass `--foreground` (or `-f`) to stay in-process.

```bash
./atheriz.sh new /tmp/MyGame          # create a new game folder
./atheriz.sh new /tmp/MyGame --overwrite  # replace an existing folder

# Non-interactive new (scripts, CI): superuser credentials via the environment
ATHERIZ_SUPERUSER_USERNAME=admin ATHERIZ_SUPERUSER_PASSWORD=admin1234 ./atheriz.sh new /tmp/MyGame --overwrite

cd /tmp/MyGame
./atheriz.sh create myaccount MyChar s3cretPass
./atheriz.sh start --foreground                 # runs on 0.0.0.0:9999 (web) + 0.0.0.0:4444 (telnet)
```

`new` prompts for superuser credentials unless the `ATHERIZ_SUPERUSER_USERNAME` / `ATHERIZ_SUPERUSER_PASSWORD` environment variables are set. `create` adds an account and character to the game: `create <account> <character> <password>` — the superuser from `new` can log in and play directly, and each further `create` adds another account. `start` launches the server; `stop` shuts it down, `reload` hot-loads rebuilt game code (never compiles — build first), and `reset` wipes the world and reseeds it (always prompts `[y/N]`, no force flag). Full detail is in [Running the server](07-server-lifecycle-cli.md).

Now open the webclient at `http://localhost:9999/` (or `/webclient`), log in as `myaccount`, select `MyChar`, and type:

```
look
```

Expect `limbo` — that is the smoke-test oracle on a fresh game. If you see `You are nowhere.` instead, the world did not seed or load; check `save/server.log` (next section) rather than retrying blindly.

Prefer telnet? Connect a terminal client to port `4444` with UTF-8 enabled — the banner, login, and character-select flow are the same, and `look` should likewise show `limbo`. Client setup for both paths is in [Connecting: webclient and telnet](09-connecting.md).

When you are done, stop the server from the game folder:

```bash
./atheriz.sh stop
```

`stop` shuts the server down gracefully (verified kill as fallback); never leave scratch servers running. If a foreground server is in the way, interrupt it first, then confirm nothing still listens with `ss -lptn 'sport = :9999'` before starting again.

## 5. Health and readiness

`/health` is liveness: it always returns `200` with `{"status":"ok",server:...}`, where `server` carries the `ServerName`. `/ready` returns `ok` only after startup completes, and `503` with `starting` while the server is still booting.

```bash
curl --max-time 5 http://localhost:9999/health
curl --max-time 5 http://localhost:9999/ready
ss -lptn 'sport = :9999'
```

`curl /health` should print `{"status":"ok",server:...}`; `ss` should show a LISTEN row for port `9999`. `/ready` prints `ok` once `DoStartup completed` is in the log, and `503` with `starting` before that. In practice: `/health` tells you the process is alive, `/ready` tells you it finished booting — scripts and supervisors should gate on `/ready` before sending players or `create` calls at it.

On any issue, read the game folder's `save/server.log` first. A healthy boot ends with `DoStartup completed`; a log stuck at `Starting` means startup hung before the world was ready. If the port is held by a leftover process, stop it via `./atheriz.sh stop` (or remove the stale claim with `kill $(cat save/server.pid)` only after confirming the server is truly dead). The ordered triage checklist (log → health → socket → `look` → pid claim) is in [Running the server](07-server-lifecycle-cli.md).

## 6. Project layout

```
Atheriz.sln
src/
  Atheriz.Core/          # engine library: objects, commands, maps, persistence, networking
  Atheriz.Server/        # host: CLI, Kestrel, websockets, telnet wiring (wwwroot/, web/)
  Atheriz.GameTemplate/  # template for `new`: GameSettings, launchers, build scripts
webclient/               # webclient source (terminal client + drawing/map editor)
atheriz.sh / atheriz.cmd # launch wrappers
build.sh / build.cmd     # build webclient + server
docs/                    # these docs
```

- `src/Atheriz.Core` is the engine library your game code builds against.
- `src/Atheriz.Server` is the host: it serves the webclient, accepts websocket and telnet connections, and runs the CLI (`new`, `start`, `create`, and friends).
- `src/Atheriz.GameTemplate` is the scaffold copied by `new` into each game folder.
- `webclient/` is the source of the browser client; built output ships inside the host and per-game `web/` folders.
- `atheriz.sh` / `atheriz.cmd` forward to the engine server DLL (Release preferred, Debug fallback); per-game copies do the same from inside game folders.
- `build.sh` wraps the webclient build plus `dotnet build`; per-game `build.sh` rebuilds the game plugin and redeploys `web/`.
- `docs/` holds these guides; the .NET 10 upgrade record sits alongside them.

## 7. Configuration in 30 seconds

Ports and paths live in `src/Atheriz.Server/appsettings.json` under the `Atheriz:` section:

| Key | Default | Meaning |
|---|---|---|
| `SavePath` | `save` | World database and logs, inside the game folder. |
| `SecretPath` | `secret` | Admin token, inside the game folder. |
| `ServerName` | `AtheriZ` | Shown on `/health` and the connection banner. |
| `ServerHostname` | `localhost` | Default host used when resolving binds. |
| `WebserverPort` | `9999` | Web port (`0.0.0.0:9999`). |
| `TelnetPort` | `4444` | Telnet port (`0.0.0.0:4444`). |
| `MaxCharacters` | `5` | Characters allowed per account. |
| `MapEnabled` | `true` | Serve the map view. |
| `LegendEnabled` | `true` | Serve the map legend. |

CLI flags (`--port`, `--host`, `--telnet-port`) and `ATHERIZ_*` environment variables override these without editing the file — for example, a custom web port for one run:

```bash
./atheriz.sh start --foreground --port 9999 --host 0.0.0.0
```

The `ATHERIZ_SUPERUSER_USERNAME` / `ATHERIZ_SUPERUSER_PASSWORD` variables are honored anywhere initial credentials are resolved — `new` and the seed paths such as `reset` — so the same non-interactive export works for scripted wipes.

Full precedence (CLI overlay, then `ATHERIZ_*` variables, then environment-specific JSON, then `appsettings.json`) plus TLS certificates and the per-game `web/` overrides are in [Hosting and configuration](08-hosting-configuration.md). Port and host changes need a `restart`, not a `reload` — `reload` hot-loads game code only, and never compiles. The `MapEnabled` / `LegendEnabled` toggles control the map view and legend the client shows; maps themselves are built in [World and maps](04-world-maps.md) and drawn in [Map editor](13-map-editor.md).

If a game-folder command fails with `Cannot determine save path ...` (or `... secret path ...`), the current directory is not a game folder — `cd` into the folder `new` created (the one holding the game `.csproj` and `GameSettings.cs`) and retry.

---
Next: [Creating a game](02-creating-a-game.md)
