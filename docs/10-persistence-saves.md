# Saves and backups

> Covers: `src/Atheriz.Core/Persistence/`, `src/Atheriz.Core/Globals/IdGenerator.cs`, `src/Atheriz.Core/Globals/SaltProvider.cs`, `src/Atheriz.Core/Globals/IpBanStore.cs`, `src/Atheriz.Core/Globals/CreationCooldownStore.cs`, `src/Atheriz.Core/Globals/BoundedDictionary.cs`, `save/` + `secret/` layout, `../persist.md`. You learn when your world is saved, which files matter, how to back up and restore a live game, and what survives each of reload, restart, and reset. CLI verbs (`stop`, `reset`) live in [Running the server](07-server-lifecycle-cli.md); what of your objects persists lives with the object guide in [Objects: creating and using them](03-objects-library.md).

## 1. The one discipline

> Load ONCE at startup; the world lives in memory; the database is written ONLY on save (checkpoints and shutdown).

Read that twice, because everything in this doc follows from it. At boot, the server reads every row out of `save/database.sqlite3` into live in-memory objects and then never touches the database file again until a save. Your game code mutates live objects (`GameObject.Create()`, property sets, containment moves, link edits) — all memory operations. The database file on disk stays stale until the next checkpoint or shutdown flushes the whole dirty set through in one transaction.

What this means for YOU:

- Never read the database file mid-game. No mid-game loads, no per-request queries, no peeking at rows to see live state — the file lags memory by design, so whatever you read there is outdated by definition.
- Never hand-edit `save/database.sqlite3`. Edits made while the server runs are invisible to the live world and overwritten at the next save; edits made while stopped bypass validation and migration. Build in-game or in code, then save.
- A crash loses everything since the last save. There is no write-ahead log of individual actions, no per-command fsync — the unit of durability is the checkpoint. If your players just finished a three-hour build session, save before you `restart`.
- `GameObject.Create()` (and every mutation helper around it) mutates memory. Persistence is not a property of the call — it is a property of the next save. `ObjectRegistry.SaveObjects()` is the checkpoint entry point that turns dirty memory into durable rows.

## 1.1 The crash story, concretely

The scheduler is opt-in: `AutosaveMinutes` defaults to `0`, which disables timed checkpoints entirely, so a default server only saves on the `save` command and on clean shutdown. Timeline on a server with autosave set (say every 20 minutes): 12:00 checkpoint commits. 12:00–12:20 players build two rooms, link them, lock a door between them, and chat. 12:20 power dies. On reboot the loader takes the 12:00 commit: the two rooms do not exist, the link does not exist, the door was never locked — but neither does any corruption exist, because the uncommitted 20 minutes were memory-only and memory is gone. The chat scrollback rewinds too (history bounded and checkpointed like everything else). Nobody's account broke, nobody's old inventory duplicated, nothing half-applied: the rewind is total and clean, which is exactly what "the unit of durability is the checkpoint" guarantees. The operator's announcement writes itself from the log's last commit line: "we rewound to 12:00." Compare the alternative the discipline refuses: per-action writes that could leave the link without its rooms, the lock without its door — a half-applied world no reboot can explain. Total rewinds are legible; partial saves are mysteries. The discipline chooses legibility.

## 1.2 What the discipline asks of each role

Players: nothing — play normally, and know a crash may rewind to the last save (bounded by the autosave interval when one is set, unbounded back to the last manual save or shutdown otherwise). Report rewinds with times; the log confirms them.

Builders: batch work between saves (§2.3 rhythm — build, confirm commit, continue), and never treat "it works in-game" as "it is stored." Live behavior proves memory; only the commit mark proves durability.

Operators: tune the interval to the community's mourning threshold (§5.1), save around events, keep three backup generations (§4.3), and read `save/server.log` before player reports arrive.

Game devs: push durable fields through settable properties (§6), request checkpoints after runtime-created state that matters, and decide per-field what survives reboot (§6.2 scenarios) — the engine persists what you flag, exactly what you flag, nothing more.

## 2. What gets saved

Everything the world needs to resume exactly where it stopped:

| Saved | Examples | Notes |
|---|---|---|
| Objects | rooms, items, characters, accounts | Full state: names, descriptions, tags, containment, scripts bound, dirty-flagged properties via `ISettable`. |
| Rooms, areas, links | area → grid → node tree, `NodeLink` exits | Exits persist as `Transitions` rows (one per direction), rooms as area-tree rows; legend and map payloads ride in map rows backing the rendered map. |
| Doors | door objects + `DoorDto` gate state | Open/locked state persists; a door left locked stays locked. |
| Game time | calendar, tick counters, alarm registrations | Alarms re-register on load, so a bell scheduled before shutdown still rings after. |
| Channels and history | channel membership, `ChannelHistoryEntry` backlog | Bounded — history keeps its cap, not the full scrollback of all time. |
| Bans and cooldowns | character/account `IsBanned` flags | Bans on characters and accounts persist (a flag on the object row) and survive restarts by design; rebooting never clears a ban. Temp IP bans and failed-login windows in `IpBanStore` are memory-only and clear on reboot. |

Safe-immediately vs needs-a-save, from the player's point of view:

| Action | Durable when? |
|---|---|
| `say`, movement, `look` | Never persisted per action — position and chat live in memory until the next save. |
| `get`/`put`/`give`, building, linking rooms, locking a door | In memory at once, durable at the next save. A crash before the save rolls them back. |
| `create` (account/character) | Same rule — the character exists in memory immediately and survives only if a save lands after. Operators scripting bulk creation should save (or `stop`, which saves) before calling it done. |
| Checkpoint / autosave / clean shutdown | Durability points — everything dirty up to that moment is flushed. |

There is no per-action durability knob. If it matters, save; if it does not, the next scheduled checkpoint covers it.

## 2.2 Why nothing is durable immediately (and why that is fine)

Per-action durability (fsync on every `get`, every step, every word) would make each command pay disk latency — the game would feel the database on every keystroke. Batching durability into checkpoints flips the cost: gameplay runs at memory speed and the disk pays once per interval for the whole dirty set. The price is the crash window (§1: everything since the last save), and the currency that buys it back is the interval knob (§5.1) plus deliberate saves around what matters (§5: before events, after build sessions, on shutdown). Operators who understand the trade tune it; operators who fight it (demanding per-command durability) tax every player for insurance nobody feels. The discipline is one sentence long because the mechanism is one mechanism: memory leads, checkpoints commit, crashes rewind to the last commit.

## 2.3 Reading the save state from in-game

Players cannot see the checkpoint schedule and should not need to — but builders mid-session can ask the operator "are we saved?" and get a real answer: the superuser `save` command ("`Saving...`" then "`Saved in ... milliseconds.`") plus the `Checkpoints` row (§2.1 table) name the most recent commit. A practical rhythm for build sessions: build for a while, ask for a `save`, hear the confirmation, keep building. The confirmation that matters is the commit mark, not the request — "saving" is a claim, "`Saved in ...`" is a fact, and the journal row (§5 crash anatomy) is the arbiter when the two disagree. (`save export <file>` dumps the object rows to JSON for inspection — same command, data path instead of durability path.)

## 3. `save/` layout: which files matter

All paths below are inside the game folder:

| Path | What it is | Back up? |
|---|---|---|
| `save/database.sqlite3` | The world — objects, maps, doors, time, channels, bans. Back THIS up. | Yes — this is the backup. |
| `save/server.log` | The server log. Diagnose here first on any issue. | Optional (useful context, not world state). |
| `save/server.pid` | Pid claim for the live server (`0600`). Stale after a crash; never restore this onto another host. | No — exclude from backups, or delete after restore. |
| `save/.wipe-lock` | Taken during `reset` wipes. Transient; never present on a clean stopped server. | No. |
| `secret/admin.token` | Admin-route shared secret (`0600` inside a `0700` `secret/`). Per-boot in web mode: a clean shutdown deletes it and the next boot mints fresh hex — no continuity to preserve. | No — back up `secret/` only for TLS certs/keys kept there (see §3.2). |
| `secret/salt.txt` | The password-hash salt (`0600`). Every stored password hash depends on it — lose it and nobody can log in. | Yes — always with the database. |

The headline rule: `save/database.sqlite3` is the world, and `secret/salt.txt` is its key. Everything else is log, lock, cert, or per-boot secret. A backup that contains the database without the salt restores a world nobody can log into; a backup missing the database contains souvenirs. Copy `secret/salt.txt` (and any TLS pair) next to `save/` in every backup and migration, modes preserved.

`save/` and `secret/` directories are created `0700`; pid and token files are `0600`. Keep those modes on restore — a token file readable by other users leaks admin access to every local account on the machine.

## 3.2 Secret continuity (why `secret/` usually stays home)

`secret/admin.token` is the admin-route credential from [Running the server](07-server-lifecycle-cli.md) and [Hosting and configuration](08-hosting-configuration.md): every `stop`, `reload`, and live `create` presents it as `X-Admin-Token`. In web mode the token is per-boot by design — a clean shutdown deletes the file and the next boot mints fresh hex at `0600` — so after the stop-then-copy backup below there is normally no token file to copy, and none is needed: the restored server mints its own and the CLI reads whatever is current. Do not chase "token continuity" across migrations; it does not exist to preserve. The one exception is deliberate rotation (compromised token, or a stale token left by a crash): stop the server, delete `secret/admin.token`, start — boot regenerates fresh hex at `0600` and the old token dies with the old file. Rotation is a stop-delete-start sequence, never an edit (a zero-byte file from a truncated write is poison: regenerated, never accepted empty).

TLS-adjacent files also live under `secret/` when configured that way (`SslCertFile`/`SslKeyFile` pointing into `secret/` is the common layout). They travel under the same rule: the pair moves with the game, permissions preserved (`0700` dirs, `0600` files), or the new host serves plaintext/fails validation per `AllowInsecureTlsFallback`. A migration checklist that names `database.sqlite3` but forgets the cert pair produces a host that plays over telnet while browsers refuse the TLS front — copy the whole `secret/` tree, not just the token.

## 3.3 What never to touch by hand

- `save/database.sqlite3` while the server runs (stale by design — memory leads, the file follows at save).
- `save/database.sqlite3` while stopped (bypasses validation, migration, and quarantine — a "fixed" row that the loader would have quarantined becomes a corrupt world the loader trusts).
- `save/server.pid` (a claim, not config — editing it forges ownership of a process; stale claims clear via start/stop, never via editor).
- `.wipe-lock` / journal spill (transient lock files — deleting a live one un-serializes a wipe or checkpoint in flight).
- `secret/admin.token` content (rotation is delete-while-stopped plus start; editing the hex mid-run races the per-request re-read and locks admin verbs until restart).

Every legitimate change to these files goes through a documented verb: play (memory) → save (checkpoint) → stop/start (`reset` for wipes, delete-plus-start for token rotation). If a procedure in this doc does not name the file, the file is not part of the procedure.

## 4. EXAMPLE — back up and restore

Back up a live game (stop first so the backup includes the latest in-memory state — `stop` saves on the way down):

```bash
cd mygame
./atheriz.sh stop
cp -a save /tmp/game-backup-2026-09-28-save
./atheriz.sh start
ls -la /tmp/game-backup-2026-09-28-save/database.sqlite3
```

If TLS certs/keys live in `secret/`, copy that tree too (`cp -a secret ...`) with modes preserved — the admin token itself needs no backup (per-boot, §3.2).

Expected outcome: `stop` exits 0, the log shows the shutdown save completing, both copies land, and `start` reports readiness with the world intact. Verify with the webclient: `look` in a known room shows the same description and contents as before the stop.

Restore (reverse, with the server stopped the whole time):

```bash
cd mygame
./atheriz.sh stop
rm -f save/server.pid
cp -a /tmp/game-backup-2026-09-28-save/database.sqlite3 save/database.sqlite3
chmod 700 save secret
./atheriz.sh start
```

Expected outcome: the world as of the backup — characters, builds, door states, game time — with no pid-claim conflict. The `rm` of a stale `save/server.pid` matters: restoring a pid file from another boot (or another host) makes liveness probes lie about a process that is not this server.

> Warning: `reset` wipes `save/` and reseeds a fresh world (always prompts `[y/N]`, no force flag). It is not a restore, not a repair, and not undoable — anything not backed up per this section is gone. Full `reset` mechanics live in [Running the server](07-server-lifecycle-cli.md).

Copying a live `database.sqlite3` without stopping first is not a backup — SQLite may be mid-transaction and the copy can land torn. Stop-then-copy is the only supported procedure. For moves and inspection without downtime pressure, `WorldExport` (§5) dumps the world through the live server instead of the file.

## 4.2 Moving hosts (full migration recipe)

```bash
cd mygame
./atheriz.sh stop
cp -a save /tmp/migrate-save
sha256sum /tmp/migrate-save/database.sqlite3 > /tmp/migrate-save/SHA256
# ...copy /tmp/migrate-save to the new host (scp/rsync preserve modes with -p).
# Copy secret/ too only if TLS certs/keys live there — the token is per-boot.
```

On the new host, inside the game folder:

```bash
./atheriz.sh stop          # ensure nothing is claimed (fresh folder: reports none)
rm -f save/server.pid
cp -a /tmp/migrate-save/database.sqlite3 save/database.sqlite3
chmod 700 save secret
sha256sum -c /tmp/migrate-save/SHA256
./atheriz.sh start
```

Expected: checksum passes before start (identical bytes — a mismatch means the transfer tore and must be redone, never started from); boot shows `DoStartup completed` with migration lines only if the engine is newer than the data; health ok; in-client `look`/`map`/door/alarm verification per §4.1. Then update DNS/proxy to the new host and keep the old copy until players confirm — the old `save/` is the rollback until the new host proves itself. Decommission (delete) the old copy only after the new host survives a full checkpoint interval with players aboard.

## 4.3 Backup retention (how many copies to keep)

Keep three generations: the last known-good (verified by a boot — §4.1), the pre-change copy before every `reset`/migration/upgrade, and the rolling periodic copy. Name each with date and reason (`game-backup-2026-09-28-pre-reset-save`), checksum each at copy time (§4.1), and store the checksums with the copies. Prune oldest-first, never newest-first, and never keep exactly one — a single backup is a single point of failure wearing a backup's name. Test restores on a staging copy quarterly: an untested backup is a rumor, and the staging boot (`DoStartup completed` + `look` in a known room showing backup-era descriptions) is the whole test. Retention lives outside the game folder (separate disk, separate host) — a backup beside the database it mirrors protects against operator error, not disk failure.

## 5. Autosave and shutdown saves

Checkpoints run on a scheduler (`Autosave`) only when the operator opts in: `AutosaveMinutes` defaults to `0` (disabled), and setting it to N enables a checkpoint every N minutes, each flushing dirty objects to the database in one transaction. Manual `save` (superuser command) and clean shutdown checkpoint regardless of the setting. There is no partial checkpoint: a checkpoint either commits the whole dirty set or rolls back quietly and retries next interval (rollback-quiet — a failed checkpoint logs and continues serving rather than crashing the game).

Clean shutdown always saves. `stop` (graceful `POST /_internal/shutdown`), Ctrl+C in `--foreground`, and service halt all funnel through `ServerLifecycle.DoShutdown`, which checkpoints before releasing the pid claim. An unclean kill (power loss, `kill -9`, OOM) skips the save — that is the crash-loses-since-last-save case from §1, not a defect.

`WorldExport` is the full-world dump path: it serializes the live world (same DTO shape as checkpoints) for moves between hosts and for inspection tooling. Use it when you need the world as data — migrating content into a fresh game folder, reviewing what a build session produced, diffing before/after a big event. It reads memory, not the database file, so it reflects the live world including not-yet-saved changes.

Seed timing in one paragraph: first boot (and `reset`) runs `InitialSetup`, which creates the superuser credentials, the starting areas, and the `limbo` room before the first checkpoint — so even a game stopped seconds after scaffolding has a valid database. Map seeding details live with the world guide in [World and maps](04-world-maps.md).

## 6. Game-dev guidance (from `persist.md`, condensed)

- Save-sensitive patterns: anything your code builds at runtime (spawned rooms, granted items, scheduled alarms, channel joins your script performs) exists only in memory until the next save. If your feature creates state players will mourn, trigger or request a checkpoint after creating it rather than assuming the autosave will land in time.
- What survives `reload` vs `restart` vs `reset`: `reload` hot-loads game code and rewires live objects — world state (positions, contents, time) is untouched. `restart` stops and starts the process with a shutdown save in between — state persists, connections drop, settings re-read. `reset` wipes `save/` and reseeds — nothing persists except game code. Pick the weakest tool that does the job.
- Set state through properties so saves pick changes up. Direct field writes that bypass `ISettable` dirty flags are invisible to the checkpoint writer — the object looks changed in memory and reverts on next load. If your change survives `reload` but not `restart`, a missed dirty flag is the first suspect.
- Never cache a database handle, row, or DTO across a save and write through it later. Handles belong to one load; the checkpoint owns the write path. Your code talks to live objects; the engine talks to SQLite.

## 2.1 Table-by-table: what each row holds

`Objects` — one row per entity with `Id` + `Data` + `Type` + `Version`. `Data` is the serialized `GameObjectDto`: identity, names, descriptions, tags, containment links, bound scripts, dirty-flagged properties, and lock definitions. `Type` selects the runtime class in the subtype factory; `Version` selects the migration path when game code outruns the data.

`MapData` — rendered-map backing rows: per-node tiles, legend bindings, and the baked views the map pane and `map` command read. Rebuilt from the area tree when the world loads; builders editing through mapedit write here through the live objects, never directly.

`Areas` — the area → grid → node tree: region records, floor/zone records, room records with coords. Links live in `Transitions`, gates in `Doors` — the area rows hold the rooms, not the wiring.

`Transitions` — `NodeLink` exits: origin node, direction, destination node. Bidirectional links are two rows (north one way, south the other), which is why linking one direction never implies the reverse.

`Doors` — door objects plus `DoorDto` gate state: which exit they guard, open/locked state, key bindings. A locked door reloaded stays locked because the gate state is data, not code.

`GameTime` — the single calendar row: tick counters, calendar position, registered alarms. Alarms re-register against the loaded clock at boot, so schedules survive restarts intact.

`Checkpoints` — one journal row (`CheckpointRow`: id 0, `State` dirty/clean, `SavedAtUnix`): the writer marks it dirty before the tables commit and clean after. A dirty row at boot means the previous checkpoint died mid-way — the tables may be torn, so the operator treats the save before it as the good one (§5 crash case).

## 3.1 The layout on disk (annotated listing)

```bash
cd mygame
ls -la save secret
```

Expected on a running game:

```text
save/
  database.sqlite3   # the world — back THIS up
  server.log         # diagnose here first
  server.pid         # live pid claim (0600) — never restore elsewhere
secret/
  admin.token        # X-Admin-Token secret (0600) — copy for continuity
```

Modes matter: `save/` and `secret/` are `0700`, pid and token `0600`. A listing that shows group/world-readable secrets means the scaffold umask was overridden — fix with `chmod 700 save secret` and `chmod 600 secret/admin.token save/server.pid` (the token regenerates if deleted while stopped; the pid claim releases on shutdown). The only transient resident you may see is `.wipe-lock` during `reset` — it should never survive a clean stop, and a leftover after a crash is diagnostic (the wipe did not finish) rather than state to preserve. The checkpoint journal is a database row, not a file, so it never appears in this listing.

## 4.1 Restore verification transcript

After the §4 restore (stop → copy → perms → start), confirm each layer before inviting players back:

```bash
cd mygame
tail -3 save/server.log
curl --max-time 5 http://localhost:9999/health
```

Expected: `DoStartup completed` in the log tail (the restored database loaded cleanly — migration lines above it name any upgrades applied); `{"status":"ok",...}` from health. Then in-client: `look` in a known room (description + contents match the backup era), `map` toggles cleanly (map subsystem intact), a locked door from before is still locked (door state survived), and the calendar/alarm bell rings on schedule (game-time row intact). Any mismatch means the wrong backup file landed — stop, re-copy, compare checksums (`sha256sum save/database.sqlite3` against the backup copy), start again.

Checksum the backup at copy time and store the hash next to it. A restore that loads but behaves oddly with a mismatched hash is a torn copy (live-copy without stopping, §4 warning); a restore that refuses to load with a matching hash is a version story (database newer than the engine reading it — upgrade the engine, never downgrade the data).

## 5.1 Tuning the checkpoint interval

Set `AutosaveMinutes` to N for a checkpoint every N minutes (`0`, the default, disables the scheduler — then durability is manual `save` plus shutdown saves). The interval trades crash-loss against I/O load:

| Style | Interval feel | Fits |
|---|---|---|
| Frequent | small loss window, steady write load | build events, heavily populated nights |
| Default | balanced | ordinary play |
| Relaxed | larger loss window, light I/O | quiet dev servers, single-player testing |

There is no universally right value — pick by how much play the community will mourn. Before a scheduled build session or event, save (or clean-stop and start) so the session begins from a fresh commit; after it, save again so the work is durable before anyone logs off. Operators announcing "saving now" before a `restart` are practicing exactly this: the shutdown save covers it, but a named checkpoint beforehand means the restart itself carries no durability risk.

Crash anatomy, concrete: power dies halfway between checkpoints. On next boot the `Checkpoints` row still reads dirty (intent without commit); the loader takes the previous committed checkpoint; everything after it (minutes of play, at most one interval's worth) is gone. The log says so plainly — read `save/server.log` before player reports arrive, so the announcement ("we lost the last N minutes") precedes the discovery.

## 6.1 Reload, restart, reset: the decision table

| Goal | Command | State | Connections | Settings |
|---|---|---|---|---|
| New game code live | rebuild + `reload` | kept | kept | not re-read |
| New settings/ports live | `restart` | saved, reloaded | dropped | re-read |
| Fresh world, same code | `reset` (`[y/N]`, no force) | wiped + reseeded | dropped | kept |
| Durable now | `save` (superuser command) | kept | kept | kept |

When two apply, pick the strongest: settings plus code means `restart` (reload would leave the settings unread); a ruined world plus new code means rebuild + `reset`. `reload` is never a substitute for `restart` (settings) or `reset` (world), and `restart` is never a substitute for `reset` (wipes nothing). The tutorial loop in [Custom code and hot reload](11-custom-code-plugins.md) practices exactly this ladder: edit → rebuild → `reload`, escalating only when the symptom demands it.

## 6.2 Scenarios: which knob your feature needs

Spawning a quest item at midnight via alarm: the alarm registration persists (`GameTime` row), the spawned item persists from the next save after it appears. If the server crashes between spawn and save, the alarm already fired (it will not refire) but the item never landed — schedule spawns shortly after a checkpoint, or checkpoint after big spawns, so "the bell rang but nothing is here" never happens.

Granting a player a new exit from their room: the `Transitions` rows persist at next save, same as any link edit. Verify with `restart` on a staging copy before doing it live — a direction typo persists as faithfully as a correct one, and there is no undo besides another edit plus another save.

Script-bound state (torch burn-down counters, follow-script cursors): persists only what the script pushes through settable properties. Counters kept in private fields tick in memory and reset on load — which is correct for "burns for this session" and wrong for "burns across reboots." Decide per field, push the durable ones through properties (§6 bullet three), and document the choice in a code comment so the next dev does not "fix" it.

Channel history your event bot posts: bounded by the per-channel history limit — the last N entries persist, older scrollback does not. Announcements players must keep (event schedules, rule changes) belong in room descriptions or persistent bulletins, not channel backlog.

## 5.2 WorldExport in practice

```bash
cd mygame
# via the live server: dump, inspect, move content without touching the DB file
./atheriz.sh stop
cp -a save /tmp/pre-event-save
./atheriz.sh start
```

The copy above is the poor operator's export (stop-copy-start, §4). `WorldExport` proper runs through the live world instead: same DTO shape as checkpoints, read from memory (so it includes not-yet-saved changes), written wherever the operator points it. Reach for it when staging needs the current live state without stopping the game (inspect what the event built, diff against the last checkpoint, seed a staging folder with tonight's world). It never replaces stop-copy-start for backups — the file copy is the restore path, the export is the data path — and it never writes to the database, so exporting is always safe mid-game.

## 6.3 Staging copies (practice without risk)

Every risky operation in this doc — `reset`, migrations, engine upgrades, experimental game code — deserves a rehearsal on a staging copy first: duplicate the game folder (or `save/` + `secret/` into a scratch game folder), run the operation, verify per §4.1, then do it for real. Staging catches the typo'd direction, the forgotten cert pair, the migration that quarantines half the world to `limbo` — all fixable in scratch, all embarrassing live. The scratch copy's pid claim and ports must differ from the live game (overlapping ports is how staging kills production — the `--port` overlay and a separate folder keep them apart). Delete scratch copies when done playing; a forgotten staging server holding an old world beside the live one is how "which copy is real?" incidents start.

## 6.4 Where to go next

Reload-side mechanics (assembly unload, live-object patching, the `build.sh --reload` loop) continue in [Custom code and hot reload](11-custom-code-plugins.md). The CLI verbs that bracket every procedure here — `stop` (saves on the way down), `reset` (wipes and reseeds), `start` (loads once at boot) — are specified in [Running the server](07-server-lifecycle-cli.md). Ports, binds, and the admin-token gate behind those verbs live in [Hosting and configuration](08-hosting-configuration.md); the login-to-puppet flow players see after every restart lives in [Connecting: webclient and telnet](09-connecting.md).

## 6.4 One-paragraph recap for the hurried

Memory leads, checkpoints commit, crashes rewind to the last commit. Back up `save/database.sqlite3` (plus `secret/` for continuity) with stop-then-copy, verify restores per §4.1, keep three checksumed generations, rehearse risk on staging, and push durable game state through settable properties. Everything else in this doc is that paragraph, expanded.

## Under the hood: the save machinery

This section names the classes and files behind every behavior above. Mechanics first, file map at the end.

The store is EF Core over SQLite. `AtherizDbContext` exposes one `DbSet` per concern — `Objects`, `MapData`, `Areas`, `Transitions`, `Doors`, `GameTime`, `Checkpoints` — and `AtherizDbContextFactory` resolves the save path (`ATHERIZ_SAVE_PATH` wins over configured `SavePath`; a relative path outside a game folder throws `Cannot determine save path ...`, fixed by running from the game folder). Loading runs once at boot: game assemblies first (so rows convert to game types), then objects with containment, a torn-checkpoint check, map/node handlers, and the game clock. Each load logs and continues past corrupt rows rather than aborting the boot.

Rows are thin; payloads are JSON. `ObjectRow` carries `Id` + `Data` + `Type` + `Version`: `Id` is the stable identity, `Data` holds the serialized `GameObjectDto`, `Type` names the entity kind for the subtype factory, `Version` gates migration. The DTO set (`src/Atheriz.Core/Persistence/Dto/`, 9 types: `GameObjectDto`, node DTOs, `NodeAreaDto`, `NodeGridDto`, `LocationRef`, `LockDefDto`, `EntityKind`, `SaveOperation`, and friends) is the versioned contract — `GameObjectDtoConverter` runs the subtype factory that rebuilds the right runtime class per `Type`, applying migrations when `Version` lags. Serializer options (naming, null handling, converters) centralize in `JsonOptions` so checkpoint and export agree byte-for-byte.

Writes go through `CheckpointWriter` inside a single SQLite transaction — no file swap. The older swap-a-temp-file-into-place strategy is gone; the transaction is the atomicity. `DbWriteGate` serializes everything that touches the database file: checkpoints, shutdown saves, `reset`'s wipe-and-reopen, offline `create`. During `reset`, the gate parks savers on a 30-second hold while the wipe runs; any holder that cannot acquire in time fails loudly rather than writing into a half-deleted directory. `CheckpointJournal` records checkpoint intent and outcome so a crash mid-checkpoint is identifiable in `save/server.log` on next boot (stale journal = the last save may be partial = the previous checkpoint is the good one). `DbTransactionHelper` wraps the gated transaction body with rollback-quiet semantics: on failure it rolls back, logs, and lets the server keep running — persistence degrades to "try again next interval" instead of taking the game down.

Supporting cast, one line each:

- `WorldExport` — serializes the live world through the same DTOs for moves and inspection (§5).
- `IdGenerator` — the unified in-memory counter backing every new object, node, and channel id; load paths push it forward with `EnsureAtLeast` per row, so ids never collide after restart.
- `SaltProvider` — the single static salt for password hashing, persisted in `secret/salt.txt` (`0600`). One salt, process-wide. Rotation is not supported, and losing or changing it invalidates every stored password hash — back it up with the database.
- `IpBanStore` — runtime-only temp-IP bans and failed-login windows (in-memory `BoundedDictionary`s, cleared on reboot). Persistent bans are `IsBanned` flags on character/account rows (§2 table); operator notes in [Connecting: webclient and telnet](09-connecting.md).
- `CreationCooldownStore` — per-source creation timestamps backing the account-creation throttle; `create` rejections with wait-and-retry come from here.
- `BoundedDictionary` — the capped map behind channel history and throttle windows: oldest entries evict past the cap, so history and rate state cannot grow without bound.

### Checkpoint write path, step by step

```text
Autosave fires (or save / DoShutdown begins)
  → DbWriteGate acquired (serializes vs reset/create/shutdown)
  → dirty live objects collected (ISettable flags decide membership)
  → DTOs serialized (GameObjectDtoConverter + JsonOptions)
  → Checkpoints row marked dirty (CheckpointJournal)
  → single SQLite transaction commits (no file swap)
  → Checkpoints row marked clean with the save timestamp
  → gate released
```

Every arrow is a place a failure is contained: gate contention waits (or, past the hold, fails loudly rather than interleaving); serialization errors abort before the journal; transaction failure rolls back quietly and the server keeps playing (rollback-quiet — next interval retries). The journal row is what makes a crash mid-path legible: dirty-without-clean on next boot means "the previous checkpoint is the good one," stated in the log before any player notices.

### Load path, stage by stage

```text
boot → save path resolved (ATHERIZ_SAVE_PATH wins over settings.SavePath;
         a relative path outside a game folder throws
         "Cannot determine save path ..." — cd into the game folder,
         the one holding the game .csproj and GameSettings.cs)
  → game assemblies load first (rows convert to game types, not base kinds)
  → Objects with containment (corrupt rows skipped with a log line each)
  → torn-checkpoint check ("Torn checkpoint detected: previous save did not
    complete; world tables may be inconsistent." — boot continues, loudly)
  → map + node handlers, AtServerStart, game clock (when time is enabled)
  → IdGenerator pushed forward per loaded row (new ids never collide)
```

Order is load-bearing in one direction: game assemblies load before the world so rows convert to game types (world-first would convert every game row to its base kind with a loud log and only patch it up after), and the ticker exists before rows convert so loaded objects can register their ticks. Corrupt object rows are skipped with a per-row log line rather than aborting the boot.

### Identity, salt, and bounded state

`IdGenerator` is one in-memory counter for every new id (objects, nodes, channels). Reseed-from-high-water at load is what keeps a restarted server from reissuing an id the database already holds — id collision after restart always means the reseed did not see the row, i.e. a save-path or wipe problem, never normal operation.

`SaltProvider` holds the single static process-wide salt for password hashing, persisted in `secret/salt.txt` (`0600`, empty file treated as corrupt, never silently re-minted). Every stored hash depends on it; losing or changing it invalidates every password in the world at once. There is no rotation story — treat the salt file with the same care as the database itself (backed up with it, never hand-edited).

`BoundedDictionary` caps the structures that would otherwise grow forever: throttle windows (per-source timestamps, oldest evict) and similar rate state. Channel history has its own per-channel limit with the same effect: the caps are what make "history" and "rate state" safe to persist — unbounded growth would turn either into a disk-exhaustion vector over a long-lived game. When players ask why old channel scrollback disappears, the bound (not a bug) is the answer.

### Migration: old data, new code

`Version` on each `ObjectRow` plus the `GameObjectDtoConverter` subtype factory is the whole migration story. When game code adds, renames, or drops a persisted field, the converter maps the stored `Version` forward: known-old versions upgrade field-by-field, unknown versions quarantine the object to `limbo` with a log line rather than crashing the load. Game devs replacing engine types pair this with `[EntityReplacement]` + `IMigrateFrom` (worked example in [Custom code and hot reload](11-custom-code-plugins.md)) — the attribute names the substitute, the migration interface carries old state into the new shape, and the checkpoint afterwards writes the new `Version` so the migration runs once, not every boot.

File map: `src/Atheriz.Core/Persistence/AtherizDbContext.cs` + `AtherizDbContextFactory.cs` (store + path), `JsonTableLoader.cs` + `JsonOptions.cs` (boot load), `CheckpointWriter.cs` + `CheckpointJournal.cs` (save path), `DbTransactionHelper.cs` + `DbWriteGate.cs` (serialized gated transactions), `PersistedTypes.cs` + `IJsonEntity.cs` (row contracts), `WorldExport.cs` (dump path), `Converters/GameObjectDtoConverter.cs` (subtype factory), `Dto/` (9 versioned contracts), `Entities/` (7 row types).

---
Next: [Custom code and hot reload](11-custom-code-plugins.md)
