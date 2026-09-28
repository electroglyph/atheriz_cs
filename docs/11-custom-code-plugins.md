# Custom code and hot reload

> Covers: `src/Atheriz.Core/Plugins/` (`PluginLoader`, `GamePluginLoadContext`, `PluginReloader`, `EntityReplacementAttribute`, `IMigrateFrom`), `src/Atheriz.Core/IGameSetup.cs`, `src/Atheriz.Server/Infrastructure/GameTemplateGenerator.cs`, per-game `build.sh`/`build.cmd`. You learn where game code lives, how to substitute engine types while keeping live state, and how to rebuild and hot-reload a running server. The HTTP side of reload (route, caps, client flags) lives in [Running the server](07-server-lifecycle-cli.md); what a save guarantees lives in [Saves and backups](10-persistence-saves.md).

## 1. Plugin model: your game is a loadable assembly

Your game project is a plain C# class library. The server loads its
compiled dll into a collectible assembly-load context at boot
(`src/Atheriz.Core/Plugins/PluginLoader.cs`, context in
`src/Atheriz.Core/Plugins/GamePluginLoadContext.cs:13-18`):

- One collectible context per load (`isCollectible: true`), so an old
  plugin version can unload on reload instead of pinning memory forever.
- The loader records `[EntityReplacement]` markers found in the assembly
  as old-type to new-type pairs (applied in §2 and §3).
- Loading at boot is best-effort
  (`PluginReloader.LoadGameAssembliesAtBoot`,
  `src/Atheriz.Core/Plugins/PluginReloader.cs:704`). When no game
  assembly is found the server keeps going engine-only and prints:

  `[Boot] No game assembly discovered — running engine-only. Build the game project first (dotnet build) so its dll is discoverable next to the game folder.`

- A plugin that fails to load never stops the server. The failure is
  logged and boot continues with engine types.
- Unload path: `PluginLoader.Unload()` drops the context,
  `WaitForUnload()` confirms collection, `Dispose()` releases. Live
  patched instances root the old context, so unload cannot strand
  half-migrated objects — reload patches instances first (§2) and only
  then unloads the previous version.

Discovery in game code is always explicit: commands register with
`Add(new MyCommand())` (never an assembly scan), and world setup goes
through `IGameSetup` (§6). Game code never scans assemblies for types.
At boot the engine discovers the game assembly next to the game folder
and then discovers new plugin modules inside it; both discovery steps are
best-effort and log-and-continue, so a half-built plugin directory still
boots the server on engine types.

What reload does not do, explicitly:

- It does not compile. Build first (§5); reload hot-loads dlls.
- It does not reseed the world. New-game seeding runs at `new`/`reset`
  time, never on reload.
- It does not touch `save/`. The database is written on checkpoints and
  shutdown (→ [Saves and backups](10-persistence-saves.md)), not by the
  reloader.
- It does not drop connections. Sessions, sockets, and puppets stay up;
  only the code behind the objects changes.

## 2. Reload: rebuild first, then hot-load

Reload swaps the running plugin for a freshly built dll without dropping
connections. It never compiles. When the dll on disk is older than your
sources, reload skips it and prints
(`src/Atheriz.Core/Plugins/PluginReloader.cs:627`):

`[HotReload] Skipping {name}: no up-to-date built dll (run 'dotnet build' first; reload never builds).`

So the loop is always the same: edit, `./build.sh` (or
`./build.sh --reload`, §5), verify in-game. The mechanics behind one
reload, step by step:

1. `POST /_internal/hot_reload` arrives at
   `src/Atheriz.Server/Hosting/AdminRoutes.cs`. A 60-second watchdog
   wraps the work (`AdminRoutes.cs:58`). When reload is still running
   after 60 seconds, the route answers success status with an error body
   (`Reload timed out after 60s; still running in background.`) and logs
   `[HotReload] Reload exceeded 60s watchdog; continuing in background.`
   The reload keeps running — the watchdog only frees the HTTP call.
2. The watchdog lives in `AdminRoutes`. It does not live in
   `PluginReloader`, and it does not live in `ServerEvents`. Account
   creation uses the same 60-second shape on the same file.
3. `PluginReloader.ReloadGameLogicAsync`
   (`src/Atheriz.Core/Plugins/PluginReloader.cs:673`, with shorter
   overloads that take just the ticker and settings, or just settings)
   loads the new plugin dll into a fresh collectible context and applies
   every recorded replacement. For each old-type to new-type pair it
   calls `PatchLiveObjects(oldType, newType)`
   (`PluginReloader.cs:261`), which rebuilds each live instance as the
   new type, runs `IMigrateFrom` migration where the new type offers one
   (§3), calls `ResolveRelations()` on the result, and re-registers tick
   methods with the ticker. It returns the count of patched instances.
4. `RewireReferences` then swaps every reference to each replaced
   object: channel listeners (`ReplaceListener`), map entries
   (`ReplaceMapEntries`), grid node values (`ReplaceNodeValue`, guarded
   by a node-type check), and puppet references held by sessions
   (`ReplacePuppetRefs`).
5. `ServerLifecycle.DoReload`
   (`src/Atheriz.Server/Infrastructure/ServerLifecycle.cs:100`) finishes
   the job on the server side. `DoReload` lives in `ServerLifecycle`
   (next to `DoStartup`/`DoShutdown`), not in `PluginReloader` — the
   reloader handles objects, the lifecycle handles the server.

After a reload, walk the world and confirm behavior end to end: `look`
at Town Square, stroll up North Road, pick up the torch, `wave` at a
neighbor. Anything stale — an old description, a burn time that reset —
means the migration missed a field. Fix `MigrateFrom` and reload again.

## 3. Example: substitute a type without losing live state

This is the centerpiece of the plugin system. Suppose the tutorial game
shipped a plain `Torch` on North Road whose remaining burn time lives in
a property:

```csharp
using Atheriz.Core;

namespace MyGame;

public class Torch : GameObject
{
    public int RemainingMinutes { get; set; } = 60;
}
```

Players are mid-session; torches are lit all over North Road. Version 2
renames and reshapes the type into `SmoulderingTorch` with its own tick
behavior. Without migration, reload would rebuild every torch from
scratch and every flame would reset to 60 minutes. Instead, mark the new
type as the replacement for the old one and carry the state across:

```csharp
using Atheriz.Core;
using Atheriz.Core.Plugins;

namespace MyGame;

[EntityReplacement(typeof(Torch), typeof(SmoulderingTorch))]
public sealed class SmoulderingTorch : GameObject, IMigrateFrom<Torch>
{
    public int RemainingMinutes { get; set; } = 60;

    public void MigrateFrom(Torch old)
    {
        RemainingMinutes = old.RemainingMinutes;
    }
}
```

How the pieces fit:

- `[EntityReplacement(BaseType, ReplacementType)]`
  (`src/Atheriz.Core/Plugins/EntityReplacementAttribute.cs:9-20`) may sit
  on the replacement class or at assembly level, and may repeat
  (`AllowMultiple`). The same mechanism substitutes engine types: pass an
  engine type as the base and your subclass as the replacement, and live
  instances of the engine type are rebuilt as your type on reload.
- `IMigrateFrom` (`src/Atheriz.Core/Plugins/IMigrateFrom.cs:13-15`)
  carries `void MigrateFrom(GameObject old)`; the generic
  `IMigrateFrom<TOld>` (`IMigrateFrom.cs:18-26`) adds the typed
  `void MigrateFrom(TOld old)` and forwards the untyped call. Prefer the
  generic form — the reloader selects it when `TOld` matches, and you get
  the old state without casts.
- A throwing `MigrateFrom` keeps the old instance live. Migration runs
  per object during `PatchLiveObjects`, so one bad torch never blocks the
  rest of the reload; the failure is logged and that object stays as it was.
- Copy every field that matters for play: burn time, lit/unlit, custom
  tags. Anything you skip resets to the new type's defaults — that is the
  usual cause of "reload ate my torch" reports.

When several replacements belong together, declare them once at assembly
level instead of scattering attributes across classes (the attribute
allows multiples on assemblies too):

```csharp
using Atheriz.Core.Plugins;
using MyGame;

[assembly: EntityReplacement(typeof(Torch), typeof(SmoulderingTorch))]
```

Class-level marks the common case (one replacement next to its type);
assembly-level collects a game's whole substitution list in one file.
Both feed the same old-type to new-type pairs at reload.

Ship it and verify in-game:

```bash
./build.sh --reload
```

Expected: the plugin builds in Release, the running server hot-loads it,
and back in the client nothing visibly changes — the North Road torch is
still lit with the same minutes left, because the state migrated instead
of resetting. Confirm with `look` at the torch, then `wave` to prove the
session survived:

```bash
> look torch
A smouldering torch. It has about 40 minutes left.
> wave
You wave.
```

(The exact burn-time line depends on your description text; the point is
the number carried over from before the reload instead of resetting.)

## 4. Template scaffold: what `new` builds for you

`GameTemplateGenerator.CreateGameFolder`
(`src/Atheriz.Server/Infrastructure/GameTemplateGenerator.cs:13`) runs
these stages in order:

1. Identifier check: the game name must be a valid C# identifier and must
   not collide with C# or Python keywords.
2. Nested-folder warning when the target sits inside another game folder
   or the engine tree.
3. `DenyRoot` (`src/Atheriz.Core/Utils/PathGuards.cs:82`): refuses
   filesystem roots and other unsafe targets.
4. Exists/overwrite rules: an existing folder needs `--overwrite`, which
   holds the path while scaffolding runs.
5. Credential prompt and validation for the superuser account — or the
   non-interactive `ATHERIZ_SUPERUSER_USERNAME` /
   `ATHERIZ_SUPERUSER_PASSWORD` environment pair, which
   `InitialSetup.ResolveCredentials` also honors, so the variables are not
   `new`-only.
6. Live-server probe: refuses to scaffold over a running server, wipes
   the `save/` leaf, and deletes a stale `secret/admin.token`.
7. `Scaffold()`: writes the project files (table below).
8. `CopyWebFolder()` (`GameTemplateGenerator.cs:725`): stages the default
   web overrides into the game `web/` folder.
9. Creates `save/` and `secret/` with private permissions (0700 on
   POSIX) and writes `.gitignore` covering `save/`, `secret/`, `bin/`,
   and `obj/`.
10. `InitialSetup.RunSetup` with best-effort plugin load: a discovered
    game `IGameSetup` runs when one exists, otherwise the engine template
    setup seeds the world.
11. Success banner with next steps: start the server, connect, `look`.

Scaffolded files:

| File | What it is |
|---|---|
| `<Game>.csproj` | Your plugin project (`net10.0`). References the engine by upward-search `ProjectReference`; generated folders get a NuGet-fallback variant instead. This is where all game code lives. |
| `GameSettings.cs` | `namespace <Game>;` with only differs-from-default values: `public const string ServerName = "<Game>"` and `public const bool WebclientSyncCheck = true` (game-code convention; server branding comes from `appsettings.json`, §4 of [Hosting and configuration](08-hosting-configuration.md)). |
| `AssemblyInfo.cs` | Assembly metadata for the plugin. |
| `README.md` | Run and rebuild notes for this game folder. |
| `atheriz.sh` / `atheriz.cmd` | Launcher forwarders with `ATHERIZ_ROOT` override support. |
| `build.sh` / `build.cmd` | Per-game build (`--no-web` / `--web` / `--reload` / `--no-engine`, §5). |
| `web/` | Your overridable client files (→ [Webclient and map editor](12-webclient-editor.md)). |

Code-generation note: `BuildParamList` (`GameTemplateGenerator.cs:381`),
`BuildArgList` (`:434`), `FriendlyType` (`:329`), and `EscapeCsString`
(`:368`) use reflection only to format generated declaration text —
parameter lists and default values in scaffolded code. Reflection is never
used to invoke game code; runtime dispatch stays explicit (`Add`,
`switch`/`is`). That carve-out belongs to engine tooling, not to game
code (§8).

## 5. Per-game build: flags and engine freshness

Run from the game folder:

| Flags | Meaning |
|---|---|
| (none) | Full pass: engine freshness check, plugin Release build, web redeploy. |
| `--no-web` | Plugin only; skip the web redeploy. The quick code-iterate loop. |
| `--web` | Web only; redeploy the client without rebuilding the plugin. |
| `--reload` | After a successful build, forward to the sibling `atheriz` wrapper to hot-load the running server. |
| `--no-engine` | Trust the current server dll; skip the engine freshness check. |

- Engine-staleness rebuild: the script detects a stale server dll and
  rebuilds the engine first, so a game folder created by an older install
  never hot-loads a plugin against a mismatched server.
- The plugin itself always builds Release. `--reload` discovers the
  Release dll (Debug as fallback) and hot-loads it — the same reload as
  §2, so the never-compiles rule applies: `--reload` without a build step
  hot-loads whatever dll is already there.
- Web redeploy ships the client through `webclient/deploy.py` — full
  detail in [Webclient and map editor](12-webclient-editor.md).
- Folders made by `new` get the same script with the same flags from day
  one, so the tutorial loop (edit, `./build.sh --reload`, playtest) works
  identically in every game folder.

A full pass with reload looks like this (schematic — exact build lines
come from the compiler, the shape is what matters):

```bash
$ ./build.sh --reload
Checking engine freshness... up to date.
Building plugin (Release)... succeeded.
Redeploying webclient... done.
Reloading running server... reloaded.
```

When the engine check reports stale instead, the script rebuilds the
server dll first and the run takes longer once. When the plugin build
fails, nothing reloads — fix the errors and run the script again. The
running server is never left half-upgraded: a failed build simply leaves
the previous plugin in place.

## 6. New end-to-end: folder to running game

Flag-by-flag CLI detail lives in [Running the server](07-server-lifecycle-cli.md);
the mechanics live here. Non-interactive creation with environment
credentials:

```bash
ATHERIZ_SUPERUSER_USERNAME=admin ATHERIZ_SUPERUSER_PASSWORD='s3cret-pass' ./atheriz.sh new mygame
cd mygame
./atheriz.sh start --foreground
```

Success looks like this: a folder holding `<Game>.csproj`,
`GameSettings.cs`, the launchers, `save/`, `secret/`, and `web/`; a
running server; `save/server.log` showing `DoStartup completed`; and a
client where you can log in with the superuser credentials and `look`
around the starting rooms. Re-running over the same folder needs
`--overwrite` (which re-runs the wipe stages from §4, step 6):

```bash
ATHERIZ_SUPERUSER_USERNAME=admin ATHERIZ_SUPERUSER_PASSWORD='s3cret-pass' ./atheriz.sh new mygame --overwrite
```

From here the authoring loop from [Creating a game](02-creating-a-game.md)
takes over: add rooms and the torch (§3), register the `wave` command
(→ [Commands](06-commands.md)), rebuild, reload, playtest.

## 7. Under the hood: what the sections above leave out

Section 1 named the loader; this section adds only what it omitted:

- Context lifetime: one collectible context per load — an assembly
  identity cannot be reloaded in place, so every reload allocates a fresh
  context and unloads the old one after patching. Contexts are named
  `game-{guid}`, so two loads never share an identity even for the same
  dll path.
- Initializers do not run eagerly on collectible-context loads: game
  modules must not depend on static-constructor side effects at load
  time. Registration happens through the explicit paths (§1, §6).
- Tick re-registration: after patching, each replacement re-registers its
  `AtTick` with the ticker, so ticking objects (the tutorial torch) keep
  ticking across reload with no gap handling needed in game code.
- Game-setup dispatch: `IGameSetup.DoSetup(SetupOptions)` receives one
  record (`SavePath`, optional `Username`/`Password`/`SecretPath`,
  `Prompt`, optional `Input`) instead of positional strings, and CLI call
  sites pass the discovered game setup explicitly into
  `InitialSetup.RunSetup` (`src/Atheriz.Core/InitialSetup.cs:71`) — the
  engine keeps no static slot for game types and never names them.
- Save mechanics after a scaffold or wipe live in [Saves and backups](10-persistence-saves.md);
  threading rules for game code live in [Engine internals](13-engine-internals.md).

## 8. Rules for game code (never-touch list)

- No assembly scanning: register commands with explicit `Add`, branch
  with `switch`/`is`.
- No `System.Reflection` in game code. The declaration-text helpers in §4
  are engine tooling, not a pattern to copy.
- Keep hook and tick handlers brief. Never `Thread.Sleep` or block — game
  logic runs on shared pool and ticker threads
  (→ [Engine internals](13-engine-internals.md)).
- Mutate the world only through `GameObject` and command APIs, so locks,
  dirty flags, and saves stay coherent.
- Never add a project reference to, or a `using` of, another game project
  or private game code. Your game references the engine only.

---
Next: [Webclient and map editor](12-webclient-editor.md)
