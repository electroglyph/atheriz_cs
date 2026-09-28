# Connecting: webclient and telnet

> Covers: `src/Atheriz.Core/Network/`, `src/Atheriz.Server/Hosting/WebSocketHandler.cs`, `src/Atheriz.Server/Hosting/Protocols.cs`, `web/templates/index.html`, `web/templates/webclient/index.html`, `wwwroot/` (compiled `webclient/`, `atheriz_draw/`). You learn how to open the webclient, how to reach the server over telnet, what the login flow looks like, and which connection limits an operator should know. Server-side ports, TLS, and routes live in [Hosting and configuration](08-hosting-configuration.md); the client deep-dive lives in [Webclient and map editor](12-webclient-editor.md).

The server listens on two ports by default: 9999 for HTTP/WebSocket (browser clients, health endpoints) and 4444 for telnet (terminal and MUD clients). Both default values can change per game folder — when a connection fails, confirm the actual ports in `appsettings.json` before assuming the defaults (see [Hosting and configuration](08-hosting-configuration.md)).

## 1. Webclient: open, play, map

Open `http://localhost:9999/` (or `/webclient`) in a browser. The landing page has a Play button that opens the terminal client: a text pane for game output, an input line at the bottom, and a map pane beside it showing your surroundings.

Terminal basics:

- Type a command, press Enter. Output scrolls in the text pane.
- Up/Down recalls input history; the line keeps focus so you can keep typing during output bursts.
- The map pane renders nearby rooms from the same map data the in-game `map` command uses. Map rendering can be toggled server-side with `MapEnabled` / `LegendEnabled` (see [Hosting and configuration](08-hosting-configuration.md)).
- The audio toggle mutes or unmutes client-side sounds. The setting persists per browser.
- If the drawing/map editor opens but the browser blocks it with a banner, allow popups for the game address and retry (1.5.0 note: the editor is a separate window and popup blockers catch it on first launch).

What you should see on a fresh game: after login and character select (section 3), `look` describes `limbo`, the seed room. If `look` instead answers `You are nowhere.`, the character has no location — tell the operator (or, if you are the operator, see the triage checklist in [Running the server](07-server-lifecycle-cli.md)).

If the page loads but commands never answer, the web server is up while the game logic is still starting: compare `/health` (liveness — `200 {"status":"ok",server:...}` once the host is up) against `/ready` (`503 starting` until `DoStartup completed` lands in `save/server.log`).

## 2. Telnet: connect with any terminal or MUD client

```bash
telnet localhost 4444
```

Any telnet client works; a dedicated MUD client gives you scrollback, triggers, and keypad movement on top of the same wire. Set the client to UTF-8 — the server speaks UTF-8 in both directions and assumes your side does too.

On connect the server negotiates terminal capabilities automatically; no action is needed on your side. The wire order, as observed per `README.md`, is:

> `DO TTYPE` on connect, then `WILL SGA` / `WILL BINARY` / `DO NAWS`; charset negotiation off; UTF-8 both ways — performed by the `telnet_cs` library's `NegotiateAsync` (engine sets UTF-8 + charset-off + window-size).

In practice that means: the server learns your terminal type, switches both directions to binary mode with single-character handling (`SGA`), learns your window size (`NAWS`), and skips charset negotiation because UTF-8 is already fixed. If your client logs negotiation bytes, the sequence above is what a healthy handshake looks like. `telnet_cs` is pinned exact at `0.19.0` (`Directory.Packages.props`).

Players using a screen reader can switch to the screen-reader command once logged in — it trims map art and decorative spacing from room output while keeping exits, contents, and names intact. The full command catalog lives in [Commands: using and writing them](06-commands.md).

## 1.1 The webclient panes up close

The landing page (`/` or `/webclient`) is a static shell; the game begins when you press Play. Three regions share the window:

- The terminal pane (left, largest): scrolling game output with input history on Up/Down, pager prompts on long output, and a focused input line that never loses keystrokes to output bursts.
- The map pane (right): your room centered with linked neighbors at their exits, legend colors per terrain, re-rendered on every move and every `map`. Builders see committed edits appear without a client refresh.
- The status strip: connection state (connected / reconnecting), the audio toggle, and the editor button that opens the drawing/map editor in a new window (allow popups — the 1.5.0 blocked-popup banner means the browser ate it, not that the editor is broken).

First-visit checklist: page loads, Play opens the client, the connection banner names the game's `ServerName`, and after login `look` describes `limbo` on a fresh game. If the banner names the wrong game, the browser tab is pointed at the wrong port — compare against the game folder's `appsettings.json` before reporting anything.

Reconnect behavior: dropping the tab and reopening it starts a new transport but the same account — log in, select the character, and the world resumes. History restarts empty (per-session recall); position, inventory, and channels resume (server-side state). A `ready: starting` window during server reboots shows the client retrying instead of failing — wait for `DoStartup completed` server-side rather than hammering reconnect.

## 2.1 Telnet clients: profiles that work

Minimal working profile for any MUD client:

```text
host:     play.example.com (or localhost for local games)
port:     4444
encoding: UTF-8
negotiation: enabled (TTYPE + SGA + BINARY + NAWS)
```

Keep negotiation enabled. The server opens with `DO TTYPE` and continues `WILL SGA` / `WILL BINARY` / `DO NAWS` with charset negotiation off and UTF-8 fixed both ways (performed by `telnet_cs` `NegotiateAsync`, exact pin `0.19.0`). A client that answers the probes gets window-size-aware map rendering; a client that ignores them still plays, but the server never learns the terminal width and map art falls back to a default width. If your client's log shows the §2 wire order, the handshake is healthy — there is nothing further to configure.

Screen-reader players: switch to the screen-reader command after login. It strips map art and decorative spacing while keeping exits, contents, and names — the same world, linearized. The command catalog (including the reader) lives in [Commands: using and writing them](06-commands.md).

## 2.2 Negotiation failure modes (and why most are harmless)

A client that answers nothing to `DO TTYPE` gets the defaults: unknown terminal type, no window size, UTF-8 assumed. The session plays normally except map art uses the fallback width — annoying, not broken. Enable negotiation in the client profile (§2.1) and the next connection picks up the real width.

A client that half-negotiates (answers `TTYPE` but ignores `NAWS`) gets a named terminal with a default width — better, still not exact. Same fix: the missing piece is the client's `NAWS` response, not anything server-side.

Garbled text in one direction only is almost always an encoding mismatch: the client is set to Latin-1 or a local code page while the server speaks UTF-8 both ways. Set the client to UTF-8 (§2.1 profile) and accented names, room descriptions, and channel chatter render correctly. Garbling in logs but not on screen points at the log viewer, not the connection.

A connection that drops during negotiation — banner never appears — is the one case worth operator attention: something between client and server (a proxy with telnet-unaware filtering, a firewall doing protocol inspection) is interfering with the option bytes. Raw `telnet(1)` from the server host itself distinguishes local misconfiguration from middlebox interference: banner locally means the path is at fault, no banner locally means the game listener is (check `save/server.log` and the triage list in [Running the server](07-server-lifecycle-cli.md)).

## 3. The session: banner, login, puppet, quit

Every connection, web or telnet, walks the same pipeline:

```
  connect
    │
    ▼
  connection banner (ServerName + login hints)
    │
    ▼
  connect <account> <password>   — returning player
  create <account> <password>     — new account, then `new <name>` for its first character (creation throttle may apply, §4)
  guest                          — look around without an account (if enabled)
    │
    ▼
  character select (accounts with several characters pick one here)
    │
    ▼
  puppet: you ARE the character — look/map/move/say act in-world
    │
    ▼
  quit — clean disconnect (session detached, character unpuppeted)
```

A `connection` is the transport (socket). A `session` is the login state riding on it. A `character` (puppet) is the in-world avatar you drive after select. An `account` owns credentials and one or more characters (cap: `MaxCharacters`, default 5). Mixing these up is the source of half of all "where did my character go" confusion: disconnecting drops the connection, logging out drops the session, and neither deletes the character.

Password input is never echoed and never logged — what you type at a password prompt goes to authentication only. Failed logins answer `Invalid password.` whether the account name or the password was wrong (unknown names hash a dummy password first, so timing leaks nothing).

Guests share the `Guest` rung of the `Privilege` ladder (Guest → … → Admin). Guest characters cannot own persistent state the way account characters do; anything a guest builds is ephemeral by policy. Operators can disable guest access entirely.

## 2.3 Choosing between webclient and telnet

Use the webclient for maps, the editor, audio, and zero-install play — it is the full experience and the only client that renders the map pane and the drawing editor. Use telnet (or a MUD client) for low-bandwidth links, screen-reader-driven play with a terminal you already trust, bots and bridges that speak raw TCP, and headless servers where no web listener exists at all. Both drive the same character through the same command pipeline: a room built over telnet renders in the webclient map pane, and a channel message sent from the browser arrives on telnet. The transport changes the framing, never the world.

MUD-client extras worth enabling: scrollback (thousands of lines — room text scrolls fast during tours), triggers on the pager prompt and on login banners (auto-login on reconnect), keypad movement bound to exit directions, and separate windows or gags for channel chatter versus room text. None of these need server support — they consume the same bytes the raw terminal shows.

## 3.1 The panes and prompts in detail

The terminal pane shows game output oldest-at-top with room text, chat, and command results interleaved in arrival order. Long output pages rather than flooding — when a command produces more than a screenful, the client holds the remainder behind a pager prompt instead of scrolling it past. The input line keeps focus during output bursts, so combat or chatty rooms never steal keystrokes.

History (Up/Down) is per browser session: closing the tab starts a fresh recall buffer, while the character, its inventory, and its position persist server-side. Reconnecting resumes the world where you left it — the client forgets, the server remembers.

The map pane draws the rooms around your character from live map data: your room centered, linked neighbors at their exits, legend colors per terrain. It re-renders on every movement and on `map`. Builders editing the world see their changes appear in the pane as soon as the edit commits — no client refresh needed, because the pane subscribes to the same data the `map` command reads.

The audio toggle covers event sounds (movement, messages, alarms). Off means silent; the toggle persists per browser so a muted laptop stays muted across visits.

Name and password rules in one paragraph: account names are unique, case-insensitive, and bounded in length; character names draw from the same uniqueness pool per account cap (`MaxCharacters`, default 5); passwords have a minimum length and are hashed with the single static salt (`SaltProvider` — see [Saves and backups](10-persistence-saves.md)) so the database never holds plaintext. Rejections name the failed rule without leaking which accounts exist.

## 3.2 Puppet edge cases

One character, one driver: puppeting a character that is already puppeted elsewhere is refused with `This character is not available.` — the old session keeps control, the new one does not fight for input. Deleting a character unpuppets every session attached to it first — sessions survive, puppets do not linger on deleted avatars.

Connection loss mid-puppet (closed laptop, dead socket) leaves the character unpuppeted but intact in its room: reconnect, log in, select, and you are back where the transport died. Position and inventory kept living in server memory the whole time — the disconnect dropped a socket, not a savegame. What does NOT survive a simultaneous server crash is anything since the last checkpoint (see [Saves and backups](10-persistence-saves.md)) — reconnecting after a crash may rewind minutes, which is the save discipline working as documented, not lost data.

## 4. Operator notes: caps, bans, throttles, size limits

Per-IP and total connection caps (`MaxConnectionsPerIp`, `MaxTotalConnections`) bound how many simultaneous connections the server accepts. Past the cap, the new socket is closed at registration — the client sees the connection drop with no message; the reason (`per-IP limit … reached` / `total limit … reached`) goes to the server log, not the client. Nothing is queued. The caps live in settings; the enforcement lives in `ConnectionManager` (see Under the hood). If legitimate players hit the cap during an event, raise it and `restart` (settings changes need a restart, not a `reload`).

Bans: a banned IP is refused at connect time (`IpBanStore` outcomes — the ban list persists across restarts, so rebooting does not clear it). Ban and unban through the in-game admin verbs (catalog in [Commands: using and writing them](06-commands.md)); never hand-edit the ban rows in the database file.

Creation throttles: account creation is rate-limited per source. Too-fast repeat `create` attempts are rejected with a wait-and-retry message — this is the creation throttle doing its job against bots, not a bug. Legitimate bursts (a classroom signing up at once) should pace themselves or ask the operator to create accounts from the server side (`create` CLI verb, [Running the server](07-server-lifecycle-cli.md)).

Message size caps — both directions, both protocols:

| Setting | Default | Meaning |
|---|---|---|
| `WebsocketMaxMessageSize` | 65536 | 64 KB max per inbound WebSocket message. Oversize input is dropped silently; the connection stays up. |
| `TelnetMaxLine` | 65536 | 64 KB max per telnet line. Same silent-drop policy. |

Both caps are 64 KB (65536), not 8 KB — older notes quoting 8 KB are stale. Reads run a 64 KB fragment/line loop: WebSocket frames accumulate up to the cap, telnet bytes accumulate to end-of-line up to the cap, and anything past the cap vanishes without a reply to the sender (the server log records the drop, throttled) rather than disconnecting them.

Log triage for connection complaints: `save/server.log` first, then the ordered checklist (`/health`, `ss`, `look` → `limbo`, pid claim) in [Running the server](07-server-lifecycle-cli.md).

## 4.1 Message examples (what each outcome looks like)

```text
# Past the connection cap (per-IP or total):
(the connection closes with no message; the server log names the limit)
# Banned IP at connect time:
(the connection closes with no message; the log notes the banned host)
# Too-fast repeat creation:
Account creation throttled — wait a moment and retry.
# Overlong WebSocket message or telnet line (64 KB caps):
(no reply at all — the drop is silent; only the server log records it)
# Bad login (account and password both hidden):
Invalid password.
```

The cap/ban/throttle/login wordings above are quoted verbatim from the code (`ConnectionManager`, `WebSocketHandler`, `ConnectCommand`, `CreationCooldownHelper`); the overlong case is silent by design, so there is no sender-side string to quote. What matters for this doc: every refusal names its cause (full, banned, throttled, wrong-credentials) so players know whether to wait, retry, split input, or ask the operator — except the size gate, which says nothing and keeps the session alive. Nothing in this table disconnects punitively except the ban — caps and throttles and size gates all refuse quietly while keeping the session alive.

## 4.2 Tuning caps for events

An event night (a dozen players arriving at once) stresses two knobs: the total connection cap (simultaneous sockets) and the creation throttle (simultaneous signups). Raise the connection cap ahead of the event, `restart` to apply (settings need a restart — `reload` only hot-loads game code), and pre-create accounts from the server side with the `create` CLI verb ([Running the server](07-server-lifecycle-cli.md)) so signup throttles never gate the door. After the event, restore the tighter values and `restart` again — generous caps left in place become abuse surface. Bans set during the event persist across both restarts by design; review them the next day rather than assuming a reboot cleared them.

## 4.3 Ban lifecycle (operator view)

Bans are set and cleared through the in-game admin verbs — never by editing the database. A ban records the address (or range), the reason, and the setter; the banned source is refused at connect time from the next connection onward (existing sessions from that source are dropped when the ban lands). Unban reverses it the same way: next connection from the address succeeds. Both directions persist across restarts — the ban list is world state in `save/database.sqlite3`, covered in [Saves and backups](10-persistence-saves.md) — so a reboot is never a pardon and never a sentence. Review standing bans after events (§4.2); stale event-night bans are the commonest cause of "it worked yesterday" complaints.

Creation throttles need no administration at all: windows slide, counters decay, and a throttled source succeeds on retry after waiting. Operators should resist clearing throttle state by hand — the decay is the feature, and manual clears teach nothing about whether the burst was a classroom or a botnet. The log's `ThrottledLog` summaries show the rate shapes; classrooms spike once and decay, botnets sustain.

## 5. Full session transcript (fresh game)

Webclient, first visit after `./atheriz.sh start`:

```text
> (banner: ServerName + login hints)
> connect
usage: connect [-h] account_name password
> create hero1 s3cretPass!
Account hero1 created.
> new HeroOne
Character HeroOne created.
> connect hero1 s3cretPass!
Welcome hero1.
Please select a character to play:
0. HeroOne

or type 'new' to create a new character
Enter your choice:
> 0
> look
limbo
...room description, exits, contents...
> map
...nearby rooms render in the map pane...
> quit
Goodbye!
(connection closes; session detached; character unpuppeted)
```

Telnet shows the same flow over a raw terminal — the only differences are the `telnet localhost 4444` dial-in and terminal echo handling at password prompts. `guest` shortens the transcript: no account, no select, straight to a guest puppet with ephemeral state. `quit` always lands the same way: transport closed, session detached, character unpuppeted, world consistent.

## 6. Client setup notes

MUD clients (Mudlet, TinTin++, MUSHclient, BlowTorch): create a profile with host = server address, port = 4444, encoding = UTF-8, and leave telnet negotiation enabled — the server's `DO TTYPE → WILL SGA / WILL BINARY / DO NAWS` exchange (§2) needs a negotiating peer to learn terminal type and window size. With negotiation disabled the session still works but the server never learns the window size, so map rendering falls back to a default width.

Raw `telnet(1)`: works as-is for smoke checks (`telnet localhost 4444`, log in, `look` → `limbo`). Line mode is fine; character mode is unnecessary since the server handles both through the `TelnetSessionReader` normalization layer.

Browser trouble: the webclient needs WebSocket (`/ws`) on the same host/port as the page. A page that loads but never runs commands usually means a proxy stripped the WebSocket upgrade — check `save/server.log` for the 403/400 handshake gates firing, then fix the proxy rather than the game. Connecting directly to the game port (bypassing the proxy) distinguishes the two cases in one step.

## 6.1 Quick-start snippets

TinTin++ (connect, UTF-8, and a movement alias in three lines):

```text
#session game localhost 4444
#config {CHARSET} {UTF-8}
#alias {n} {north}
```

Mudlet: open Profile → New, host `localhost`, port `4444`, encoding `UTF-8`, save, Connect. Bind the keypad (Settings → Keybindings) to `north/south/east/west/up/down` for one-key movement. Enable logging per profile when touring new areas — the scrollback outlives the session and doubles as a builder's notebook.

Browser with no install: open `http://localhost:9999/`, Play, log in — nothing to configure when the server and browser share the machine. Remote play needs only the host swapped for the server address (and `https` when the operator fronts TLS — see [Hosting and configuration](08-hosting-configuration.md)).

## 7. When connecting fails: player-side checklist

1. Wrong port? Confirm the game's actual web (9999 default) and telnet (4444 default) ports — game folders override them.
2. Server still starting? `/health` ok + `/ready` `starting` means wait for `DoStartup completed`, not retry credentials.
3. Banned IP? A connect-time refusal naming the ban is final for that address — ask the operator (bans persist across restarts).
4. Throttled creation? A `create` rejection naming the cooldown means wait and retry — rapid repeats extend the wait.
5. Overlong input? Anything past 64 KB per message/line is dropped with a notice (§4) — split pastes into smaller chunks.
6. Stale client? After a server upgrade, hard-refresh the webclient before reporting missing UI — entry HTML is no-cache but the browser may still hold old bundles from before.

## 7.1 What to send the operator (bug-report recipe)

A connection complaint the operator can act on fits in six lines:

```text
who:      account/character name (or guest + approximate time)
where:    webclient (browser + version) or telnet (client + profile)
what:     exact refusal or symptom, quoted verbatim from the screen
when:     clock time with timezone (lets them find it in save/server.log)
health:   /health answer and /ready answer at that moment
worked:   last time it worked, and anything that changed since
```

The `/health` + `/ready` pair (§1) halves the diagnosis before it starts: both ok means the fault is past the server (account, ban, throttle, proxy); `starting` on ready means the server is mid-boot and the only fix is waiting; refused health means the server is down and the operator's triage checklist in [Running the server](07-server-lifecycle-cli.md) takes over. Reports without the refusal text get one round-trip asking for it — quote the screen first and skip the round-trip.

## 8. Proxies, TLS, and the WebSocket upgrade

The webclient speaks WebSocket (`/ws`) to the same host and port that served the page. Anything between the browser and the game — reverse proxy, TLS terminator, container ingress — must forward the WebSocket upgrade (the `101 Switching Protocols` handshake) rather than treating it as a plain HTTP request. Symptoms of a proxy eating the upgrade: the page loads, the banner may even render from cache, but no command ever answers and the status strip cycles reconnecting. The server log confirms it: handshake-gate lines (403/400) or upgrade failures per attempt, none of which appear when the game itself is at fault.

Checklist for proxy fronts:

1. Forward `Upgrade` and `Connection` headers intact to the game port (9999 default). A proxy that strips them turns every WebSocket dial into a plain GET the game correctly rejects.
2. Keep idle timeouts above the 20-second WebSocket keep-alive interval (see [Hosting and configuration](08-hosting-configuration.md)) — a proxy that kills "idle" sockets faster than the keep-alive will drop quiet players who are reading room text.
3. TLS must terminate somewhere exactly once: either the proxy holds the certificate and speaks plain HTTP to the game, or the game holds the pair (`SslCertFile`/`SslKeyFile`) and the proxy passes TCP through. Terminating twice (or zero times on a public host) produces handshake failures both sides log but neither explains.
4. Bypass test: connect a client directly to the game port, skipping the proxy. Commands answering directly while failing through the proxy convicts the proxy config in one step — no game-side change needed.

Telnet behind NAT needs none of this — raw TCP has no upgrade to strip. When telnet works and the webclient does not through the same front, the proxy's WebSocket handling is the suspect, full stop.

## Under the hood: how connections really work

This section is for operators diagnosing failures and game devs extending the pipeline. It names the classes and files behind every behavior above.

`ConnectionManager` (`src/Atheriz.Core/Network/ConnectionManager.cs`) is the protocol-agnostic registry: every live connection registers here regardless of transport. It enforces the per-IP and total caps (`MaxConnectionsPerIp`, `MaxTotalConnections` — over-cap sockets are closed at registration with the reason logged server-side), keeps per-host buckets for throttle accounting, sweeps orphans (connections whose transport died without a clean close), and dispatches parsed input to the command layer — JSON-array command envelopes on WebSocket, plain lines on telnet. Nothing protocol-specific lives here — that is the point: caps, sweep, and dispatch behave identically for web and telnet clients.

`BaseConnection` owns one connection's lifetime: the inbound input queue, ordered delivery to dispatch, and disposal. On disconnect the queue drains (already-accepted input still runs) and then the session detaches — which is why `quit` is graceful but killing a socket still leaves the world consistent.

WebSocket path: `src/Atheriz.Server/Hosting/WebSocketHandler.cs` (`HandleAsync`) plus `src/Atheriz.Core/Network/WebSocketProtocol.cs`. The handshake gates reject banned IPs (403, before anything else) and non-websocket upgrades (400 for malformed requests) before any game state is touched. Each accepted socket gets a `GenerateConnectionId`, is `RegisterConnection`-ed with the manager, then reads in a 64 KB fragment loop gated by `WebsocketMaxMessageSize` — an oversize message is dropped at the shared `HandleCommand` entry (log line server-side, no reply, connection stays up). Each complete message runs per-message `HandleCommand`; `Disconnect` runs in a `finally` so it cannot be skipped by an exception; `ApplicationStopping` shutdown closes all sockets during `DoShutdown`.

Telnet path: `TelnetHostedService` (`src/Atheriz.Core/Network/TelnetHostedService.cs`, wired by `src/Atheriz.Server/Hosting/Protocols.cs`) accepts raw TCP clients in both hosting modes — headless servers still serve telnet, because the hosted service does not depend on Kestrel. Per-connection framing splits across `TelnetProtocol` (option parsing + negotiation state), `TelnetSessionReader` (bytes → lines: buffering, normalization, the 64 KB `TelnetMaxLine` gate), and `TelnetCsWriter` (buffered writes back to the socket). Password redaction happens at the reader layer: bytes typed at a password prompt are consumed by authentication and never enter logs or echo buffers.

Negotiation itself is delegated: the `telnet_cs` library's `NegotiateAsync` performs the `DO TTYPE → WILL SGA / WILL BINARY / DO NAWS` exchange quoted in §2; the engine configures UTF-8 both ways, charset negotiation off, and window-size tracking, then reads the results. Engine code never hand-assembles negotiation bytes.

`InputFuncs` (`src/Atheriz.Core/Network/InputFuncs.cs`) holds the `[InputFunc]`-marked handlers: the small state machines behind multi-step connection-screen input (password prompts, character select, paged output). Each handler is a pure function of the session plus the new line — no sockets, no threads.

Rate control splits three ways: `PendingLimiter` bounds queued-but-undispatched input per connection (a flooding client gets capped, not the server); `ThrottleWindow` measures per-source creation/login rates for the throttles in §4; `ThrottledLog` keeps the server log readable under abuse by collapsing repeated refusal lines into counted summaries.

`MapEditorSettings` plus the mapedit wire validation (`src/Atheriz.Core/Network/MapEditorSettings.cs`) validate the drawing-editor settings object arriving over either transport. `TryParse` requires every key and bounds every value; failures return a message naming the field:

| Field | Invalid input | Outcome |
|---|---|---|
| `fgColor` / `bgColor` / `roomColor` | not an `[r,g,b]` triple of 0–255 integers | rejected with a message naming the field |
| `fgSlots` / `bgSlots` | not a list of 1–64 colors | rejected with a message naming the field |
| `gradientStops` | not a list of 2–64 colors | rejected with a message naming the field |
| `selectedChar` / `fontFamily` | over the length caps | rejected with a message naming the field |
| `fontSize` | not an integer 1–`MaxFontSize` | rejected with a message naming the field |
| `tools` / `roomVisible` | wrong JSON shape | rejected with a message naming the field |
| unknown keys | extra data | dropped — the canonical form (`ToJson`) keeps known keys only |

The editor UI that speaks this protocol is documented in [Webclient and map editor](12-webclient-editor.md).

### Handshake gates in detail

The WebSocket handshake runs two gates before any game state is allocated. The ban gate (403) refuses IPs on the ban list — a 403 in the log during normal play means the source address is banned, not that the handshake is malformed. The well-formedness gate (400) rejects requests that are not WebSocket upgrades at all — plain GETs to `/ws` from health checkers, bots, or misconfigured monitors land here, harmlessly. Neither gate touches the world: rejected handshakes never register a connection, never allocate a session, never log a character in. A burst of 400s from a monitoring probe is noise, not an attack — point the probe at `/health` instead and the noise stops. (There is no origin gate: same-origin policy is a browser concern, and the engine accepts sockets from any page including its own.)

Telnet has no handshake gates because raw TCP has no handshake to gate: the accept loop takes the socket, negotiation runs (§2), and the connection banner goes out. Refusals happen one layer up (caps, bans) where the manager can name the cause (§4.1). A telnet listener that accepts and immediately drops is the caps path refusing; a listener that never accepts is the hosted service itself down — different layers, different log lines, different fixes.

### How the pieces fit on the wire

A WebSocket `look` from keypress to room text:

```text
keypress → browser frame → Kestrel → UseWebSockets (20 s keep-alive)
  → WebSocketHandler.HandleAsync → 64 KB fragment loop
  → HandleCommand per message → ConnectionManager dispatch
  → command Run → Msg fan-out → frames back down the same socket
```

A telnet `look` from keypress to room text:

```text
keypress → TCP → TelnetHostedService accept → TelnetProtocol option state
  → TelnetSessionReader (buffer + normalize + 64 KB TelnetMaxLine gate)
  → ConnectionManager dispatch → command Run → Msg fan-out
  → TelnetCsWriter buffered writes → TCP
```

Both paths converge at `ConnectionManager` dispatch and diverge only at framing — which is why caps, throttles, and the input pipeline behave identically for web and telnet clients, and why a bug in one transport's framing never corrupts the other's sessions.

### Queues, floods, and logs under abuse

A client pasting megabytes at the game hits three defenses in order: the 64 KB message/line gate drops the excess silently (connection survives); `PendingLimiter` caps the queued-but-undispatched backlog (the flooder waits, everyone else plays); `ThrottleWindow` records the burst for the creation/login throttles when the flood is credential-shaped. Operators watching `save/server.log` during such an event see `ThrottledLog` summaries (counts per window) instead of one line per dropped chunk — the collapse is what keeps the log readable while the attack runs. None of these disconnect the client: refusal-with-notice is the policy at every layer, so a misbehaving script degrades to slowness rather than a ban (bans stay an explicit operator action via `IpBanStore`).

### Connection limits at a glance

| Layer | Where | Over it means |
|---|---|---|
| Per-IP cap | `ConnectionManager` host buckets | one address holds too many sockets — refused with busy |
| Total cap | `ConnectionManager` registry | whole server full — refused with busy |
| Pending queue | `PendingLimiter` per connection | unread input piles up — sender capped, others unaffected |
| Message/line size | 64 KB gates (§4) | single input too large — dropped with notice |
| Creation rate | `ThrottleWindow` per source | signing up too fast — wait and retry |
| Ban list | `IpBanStore` | banned — refused at connect |

Caps refuse, gates drop-without-a-word, throttles delay, bans exclude — four different verbs for four different intents. Cap and ban refusals carry no client-facing text (the reasons land in `save/server.log`); throttle, login, and size-gate outcomes answer — or stay silent — as §4.1 quotes.

### File map

- `src/Atheriz.Core/Network/ConnectionManager.cs` — registry, caps, orphan sweep, dispatch, host buckets.
- `src/Atheriz.Core/Network/BaseConnection.cs` — input queue, lifetime, dispose, drain-on-disconnect.
- `src/Atheriz.Core/Network/WebSocketProtocol.cs` — handshake gates, fragment loop, `MessageTooBig`, per-message dispatch.
- `src/Atheriz.Core/Network/TelnetProtocol.cs` — option parsing + negotiation state.
- `src/Atheriz.Core/Network/TelnetSessionReader.cs` — bytes to lines, normalization, line gate, password redaction.
- `src/Atheriz.Core/Network/TelnetCsWriter.cs` — buffered writes.
- `src/Atheriz.Core/Network/TelnetHostedService.cs` — TCP accept loop (both hosting modes; wired by `src/Atheriz.Server/Hosting/Protocols.cs`).
- `src/Atheriz.Core/Network/InputFuncs.cs` — `[InputFunc]` multi-step input handlers.
- `src/Atheriz.Core/Network/PendingLimiter.cs`, `ThrottleWindow.cs`, `ThrottledLog.cs` — rate control and log collapse.
- `src/Atheriz.Core/Network/MapEditorSettings.cs` — mapedit wire validation.
- `src/Atheriz.Server/Hosting/WebSocketHandler.cs` — `HandleAsync`, registration, shutdown close.

---
Next: [Saves and backups](10-persistence-saves.md)
