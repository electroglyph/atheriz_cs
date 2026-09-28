# World and maps: areas, nodes, links

> Covers: `src/Atheriz.Core/Objects/Node.cs`, `Node.Links.cs`, `Node.Settable.cs`, `NodeArea.cs`, `NodeGrid.cs`, `NodeLink.cs`, `Globals/NodeHandler.cs`, `NodeHandler.Partial.cs`, `Globals/MapHandler.cs`, `MapInfo.cs`, `MapEdit*.cs` (4), `Globals/GameTime.cs`, `Settings/AtherizSettings.cs` (MapEnabled/LegendEnabled). You learn to lay out areas and rooms, link them, edit maps in-game, and run timed behavior; tick scripts in full are in [Hooks, scripts, and text](05-hooks-scripts-text.md).

## 1. Areas, grids, nodes in one page

The world is a hierarchy: area → grid → node. An area is a region (a town, a dungeon). A grid is a floor or zone inside an area. A node is a single room. Every node sits at a `Coord(Area, X, Y, Z)` — area name plus three integers — and you read a coord as "area X,Y,Z", e.g. `town 0,1,0` is one step north of `town 0,0,0`.

```text
town (area)
└── ground (grid)
    ├── town 0,0,0  Town Square
    ├── town 0,1,0  North Road
    └── town 1,0,0  Market Stall
```

Areas exist so maps stay small and moves stay cheap; grids exist so multi-floor buildings work (Z = level). Nodes are `GameObject`s, so everything in [Objects](03-objects-library.md) — names, descriptions, contents, locks — applies to rooms too.

## 2. Example: build three linked rooms in code

Create the area, a grid, three nodes, and link them both ways. Links are one direction each: `Node.AddLink(NodeLink)` publishes a single exit, so add the reverse explicitly (or `AddLinkIfAbsent(name, factory)` when seeding idempotently).

```csharp
var handler = GlobalServices.GetNodeHandler();

var square = new Node(new Coord("town", 0, 0, 0));
square.Name = "Town Square";
square.Desc = "A cobbled square with a dry fountain at its center.";
handler.AddNode(square); // creates the town area and grid as needed

var road = new Node(new Coord("town", 0, 1, 0));
road.Name = "North Road";
road.Desc = "A dusty road leading north out of town.";
handler.AddNode(road);

var market = new Node(new Coord("town", 1, 0, 0));
market.Name = "Market Stall";
market.Desc = "A canvas awning shading crates of vegetables.";
handler.AddNode(market);

square.AddLink(new NodeLink("north", new Coord("town", 0, 1, 0), ["n"]));
road.AddLink(new NodeLink("south", new Coord("town", 0, 0, 0), ["s"]));
square.AddLink(new NodeLink("east", new Coord("town", 1, 0, 0), ["e"]));
market.AddLink(new NodeLink("west", new Coord("town", 0, 0, 0), ["w"]));
// Changes live in memory; the next checkpoint writes them (see [Saves and backups](10-persistence-saves.md)).
```

Walk-through transcript:

```text
> look
Town Square
A cobbled square with a dry fountain at its center.
Exits: north, east
> north
North Road
A dusty road leading north out of town.
Exits: south
> south
Town Square
A cobbled square with a dry fountain at its center.
Exits: north, east
```

Occupied-graft rule as outcome: two rooms cannot stack on one coord — creating a node at an occupied coord is refused (or grafts per handler policy) rather than silently replacing the room that is already there. Coords are caller-validated: a bad coord errors at the call site, not deep in the engine.

## 3. Exits and doors

Exits are links; doors are objects that gate a link. A door sits on an exit direction and adds open/locked/remove flows around it. Direction names are case-insensitive (`OrdinalIgnoreCase`): `North`, `NORTH`, and `north` all match the same exit.

```csharp
// Hang a door on the square's north exit: Door.Create takes both coords
// plus both exit names, and the door must be registered with the handler.
var gate = Door.Create(
    new Coord("town", 0, 0, 0), "north",
    new Coord("town", 0, 1, 0), "south",
    closed: true, locked: true);
GlobalServices.GetNodeHandler().AddDoor(gate);
```

Door messages render through `$You`/`$conj`, so the actor sees "You …" and the room sees the third person form:

```text
> north
You try to open the door, but it won't budge.
> unlock north
You try to unlock the door, but you lack the key.
```

With the key in inventory, `unlock north` answers `You unlock the door.` (the room sees `<name> unlocks the door.`); then `open north` answers `You open the door.` and `north` walks through. `lock` / `close` work the same way, one direction at a time (`open north`, `close up`).

Builder removal is the `door` command with `--remove` (`door --remove north` answers `Removed …`; with no doors there: `There are no doors here.`). See [Objects](03-objects-library.md) for the door object API and [Commands](06-commands.md) for the builder verbs.

## 4. Build commands (in-game)

Builders work from inside the game with flag-style verbs: `build` grows rooms around you, `desc` rewords the current room, `door` hangs doors, `move` teleports by coord — no code, no restart, then save. Entry points (full catalog in [Commands](06-commands.md)):

```text
> door -a north
Created link 'south' from node at town 0,3,0 linking to town 0,1,0.
Created link 'north' from node at town 0,1,0 linking to town 0,3,0.
Created door at town 0,2,0.
> build --room -e
Created new node at town 1,1,0.
> build --desc A stone arch straddling the road.
Updated current location's description.
> desc A stone arch straddling the road.
A stone arch straddling the road.
```

Standing in North Road (`town 0,1,0`), `door -a north` hangs a two-step door node between you and the far room (`-a` auto-creates it; without `-a` a missing destination answers `There is no node at the destination coord …, use -a to auto-create it.`). `build --room -e` raises `town 1,1,0` (`Created new node at …`; on an occupied coord it answers `Updating node at …` instead), links it both ways, and moves you into it — so the following `--desc` words the new room. `build` takes `--room` / `--road` / `--path`, a direction (`-n -e -s -w -u -d`, or `-x` for here), and `--desc`; `move` teleports by coord (`move <area> <x> <y> <z>`, or the comma form `move (town,1,1,0)`) and answers `Moved to town 1,1,0.`; `examine` on a node answers `Examining Node at town 1,1,0 in area 'town', z=0 (#<id>):`.

Mapedit validation outcomes: `mapedit` opens the drawing editor in a new tab (`Opening AtheriZ Draw in a new tab.`); the settings wire protocol rejects bad input with a message instead of corrupting the map (`'fgColor' must be an [r,g,b] triple of 0-255 integers`, `'fontSize' must be an integer 1-…`, `'tools' must be an object`). What you see is always an error line in the client, never a half-applied map.

## 5. Legend and map view

Legend entries map glyphs to labels so the map pane is readable: pass `symbol` / `legendDesc` to the `Node` constructor (or a `LegendEntry` list to `MapInfo`), and the client draws the rendered area around the player's position plus the legend.

```text
> map
Map enabled.
> map
Map disabled.
```

`map` is a per-player toggle for the client map pane — not a chat render. What the player sees on the pane is the rendered area around their position plus the legend.

Two settings toggle this surface: `MapEnabled` gates rendering in the engine (when off, no map data is produced); `LegendEnabled` is a plain setting with no engine gate — it records the operator's preference for showing the legend, but no code path branches on it. Do not rely on `LegendEnabled` to suppress rendering; check `MapEnabled`.

## 6. Example: game time — a bell every minute

`GameTime` drives ticks, the calendar, and alarms. `?` is a wildcard in alarm specs (`?` minute = every minute). `GetTimespan(long ticks)` returns a `TimeSpanInfo` whose `Desc` renders the span with its direction built in (`"now"`, `"3 minutes ago"`, `"2 days in the future"` for negative ticks) — there is no separate `TimespanDesc` type.

```csharp
var gameTime = GlobalServices.GetGameTime();

// A bell that rings every minute from Town Square.
var bell = GameObject.Create("chapel bell", "A bronze bell on a wooden frame.");
bell.MoveTo(square);
gameTime.AddAlarm("?", "?", bell, repeat: true);

// Elsewhere: when was the last save?
TimeSpanInfo span = gameTime.GetTimespan(ticksSinceSave);
square.MsgContents($"Last save was {span.Desc}.");
```

```text
The chapel bell tolls.
Last save was 3 minutes ago.
```

Tick mechanics in full (binding scripts, follow scripts, alarm dispatch) are in [Hooks, scripts, and text](05-hooks-scripts-text.md). Autosave timing in one line: checkpoints run on a scheduler and write the DB; full save mechanics are in [Saves and backups](10-persistence-saves.md). First-run seeding (`InitialSetup`) in one paragraph: on a fresh game folder, `InitialSetup` creates the limbo area, starter rooms, and the superuser account before the first connection is accepted.

## 7. Multi-floor grids and Z levels

Z is the floor. A stair link moves the character one Z up while staying on the same X/Y, usually into a second grid (`upstairs`) of the same area:

```csharp
var up = handler.GetNode(new Coord("town", 0, 0, 1));
if (up is null)
{
    up = new Node(new Coord("town", 0, 0, 1));
    handler.AddNode(up);
}
up.Name = "Bell Tower";
up.Desc = "A windy platform above Town Square.";
square.AddLink(new NodeLink("up", new Coord("town", 0, 0, 1), ["u"]));
up.AddLink(new NodeLink("down", new Coord("town", 0, 0, 0), ["d"]));
```

```text
> up
Bell Tower
A windy platform above Town Square.
Exits: down
```

Cross-area links work: a link whose coord names another area registers a transition (`SyncCrossAreaTransitions`), so `town 0,0,0` and `dungeon 0,0,0` are different rooms and a link between them walks across areas.

## 8. Map windows, legend projection, and seeding

`MapInfo` resolves which coords fall in the visible window around the player; the render path draws shared glyph runs, pre-renders in place, lets the first glyph mapping win on collision, and projects legend labels onto the result. Seeding on a fresh game folder (`InitialSetup`) creates the limbo area and starter rooms before the first connection:

```csharp
// What fresh-boot seeding guarantees before you connect:
var limbo = handler.GetNode(new Coord("limbo", 0, 0, 0));
limbo.Msg("Limbo hums quietly."); // sanity: the room exists
```

```text
> look
limbo
You are in a vast nothingness.
```

`GetTimespan` rendering examples (`TimeSpanInfo.Desc` — direction included):

```csharp
GameTime gt = GlobalServices.GetGameTime();
character.Msg($"Server time is {gt.GetTime().FormattedShort}.");
character.Msg($"Last save was {gt.GetTimespan(ticksSinceSave).Desc}.");
```

```text
Server time is 08:00:00, 12th of Ianuarius, year 1.
Last save was 3 minutes ago.
```

Alarm wildcards: `?` matches any value, so `("?", "?")` fires every minute, `("12", "?")` fires every minute of hour 12, and `("?", "00")` fires at the top of every hour.

## 9. Troubleshooting maps

- `You are nowhere.` after `look`: the character has no location — re-puppet or re-place; on a fresh game the oracle is `limbo`, not this message.
- Bad coord errors: coords are caller-validated; fix the call site (area name spelling, integer fields).
- Two rooms on one coord: refused by the occupied-graft rule — pick an empty coord.
- Map pane empty: check `MapEnabled` (the engine gate). `LegendEnabled` gates nothing — it is a preference setting with no code behind it.
- Hand-editing the database for a map change: never do this — build in game or in code, then save.

## 10. Build-command reference

Full builder surface (catalog in [Commands](06-commands.md); mechanics here):

| Builder input | What it does | Failure outcome |
|---|---|---|
| `build --room -n` (etc.) | creates a node one step in the direction, links both ways, moves you in | occupied coord → `Updating node at …` |
| `build --desc <text>` | sets current room description | no flags at all → usage |
| `desc <text>` | sets current room description, shows the room | empty text → usage |
| `door north` / `door -a north` | hangs a door (+links) toward the direction | missing far room → `There is no node at …` (without `-a`) |
| `door --remove north` | removes one door | none there → `There is no door north.` |
| `move <area> <x> <y> <z>` | teleports to the coord | no node → `No node found at …` |
| `examine` | describes current room / target | nowhere → `You are nowhere to examine.` |
| `mapedit` | opens cell editing in a new tab | no location/connection → error line |

```text
> build --room -e
Created new node at town 1,1,0.
> move town 0,0,0
Moved to town 0,0,0.
> door --remove north
There is no door north.
> desc The clang of hammers fills the air.
The clang of hammers fills the air.
```

Legend entries come from code, not chat: `new Node(coord, symbol: "S", legendDesc: "Town Square")`, or a `LegendEntry("S", "Town Square", (x, y))` list on `MapInfo`. There is no `build legend` verb.

GameTime calendar: ticks advance the clock; the calendar derives day/hour/minute from the tick count; alarms match hour/minute with `?` wildcards and fire their bound object's tick handler; repeating alarms re-arm after firing. Autosave checkpoints run on their own scheduler and write the DB — see [Saves and backups](10-persistence-saves.md) for timing and guarantees.

## 11. Area management and MapEnabled toggle

```csharp
var handler = GlobalServices.GetNodeHandler();
// Remove a node or a whole area from the registry.
handler.RemoveNode(new Coord("town", 1, 1, 0));
handler.RemoveArea("dungeon");
```

`RemoveNode` drops the room from its grid and evicts it from the registry — it does not re-home the room's contents, so move out anything living there first. Removing an area unregisters its grids and nodes in one pass. Tick re-registration runs after any structural change so surviving ticking objects keep their subscriptions.

Map toggle (operator view): `MapEnabled` is a host setting (`appsettings.json`, not an in-game verb), while `map` is the per-player client toggle:

```text
> map
Map enabled.
> map
Map disabled.
```

When `MapEnabled` is off the engine produces no map data at all — clients get no render, not an empty grid. `LegendEnabled` changes nothing in this path: it is a stored preference with no engine gate, so toggling it never enables or suppresses rendering.

## 12. Node properties via Settable

Rooms are settable like any object — builder `set` verbs assign through `Node.Settable.cs` so descriptions, names, and flags save correctly (`set <target> <attr> <value>`, where `here` means the current room):

```text
> set here desc A cobbled square, wet with morning rain.
Set Town Square.desc = 'A cobbled square, wet with morning rain.'
> set here name Town Square
Set Town Square.name = 'Town Square'
> examine here
Examining Node at town 0,0,0 in area 'town', z=0 (#<id>):
Town Square
A cobbled square, wet with morning rain.
Exits: north, east, up
```

How to read a coord, once more: `town 0,0,0` = area `town`, X 0 (east/west), Y 0 (north/south), Z 0 (floor). Positive Y runs north, positive X runs east, positive Z runs up. `move` takes a comma-coord in parentheses (`move (town,0,0,0)`) to teleport straight to that room instead of walking.

Starter-walk transcript (the full tutorial loop in one place):

```text
> look
Town Square
A cobbled square with a dry fountain at its center.
Exits: north, east, up
You see: torch
> get torch
You picked up: torch
> north
North Road
A dusty road leading north out of town.
Exits: south
> south
Town Square
A cobbled square with a dry fountain at its center.
```

From here the how-to continues: objects in [Objects](03-objects-library.md), ticking scripts in [Hooks, scripts, and text](05-hooks-scripts-text.md), builder verbs in [Commands](06-commands.md), saving your work in [Saves and backups](10-persistence-saves.md).

Cross-area example (names matter, coords alone do not): a link from `town` to `dungeon 0,0,0` registers a transition, so the walk carries across areas:

```csharp
// A gate out of the world: link across areas and the grid syncs a transition.
square.AddLink(new NodeLink("gate", new Coord("dungeon", 0, 0, 0)));
```

## 13. Under the hood

`NodeHandler.cs` + `NodeHandler.Partial.cs`: the area/grid/node index (lookup by coord, batch lookup for map windows), `AddNode` (auto-creates area and grid) / `RemoveNode` / `RemoveArea`, `AddDoor` / `AddTransition`, and tick re-registration (nodes with ticking scripts re-subscribe after load).

`MapHandler` cache/load-swap + `MapInfo` coord resolution: rendered map data is cached per area and swapped on save/load so readers never see a half-written map; `MapInfo` resolves which coords fall in the visible window. Render path: `PreRender` (in-place pre-pass) then `Render(force)` with the `AtPreMapRender` hook.

`MapEdit.cs` + `MapEditChain.cs` + `MapEditResult`/`MapEditStatus`: chained edit grants with a cap (`MapeditMaxChains`, 256 by default — only the cap evicts), consumed on apply, discarded on disconnect.

`GameTime.cs`: tick counter, calendar fields, `GetTimespan`/`TimeSpanInfo.Desc`, `?`-wildcards in alarm specs, `GameTimeRow` persistence shape (full row mechanics in [Saves and backups](10-persistence-saves.md)).

`MapEnabled` render gate vs `LegendEnabled` setting-with-no-gate (stated in §5 — do not invert them). `Coord` parse failures surface at the parsing call site. Caller-error coords note: passing a malformed coord is the caller's bug; the engine reports it and moves on rather than guessing.

---
Next: [Hooks, scripts, and text](05-hooks-scripts-text.md)
