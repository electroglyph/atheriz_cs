# Map editor

> Covers: the `/atheriz_draw` entry (AtheriZ Draw), the in-game `mapedit` launch and its one-time grant, the canvas model and the violet server grid, drawing tools, the room overlay and Room panel, Create/Delete Rooms, Save to server, legend editing, and the ack/deny round-trip. You learn to draw map art, create and wire rooms, and get the result onto the live map. The terminal client lives in [Webclient: terminal client](12-webclient.md); map data, legends, and builder commands live in [World and maps](04-world-maps.md).

## 1. Opening the editor

Builders open the editor from inside the game with the `mapedit`
command. The server answers `Opening AtheriZ Draw in a new tab.` and
opens `/atheriz_draw` in a new tab carrying a one-time grant (a server
key plus the map payload: area, z-level, rooms, legend, and origin). The
grant is stored in the browser and expires after 60 seconds, so an idle
tab opened a minute later starts a plain drawing canvas instead of your
map session. Launching twice within a second is throttled
(`Draw launch throttled; ...`) — the newest grant is kept, so wait a
moment and run `mapedit` again. If the browser blocks the popup, the
terminal client shows a fallback banner with an
`Open AtheriZ Draw in a new tab` link (self-dismissing after 10
seconds): allow popups for the game address and retry.

Without a grant the editor still works as a drawing canvas, but every
map operation refuses with `No map edit session — re-run mapedit
in-game.` Nothing is lost by this: artwork is local until you save it to
the server (§6).

## 2. The canvas model and the violet line

The canvas is an infinite black viewport with one rectangle drawn over
it: the **violet outline**, also called the server grid. The rule is one
sentence — **only cells inside the violet outline go to the server**.
Everything the editor sends, exports, or previews is cropped to that
rectangle: Save to server writes it, Export ANSI exports it, and the
Preview window renders it. Cells outside the outline are scratch space:
useful for staging artwork, invisible to the game.

The outline moves itself so you rarely manage it by hand:

- Drawing, moving, rotating, filling, or typing past the outline grows
  the viewport instead of clipping, and the grid expands to include the
  new content.
- Rooms force coverage: whenever the room list changes, the grid grows
  until every known room sits inside it, so room glyphs never fall
  outside the saved rect.
- Flood fill and gradient recolor stay inside the outline; a fill seed
  outside it grows the grid first.

The classic mistake is drawing a beautiful border two cells past the
outline and wondering why the game never shows it. If the outline does
not enclose it, the server never receives it — draw past the line and
let the grid follow, then save.

## 3. Drawing tools

The top toolbar carries the full drawing set; the bottom toolbar carries
session actions (§6). The common map-making jobs:

| Job | Tool |
|---|---|
| Paint single cells (cobbles, walls, grass) | Brush |
| Straight runs: a road stretching off the square | Line |
| Boxes and round features: plinths, wells, ponds | Rectangle, Oval |
| Flood an area with one terrain cell | Fill |
| Free color transitions | Gradient |
| Corrections and restyling | Eraser, Eyedropper, Move, Rotate, Type |
| Stamp labels such as `Town Square` | Text (renders a font to cells) |
| Rearrange finished art | Select (rectangle, lasso, magic, color-match, color-fuzzy, single) |

Supporting controls: undo/redo (`Ctrl+Z` / `Ctrl+Shift+Z`), zoom,
display-font select, color adjust, and the foreground/background/gradient
pickers plus character palette in the left sidebar. Layers (right
sidebar) keep terrain on one layer and labels/props on another so a
repaint never wipes the square's name. Settings round-trip: the editor
restores your last save (active tool, colors, map source) when you
reopen it — editor preferences, not game data, which the server saves on
checkpoint (→ [Saves and backups](10-persistence-saves.md)).

## 4. Rooms: overlay, selection, and the Room panel

Known rooms draw as a colored overlay on their squares. `Hide Room
Color` toggles the overlay off when it fights with the artwork; the
`Room Color` swatch recolors it. The overlay is purely visual — it never
enters the saved cells.

Select squares with the Select tool and the right sidebar follows:

- Exactly one room selected: the **Room** panel shows its `Name` and
  `Description` (plain-text inputs with foreground/background swatches
  and a live preview of how each field looks with its colors) plus its
  `Exits (n)` list.
- Anything else selected: the panel shows `Select a single room to edit
  it.`

The panel's **Save** button writes that room's name, description, and
exits to the server. Feedback lands under the button: `Saved to server.`
on ack, `Denied: <reason>` on refusal (a dialog repeats the reason).
Exits edit in place: `+` adds a blank exit, which must be named and given
a target before it can save (`Blank exit added — name it and set its
target.`). Retargeting an exit arms picking mode
(`Click a room for the exit to lead to…`) — the next room square clicked
becomes the exit's destination. Every exit edit applies optimistically
and rolls back to the pre-save list if the server denies it, so the
panel never shows exits the server refused.

## 5. Create Rooms and Delete Rooms

Two toolbar buttons appear only when they apply, driven by the current
selection:

- **🏠 Create Rooms** shows when the selection is non-empty and every
  selected square is fresh (no pre-existing rooms). It turns each
  selected square into a room and links fresh rooms to every orthogonal
  neighbor — fresh or pre-existing — with reciprocal cardinal exits. It
  never overwrites a hand-built exit name and never double-links an
  already-linked target. New rooms land optimistically and the selection
  clears; the server ack confirms (`Saved to server.`, fresh rooms gain
  their default description), while a deny removes the planned rooms,
  restores touched neighbors, and reports the reason. Undo/redo cover
  the whole batch.
- **🗑️ Delete Rooms** shows when at least one selected square is a
  known room; empty squares in the selection are ignored, not deleted.
  Each deleted room's occupants evacuate to the nearest surviving room,
  and surviving rooms' exits into deleted rooms are stripped
  automatically (mirrored locally). A deny puts everything back: rooms,
  exit lists, and undo state.

Batch limits are server-enforced (up to 256 rooms per create/delete);
oversized or invalid batches are refused whole — nothing partial is
ever written.

## 6. Save to server, export, preview

The bottom toolbar's green **💾 Save to server** button writes the drawn
cells inside the violet outline to the game map (area and coordinates
from the launch grant). Export/validation failures name the offending
cells — unknown symbols, entries missing colors — and nothing partial
writes: fix the flagged cells and save again.

- **💾 Export ANSI** downloads the violet-grid cells as an `.ans` file;
  **📂 Load ANSI** loads one back onto the canvas.
- **👁 Preview** renders exactly what the server gets (violet grid
  only) in a terminal preview.

The editor edits the same legend the game renders
(→ [World and maps](04-world-maps.md)): **🏷️ Edit Legend** binds cells
to legend entries (symbol plus color), so a cobble cell here becomes a
cobble on every player's `map`. Name entries for players (`cobble`,
`road`, `door`), not for the drawing — the names surface in map output.

## 7. Worked example: draw a room and put it on the map

Goal: draw Town Square cobbles, export the drawing to the game map, and
see it on `map` in the terminal client.

1. Confirm the game enables maps and legends (the `MapEnabled` /
   `LegendEnabled` settings, → [World and maps](04-world-maps.md)): a
   drawing saved into a game with maps off has nowhere to render. Pick
   the target area now, not after the artwork is done — an export aimed
   at the wrong area writes good cells to the wrong place, which reads
   as map corruption until you compare coordinates.
2. Run `mapedit` and draw the square (§3): brush for cobbles, line for
   the road running off, text for the `Town Square` label on its own
   layer. Check the violet outline encloses all of it (§2).
3. Bind cells in the legend dialog (§6) and press **Save to server**.
   Fix any flagged cells and save again.
4. Select fresh squares beyond the square and press **Create Rooms** to
   extend the neighborhood (§5); wire exits in the Room panel (§4).

Verify in the terminal client (`map` is a per-player toggle, not a
render — → [World and maps](04-world-maps.md)):

```bash
> map
Map enabled.
> look
Town Square
A cobbled square with a fountain in the middle.
You see: torch.
Exits: north
```

Editor or in-game commands? Both edit the same map, and the choice is
about shape, not power. Draw in the editor when the work is visual:
terrain masses, road curves, districts that read as pictures. Type the
in-game build commands when the work is structural: creating rooms,
linking exits, setting descriptions and legend entries one by one
(→ [World and maps](04-world-maps.md)). A common rhythm is editor first
for the landmass, then in-game commands for exits and text, then one
final save to reconcile drift. When drawing and room disagree (a wall
cell across an exit), fix the drawing and save again — export
overwrites.

---
Next: [Engine internals](14-engine-internals.md)
