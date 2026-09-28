# Webclient and map editor

> Covers: `webclient/` source (terminal client and draw/map-editor entries), `webclient/deploy.py` (`package`/`game`, `--no-build`, `--web-root`), per-game `web/` overrides, per-game `build.sh [--web]` redeploy. You learn to play through the browser, draw rooms in the editor and put them on the map, and ship the client with your game. Connection basics (banner, login, puppet) live in [Connecting: webclient and telnet](09-connecting.md); static serving and cache ages live in [Hosting and configuration](08-hosting-configuration.md). Current webclient version is 1.5.0 (`webclient/package.json:3`); its history split off into `webclient/CHANGELOG.md`, separate from the engine changelog.

To reach the client you need a running server and its web address (host
and port from the game operator, defaults in
[Getting started](01-getting-started.md)). The client is only a window:
accounts, characters, and the world all live server-side, so anything the
browser forgets (history, settings, sounds) is cosmetic, while anything
the server forgets is a save issue
(→ [Saves and backups](10-persistence-saves.md)).

## 1. Terminal client

Open `/` or `/webclient` in a browser and press Play. The terminal client
gives you three panes and a prompt:

- Input line with history: type a command, Enter to send, up/down to
  recall earlier input. `look`, `north`, `get torch`, `wave` all work as
  they do over telnet.
- Output pane: room descriptions, messages, and command results stream here.
- Map pane: renders your surroundings when the game enables maps
  (→ [World and maps](04-world-maps.md)).
- Audio toggle: mutes client sounds without touching the server.

Two entries ship in the client: the terminal client (`/webclient`) and
the draw/map editor (`/atheriz_draw`), both served from the install
`wwwroot/` unless the game overrides them (§4).

Input basics in one table:

| Key | Effect |
|---|---|
| Text + Enter | Send a command (`look`, `north`, `get torch`, `wave`). |
| Up / Down | Recall earlier input for editing and resend. |
| Audio toggle | Mute client sounds; server-side behavior is unchanged. |

The map pane follows the character: moving updates the surroundings
without a page reload. When the game disables maps, the pane stays empty
— that is configuration (→ [World and maps](04-world-maps.md)), not a
client fault. Overlong input is dropped silently on the server side
(the 64 KB cap, → [Connecting: webclient and telnet](09-connecting.md)):
no error comes back, so a pasted wall of text that vanishes was
rejected, not lost.

Popup-blocked banner: the editor opens in a new window or tab. When the
browser blocks it, the client shows a `Popup blocked. ...` fallback
banner instead of the editor (one div, self-dismissing after 10
seconds) — allow popups for the game address and open the
editor again. Nothing is broken; the browser simply refused the window.

## 2. Example: draw a room and put it on the map

Goal: draw Town Square cobbles in the editor, export the drawing to the
game map, and see it on `map` in the terminal client.

1. Open the editor at `/atheriz_draw` (allow popups when the banner from
   §1 appears). Before drawing, confirm the game enables maps and legends
   (the `MapEnabled` / `LegendEnabled` settings,
   → [World and maps](04-world-maps.md)): a drawing exported into a game
   with maps off has nowhere to render, and the export succeeding while
   the client shows nothing is the classic symptom. Pick the target area
   now, not after the artwork is done.
2. Pick a tool. The toolbar carries a full drawing set — brush,
   text, rectangle, oval, line, gradient, and fill for laying down
   artwork, plus eraser, eyedropper, move, selection, rotate, and type
   for corrections. The common room-drawing jobs:

   | Job | Tool |
   |---|---|
   | Paint single cells (cobbles, walls, grass) | Brush |
   | Straight runs: North Road stretching off the square | Line |
   | Boxes and round features: plinths, wells, ponds | Rectangle, Oval |
   | Flood an area with one terrain cell | Fill |
   | Stamp labels such as `Town Square` onto the drawing | Text |

3. Work in layers: keep terrain on one layer and labels/props on another
   so a repaint never wipes the square's name. Pick the export target
   before drawing in earnest — area plus coordinates — and confirm the
   target grid has room for the drawing's extents; an export aimed at the
   wrong area writes good cells to the wrong place, which reads as map
   corruption until you compare coordinates.
4. Open the legend dialog and bind each cell to a legend entry
   (symbol plus color). The editor edits the same legend the game renders
   (→ [World and maps](04-world-maps.md)), so a cobble cell here becomes a
   cobble on every player's `map`. Entries normalize on export: a cell
   with no symbol, or an entry with no color, is flagged before anything
   writes, the same validation the wire protocol applies to mapedit
   commands. Name entries for players (`cobble`, `road`, `door`), not for
   the drawing — the names surface in map output.
5. Export to map: pushes the drawn cells into the game map at the target
   area and coordinates. The export validates sizes and cell input the
   same way the in-game mapedit commands do — bad input is rejected with
   an error instead of writing half a room. Typical rejections: a drawing
   larger than the target grid, an unknown cell symbol, or a legend entry
   missing its color. Fix the flagged cells and export again; nothing
   partial is ever written.
6. Settings round-trip: the editor restores your last save (active tool,
   colors, map source) when you reopen it, so closing the tab mid-square
   loses nothing. The round-trip covers editor preferences, not game
   data — the map itself is saved by the server on checkpoint
   (→ [Saves and backups](10-persistence-saves.md)).

Verify in the terminal client. Walk to Town Square and flip your
personal map view on (`map` is a per-player toggle, not a render —
→ [World and maps](04-world-maps.md)):

```bash
> map
Map enabled.
> look
Town Square
A cobbled square with a fountain in the middle.
You see: torch.
Exits: north
```

The drawn cobbles now frame the room you stand in; `north` walks the line
you drew up North Road. When the drawing and the room disagree (a wall
cell across an exit), fix the drawing and export again — the map is data,
and export overwrites it.

Editor or in-game commands? Both edit the same map, and the choice is
about shape, not power. Draw in the editor when the work is visual:
terrain masses, road curves, districts that read as pictures. Type the
in-game build commands when the work is structural: creating rooms,
linking exits, setting descriptions and legend entries one by one
(→ [World and maps](04-world-maps.md) for the full builder workflow). A
common rhythm is editor first for the landmass, then in-game commands for
exits and text, then one final export to reconcile any drift. Either side
rejects bad input the same way, so alternating never corrupts the map.
After each export, re-read any export errors before closing the editor:
a partial write never happens, so a flagged cell simply did not land,
and the error names it for the next attempt.

Prompt behavior, one paragraph to close the loop: typed input echoes
locally, server output streams above it, and the prompt redraws after
each burst so mid-typing output never eats the line being composed. A
`wave` answered pages later is lag or a busy server, not a lost keystroke
— resend only after the output settles.

## 3. Fonts

Two tiers, one paragraph: a set of bundled families ships with the client
and works with no build step, while the full catalogue offers more faces
that each player picks from settings. Font choice is per player and
reversible — nothing a builder picks constrains what a player reads with.
When text renders as boxes or the wrong face after a redeploy, the cause
is almost always the stale-client chain in §4 (old bundle, old catalogue
reference), not the setting itself: redeploy, hard-refresh, and only then
suspect the font files. Screen-reader players should prefer the plainest
bundled face and leave styling to the terminal: fewer glyphs, fewer
surprises.

## 4. Deploy to your game

Source of truth first: the client is built from `webclient/src/`, plus a
game's `web/` overrides. Never hand-edit the installed `wwwroot/` — the
next redeploy overwrites it, and your edit vanishes without a trace. For
orientation, the installed tree holds the two entries plus their assets:
`webclient/` (terminal client), `atheriz_draw/` (editor), `assets/`,
`fonts/`, and the shared runtime files. Game overrides in `web/` shadow
matching paths; everything else falls through to the install. So a custom
banner or stylesheet belongs in the game `web/` folder, and a client bug
fix belongs upstream in `webclient/src/` — location decides who survives
the next deploy.

`webclient/deploy.py` (`deploy.py:105-123`) stages bundles. The two
targets and their jobs:

| Target | Job |
|---|---|
| `package` | Stage into the installed package source (engine-side client). |
| `game` | Stage into a game web root — requires `--web-root`. |

```bash
python3 webclient/deploy.py game --web-root mygame/web
python3 webclient/deploy.py game --web-root mygame/web --no-build
```

- Target `package` stages into the installed package source;
  target `game` stages into a game web root (`--web-root` required).
- `--no-build` reuses an already-built `dist/` instead of running the npm
  build. Without it, Node must be on `PATH`.
- Day to day you rarely call it directly: per-game `./build.sh [--web]`
  redeploys for you (`--web` for web only, default for plugin plus web;
  flag table in [Running the server](07-server-lifecycle-cli.md)).

`WebclientSyncCheck` (engine default true,
`src/Atheriz.Core/Settings/AtherizSettings.cs:69`): at
startup the server compares the installed client hash against the engine
`web/` source and logs a warning when they drift. A drift warning after a
client upgrade means exactly one thing — rebuild and redeploy the
client. The switch the server reads is the live `Atheriz:WebclientSyncCheck`
setting (the game folder's `appsettings.json` overrides it per game);
the matching `GameSettings.cs` constant in new folders is game-code
convention, not the server switch. Leave the check on: silencing hides
real drift — prefer a redeploy so the warning has nothing to report.

Stale-client checklist, in order: confirm the redeploy finished
(`build.sh` prints the web step), hard-refresh the browser (entry HTML is
uncached but the browser may still hold it), then compare the served
bundle hash against the engine source. When all three agree and the page
still looks old, the override in the game `web/` folder is shadowing the
new file — inspect the override before touching anything else.

Cache notes, briefly: hashed bundles serve immutable with long cache
ages, `.wasm` shorter, entry HTML uncached — the full table lives in
[Hosting and configuration](08-hosting-configuration.md). After a
redeploy, hard-refresh the browser when the old client lingers; the
server already serves the new files.

---
Next: [Engine internals](13-engine-internals.md)
