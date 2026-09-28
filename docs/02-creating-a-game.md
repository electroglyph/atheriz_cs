# Creating a game

> Covers: `src/Atheriz.GameTemplate/` (csproj, `GameSettings.cs`, `AssemblyInfo.cs`, `GlobalUsings.cs`, `README.md`), per-game `atheriz.sh` / `atheriz.cmd` and `build.sh` / `build.cmd`, the `new` / `create` / `reload` CLI path. What the reader learns: scaffold a game folder, read every file in it, add two rooms, a takeable torch, and a wave command, then play them. The full object API is in [Objects: creating and using them](03-objects-library.md); links and nodes in [World and maps](04-world-maps.md).

## 1. Create the folder

Game-folder commands need the current directory to be the game folder, so create it first, then `cd` into it. All of `start` / `new` / `reset` / `restart` default to background; pass `--foreground` (or `-f`) to stay in-process.

```bash
./atheriz.sh new /tmp/MyGame --overwrite
cd /tmp/MyGame
```

`new` accepts `[--overwrite/--force] [--port] [--host] [--telnet-port] [--foreground]`. Without `--overwrite` (or `--force`), `new` refuses to touch an existing folder. With it, the folder contents are replaced — stop the server first if one is running from that folder.

`new` prompts for superuser credentials. For scripts and CI, pass them through the environment instead:

```bash
ATHERIZ_SUPERUSER_USERNAME=admin ATHERIZ_SUPERUSER_PASSWORD=admin1234 ./atheriz.sh new /tmp/MyGame --overwrite
```

The folder name becomes a C# identifier for the game project, so names with spaces or leading digits are rejected with an identifier error (C# plus Python keywords are checked). Creating directly on the filesystem root is refused (`DenyRoot`), and nesting a game folder inside another live game folder warns — pick an empty directory.

Success looks like this: a folder holding `MyGame.csproj`, `GameSettings.cs`, `README.md`, `atheriz.sh` / `atheriz.cmd`, `build.sh` / `build.cmd`, plus fresh `save/`, `secret/`, and `web/` directories.

## 2. Tour the scaffold

| File | What it is | What you do with it |
|---|---|---|
| `MyGame.csproj` | Game project on `net10.0` with a `ProjectReference` to the engine (`../Atheriz.Core`; generated folders get a NuGet-fallback variant instead). | Add game code files here; they compile into the plugin `reload` hot-loads. |
| `GameSettings.cs` | Game bootstrap: 39 lines, mostly comments around two `const` overrides (`ServerName`, `WebclientSyncCheck`). Doubles as the game-folder marker next to the csproj. | Conventional display name / sync flag for game code; server branding comes from `appsettings.json` (§2). |
| `AssemblyInfo.cs` | Assembly metadata for the game plugin. | Leave alone unless branding the build. |
| `GlobalUsings.cs` | Shared `using` lines in the repo template (generated folders rely on `ImplicitUsings` instead and do not get this file). | Copy one in if the game wants project-wide usings. |
| `README.md` | Per-game readme written by the scaffold. | Notes for anyone joining the game project. |
| `atheriz.sh` / `atheriz.cmd` | Forwarders to the engine launcher. | Run all server commands from the game folder; override the engine location with `ATHERIZ_ROOT` if the repo moved. |
| `build.sh` / `build.cmd` | Per-game build: `--no-web` / `--web` / `--reload` / `--no-engine`. | Rebuild the plugin, redeploy `web/`, optionally hot-load. |
| `web/` | Overridable client files served for this game. | Customize pages and assets; never hand-edit the host `wwwroot/` copy. |

The shape of `GameSettings.cs` (39 lines on disk; `namespace MyGame` pattern — a generated folder uses `namespace <YourGame>`):

```csharp
namespace MyGame;

public static class GameSettings
{
    public const string ServerName = "MyGame";
    public const bool WebclientSyncCheck = true;
}
```

`ServerName` is the scaffold's conventional display name for game code; the banner and `/health` read the server's own `ServerName` setting (`AtheriZ` by default — set `Atheriz:ServerName` in the game folder's `appsettings.json` to brand them, see [Hosting and configuration](08-hosting-configuration.md)). `WebclientSyncCheck=true` warns at startup when the game `web/` copy drifts from the engine webclient. `MyGame.csproj` plus `GameSettings.cs` are what make a directory a game folder — running a game-folder command elsewhere fails with `Cannot determine save path ...`.

## 3. EXAMPLE 1 — your first custom room

Add two rooms to the game setup (or seed) code: Town Square is the start room, North Road sits to its north, linked `north` / `south` so players can walk both ways. Rooms are `Node` objects (a `GameObject` subclass); the exit mechanics run through `Node.AddLink` (full detail in [World and maps](04-world-maps.md)).

```csharp
var squareCoord = new Coord("town", 0, 0, 0);
var roadCoord = new Coord("town", 0, 1, 0);

var townSquare = new Node(squareCoord, name: "Town Square", desc: "A cobbled square with a fountain in the middle.");
var northRoad = new Node(roadCoord, name: "North Road", desc: "A dusty road running south back to town.");

// Exits, both directions: Town Square --north--> North Road, North Road --south--> Town Square.
townSquare.AddLink(new NodeLink("north", roadCoord));
northRoad.AddLink(new NodeLink("south", squareCoord));
```

Then build and hot-load, and walk in:

```bash
./build.sh --no-web
./atheriz.sh reload
```

```
> look
Town Square
A cobbled square with a fountain in the middle.
Exits: north
> north
North Road
A dusty road running south back to town.
Exits: south
> south
Town Square
A cobbled square with a fountain in the middle.
Exits: north
```

If `look` shows `limbo`, the fresh-game start room, the new rooms did not load — confirm the snippet ran in setup/seed and that the build preceded `reload`. If a lookup misses, players see the standard message from `CommandHelpers.FormatCouldNotFind`, e.g. `Could not find 'fountain'.`. The full object API (names, descriptions, tags) is in [Objects: creating and using them](03-objects-library.md).

## 4. EXAMPLE 2 — a takeable object

Put a torch in Town Square. Placement is containment: create the object, then move it into the room with `MoveTo`. The standard item verbs handle the rest — no custom code needed. Look up nearby things from code with `Search(query)`.

```csharp
var torch = GameObject.Create("torch", "A wooden torch wrapped in oil-soaked cloth.");
torch.MoveTo(townSquare);

// Later, from code standing in the room:
var found = townSquare.Search("torch");
```

Build, reload, and play:

```bash
./build.sh --no-web
./atheriz.sh reload
```

```
> look
Town Square
A cobbled square with a fountain in the middle.
You see: torch.
Exits: north
> get torch
You picked up: torch.
> inventory
You are carrying: torch.
> drop torch
You dropped: torch.
```

`get`, `inventory`, and `drop` are the standard item verbs from [Commands: using and writing them](06-commands.md); containment rules (put, get, give, containers) are in [Objects: creating and using them](03-objects-library.md).

## 5. EXAMPLE 3 — your first custom command

Add a `wave` social command: a minimal `LoggedInCommand` subclass whose `Run` answers the caller, registered with one explicit `Add` line — never an Assembly scan. Room-wide announcements use `MsgContents` (full pattern in [Objects](03-objects-library.md) and [Hooks](05-hooks-scripts-text.md)); help text ships on the command via `Desc` so `help wave` works.

```csharp
public sealed class WaveCommand : LoggedInCommand
{
    public override string Key => "wave";
    public override string Desc => "Wave at everyone in the room. Usage: wave";
    public override bool UseParser => false;

    protected override void RunPuppetRaw(GameObject puppet, string raw, CancellationToken ct)
    {
        puppet.Msg("You wave.");
        puppet.ResolveLocationObject()?.MsgContents($"{puppet.Name} waves.", exclude: [puppet]);
    }
}

// Registration (explicit, one line per command):
myCmdSet.Add(new WaveCommand());
```

Build with hot-load in one step and try it next to the torch:

```bash
./build.sh --no-web --reload
```

```
> wave
You wave.
```

Everyone else in Town Square sees `MyChar waves.` The full authoring guide — parser arguments, subclass choice (`Command` vs `LoggedInCommand` vs `BuilderCommand`), aliases, targets, and errors — is in [Commands: using and writing them](06-commands.md); event hooks behind room announcements are in [Hooks, scripts, and text](05-hooks-scripts-text.md).

## 6. Iterate

The loop for all three examples above is the same:

1. Edit game code.
2. `./build.sh` (add `--no-web` for plugin-only speed while iterating on C#).
3. `./atheriz.sh reload` to hot-load the rebuilt plugin.
4. Try it in the client (`look`, `get torch`, `wave`).

`reload` never compiles — stale sources answer with "run `dotnet build` first", which means re-run `./build.sh` before reloading. When settings or ports change, `reload` is not enough: `restart` (stop plus start) instead. When the world itself needs a fresh start, `reset` wipes `save/` and reseeds — it always prompts `[y/N]` and has no force flag, by design. Never edit `save/database.sqlite3` by hand; build in game or code, then save (why is in [Saves and backups](10-persistence-saves.md)).

## 7. Invite someone

On the live server, create an account and character for the new player (no restart needed):

```bash
./atheriz.sh create friendaccount FriendChar s3cretPass
```

The new player opens the webclient, sees the connection banner, logs in with `connect friendaccount s3cretPass`, selects `FriendChar`, and arrives in the world — `look` shows Town Square once Example 1 is loaded. What happens between connect and puppet (banner, login, character select, quit) is in [Connecting: webclient and telnet](09-connecting.md); the CLI side (live-first `create`, offline fallback) is in [Running the server](07-server-lifecycle-cli.md).

---
Next: [Objects: creating and using them](03-objects-library.md)
