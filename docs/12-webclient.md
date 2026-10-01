# Webclient: terminal client

> Covers: `webclient/` terminal entry (`/webclient`), `webclient/deploy.py` (`package`/`game`, `--no-build`, `--web-root`), per-game `web/` overrides, per-game `build.sh [--web]` redeploy. You learn to play through the browser and ship the client with your game. Connection basics (banner, login, puppet) live in [Connecting: webclient and telnet](09-connecting.md); static serving and cache ages live in [Hosting and configuration](08-hosting-configuration.md). The drawing and map editor has its own guide ([Map editor](13-map-editor.md)). Current webclient version is 1.10.0 (`webclient/package.json:3`); its history split off into `webclient/CHANGELOG.md`, separate from the engine changelog.

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

Popup-blocked banner: the map editor opens in a new window or tab. When
the browser blocks it, the client shows a `Popup blocked. ...` fallback
banner with an `Open AtheriZ Draw in a new tab` link instead of the
editor (one div, self-dismissing after 10 seconds) — allow popups for
the game address and open the editor again. Nothing is broken; the
browser simply refused the window. Repeating the launch within a second
is throttled (`Draw launch throttled; ...`) rather than opening a second
tab; the latest grant is kept, so waiting a moment and retrying opens
the current session.

Prompt behavior, one paragraph to close the loop: typed input echoes
locally, server output streams above it, and the prompt redraws after
each burst so mid-typing output never eats the line being composed. A
`wave` answered pages later is lag or a busy server, not a lost keystroke
— resend only after the output settles.

## 2. Fonts

Two tiers, one paragraph: a set of bundled families ships with the client
and works with no build step, while the full catalogue offers more faces
that each player picks from settings. Font choice is per player and
reversible — nothing a builder picks constrains what a player reads with.
When text renders as boxes or the wrong face after a redeploy, the cause
is almost always the stale-client chain in §3 (old bundle, old catalogue
reference), not the setting itself: redeploy, hard-refresh, and only then
suspect the font files. Screen-reader players should prefer the plainest
bundled face and leave styling to the terminal: fewer glyphs, fewer
surprises.

## 3. Deploy to your game

Source of truth first: the client is built from `webclient/src/`, plus a
game's `web/` overrides. Never hand-edit the installed `wwwroot/` — the
next redeploy overwrites it, and your edit vanishes without a trace. For
orientation, the installed tree holds the two entries plus their assets:
`webclient/` (terminal client), `atheriz_draw/` (map editor,
→ [Map editor](13-map-editor.md)), `assets/`,
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
Next: [Map editor](13-map-editor.md)
