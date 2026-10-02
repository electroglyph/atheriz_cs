# Webclient Changelog

Webclient (`webclient/`) release notes, split out from the engine `CHANGELOG.md`
2026-09-25. Versioning follows `webclient/package.json`.

## [1.11.3] - 2026-10-02

- The landing-page log section starts hidden and only reveals itself
  once the snapshot loads, so a disabled endpoint never flashes an
  empty terminal before disappearing.

## [1.11.2] - 2026-10-02

- The map pane no longer closes on disconnect. It keeps showing the
  last known map while the client retries, and the server re-pushes
  fresh map frames on login.

## [1.11.1] - 2026-10-02

- The server-log viewer passes the snapshot's position to the live
  stream, so reloading the landing page no longer renders every line
  twice.

## [1.11.0] - 2026-10-01

- The game client accepts a login token from the landing page Play
  button (`webclient/index.html#token=...`) and passes it as `?token=`
  on the game socket, so a landing-page login starts the session
  already authenticated.

## [1.10.2] - 2026-10-01

- The server-log viewer renders each line's time in the viewer's own
  timezone instead of the server's (the snapshot/stream now carry the
  UTC moment per line).

## [1.10.1] - 2026-10-01

- New landing-page server-log viewer bundle (snapshot plus live stream
  of the server channel).

## [1.10.0] - 2026-10-01

- The terminal client focuses the command input when typing (any
  printable key) outside an editable element — including inside either
  terminal pane, whose keys xterm.js would otherwise swallow. Shortcuts,
  navigation keys, and terminal text selection are unaffected.

## [1.9.0] - 2026-09-30

- Fixed: lines after a soft wrap kept the text color when a colored span
  ends right at the wrap point; the continuation used to fall back to
  terminal white. Stacked styles (bold plus foreground/background) now
  survive wraps as well.
- The map editor's "Save to server" button is now green with white text
  so it stands out from the other toolbar buttons.
- The map editor toolbar no longer shows the Grid button; the resize
  dialog, its modal, and its wiring are removed entirely.
- The map editor's Create Rooms button hides when any selected square
  is already a room, instead of offering a partial create.

## [1.8.2] - 2026-09-30

- New: room Name and Description now work like legend rows. Both inputs
  hold plain text while foreground/background swatches own the field
  color (decoded from the stored text on select); Save wraps the whole
  field in 24-bit control codes, normalizing legacy palette codes.

## [1.8.1] - 2026-09-30

- Fixed: room-move validation on dense maps (unfolding matched moves
  from the same batch and stole origins across rooms; folded hops rode
  along as context and failed every re-touched room; accept tracking
  hijacked neighbors sharing a square).
- Fixed: denied room moves restore only their squares instead of
  reverting the whole canvas, so later strokes survive; queued batches
  invalidated by a deny are dropped instead of cascading.

The draw canvas is now an infinite black viewport; a violet outline
marks the server grid that is actually sent back on save.

- New maps start with the picked size as the violet grid, centered in a
  larger black viewport. Drawing or moving content past the outline
  grows it automatically in all four directions.
- Only cells inside the violet grid are sent to the server, exported,
  or previewed. Shrinking the grid reports the cut cells as deletions.
- Resize is now grid size instead of canvas size; the viewport follows
  content on its own.
- Text-to-ANSI takes an explicit width and height in characters and
  lands centered on the violet grid instead of fitting the canvas.
- Fixed: strokes that stay inside the viewport but cross the violet
  outline now move the outline at once (renderer, save diff, and origin
  stay in step). The whole viewable area is black grid, with no gray
  margin around the canvas.
- Fixed: canvas grid lines sit on half-pixel boundaries so they render
  as crisp 1px strokes matching the grid past the canvas edge.
- Fixed: the canvas backing store now scales with the device pixel
  ratio, so the grid stays crisp on HiDPI and OS-scaled displays
  instead of going soft next to the surrounding CSS grid.
- Fixed: the scroller grid past the canvas edge is a hard-edged SVG
  tile baked at the live cell size instead of gradient tiles, which
  browsers resample soft and read lighter than the canvas strokes.
- Fixed: the canvas element now covers the whole viewport with storage
  at a fixed inset, so every visible grid square is drawable surface —
  no separate undrawable margin, and one grid renderer means no tone
  seam. Strokes past storage grow it in all four directions.
- Fixed: the room union boundary now draws at the same inset as the
  room fill (it was stranded in the top margin, leaving a detached
  cyan scribble instead of a room indicator — crossing map glyphs on
  large maps).
- Fixed: the violet outline is mirrored into the renderer right after
  map load (it kept the stale boot-time rect instead of the loaded
  map's grid until the first edit refreshed it).
- Fixed: the editor scrolls the loaded/fresh grid into view on open
  (the map sits at the view inset while the scroller starts at the
  origin, so without this it opened on empty margin grid).
- Fixed: combined storage-plus-grid growth now publishes the expanded
  grid to the host (the mirror fired before bounds expanded and the
  follow-up was skipped, leaving the violet outline and the save diff
  one step behind every growth).
- Fixed: large saves go out as sequential size-capped messages instead
  of one frame (the server closes oversized frames, which used to kill
  the session and brick saving until reload).

## [1.7.0] - 2026-09-30

- The left terminal shows a draggable scrollbar thumb while scrolled
  up; it stays hidden at the bottom.

## [1.6.0] - 2026-09-29

Map editor rooms carry names now, with a Room panel in the right pane
that round-trips to the server.

- Rooms arriving in the `launch_draw` payload include the room name; the
  dev-only room log prints it. Rooms without a display name leave the
  name field empty instead of showing the coordinate string.
- New Room panel below the Layers listing: it shows the single selected
  room, or asks you to select one.
- Saving a room sends its name and description to the server; the panel
  shows whether the save landed or the room was already gone.
- Each exit has an editable name, an editable `(Area,x,y,z)` target, a
  Link button (click the destination room on the map), and a red delete
  button. Deleting an exit that has a return exit asks first.
- Exit edits save immediately and participate in Ctrl+Z/Ctrl+Shift+Z
  alongside paint strokes.
- New Add exit button appends a blank exit row; it stays local until it
  is named and given a target, and unfinished rows block other commits
  with a hint instead of risking the editing session.
- Exit aliases are editable as a comma-separated list.
- New Create Rooms button next to Edit Legend, visible while squares are
  selected: it turns the selection into rooms linked to each neighbor both
  ways. Undoable, including server-side deletion; a refused batch rolls
  back with a notice.
- Creating rooms clears the square selection, so the new rooms show the
  room-color outline instead of staying selection-yellow.
- New red Delete Rooms button next to Create Rooms, visible while the
  selection includes a room: it deletes the selected rooms, moves
  occupants to the nearest surviving room, and strips links into them
  automatically. Undoable; a refused delete rolls back with a notice and
  reports `Deleted N room(s).` on success.
- The room highlight rebuilds from the live room list on every mutation,
  so it can't strand on a deleted square.
- Room presence renders as a subtle cell fill plus the union boundary of
  each room region, so deleting an interior room visibly empties its
  square (per-cell outlines repainted shared edges and could never change
  a pixel).
- Canvas moves relocate rooms by identity at send time; accepts and
  refusals reconcile list and highlight the same way, so overlapping
  edits can't scramble or strand them.
- Denial dialogs stay up until OK is clicked instead of vanishing on
  Escape or backdrop click, so a refused op can no longer look like a
  success.

## [1.5.0] - 2026-09-27

Failed editor launches no longer recommend `:draw` as the retry; the
messages point at the fallback link or a fresh `mapedit` instead. The
popup-blocked fallback banner dismisses itself after ten seconds.

## [1.4.0] - 2026-09-27

Map-editor settings round-trip through the server: explicit save sends
colors, slots, stops, palette, font, tools, and room overlay; reopening
the editor restores them. Layers panel defaults to 600px; blocked editor
popups no longer report as throttling.

## [webclient 1.3.0] - 2026-09-25

- Terminal no longer stamps the prompt onto every `text` frame. Text output
  erases the live prompt and prints only text; the stored server prompt is
  redrawn once per writer drain, so it stays on the bottom line instead of
  sealing one `>` line per message into scrollback through bursts (town
  generation, sunrise/sunset/moonrise broadcasts). Pinned by `webclientText`
  prompt regression tests.

## [webclient 1.2.0] - 2026-09-21

Fixes from the `webclient.md` audit (2026-09-20), covering data loss,
crashes/hangs, ANSI escape injection, validation gaps, and tool/state/UI
issues across the draw app and webclient. Pinned by new regression suites
`mapeditSessionRebind`, `transportTextFixes`, `validationFixes`,
`toolStateFixes`, `uiUtilsFixes`, and `typeToolFixes`; full webclient suite:
72 files / 756 tests green, `tsc` clean (app + tests configs).

### Data loss (P0)

- `MapEditSession` now has a live-canvas binding (`rebindCanvas`): every
  canvas swap in `main.ts` (TextTool, undo/redo, moves-denied restore,
  ImageImport, ANSI load, ColorAdjust) re-baselines the session, and New
  keeps the old baseline so the cleared map reaches the server as a
  deletion diff instead of `Nothing to save`.
- `flush()` and the reconnect resends now honor `conn.send()`'s boolean:
  a failed send requeues the batch and clears `inFlight` instead of
  dropping it and wedging the queue.
- Binary WebSocket frames are no longer dropped: the socket sets
  `binaryType = 'arraybuffer'` and Blob frames decode via an async path.

### Crashes / hangs (P1)

- `getLinePoints`/`getEllipsePerimeter` return `[]` for non-finite input
  instead of looping forever.
- `FillTool`/`SelectionTool`/`MoveTool`/`RotateTool`/`GridRenderer` skip
  malformed cell keys (radix-10 parse + integer check) instead of crashing
  on `cells[NaN][NaN]`; `setSelection` copies the caller's set.
- `CanvasState` validates constructor/resize dimensions (finite integers,
  1..2048) and `getActiveLayer()` recovers from empty/stale layers instead
  of returning `undefined`.

### ANSI escape injection (P1)

- `text.ts`/`ansiParser.ts` strip the full CSI range plus OSC sequences
  instead of SGR-only; user input echo and server legend/desc/area text are
  sanitized through the shared helpers before render.

### Validation (P2)

- `payload.ts` rejects `NaN`/`Infinity` positions and bounds, caps legend
  symbol/description lengths; `wrapText` handles `NaN` width; `colors.ts`
  clamps channels and parses hex strictly (`null` on bad input, callers
  keep the old color); `sampleGradient`/`lerpColor` guard non-finite `t`.
- `TypeTool` clips both axes, pushes undo only when something paints,
  reads the live canvas at confirm time, and iterates code points so emoji
  survive; `FillTool`/`GradientTool` `both` target paints both channels,
  empty stop lists fall back to the default gradient, and opaque black
  counts as ink everywhere; `GridRenderer` previews resolve transparency
  to black instead of emitting `rgb(-1,-1,-1)`.

### Tools / state (T1–T12)

- `ToolManager` warns once per unknown tool id; Brush/Eraser/Line/
  Rectangle/Oval clip to the canvas before `applyBatch` with in-bounds-only
  junction lookups; LineTool preview/commit agree on single points,
  commits on drag-release, undoes its segment on ESC, and shares one toast
  node with `destroy()`; SelectionTool uses a per-instance ESC listener,
  mode-at-mousedown, noop-guarded delete (opaque clear on layer 0), and
  paste that reuses the `Pasted` layer and selects the result; Move/Rotate
  skip empties, skip undo on no-change, clip OOB destinations, and use an
  epsilon + center-click guard for free rotate; Eyedropper reads the active
  layer for transparency; exporter/preview guard empty layers, omit empty
  overlay markers, and agree on no trailing newline; `UndoStack.undoTo`
  validates range and `reset()` clears current state; `CanvasController`
  refreshes modifiers on mousedown, ends drag on any mouseup, and stops
  swallowing Enter/`ctrl+x`/`ctrl+shift`; `newCanvas` validates dimensions
  and clears previews.

### UI / utils (U1–U12)

- Modal stacking is class-based (covers legend-error and map-error
  modals); CharMapDialog hardens CSS escaping, null-guards elements, adds
  `destroy()`, and validates font family; launch/history storage access is
  guarded with capped sizes; numeric/color inputs are validated (radix,
  finite, ranged) with gradient zero-width and `roundRect` guards;
  font/network paths have timeouts, abort controllers, cache caps, and
  success signals; dialogs/pickers clean up listeners, timers, observers,
  and modal divs on `destroy()`; font-family/URL inputs are sanitized;
  font metrics null-check contexts and require finite numbers; ANSI import
  caps dimensions, guards zero width, clamps colors, and disposes the
  terminal safely.

## [webclient 1.1.1] - 2026-09-20

- Fixed draw image conversion (chafa wasm 404): the factory ignores
  `locateFile`, so it's called with no options and uses the hashed asset
  URL. Unhashed `assets/chafa.wasm` copies placed in the served roots as
  a live stopgap.
- Webclient banner prints `version <package.json version>` instead of the
  timestamp revision string; revision cache machinery removed.
- Render text scales to span the map width, preserving aspect (shrinks
  to fit the map height when needed); the map itself is never resized.
- Fixed cleared maps resurfacing under rendered text: conversion returns
  unapplied cells, and the dialog reads the live canvas through a getter
  on every open/confirm instead of holding a stale reference.

## [webclient 1.1.0] - 2026-09-20

### Server static hosting (`mapedit` draw URL)

- `GET /static/atheriz_draw/` (the URL the `mapedit` command opens via
  `launch_draw`) returned 404 even though
  `web/static/atheriz_draw/index.html` was present: the static-files
  middleware has no default-document handling, so directory URLs fell
  through the pipeline. `StaticFileConfig` now registers default files
  (`index.html` only) for `/static`, so directory URLs resolve their
  entry HTML. File URLs are untouched.
- `GET /atheriz_draw/` (and `/webclient` without slash) returned 500:
  the slash/no-slash route pairs both matched either spelling, throwing
  `AmbiguousMatchException`. Each alias is now a single registration
  (the bare pattern covers both spellings); `/atheriz_draw/index.html`
  keeps its own pattern.
- Pinned by `ServerHostingDirectTests` (draw directory URL serves the
  entry, all draw alias spellings 200, webclient aliases redirect) plus
  an updated `StaticRouteHelperTests` structural pin; the directory-URL
  pin fails with 404 when the default-files registration is neutered.
  Live-verified on a scratch game (port 9998, owner's :9999 server left
  running).

### Server sync check covers the draw app

- `WebclientSyncChecker` only compared `webclient/` trees, so a stale
  `atheriz_draw/` entry never warned — the exact blind spot behind the
  mapedit report. It now also compares the draw entry HTML (which embeds
  hashed asset names, so any rebuild changes it), reported as
  `web/static/atheriz_draw`, against the shipped/project copy in
  preference to the layout-derived engine dir (which self-compares in the
  DLL-run layout). The deploy-command recommendation now also triggers on
  a draw-only mismatch.
- Pinned by five `PortedWebclientSyncTests` (identical/different/missing,
  engine-without-draw ignored, deploy-command wording);
  sensitivity-proven (3 fail with the engine lookup neutered). Full suite:
  5019/5019 green. Live-verified on a scratch game: stale draw entry
  warns at startup, fresh copy stays silent.

### Launcher webclient sync check (`atheriz.sh` / `atheriz.cmd`)

- The launchers never warned when the served web assets were stale (the
  exact failure behind the mapedit report: `dist/` rebuilt but the game
  copy never redeployed). On `start`/`restart`/`reload` they now compare
  the game's staged `atheriz_draw`/`webclient` entry HTML against the
  staged server copy and print the exact refresh command
  (`python webclient/deploy.py game --web-root "<game>/web"`); on
  `start`/`restart`/`reload`/`new`/`create` they also warn when
  `webclient/src` is newer than the staged copy (`./build.sh` /
  `build.cmd`). Warnings only, startup never blocks. The draw entry was
  chosen because it embeds hashed asset names, so any rebuild changes it.
- Also fixed `build.cmd` always rebuilding: `echo %hash% > file` left a
  trailing space in `.webclient-hash`, so the unchanged-skip comparison
  could never match (the launcher tolerates both old and new hash files).

### Map editor New canvas clears room outlines

- Creating a new canvas mid-mapedit left the previous map's teal room
  outlines painted on the fresh canvas: the New handler cleared only the
  (yellow) selection, never the room-cells overlay. The reset sequence
  now lives in `webclient/src/canvas/newCanvas.ts` (`beginNewCanvas`,
  called by `main.ts`), which resets the room set and clears both
  overlays; the mapedit session stays bound so Save still targets the
  same map.
- Pinned by `webclient/tests/newCanvasReset.test.ts`, which exercises
  the real helper with the real `GridRenderer`/`SelectionTool`/
  `CanvasState`/`UndoStack` (sensitivity-proven: neutering the overlay
  clears fails the room and selection pins).

### Webclient Google Fonts (local cache → featured set + CDN streaming)

- `webclient/public/gfonts/` now bundles 50 featured fonts (top 10 per
  Google category, excluding the multi-MB `Noto Sans JP` / `Noto Serif JP`
  families) instead of all ~1,950: ~7 MB on disk, down from ~631 MB
  (`dist/` shrinks from ~648 MB to ~25 MB accordingly).
- The Text tool's font dropdown lists local fonts plus the 50 featured
  fonts under a `── Featured Google Fonts ──` separator, so they stay
  selectable for ANSI art with no network. The existing `G Fonts` button
  still opens the full catalogue; anything not bundled streams from
  `fonts.googleapis.com` on demand via the loader's existing local-first
  fallback (now with `&display=swap` and preconnect hints).
- `scripts/fetch_google_fonts.cjs` refreshes only the full catalogue
  (1,929 → 1,946 families);
  `src/data/featuredGoogleFonts.ts` (single source of truth for the
  bundled set) is frozen at the 50 families above — Google's live
  popularity ranking reshuffles constantly, so regenerating from it would
  churn the bundled files and manifest for no user-visible benefit.
  `scripts/cache_google_fonts.cjs --full --featured` reproduces the cache
  from the frozen list.
- Pinned by `tests/featuredGoogleFonts.test.ts` (set shape, frozen
  membership, manifest/disk parity, CDN streaming) and
  `tests/textToolFeaturedFonts.test.ts` (dropdown section, idempotent
  open, load-before-preview on selection).
