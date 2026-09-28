# Hosting and configuration

> Covers: `src/Atheriz.Server/Hosting/`, `src/Atheriz.Server/Infrastructure/ServerLifecycle.cs`, `src/Atheriz.Core/Settings/`, `appsettings.json`, `appsettings.Development.json`, `Properties/launchSettings.json`, `wwwroot/.webclient-hash`. You learn the two hosting modes, which ports and routes the server exposes, how admin authentication works, and how every setting resolves from CLI flags down to defaults. CLI verbs themselves live in [Running the server](07-server-lifecycle-cli.md); connecting as a player lives in [Connecting: webclient and telnet](09-connecting.md).

## 1. Two modes: web app vs headless host

`ServerHost` boots one of two hosts depending on `WebserverEnabled`:

- WebApplication (Kestrel + static files + WebSocket route + admin routes) when `WebserverEnabled` is true. This is the normal mode: browser clients, health endpoints, and `/_internal/*` admin routes all served from one process.
- Headless generic Host when `WebserverEnabled` is false. It prints the `HeadlessBanner` instead of the web banner. Telnet still serves — `Protocols` wires `TelnetHostedService` in both modes, so turning off the web server never silences the MUD port.

Common boot runs the same way in both modes:

1. `AddAtherizCore` registers settings (with `ValidateOnStart`), the `ConnectionManager` singleton, and admin authentication.
2. `ApplyCliOverrides` layers `--port` / `--telnet-port` / `--host` over everything else (§5).
3. `EnsureDirs` plus `PathGuards` create and verify `save/` and `secret/`.
4. `PidFile.TryAcquire` claims `save/server.pid` (success logs `PID … acquired`).
5. `AdminToken.EnsureToken` guarantees `secret/admin.token` exists (web mode; headless skips token creation since there are no admin routes to guard).
6. `ServerLifecycle.DoStartup` loads the world, starts schedulers, and logs `DoStartup completed`.
7. `UseWebSockets` with a 20-second keep-alive interval, then `Map("/ws")` for the websocket endpoint (§3).
8. Startup banners, then `RegisterShutdown`: a single owner runs `DoShutdown` + pid `Release` + token `DeleteToken`, with a 5-second `ShutdownTimeout` bounding the whole sequence. The token file is deleted at every clean shutdown — each boot mints a fresh secret via `EnsureToken`, so there is no token continuity to preserve across restarts.

Shutdown is single-owner by construction — only one path (Ctrl+C, `stop`, service halt) runs the teardown, so checkpoint-on-exit cannot run twice concurrently.

## 2. Kestrel: binds, limits, TLS

`Hosting/KestrelConfig.cs` owns the listener setup:

- No bind at all when the web server is disabled.
- The configured host goes through `IPAddress.TryParse` — or the boot throws. There is never a silent fallback to another address; a typo in the bind is a loud startup failure, not a server listening somewhere unexpected.
- `0.0.0.0` / `::` bind via `ListenAnyIP`; any other IP binds via `Listen(ip, port)` on that address only.
- Limits: 4 MB max request body, 30-second header timeout, 2-minute keep-alive timeout.

TLS is opt-in via certificate files. `TlsCertLoader.Load(certPath, keyPath)` loads the pair (see `src/Atheriz.Core/Utils/TlsCertLoader.cs`), and `KestrelCertSelector` serves the single preloaded `X509Certificate2` per handshake — no filesystem access on the select path, so per-connection latency never touches disk. A missing or unloadable certificate throws at startup unless `AllowInsecureTlsFallback` is set, in which case the server warns loudly and serves plaintext. `Properties/launchSettings.json` ports (5083/7012) are development-only and never apply to a game-folder server.

Minimal public-host override (`appsettings.json` in the game folder):

```json
{
  "Atheriz": {
    "ServerName": "MyGame",
    "ServerHostname": "0.0.0.0",
    "WebserverPort": 9999,
    "TelnetPort": 4444
  }
}
```

TLS file fields (paths resolved relative to the game folder unless absolute):

```json
{
  "Atheriz": {
    "SslCertFile": "secret/cert.pem",
    "SslKeyFile": "secret/key.pem"
  }
}
```

Or without touching the file — environment only:

```bash
ATHERIZ_SSL_CERTFILE=secret/cert.pem ATHERIZ_SSL_KEYFILE=secret/key.pem ./atheriz.sh start
```

## 3. Routes: pages, health, admin, static

| Route | What serves it | Notes |
|---|---|---|
| `/` | Game `web/templates/index.html` when present, else a fallback `<h1>ServerName</h1>` page with a Play link | Entry point for browsers. |
| `/webclient` and `/webclient/index.html` | Compiled terminal client | See [Connecting: webclient and telnet](09-connecting.md). |
| `/atheriz_draw` and `/atheriz_draw/index.html` | Compiled drawing / map editor | See [Webclient and map editor](12-webclient-editor.md). |
| `/health` | Liveness, unconditional | Always `200` with `{status:"ok",server:...}` once the web host is up, even mid-startup. `server` carries the configured `ServerName`. |
| `/ready` | Readiness | `200` with ok only after `StartupSucceeded`; before that, `503` with `starting`. Poll this one behind load balancers. |
| `/_internal/*` | Admin only (`hot_reload`, `shutdown`, `create_account`) | `X-Admin-Token` required (§4). Used by the CLI verbs in [Running the server](07-server-lifecycle-cli.md). |
| `/static` | `PhysicalFileProvider` + `DefaultFiles` | Hashed bundles ship immutable `max-age=31536000`; `.wasm` files get 86400 seconds; entry HTML is never cached. |

The health/ready mappings live in `src/Atheriz.Server/Hosting/StaticFileConfig.cs`. The split is deliberate: `/health` answers "is the process alive", `/ready` answers "did the world finish loading". During a slow `DoStartup`, health is `200 ok` while ready is `503 starting` — exactly the window the background-start readiness poll (§2 in [Running the server](07-server-lifecycle-cli.md)) bridges with its pid-plus-port check.

Request-routing waterfall (first match wins):

```
  incoming HTTP request
        │
        ▼
  /_internal/*? ──yes──▶ AdminAuth gate → AdminRoutes handler
        │ no
        ▼
  /ws? ──yes──▶ WebSocketHandler (only when WebsocketEnabled)
        │ no
        ▼
  /health or /ready? ──yes──▶ status JSON (no auth)
        │ no
        ▼
  /webclient/* or /atheriz_draw/*? ──yes──▶ compiled app (cached per §3 table)
        │ no
        ▼
  /static/*? ──yes──▶ PhysicalFileProvider
        │ no
        ▼
  / → game template or fallback Play page
```

## 4. Admin auth: token, loopback, timeouts

Three pieces cooperate: `AdminAuth` (the gate), `AdminToken` (the secret on disk), and `ShutdownClient` (the CLI-side caller).

- Every `/_internal/*` request must carry `X-Admin-Token` matching the contents of `secret/admin.token`. Comparison is constant-time; the token file is re-read per request so rotation (delete + restart) takes effect without a code path for hot rotation.
- A loopback peer check rejects admin calls arriving on non-loopback interfaces, even with a valid token. Admin is local-only by design — remote administration means SSH to the host first.
- Failures return `401` with `{status:"error",message:...}` naming the cause (missing header, bad token, non-loopback peer).
- Token format is 32 random bytes rendered as hex (`token_hex(32)`-equivalent). The `secret/` directory is `0700`, the token file `0600`. A zero-byte token file is treated as poisoned and regenerated at next boot rather than accepted as an empty secret.
- The CLI client (`ShutdownClient` and the `reload`/`create` callers) uses a loopback-only `HttpClient` that tolerates self-signed certificates only on loopback, a 5-second request timeout, and one TLS-flip retry (try the configured scheme, then the other) so a cert/plain mismatch reads as a retry, not a dead server.

Lose the token and the CLI admin verbs stop working with `AuthRejected`. Fix: stop the server via the verified-kill path, start again — boot regenerates the token (a clean shutdown already deletes it, so a leftover `secret/admin.token` after a stop is either gone or stale — either way the next boot mints fresh hex with `0600` mode, and admin verbs work again). Back up `save/` when migrating hosts; `secret/` needs no migration — the token is per-boot by design (procedure in [Saves and backups](10-persistence-saves.md)).

## 5. Settings: the full table and precedence

Settings type: `src/Atheriz.Core/Settings/AtherizSettings.cs`. Validation: `src/Atheriz.Core/Settings/AtherizSettingsValidator.cs` (range checks run at startup via `ValidateOnStart` — a bad value fails boot loudly).

| Key (`Atheriz:`) | Default | What it controls |
|---|---|---|
| `SavePath` | `save` | World database + pid + log directory. |
| `SecretPath` | `secret` | Admin token + TLS-adjacent secrets. |
| `ServerName` | `AtheriZ` | Shown on `/`, `/health`, banners. Set it in the game folder's `appsettings.json` to brand a game (the scaffold's `GameSettings.ServerName` const is a convention for game code — the server itself reads only `AtherizSettings`). |
| `ServerHostname` | `localhost` | Bind address (must parse as IP when overridden via CLI). |
| `WebserverPort` | 9999 | HTTP/WebSocket port. |
| `TelnetPort` | 4444 | Telnet port. |
| `WebserverEnabled` | true | WebApplication vs headless Host (§1). |
| `WebsocketEnabled` | true | Whether `/ws` is mapped. |
| `TelnetEnabled` | true | Whether the telnet hosted service runs. |
| `MaxCharacters` | 5 | Cap on characters per account. |
| `MapEnabled` | true | Map rendering gate. |
| `LegendEnabled` | true | Legend layer toggle (a setting with no engine render gate — display-side only). |
| `WebclientSyncCheck` | true | Compare game `web/` against the engine hash at startup; warn on drift. |
| `WebsocketMaxMessageSize` | 65536 | 64 KB inbound message cap (see [Connecting: webclient and telnet](09-connecting.md)). |
| `TelnetMaxLine` | 65536 | 64 KB telnet line cap (same doc). |
| `SslCertFile` / `SslKeyFile` | unset | TLS pair (§2). |
| Threadpool keys | tuned defaults | `AsyncThreadPool` sizing (see [Engine internals](13-engine-internals.md)). |

Override precedence, highest first:

1. CLI overlay (`--port`, `--telnet-port`, `--host`) — last flag wins when repeated.
2. Unprefixed environment values plus individually-read `ATHERIZ_*` variables (`ATHERIZ_TELNET_PORT`, `ATHERIZ_SSL_CERTFILE` with its `ATHERIZ_SSL_KEYFILE` sibling, `ATHERIZ_SUPERUSER_*` for credentials). `ATHERIZ_ROOT` is read only by the `atheriz.sh`/`atheriz.cmd` wrapper scripts to locate the engine checkout — it is not a server setting.
3. `appsettings.{env}.json` for the current environment name (`Production` by default; `Development` only when the environment says so).
4. `appsettings.json` base file.

`ATHERIZ_DAEMON=1` is set by the background-start spawner on its detached child (see [Running the server](07-server-lifecycle-cli.md)) — it marks daemon context, not a user setting. Do not set it by hand.

`ATHERIZ_SUPERUSER_*` deserves one extra note: it is honored by `InitialSetup.ResolveCredentials`, which runs at `new` time, `reset` time, and first boot — so the variables are not `new`-only. Any flow that seeds credentials reads them.

## 6. Asset resolution and the webclient sync check

Static content resolves per game, not per install. `Infrastructure/AssetPathResolver.cs` searches in order:

```
  ResolveWwwRoot / ResolveTemplates
        │
        1. CWD game folder (game web/ or wwwroot/)  ──▶ wins when present
        │
        2. contentRoot install tree                 ──▶ packaged layout
        │
        3. engine assembly directory                ──▶ dev-loop fallback
        │
        4. appBase                                  ──▶ last resort
```

Game-before-install is the rule: a file in the game folder's `web/` tree shadows the shipped default. `ResolveWwwRoot` prefers a game `wwwroot/` and falls back to `web/` + `static`; `ResolveTemplates` prefers game `web/templates/` and falls back to the engine `templates/`.

`WebclientSyncChecker.CheckSync` then compares the game folder's `web/` tree (SHA256) against the engine `wwwroot/.webclient-hash` recorded at build time. It honors `WebclientSyncCheck`: when true (default) a drift prints the sync warning at startup telling you to redeploy the client into the game folder. The warning is advisory — the server starts anyway. Redeploy with the per-game `./build.sh --web` (details in [Webclient and map editor](12-webclient-editor.md)).

## 1.1 Boot transcript

A foreground start on a fresh game folder prints the sequence in order:

```bash
cd mygame
./atheriz.sh start --foreground
```

Expected output spine (game-specific lines vary, the anchors do not):

```text
PID 1234 acquired at save/server.pid
Admin token ensured at secret/admin.token
DoStartup completed
Web server listening on http://0.0.0.0:9999
```

`PID … acquired at …` means the pid claim succeeded — no other server owns this game folder. `Admin token ensured at …` means the per-boot admin secret is ready. `DoStartup completed` means the world loaded, schedulers started, and `/ready` flipped from `503 starting` to `200 ok`. `Web server listening on …` names the actual bind (scheme + address + port — `https` only when the cert loads). If the log stops before the first line, another process holds the claim; if it stops between the lines, the world load hung and the lines above the stall name the subsystem.

Shutdown mirrors the order: `DoShutdown` checkpoints, releases the pid claim, and deletes the token file (fresh secret next boot — by design, not a loss), all inside the 5-second `ShutdownTimeout`. A shutdown that exceeds the timeout is cut off — the log says so, and the next boot recovers from the last completed checkpoint (see [Saves and backups](10-persistence-saves.md)).

## 2.1 Bind examples

Localhost-only (default posture — develop and test here):

```json
{
  "Atheriz": {
    "ServerHostname": "127.0.0.1",
    "WebserverPort": 9999,
    "TelnetPort": 4444
  }
}
```

All-interfaces (public host — pair with TLS §2 and firewall rules):

```json
{
  "Atheriz": {
    "ServerHostname": "0.0.0.0",
    "WebserverPort": 9999,
    "TelnetPort": 4444
  }
}
```

IPv6 any (dual-stack where the OS supports it):

```json
{
  "Atheriz": {
    "ServerHostname": "::",
    "WebserverPort": 9999,
    "TelnetPort": 4444
  }
}
```

`0.0.0.0` and `::` take the `ListenAnyIP` path; anything else takes `Listen(ip, port)` on exactly that address. A hostname that is not an IP literal (for example `"localhost"` spelled as a name in a CLI `--host`) is refused at the CLI layer with exit 2 — resolve it to `127.0.0.1` or `::1` yourself. A typo in the config file (like `999.999.999.999` in `ServerHostname`) throws at Kestrel bind time — after the pid claim, so the stranded claim is released by the process-exit hook as the server dies; either way a bad bind never leaves a running server behind, and `save/server.log` names the value (`Unparseable WebserverInterface '…'`).

Headless (no web server — telnet only, MUD-port appliances):

```json
{
  "Atheriz": {
    "WebserverEnabled": false
  }
}
```

Expected: the `HeadlessBanner`, no Kestrel bind, no `/health` (nothing listens on 9999 — that is normal, not an outage), telnet still answering on 4444. `/ws` never exists in this mode because `Map("/ws")` is skipped alongside the rest of the web pipeline.

## 3.1 Route probes

```bash
curl --max-time 5 http://localhost:9999/health
curl --max-time 5 http://localhost:9999/ready
curl --max-time 5 -H "X-Admin-Token: wrong" http://localhost:9999/_internal/shutdown -X POST
```

Expected: `{"status":"ok",server:"AtheriZ"}`-shaped liveness (`server` carries the configured `ServerName` — `AtheriZ` unless the game folder brands it via `appsettings.json`); readiness ok (or `503 starting` mid-boot); `401 {"status":"error",...}` for the bad token. The third probe is safe — a wrong token changes nothing. The cache story is checkable the same way: hashed bundles under `/static` return `Cache-Control: max-age=31536000`, `.wasm` returns 86400 seconds, entry HTML returns no-cache directives. When players report stale clients after an upgrade, the entry-HTML headers are the first thing to verify before blaming the sync checker (§6).

## 4.1 Token loss recovery

Symptoms: every admin verb (`stop`, `reload`, live `create`) answers auth rejection while the game itself plays fine. The world is healthy; only the secret disagrees — typically a stale `secret/admin.token` copied over from a crashed host (whose boot will mint its own), a zero-byte `admin.token` left by a crashed write (poisoned: regenerated at next boot, never accepted empty), or a token replaced by hand mid-run (the per-request re-read picked up the edit; the CLI still sends the old one).

```bash
cd mygame
./atheriz.sh stop        # falls to verified kill: admin unreachable, pid checks pass
rm secret/admin.token
./atheriz.sh start
```

Expected: stop succeeds through the kill path (the log shows the graceful attempt failing closed, then the verified kill), the token file reappears at boot with fresh hex and `0600` mode, and admin verbs work again. There is no token to preserve across migrations — copy `secret/` in a backup only for TLS certs/keys kept there (procedure in [Saves and backups](10-persistence-saves.md)).

Never fix auth rejection by disabling the gate, binding admin to a public interface, or sharing the token over an insecure channel. The loopback check plus constant-time compare plus per-request re-read is the whole admin security story; weakening any leg exposes world-wiping verbs (`shutdown`, `hot_reload`) to the network.

## 5.1 Validation ranges and environment table

`AtherizSettingsValidator` runs at startup (`ValidateOnStart`) — a value outside its range fails boot with a message naming the key and the allowed range. Common rejections: ports outside 1–65535, negative caps, `MaxCharacters` below 1, empty `ServerName`, cert file configured without its key sibling. Fix the value and start again; the validator runs before the pid claim, so a rejected config never blocks a corrected retry.

Environment variables read individually (no prefix-stripping pass — each name below is matched exactly):

| Variable | Equivalent setting | Notes |
|---|---|---|
| `ATHERIZ_TELNET_PORT` | `TelnetPort` | Also aliased as `Atheriz__TelnetPort`. CLI `--telnet-port` wins over both. |
| `ATHERIZ_SSL_CERTFILE` | `SslCertFile` | Requires the sibling below. |
| `ATHERIZ_SSL_KEYFILE` | `SslKeyFile` | Missing sibling with cert set fails validation. |
| `ATHERIZ_SUPERUSER_USERNAME` | seed credential | Honored by `InitialSetup.ResolveCredentials` (`new`, `reset`, first boot). |
| `ATHERIZ_SUPERUSER_PASSWORD` | seed credential | Same callers; never logged. |
| `ATHERIZ_ROOT` | — | Wrapper scripts only (locate the engine checkout). Not a server setting. |
| `ATHERIZ_DAEMON` | — | Set to `1` by the background spawner on its child. Never set by hand. |

Unprefixed environment values and `appsettings.{env}.json` fill the middle layers (§5 precedence): `Production` file by default, `Development` file only when the runtime environment name says `Development`. `appsettings.json` is the base everything overrides. When a value surprises you, walk the precedence ladder top-down — CLI overlay first, then env, then env-specific file, then base — and the first layer that sets the key is the winner (last flag wins within the CLI layer).

## 6.1 Sync-warning example

A game folder whose `web/` tree drifted from the shipped client prints at startup (exact first line, per-area details follow):

```text
WARNING: Game webclient is out of sync with the server's!
  web/templates/webclient: 2 modified
    e.g. index.html, ...
```

The server starts anyway — the warning is advisory, not a gate. Redeploy:

```bash
cd mygame
./build.sh --web
./atheriz.sh restart
```

Expected: the rebuild refreshes the game `web/` tree from `webclient/src/` plus game overrides, the hash matches again, and the warning disappears on the next boot. `restart` (not `reload`) because static content resolves at boot through `AssetPathResolver`. Never hand-edit the compiled files under the engine `wwwroot/` to silence the warning — the source of truth is `webclient/src/` plus the game folder's `web/` overrides, and the next engine build overwrites hand edits (client deploy details in [Webclient and map editor](12-webclient-editor.md)).

## 7. Worked example: putting a game on the network

Goal: a game reachable by browser clients at `https://play.example.com` and telnet clients at `play.example.com:4444`, starting from a local-only game folder.

1. Set the public bind and name in the game folder's `appsettings.json` (§2 public-host override), keeping 9999/4444 unless the host policy says otherwise.
2. Place the certificate pair and set `SslCertFile` / `SslKeyFile` (or the `ATHERIZ_SSL_CERTFILE` / `ATHERIZ_SSL_KEYFILE` pair) — then foreground-start once and watch for the boot spine (§1.1). Any cert problem throws before the pid claim, so a bad path never strands a half-started server.
3. Probe from the host itself: `/health` → ok, `/ready` → ok (not `starting`), webclient `look` → `limbo`. Only then open the firewall.
4. Probe from outside: `curl --max-time 5 https://play.example.com/health` and `telnet play.example.com 4444`. A working TLS front with a refused telnet port means the web bind is right and the telnet bind (or its firewall rule) is wrong — the two listeners configure independently.
5. Leave `WebclientSyncCheck` on. The first startup warning about drift (§6.1) is normal after copying a game folder between machines; `./build.sh --web` plus `restart` clears it.

Roll back the same way if anything misbehaves: restore the `127.0.0.1` bind, `restart`, and the game is local-only again with the world untouched — bind changes never touch `save/database.sqlite3`.

## 8. Settings checklist before going public

- [ ] `ServerName` set (shows on `/`, `/health`, banners).
- [ ] `ServerHostname` an explicit IP (`0.0.0.0` / `::` for any-interface, or the single public IP).
- [ ] TLS pair configured, or `AllowInsecureTlsFallback` consciously accepted (plaintext on the open net is a choice, not a default).
- [ ] Connection caps reviewed against expected event load (caps refuse past the limit — see [Connecting: webclient and telnet](09-connecting.md)).
- [ ] `secret/salt.txt` backed up next to `save/` (password logins depend on it), plus any TLS pair kept in `secret/` — see [Saves and backups](10-persistence-saves.md).
- [ ] `/health` vs `/ready` wired into the supervisor or load balancer the right way round (§3): liveness restarts dead processes, readiness gates traffic during slow boots.

## 9. `launchSettings.json` and per-game web overrides

`Properties/launchSettings.json` (ports 5083/7012) applies to IDE / `dotnet run` development loops only — it never follows a game folder. A game folder's ports come from its own `appsettings.json` plus CLI overlay, full stop. If a game answers on 5083, someone launched the engine project directly instead of the game launcher; `cd` into the game folder and use its `./atheriz.sh`.

Per-game web overrides live in the game folder's `web/` tree: `web/templates/index.html` replaces the landing page, `web/templates/webclient/index.html` replaces the client shell. Anything not overridden falls through to the engine defaults via the §6 resolution order. Overrides deploy with the game (they are files in the game folder, backed up with everything else) and take effect at boot — static resolution runs once at startup, so web file changes need a `restart`, never just a `reload`.

## 10. Troubleshooting hosting issues

No listener on 9999 after start: the web server is disabled (`WebserverEnabled: false` — headless is intentional, use telnet), or Kestrel threw on a bad bind (read `save/server.log` — `IPAddress.TryParse` failures name the value), or the port is held by a previous server that outlived its pid claim (`ss -lptn` shows a stranger on the port — stop it via the triage checklist in [Running the server](07-server-lifecycle-cli.md), never by editing files).

`/health` ok but `/ready` stuck at `503 starting`: the web host is up and the world load is not done — `save/server.log` shows how far `DoStartup` got. Slow first boots on large databases are normal; a log that stops moving names the stuck stage.

Admin verbs fail but the game plays: token mismatch (§4.1) — recover there, not here.

Players see a stale webclient after an upgrade: entry HTML is no-cache but old hashed bundles can linger behind proxies — confirm the `/static` cache headers (§3.1), redeploy with `./build.sh --web`, and have players hard-refresh before investigating further.

Prefer explicit IPs over wildcards on multi-homed hosts: binding the game to its public address (rather than `0.0.0.0`) keeps game traffic off management interfaces. (Admin routes are loopback-gated independently of the bind — the bind choice does not change admin exposure.)

Hot-reload (`reload`) never rebinds listeners, re-reads ports, or flips hosting modes — it rewires game logic inside the running host. Mode, bind, TLS, and route changes all need `restart`. Code changes need rebuild plus `reload`. Keep the two apart and half of all "my change did nothing" mysteries disappear.

One last hosted-service note: the telnet listener has no readiness endpoint of its own — there is no telnet equivalent of `/health`. Liveness for the MUD port is the TCP accept itself (`telnet host 4444` connects and the banner appears) plus the pid-claim check from [Running the server](07-server-lifecycle-cli.md). Monitoring that needs both protocols should probe `/health` for the web side and open a TCP connection for the telnet side.

Under the hood: hosting wires in `src/Atheriz.Server/Hosting/` — `ServerHost.cs` (mode select + boot order), `Protocols.cs` (telnet hosted-service registration, both modes), `KestrelConfig.cs` + `KestrelCertSelector.cs` (binds + per-handshake cert), `WebSocketHandler.cs` (`Map("/ws")` + 20 s keep-alive via `UseWebSockets`), `AdminRoutes.cs` + `AdminAuth.cs` (watchdog-wrapped `hot_reload`/`shutdown`/`create_account`, token + loopback gates), `StaticFileConfig.cs` (route table + cache headers). `Infrastructure/ServerLifecycle.cs` (`DoStartup` / `DoShutdown` / `DoReload`) sequences world load, scheduler start, checkpoint-on-exit, and hot-reload rewire; `Infrastructure/PidFile.cs` and `Infrastructure/AdminToken.cs` own the pid claim and token lifecycle used by every CLI verb.

---
Next: [Connecting: webclient and telnet](09-connecting.md)
