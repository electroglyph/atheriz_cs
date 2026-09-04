this is just an experiment with letting a clanker mess around

# AtheriZ — C# Port

C# port of `atheriz` (Python MUD server, v0.9.0) on **.NET 8**. Core engine is in `src/Atheriz.Core`, the server in `src/Atheriz.Server`, and game templates in `src/Atheriz.GameTemplate`. The webclient (terminal + drawing editor) is included.

## Prereqs

- **.NET 8 SDK** (`8.0.130` or newer, see `global.json`):
  ```bash
  dotnet --version
  ```
- **Node 18+** and **npm** — only needed to build the webclient:
  ```bash
  node --version
  npm --version
  ```
  On Arch: `sudo pacman -S aspnet-runtime-8.0 nodejs npm`

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

That creates `MyGame.csproj`, `GameSettings.cs`, `CustomObject.cs`, etc., plus `save/`, `secret/` and `web/` (with the webclient).

From inside your game folder:

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

If you prefer `dotnet` directly:

```bash
dotnet run --project src/Atheriz.Server -- --help
# game-folder commands need to keep your current directory as the game folder,
# so use the built dll:
dotnet build src/Atheriz.Server
dotnet src/Atheriz.Server/bin/Debug/net8.0/Atheriz.Server.dll create myaccount MyChar pass
```

The server also supports `restart` and `test`.

## Test

From the repo root:

```bash
dotnet test Atheriz.sln              # all ~1955 tests, ~90s
dotnet test --filter Ported          # just the ported suite (144 Python files)
dotnet test --filter PortedAccountTests
```

## Configuration

Ports and paths are in `src/Atheriz.Server/appsettings.json` (`Atheriz:` section). Defaults: `save` / `secret` in the game folder, `ServerName AtheriZ`, web `0.0.0.0:9999`, telnet `0.0.0.0:4444`.

You can override with `appsettings.Development.json` or `ATHERIZ_` environment variables (e.g. `ATHERIZ_SSL_CERTFILE` for TLS).

Game folders require `GameSettings.cs` + `*.csproj` (created by `new`). Running a game-folder command outside a game folder will fail with `Cannot determine database path` — create a game folder first.

## Project Layout

```
Atheriz.sln
src/
  Atheriz.Core/          # engine
  Atheriz.Server/        # server + webclient (wwwroot/, web/templates/)
  Atheriz.GameTemplate/  # template for `new`
  webclient/             # webclient source (vite, xterm.js, drawing editor)
tests/
  Atheriz.Core.Tests/    # ported + infra tests
atheriz.sh / atheriz.cmd # launch wrappers
build.sh / build.cmd     # build webclient + server
```
