# MyGame Template — Atheriz.GameTemplate

C# analogue of `atheriz new my_game` generated folder (ports `atheriz/new.py:784` + `atheriz/initial_setup.py:173`).

## What this is

This project is the template source for `atheriz-cs new <name>`. The CLI copies/scaffolds these files into the target folder. You can also use it as a starting point for a game project:

```bash
dotnet run --project src/Atheriz.Server -- new MyGame
cd MyGame
dotnet run --project ../src/Atheriz.Server -- --foreground
# or: dotnet run --project MyGame.csproj
```

## Files

- `GameSettings.cs` — mirrors `settings.py` CLASS_INJECTIONS + SAVE_PATH/SecretPath/ServerName defaults; points to `Atheriz.Core.Settings.AtherizSettings`
- `CustomObject.cs` — `class CustomObject : GameObject` with `AtCreate` placeholder (mirrors `object.py`)
- `CustomNode.cs` — `: Node` (mirrors `node.py`)
- `CustomAccount.cs` — `: Account` (mirrors `account.py`, global static salt wontfix)
- `CustomChannel.cs` — `: Channel` (mirrors `channel.py`, lazy _channel_cache)
- `CustomScript.cs` — `: Script` (mirrors `script.py`, before hooks advisory)

Each <100 lines, `namespace MyGame;`, referencing `Atheriz.Core`. They demonstrate the injection pattern via `[EntityReplacement]` attributes processed by `Atheriz.Core.Plugins.PluginLoader`.

Sample instance: `test/` at the repo root is a live game folder generated from this template
(`test/test.csproj`, `save/`, `secret/`, `web/`). It is private owner code: excluded from
`Atheriz.sln` and the main build per AGENTS.md — build it directly
(`dotnet build test/test.csproj`) when needed. Both share `namespace MyGame`; template is the scaffold source
(`GameTemplateGenerator` copies these stubs), sample is the runnable instance.

## How template discovery works

`Atheriz.Server.Infrastructure.GameTemplateGenerator.CreateGameFolder` scaffolds a new folder either by copying these source files (when available) or generating equivalent stubs inline. It mirrors `new.py:create_game_folder` checks: validates folder name is identifier, refuses if target exists and not empty unless `--overwrite`, creates `save/` (POSIX chmod 0o700 try/catch, no Windows ACLs), prints next-steps.

`PluginLoader` (`src/Atheriz.Core/Plugins/PluginLoader.cs`) is the C# analogue of `atheriz/atheriz.py:103 setup_game_folder` + `reloader.py:536` hot-reload. It uses collectible `AssemblyLoadContext(isCollectible:true)` scanning for `[EntityReplacement]` attributes, registering `Dictionary<Type,Type> Replacements`, with `Unload()` + `PatchLiveObjects` stub commenting live FieldInfo copy mirroring `reloader._apply_patch`.

## Discovery

At runtime, `setup_game_folder` equivalent would call `PluginLoader.Load("MyGame.dll")` to register replacements via DI. Hot-reload: `Unload` + `Load` + `PatchLiveObjects` (reflection FieldInfo copy, skipping session/listeners/command, re-ResolveRelations, excluded `Microsoft.*`/`Atheriz.Core` like `reloader._EXCLUDED_MODULES`). Webclient sync check stays off (`WEBCLIENT_SYNC_CHECK=False`) per spec.

## Conventions

- No webclient: `web/` is intentionally not scaffolded.
- No Windows ACL hardening: POSIX best-effort `File.SetUnixFileMode` with `try/catch`, `wontfix` on Windows (parent ACL inherits).
- Puppet snapshot only `is_pc`/`privilege_level` per AGENTS.md.
- Keep `GameSettings.SavePath = "save"` relative; `PathGuards.GuardSavePath` requires absolute or in-game-folder.
