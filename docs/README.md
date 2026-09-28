# Atheriz-CS docs

> Covers: [`../README.md`](../README.md), [`../CHANGELOG.md`](../CHANGELOG.md), this `docs/` folder. Start here: what this project is, which document to read for your goal, and how the files fit together. This page summarizes only — the guides it links to carry the detail.

Atheriz-CS is a C# port of the `atheriz` Python MUD server on .NET 10 (C# 14, `net10.0`): the engine lives in `src/Atheriz.Core`, the server host in `src/Atheriz.Server`, game scaffolding in `src/Atheriz.GameTemplate`, and the webclient (terminal + drawing editor) in `webclient/`. Engine 0.24.0.0, webclient 1.5.0.

"(everything here is 100% clanker generated, but i've tried to do it as sanely as possible)"

## 1. What this is

A multi-user dungeon (MUD) engine and server: builders describe rooms, objects, and commands in C#; players connect over the webclient or telnet and play in a shared persistent world. The world lives in memory while the server runs and is written to the database only on save — checkpoints and shutdown — so operating the server means understanding that save discipline, not just the CLI.

These docs teach the user path (install, create a game, run a server, connect, build a world, write game code, operate and back up a live game) and document each subsystem thoroughly, with runnable C# examples and "Under the hood" sections naming the engine classes behind the behavior. Every library guide answers "how do I use this" first and names the mechanism second. Behavior history per release is in the [changelog](../CHANGELOG.md).

Current pins: engine 0.24.0.0, webclient 1.5.0, `System.CommandLine` 2.0.0, `telnet_cs` 0.19.0, .NET SDK 10.0.100 (`latestPatch`).

How the guides are written: each one answers "how do I use this" with copy-paste shell transcripts and runnable C# snippets, then names the engine classes and files behind the behavior in "Under the hood" sections. Player-visible messages are quoted verbatim — keep them byte-identical in custom code rather than paraphrasing. Every guide ends with a link to the next one, so the whole set reads as one chain from first build to engine internals.

## 2. How to read these docs

Pick the path that matches your goal. Each path is ordered — read top to bottom. Every file ends with a `Next:` link to the following file, so staying on the chain is automatic.

- Play / try it: [Getting started](01-getting-started.md) → [Connecting](09-connecting.md) → [Commands](06-commands.md).
  Build once, connect a client, learn what to type.
- Host it: [Getting started](01-getting-started.md) → [Running the server](07-server-lifecycle-cli.md) → [Hosting and configuration](08-hosting-configuration.md) → [Saves and backups](10-persistence-saves.md).
  CLI lifecycle, ports and TLS, then the save discipline and backups.
- Build a game: [Getting started](01-getting-started.md) → [Creating a game](02-creating-a-game.md) → [Objects](03-objects-library.md) → [World and maps](04-world-maps.md) → [Hooks, scripts, and text](05-hooks-scripts-text.md) → [Commands](06-commands.md) → [Custom code and hot reload](11-custom-code-plugins.md).
  Tutorial first, then one library guide per topic, ending with shipping code.
- Engine deep-dives: [Engine internals](13-engine-internals.md) → [Saves and backups](10-persistence-saves.md) → [Connecting](09-connecting.md) → [Hosting and configuration](08-hosting-configuration.md).
  Threading and locks first, then the subsystems that depend on them.

## 3. File map

One line each — the linked file carries the detail.

- [Getting started](01-getting-started.md) — Install, build, first game folder, first connection.
- [Creating a game](02-creating-a-game.md) — Worked tutorial: scaffold, first rooms, torch object, wave command.
- [Objects: creating and using them](03-objects-library.md) — Create, find, move, and message `GameObject`s; containment, tags, accounts, channels, doors.
- [World and maps](04-world-maps.md) — Areas, grids, nodes, exits and doors, build commands, legend, game time.
- [Hooks, scripts, and text](05-hooks-scripts-text.md) — Event hooks, ticking scripts, locks, `$func()` text, verbs, menus.
- [Commands: using and writing them](06-commands.md) — Player and builder command catalog plus the command-authoring guide.
- [Running the server](07-server-lifecycle-cli.md) — CLI operator guide: start, stop, restart, reload, reset, create, new.
- [Hosting and configuration](08-hosting-configuration.md) — Web vs headless hosting, ports, TLS, routes, settings.
- [Connecting: webclient and telnet](09-connecting.md) — Connect with the webclient or a telnet client; login and puppet flow.
- [Saves and backups](10-persistence-saves.md) — Save discipline, `save/` layout, backup and restore, autosave.
- [Custom code and hot reload](11-custom-code-plugins.md) — Game plugin projects, entity replacement, per-game builds, reload.
- [Webclient and map editor](12-webclient-editor.md) — Terminal client, drawing and map editor, fonts, deploy.
- [Engine internals](13-engine-internals.md) — Threads, locks, pathfinding, clocks, helpers, logging, boot order.

## 4. Repo map

Copied from the project README layout, extended with `docs/` itself:

```
Atheriz.sln
src/
  Atheriz.Core/          # engine
  Atheriz.Server/        # host (wwwroot/, web/)
  Atheriz.GameTemplate/  # template for `new`
webclient/               # webclient source (vite, xterm.js, drawing editor)
atheriz.sh / atheriz.cmd # launch wrappers
build.sh / build.cmd     # build webclient + server
docs/                    # these docs
```

- `Atheriz.sln` — Builds everything: engine, host, and template.
- `src/Atheriz.Core/` — The engine library: objects, commands, maps, persistence, networking.
- `src/Atheriz.Server/` — The host: CLI, Kestrel, websockets, telnet wiring, served `wwwroot/` and overridable `web/`.
- `src/Atheriz.GameTemplate/` — The scaffold copied by `new` into each game folder.
- `webclient/` — Browser client source: terminal client plus the drawing and map editor.
- `atheriz.sh` / `atheriz.cmd` — Launch wrappers; per-game copies forward to the engine.
- `build.sh` / `build.cmd` — Build the webclient and the server (`build.sh` builds Release and Debug; `build.cmd` builds Release only).
- `docs/` — These guides.

What `new` creates next to that tree — a game folder is separate from the repo:

```
MyGame.csproj
GameSettings.cs
atheriz.sh / atheriz.cmd   # forwarders; ATHERIZ_ROOT overrides the engine location
build.sh / build.cmd       # --no-web / --web / --reload / --no-engine
save/                      # database.sqlite3, server.log, server.pid
secret/                    # admin.token
web/                       # this game's client files
```

- `save/` holds the world database plus the logs and pid file the operator guides triage from.
- `secret/` holds the admin token the CLI uses to talk to a running server.
- `web/` is the per-game client copy — customize here, never in the host `wwwroot/`.

## 5. Related docs

User-relevant root files, one line each — the new guides above are the curated path, these remain the archive.

- [Project README](../README.md) — Install, build, run, and configuration overview.
- [Changelog](../CHANGELOG.md) — Behavior history per release (current engine 0.24.0.0).
- [Save/load guidance](../persist.md) — Persistence notes for game developers.
- [Telnet notes](../telnet.md) — Telnet negotiation behavior on the wire.
- [Webclient notes](../webclient.md) — Webclient behavior notes and fixes.
- [Background process notes](../background.md) — Start and stop behavior details.

Start: [Getting started](01-getting-started.md)
