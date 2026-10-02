# Changelog

All notable changes to this project are documented here. Versioning is
`Major.Minor.Build.Revision` (see `Directory.Build.props`).
Webclient release notes live in `webclient/CHANGELOG.md`.

## [0.30.0.0] - 2026-10-02

- Staging the webclient into a game folder is now a server command
  (`deploy game --web-root <game>/web`) instead of the
  `webclient/deploy.py` script, which is deleted. The generated
  per-game build scripts and the out-of-sync warning already point at
  it, so redeploys keep working with no Python needed.
- `build.sh` no longer requires rsync: the three local staging copies
  are plain `cp -r`.

## [0.29.0.12] - 2026-10-02

- Bare `help` now groups each command category inside its own
  box-drawing border, laid out in two independent columns with command
  names comma-separated across as many lines as needed. Per-command
  descriptions moved to `help <command>`; screenreader sessions keep
  the plain borderless list. The listing closes with a blank line and
  a `help <command> for more information` pointer.
- Boxes on the same side are separated by a single empty line, and
  the columns flow independently — no padding rows, so no stray
  vertical lines appear beside a taller box.
- `reload` lists under Admin and `mapedit` under Building in the help
  overview instead of General.
- Logging in now re-asserts the default `>` prompt after the puppet
  attaches, so menu prompts no longer linger into the game session.
- `new`/`create` now rebuild the webclient first when its sources are
  newer than the staged copy, instead of printing a warning and
  scaffolding the game with stale assets. Other commands keep the
  warning-only behavior.
- `reload` now rebuilds the game plugin projects in the current folder
  first, so new commands are picked up without a separate build step.
  A failed build aborts the reload instead of reloading stale code.

## [0.29.0.11] - 2026-10-02

- The server-channel log is public by default now; operators opt out
  with `ServerLogPublic: false` instead of opting in.

## [0.29.0.10] - 2026-10-02

- The landing-page server-log stream now sends its headers the instant
  the stream opens, instead of holding them until the first log line.
  A caught-up viewer previously sat with zero bytes for up to 20
  seconds, which some browsers failed instead of riding out — showing
  the live-stream-unavailable note on a healthy server.
- Server-log stream connections are now logged (open, resume point,
  disconnect, errors) so a stuck viewer is diagnosable from the server
  log.

## [0.29.0.9] - 2026-10-02

- The admin item-type filter's catch-all is now labeled `other` instead
  of `object`, since everything is an object and the name hid what the
  filter lists (plain objects with no player/item/npc/script kind).

## [0.29.0.8] - 2026-10-02

- Switching the item type in the landing-page admin search now collapses
  the open item editor, so the previous item's properties no longer linger
  under the new list.

## [0.29.0.7] - 2026-10-02

- Reloading the landing page no longer shows every server-log line
  twice. The snapshot now tells the live stream where it ended, so the
  stream only sends newer lines.

## [0.29.0.6] - 2026-10-01

- Location and home are editable in the landing-page object editor. A
  location change parses the entered string (coord, `#id`, or null) and
  moves the object through the normal move path; home only stores the
  respawn marker. Rooms refuse location, and bad input explains the
  accepted shapes.

## [0.29.0.5] - 2026-10-01

- Fixed the Play button auto-login ending on the welcome screen: the
  client's ready signal fired after the token had already bound the
  account, and the welcome screen (advertising `connect ... to login`)
  was printed over the character-selection prompt. Authenticated
  sessions now skip it, so a landing-page login lands on character
  selection like the in-band login does.

## [0.29.0.4] - 2026-10-01

- Boolean properties in the landing-page admin editor render as a
  true/false dropdown instead of a text box.
- The list's Open button becomes Close while that object's editor is
  open, and Close (in the row or below the property table) collapses it.
- Choosing a type in the admin search now lists objects of that type
  immediately, and a hint notes that an empty search lists them all.

## [0.29.0.3] - 2026-10-01

- An account's own stored privilege level no longer feeds anything: the
  HTTP API acts with the best privilege among the account's characters
  (Player when there are none). Setting `PrivilegeLevel` on an account
  over HTTP is refused with a pointer to grant on a character instead,
  and the row shows read-only. Characterless legacy accounts floor at
  Player instead of Guest.

## [0.29.0.2] - 2026-10-01

- The admin object list and detail header show an account's acting
  privilege (best of account plus characters) instead of the account
  row's own stored level, which is usually Player or, on old accounts,
  Guest. The property table still shows and edits the stored value, with
  a read-only `acts_as` note beside it while the two differ.

## [0.29.0.1] - 2026-10-01

- Landing-page login tokens no longer expire: a token stays valid until
  the user logs out (or the server restarts and the in-memory store goes
  with it).

## [0.29.0.0] - 2026-10-01

- The landing page now has account login. Logging in stores a bearer
  token in the tab, and the Play button hands it to the webclient so the
  game session starts already authenticated (no password retyped). A
  token passed as `?token=` over the game socket takes the same path as
  the in-band login, including the character-selection step.
- Builders get a Server admin section on the landing page: find objects
  by name, id, or tag, filter by kind (players, rooms, items, NPCs,
  accounts, channels), inspect every visible property, and edit values
  inline. Builders can only touch objects below their own privilege
  level; admins are unrestricted. All of this is enforced server-side.
- HTTP login shares the game's brute-force protection: hosts backed off
  by failed attempts are answered 429 until the cooldown expires, and
  every failure still feeds the shared counter.
- New HTTP surface under `/api`: login, logout, who-am-I, object
  list/detail/update, and extra-property delete. Tokens are random and
  live until logout.

## [0.28.0.2] - 2026-10-01

- The landing-page server log carries the UTC moment with every line
  and the viewer renders it in the viewer's own timezone. In-game
  channel display is unchanged (still server time).

## [0.28.0.1] - 2026-10-01

- The webclient `game` deploy target now syncs the engine landing page
  (`web/templates/index.html`) into the game web root alongside the JS
  bundles. Previously it staged only `web/static`, so game folders kept a
  stale landing page forever (their override wins over the shipped copy
  at runtime) and new landing features never appeared after redeploy.
- Fixed the deploy log line naming the server-log viewer location
  (it lives under `assets/`, not the static root).
- The landing page Play button opens the webclient in a new tab instead
  of navigating away from the landing page.
- The landing page loads the server-log viewer as an ES module
  (`type="module"`). The bundle imports the xterm chunks, so the previous
  classic script tag threw at load and the log never rendered.

## [0.28.0.0] - 2026-10-01

- The landing page can show the server channel's recent history (up to
  the last 100 messages) in a read-only live terminal: a `/server-log`
  snapshot plus a `/server-log/stream` event stream carry the same
  formatted bytes players see in-game. Both stay off unless the new
  `ServerLogPublic` setting is enabled, since channel traffic is public
  once published on the web.
- Channel history entries carry a monotonic sequence number (persisted,
  old saves load with 0) so live reconnects replay exactly the gap.
  Channels are now created with the `ChannelHistoryLimit` setting as
  their cap instead of a hardcoded 50.
- New `ServerLogPublic` setting (default false).
- System messages sent to the server channel (shutdown/reload notices,
  login announces, autosave reports) now use the Channel-typed send, so
  they actually reach subscribers and history instead of vanishing into
  the channel object's own log. In-game subscribers will see login and
  shutdown notices for the first time.

## [0.27.0.0] - 2026-09-30

- The map editor protocol accepts cells at negative coordinates; a new
  parsing test pins that behavior (no wire changes).
- Version bump to match the webclient 1.8.0 map-editor release below;
  no other engine changes.

## [0.26.0.0] - 2026-09-29

- The `mapedit` launch payload now includes each room's display name
  (empty when unset), description, and exits, so the map editor can
  show them.
- Room name and description edits from the map editor are now applied to
  the live room on the server, with a confirmation sent back for each save.
- Saving a room that no longer exists reports the miss and keeps the
  editing session alive instead of dropping it.
- Rooms now have a settable display name (empty by default). Everyone
  viewing a room sees its name; builders additionally see its coordinates.
- The map editor can replace a room's whole exit list in one save:
  renames, target rewrites, and deletions validate the same way, report
  per-save confirmations, and keep the editing session alive on misses.
- New `map_create_rooms` map-editor command turns a batch of empty squares
  (up to 256) into rooms linked to orthogonal neighbors both ways,
  including reciprocal exits onto pre-existing rooms. Batches validate
  before changing anything (a bad target or unknown square refuses the
  whole batch); replays re-apply without duplicating.
- New `map_delete_rooms` map-editor command removes a batch of rooms
  (up to 256): occupants move to a per-room fallback, links into deleted
  rooms are stripped automatically, and occupants keep their remaining
  exits. Batches are refused (changing nothing) for occupied rooms with
  no fallback, door endpoints, or fallbacks being deleted; missing rooms
  are skipped.
- `map_create_rooms` stamps the room placeholder onto empty map squares
  for the new rooms (like `dig` does), so created rooms appear on the
  in-game map and the character symbol lands on them. Squares with drawn
  art are never overwritten.
- `stop` accepts `-f`/`--force`: with a pid file it kills the named
  process without verification, and with no pid file it kills every
  process holding the port listener. All refusals now name the flag.

## [0.25.0.0] - 2026-09-28

- Hook names now come from one declared list: the `HookName` enum is generated from the `HookNames` constants, so the two can no longer drift apart.
- Installing a hook handler with the wrong argument count is checked in one shared place; mismatched handlers are still refused at install, never mid-game.
- Installing an unmarked handler now logs a one-time warning naming the site; dispatch itself stays silent.
- Installing the same handler twice on one site keeps a single copy in its original position; handlers fire in install order.
- After-hooks prefer the args-plus-result shape and fall back to args-only, decided from the cached signature instead of try/catch probing.
- New server lifecycle hooks (`at_server_start`, `at_server_stop`, `at_server_reload`) fire on every object with zero args or one sender arg.
- Overriding an `At*` method without calling base drops that site's script hooks; `AtMsgReceive`/`AtMsgSend` are not overridable — customize them with hooks.
- Fixed `at_pre_hear`/`at_pre_emit_sound` install checks rejecting valid 5-argument handlers.

## [0.24.0.0] - 2026-09-28

- Hook arg-count checks use plain words now (`DispatchArities` → `DispatchArgCounts`, `AcceptsArity` → `AcceptsHookArgs`, `CoordParseFailure.Arity` → `WrongPartCount`); behavior is unchanged.
- The `test` server command still accepts zero or more pass-through args without naming the parsing library's setting; parsing behavior is unchanged.

## [0.23.0.0] - 2026-09-28

- New `docs/` user guides: thirteen chapters covering install, game creation, objects, maps, hooks, commands, server lifecycle, hosting, connecting, saves, plugins, webclient, and engine internals, all verified against the engine sources.
- Door directions now match regardless of letter case, so a locked door blocks movement and answers open/remove commands however its direction was capitalized.
- Container search only accepts plain `#id` references; signed or spaced variants no longer resolve anything.
- The server process check no longer mistakes an unrelated process for a live server just because its filename starts with the server name.
- Per-game `build.sh`/`build.cmd` now rebuild the engine server (Release) when its dll is missing or older than the engine sources, before building the game plugin. New game folders get the same step, so a stale server can no longer silently boot the game engine-only.

## [0.22.0.6] - 2026-09-27

Webclient-only fix: reopening the map editor ignored previously saved
settings because the two sides disagreed on the handoff format. They agree
now, so the last save restores.

## [0.22.0.5] - 2026-09-27

Webclient-only fix: blocked editor popups no longer masquerade as throttling.

- `launchDraw` returns `opened`/`throttled`/`blocked` instead of a bare
  boolean, and the terminal prints a popup-blocked message (click the
  fallback link or allow popups, then `:draw`) instead of the "wait a
  moment" lecture on every retry. The fallback link scrolls into view so it
  can be found. The 1-per-second throttle itself is unchanged.

## [0.22.0.4] - 2026-09-27

Map-editor settings now round-trip: reopening the editor restores the last save.

- `mapedit` embeds the account's saved editor settings as `editorSettings`
  in the `launch_draw` grant payload (re-validated before embedding; corrupt
  or absent settings simply open a default editor). The draw tab restores
  colors and slots, gradient stops, custom chars and selected char, tool
  modes, font family and size, and the room highlight color/visibility.
- New widget restore points: `ColorPicker.setHistory`,
  `GradientPicker.setStops`, `Toolbar.syncFromState` (adds a font option
  when the saved family is not listed). `isValidColor` moved to
  `utils/colors` as the shared guard.

## [0.22.0.3] - 2026-09-27

Map-editor save-to-server now persists editor settings on the account.

- `map_edit` accepts an optional fourth arg with the editor chrome (fg/bg
  colors plus all color slots, gradient stops, custom chars, current font and
  size, tool settings, room highlight color/visibility). It is sent only on
  explicit save, never on autosync. Bad payloads reject loudly with
  `map_edit_reject` before the seq is consumed, like bad cells/legend entries.
- Validated settings are stored as normalized JSON on the granting session's
  account (`Account.MapEditorSettingsJson`, read-only via `set`/`examine`),
  persisted under the `mapEditorSettings` extra key at checkpoints. Retries
  ack without re-saving; grants without an account ack without storing.

## [0.22.0.2] - 2026-09-27

- New `GameObject.RemovePersisted(key)`: the typed-API spelling for extra
  removal. Returns true when a value was present and marks `IsModified`
  only then (same implementation as `TryRemoveExtraJson`, which stays
  for untyped callers).

## [0.22.0.1] - 2026-09-27

Follow-ups to the 0.22.0.0 persistence pass.

- Subtype loads fall back from assembly-qualified markers: an
  `__object_type`/`ObjectType` value of the form `key, Assembly, ...` retries
  the registry with the assembly suffix stripped, so old rows still restore
  their registered subtype (with the stored `<marker>_version` handed to
  `OnLoadMigrate`). Truly unknown keys still load as the base kind with a
  loud log.
- `BuildDto` snapshots the `__object_type`/`__script_type` markers (and
  version siblings) before `SaveExtra` and restores them afterwards with a
  loud log if a hook overwrote them, so a game hook can no longer silently
  rekey a row.
- `Account.SaveExtra` no longer takes the object lock; the hook runs under
  the object lock already, so it reads its fields directly.
- `PersistedTypes` reports a key registered in both the object and node
  registries loudly, both at registration time and in the merged
  `RegisteredTypes` view (the merge keeps the node entry).
- Typed extra getters carry `[NotNullWhen(true)]` on their `out` values, and
  the cookbook comment shows the detached-copy pitfall with a
  `ModifyPersisted` example.
- `CheckpointWriter` comment now says the inner saves re-enter through the
  re-entrant gate instead of skipping it.

## [0.22.0.0] - 2026-09-27

Persistence usability and checkpoint unification.

- Typed extra API on `GameObject`: `GetPersisted<T>` (with and without
  default), `TryGetPersisted<T>`, `SetPersisted<T>`, `HasPersisted`, and
  `ModifyPersisted<T>` (snapshot under read, update outside the locks, write
  back under write). Getters take the read lock and never dirty; only the
  setters mark `IsModified`. The `JsonElement` path is unchanged.
- Save/load hooks: subtypes override `SaveExtra`/`LoadExtra` (plus
  `WriteExtra<T>`/`ReadExtra<T>` helpers) instead of the whole DTO path.
  `ToDto`/`GetSaveOperation` stay virtual as the escape hatch. `Account`
  now persists through the hooks; `Channel` keeps its snapshot-first save
  (history under its own lock must never nest inside the object lock, the
  inverse of message-delivery order) as the reference implementation.
- One registration call: `PersistedTypes.Register<T>` (plus `RegisterNode<T>`
  for node factories) covers the object and node registries, with an explicit
  key (default: type full name) and a `persist: false` opt-out for code-only
  types. The full registered set is logged at boot. Duplicate keys for
  different types log loudly.
- Per-type versions: the persisted key now rides with a
  `<marker>_version` value (objects, scripts, and `NodeDto.ObjectTypeVersion`)
  handed to the new `OnLoadMigrate(savedVersion)` hook. No old-save
  fallbacks: subtype markers require an exact registry match (the
  assembly-suffix/short-name matching is gone), unregistered node subtypes
  save as base nodes with a loud log instead of an assembly-qualified name,
  pre-triple channel history entries are no longer read, and node coords come
  from `Location` only.
- Unified checkpoint: `CheckpointWriter` runs objects, map, node, and time in
  one context with one transaction for `save`, autosave, and shutdown/reload
  saves (shutdown now includes time, matching autosave). Per-section logging
  is kept, the gate hold time is logged, and a poison object row is skipped
  and reported (left dirty for the next checkpoint) instead of aborting the
  whole checkpoint.
- `save` is no longer hidden, and `save export <file>` streams the object
  rows to a JSON file from read-only snapshots. Import is offline-only via
  `WorldExport.ImportFromFile` (validated DTOs, no live mutation); there is
  deliberately no live import command.

## [0.21.0.2] - 2026-09-26

- `LoadGameAssembliesAtBoot` loads each candidate into a fresh loader and
  displaces the live loader only on success, so one bad game assembly at
  boot no longer discards the previous generation (the hot-reload path
  already followed this rule; the boot loop did not).
- `RestoreClosedDoor` re-closes through `ForceClose` instead of a raw flag
  write under an explicit hold, so the forced revert fires the
  doors-modified mark and checkpoints instead of silently dropping the
  state change.

## [0.21.0.1] - 2026-09-26

Follow-ups to the 0.21.0.0 correctness pass, found by re-verifying every
fix. Each item below is pinned by a regression test; each pin was
sensitivity-checked (neutered to confirm it fails, then restored).

- The settings validator rejects `NaN` `TickMinutes` (`<= 0` is false for
  `NaN`, so the old spelling accepted it), and `GameTime.GetTimespan`
  screens `NaN`/`Infinity` before the exact-decimal parse, which has no
  representation for either and threw `FormatException` on every call.
- The `map_edit` sequence number rejects `long` values outside `Int32`
  instead of wrapping, so an ack can no longer echo — or look up the edit
  chain for — a truncated sequence number.
- Corrected two overstated comments on `Door` construction/restore: the
  per-setter marks still fire when a handler is current (accepted,
  idempotent noise — suppression there only groups the writes).
- The `AtherizDbContext` settings constructor honors `ATHERIZ_SAVE_PATH`,
  and map/node saves write to their constructed settings database (with the
  same override precedence), so loads and saves use one file. `NodeHandler`
  keeps its constructed settings for the same reason `MapHandler` does.
- `Node.RegisterPersistedSubtype` prunes the superseded `Type` key on
  re-register, matching the game-object registry.
- `EvictStaleCommands` with no prior assembly reports it instead of
  returning silently, so first boot is distinguishable from a clean eviction.
- The transitions migration log counts constraint-violating rows with the
  undecodable ones instead of mislabeling them.
- `reset` on Windows sweeps the save directory around the held wipe lock
  instead of deleting through the open handle (sharing violation).
- Map coordinate parsing rejects `long` values outside `Int32` in room
  cells, draw cells, move lists, and legend entries, and the shared
  conversion throws instead of wrapping on overflow.
- The `-f`/`--flag` alias pair keeps `type`, `default`, `choices`,
  `required`, `nargs`, and `action` instead of dropping the long form when
  any of them is set.
- Deleting an object whose `AtDelete` throws is a veto (fail-closed) on the
  entry object too, and the non-recursive re-delete is guarded so one bad
  hook cannot abort the sibling loop.
- Deleting a player character while it has no live session still frees its
  account character slot via an owner scan.
- `AddObjectUnique` checks the uniqueness predicate against a snapshot
  outside the registry lock and inserts only when the membership version is
  unchanged (retrying on concurrent add/remove), so predicates reading
  object state no longer nest the registry lock inside object locks.
- Installed exit commands run the door open/move/re-close sequence, so a
  closed-but-unlocked door is traversed (with announces) instead of refused.
- The in-game `shutdown` command refuses a blank `admin.token` locally
  instead of posting an empty bearer.

## [0.21.0.0] - 2026-09-26

Correctness fixes across auth, settings, persistence, dispatch, movement,
plugins, host, and locking. Every fix below is pinned by a regression test;
each pin was sensitivity-checked (neutered to confirm it fails, then restored).

### Auth and tokens

- A zero-byte `admin.token` no longer authenticates admin requests:
  `ValidateToken` rejects blank tokens before comparing, and compares
  `SHA256` hashes so the comparison is constant-time regardless of length.
- `CheckAdmin` treats a blank token file as missing (fail-closed) instead of
  comparing empty against empty.

### Settings and game clock

- The settings validator rejects `DaysPerWeek`, `LunarCycleDays`, and
  `TickMinutes` values at or below zero.
- `GameTime` clamps those three divisors (and the `AppendUnit` choke point)
  to safe values, so a bad config can no longer throw `DivideByZeroException`
  every tick or freeze the clock at 00:00:00.

### Persistence and paths

- `MapHandler.Load()` boots from its own settings instead of the ambient
  factory path, so map and nodes load from one database.
- The transitions migration uses plain `INSERT` and folds constraint
  failures into the `dropped` count, so the log reports real loss instead of
  "migrated 1/1" over an empty table.
- Startup, checkpoint, shutdown, and save paths all resolve the database
  through `ResolveSavePath`, ending the split-brain under `ATHERIZ_SAVE_PATH`.
- `new --overwrite` no longer deletes its own `.wipe-lock` mid-wipe.

### Dispatch and commands

- `term_size`/`map_size` reject `long` values outside `Int32` before the
  unchecked narrowing cast, closing the wrap-to-1 lower-bound bypass.
- `DrainInput` logs handler failures through the robust logger and resets
  `_inputRunning` in a `finally`, so one broken logger can no longer wedge
  a connection's input pipeline permanently.
- `SplitHeadTail(text, 0)` returns the whole text as the tail instead of
  throwing on `ends[-1]`.
- The `AddArgument` short/long alias pair is detected regardless of `type`,
  so a typed `-n`/`--num` option keeps both forms.

### Movement, doors, and deletion

- Installed exit commands refuse movement through closed or locked doors and
  pass the exit name to `MoveTo`, so departures announce the direction used.
- Moving an object into a room from `NullLocation` installs exit commands.
- Deleting a PC removes it from its account's character list, freeing the slot.
- Non-recursive container delete probes the child's `AtDelete` before
  detaching, so a throwing hook vetoes instead of stranding the child at
  nowhere.

### Plugins

- `RegisterSubtype` prunes superseded `Type` keys on re-register, so a hot
  reload no longer roots the previous plugin generation forever.
- A failed plugin load keeps the old loader until a load succeeds, so
  stale-command eviction still has an `oldAssembly` to compare against.

### Host and shutdown

- The CLI no longer lets the engine's `appsettings.json` override the game
  folder's config, so `stop`/`reload` target the right ports and tokens.
- The in-game `shutdown` command selects the scheme from `SslCertFile`,
  tolerates loopback certificates, retries with a flipped scheme, and no
  longer fires `at_server_stop` twice.

### Sessions and evaluation

- Sending `connect` twice on one connection is answered with "A character
  selection is already in progress." Previously the two overlapping login
  prompts answered each other with blank input, looping "Invalid choice."
  messages with no player input until the session was torn down.
- `$eval` nesting is capped (depth 32) in both the literal evaluator and the
  arithmetic parser, bounding per-line CPU and stack use.

### Locking

- `IsStillSaveable` never holds the registry lock and an object lock
  together (check under `AllLock`, read flags under the object lock,
  re-check membership).
- `RemoveNode` resolves and ungrids under the handler hold but evicts from
  the registry after release, matching `ReplaceArea`/`Clear`.
- Door marks never fire under the door write hold: `MarkAfterWrite` returns
  early while the recursive write lock is held, and the outer compound path
  marks once after release, closing the AB-BA with `RemapDoors`.

## [0.20.0.0] - 2026-09-25

### Host, CLI, and tokens

- `DoStartup` creates the async ticker before loading saved objects:
  loading first left every loaded tickable unregistered on fresh boots,
  so no object received ticks until reload.

## [0.19.0.0] - 2026-09-25

Thread-safety fixes. Every fix below is pinned
by a regression test in `tests/Atheriz.Core.Tests/Features/Concurrency/`
(plus updates to stale tests that asserted the
old shapes). Investigated items that showed no defect need no code change;
inconclusive probes without an identified code defect remain open.

### Containment and movement

- `GameObject.AddObject`/`RemoveObject` take container and child locks in
  the shared `CompareLockOrder` sequence instead of container-then-child,
  closing the ABBA deadlock with concurrent `MoveTo`.
- `MoveTo` re-runs the self/cycle guard under the held locks before
  mutating, so a graft landing between the lock-free guard and the acquire
  can no longer commit a containment cycle.
- `MoveTo` refuses a deleted mover instead of re-homing it into room
  contents as a ghost member.
- `MoveTo` reports whether membership actually churned via a new overload,
  and the bulk `drop`/`get`/`give` loops skip hooks and announces for
  no-op moves instead of double-firing them.
- `Node.RemoveLink` and `NodeGrid.RemoveNode` withdraw the exact
  `(from, dest)` transition instead of bulk-deleting every fan-in edge to
  the destination, so a concurrent fresh link survives.
- `NodeArea.AddGrid` detaches the grid from its prior owner atomically
  under both area locks (name order), ending permanent dual-membership.
- Cross-map moves (`MapHandler.MoveListener`/`MoveMapable`/
  `MoveListenerAndMapable`) skip when the object's live location no longer
  matches the move's destination, so a superseded move cannot dual-home map
  presence.
- `FollowScript` pairs pending moves by destination instead of a blind
  LIFO stack, so a veto/cancel pops only its own move's entry.
- Door dirty marks are generation-guarded: a single-field write landing
  inside a no-change `SetEndpoints` window still marks instead of losing a
  checkpoint entry.

### Puppet, session, and messaging

- `GameObject.Puppet` refuses a second puppet on a live session, refuses to
  re-push an already-live target, and `Unpuppet` pops only entries its
  caller owns (live target or originator), with a restore-pending marker
  across the pop-then-restore gap — ending cross-pop privilege loss.
- `SessionPuppetHelper.TryAttach` refuses when the session already has a
  puppet.
- `Session.Msg` drops sends after disconnect, and `Account`/`AccountId`
  publish as an atomic pair with a `GetAccountPair` snapshot reader.
- `GameObject.Msg` drops delivery into deleted objects, checked both
  before and under the log hold.
- `AtLook`/`ReturnAppearance` (base and node) render nothing for deleted
  targets instead of leaking appearance.
- `GameObject.Unsubscribe` re-checks membership atomically with the
  command removal, so a concurrent re-subscribe keeps its command.
- `AtPostPuppet` stops re-adding channel listeners once the puppet is
  deleted.
- `GameObject.AddSecondsPlayed` replaces the disconnect get-plus-set RMW
 .
- New `Account.ChangePassword` verifies and sets atomically under the
  write lock for future change-password flows.

### Registry, time, and map

- `GameTime.OnTick` collects and claims one-shot alarms in a single write
  hold, so overlapping ticks fire each alarm exactly once.
- `MapCommand` toggles through an atomic `ToggleMapEnabled` instead of a
  split read-modify-write.
- `NodeArea.Equals`/`GetHashCode` snapshot the link sets under read locks
  instead of enumerating them live.
- `Node.GetRandomLink` returns a copy taken under the read lock, so
  wanderers never observe a half-remapped edge.
- `Channel.CreatedBy` is lock-guarded, and group kick re-verifies
  leadership after the announce before removing.
- Group leave snapshots the listener set once for the transfer and the
  empty-check instead of reading it twice.
- `Channel.GetCommand` constructs the command outside the channel lock
  and installs it under lock with a re-check, removing the
  channel-to-object nesting against delete paths.
- `Node.AddObjects` refuses implants into a deleted node like the
  singular `AddObject` already did.
- `SaltProvider.GetSalt` probes the cache under the lock but does the
  RNG and all file I/O outside it with a re-check on store, so
  concurrent callers on fresh paths no longer serialize behind disk
  latency.

### Network and dispatch

- `PendingLimiter.TryReserve(Task, …)` refuses a duplicate reservation on
  an already-tracked task, so one release always balances.
- `ConnectionManager.Dispatch` copies caller-owned args/kwargs at entry
  and the websocket send snapshots them before serializing.
- `Command.WrapWithLagCheck` captures the gate once instead of re-reading
  it at invoke time.
- `GameArgumentParser` snapshots the def list for defaults/help/
  required-optionals reads while adds mutate under the cache lock.
- `CommandRegistry` first-touch reads publish through `Volatile.Read`
 .
- Telnet `_closing` and websocket `_closing` are volatile.
- Telnet drain join uses a waiter registry instead of a single orphanable
  TCS, arms before counting, and `Dispose` re-checks in-flight past the
  event; `ScheduleWrite` refuses past teardown and the off-loop close is
  tracked like a write.
- The websocket disposer waits out sends parked between `Task.Run` and
  `Track` before snapshotting.
- `TelnetSessionReader` fails fast on concurrent readers instead of
  interleaving pump state.
- `BaseConnection._disposed` publishes under the lock with a volatile
  fast-path read, and the drain drops post-dispose input.
- `InputFuncs.Text` snapshots closed/future/puppet in one session hold
  instead of two.
- The session scalar-fields doc now states plain atomic stores instead of
  claiming lock-guarded writes.
- `EmitSound` reports whether the sound was accepted (`false` on pool
  rejection) instead of only warning.
- Dead draw keys still reject `unknown_key` on the wire and now also tell
  the user to reopen the editor.

### Creation, cooldowns, gates, and persistence

- `ServerEvents.AtCharCreate` verifies the password before taking the
  creation lock and only re-hashes inside on rotation, instead of running
  PBKDF2 under the global lock.
- Character creation reserves the account cap atomically via
  `Account.TryAddCharacter`, in both the sync/async `new` paths and
  `ServerEvents`.
- `ConnectCommand` re-verifies the ban after the PBKDF2 window before
  binding.
- Creation-cooldown reservations carry owner tokens: a validation failure
  clears only its own reservation, never another drain's live hold on the
  same host.
- Concurrent same-flow `DbWriteGate.EnterAsync` takes nest behind the
  outstanding take instead of clobbering its claim.
- The ambient-transaction path in `DbTransactionHelper` takes the write
  gate instead of running with zero exclusion.
- `AtherizDbContext.Close` is instance-scoped; only the explicit
  `CloseDatabase`/`ReopenDatabase` maintenance pair still flips the
  process-global flag.
- `AsyncTicker.TickOnce` re-checks membership before firing, so a coro
  removed after submit never runs.
- `AtherizSettings`-heavy build commands observe one captured generation
  per command instead of mixing swapped generations mid-command.
- `PluginReloader.IsExcludedAssembly` snapshots under a lock with locked
  add/remove helpers and a bounded retry for legacy direct writes.
- `AtherizLogger.ApplySettings` publishes path and level in one hold,
  `_level` is volatile for the hot path, and a locked
  `SnapshotSettings` observes the pair atomically.
- `GameClock.MonotonicSeconds` reads the clock seam once.
- `GameObjectDtoSerializer` hooks are volatile and captured per call
 .
- `AsyncThreadPool` watchdog fuses busy and queue reads under one
  `_lock`→`_queueLock` hold.
- FuncParser static tables and the socials table gain locked
  add/remove helpers with snapshot reads on the dispatch paths.
- Ban/unban account scopes apply `IsBanned`/`BanReason` under one
  id-ordered multi-lock hold instead of per-object holds.
- Door placement re-sweeps evacuees and refuses to remove under a
  remaining occupant instead of stranding joiners.
- Exit moves re-validate the door is still open after `TryOpen` before
  moving.

### Host, CLI, and tokens

- `ServerLifecycle` commits startup success by generation under the world
  lock, so a mid-flight shutdown (or a newer startup) leaves readiness
  false.
- `PidFile.ReleaseIfOwner` renames aside atomically before verifying, so
  a successor claiming in the gap never loses its live file.
- `StopHandler` re-verifies pid file and process identity immediately
  before signalling.
- `RestartHandler` waits for the telnet port as well as the webserver
  port before starting the replacement.
- Offline `create` re-verifies the liveness backstop after loading and
  before writing.
- The daemon spawner's readiness poll requires the claim to name its own
  child pid, not any live claim.
- `AssetPathResolver` snapshots CWD once per call instead of reading it
  per row.
- `reset` and `new --overwrite` hold a `.wipe-lock` across their
  irreversible deletes (re-armed after the recursive delete); pid claims
  refuse while it is fresh, and clean completion deletes it.
- The transitions-table migration serializes starters on a save-dir lock
  file and creates idempotently.
- Empty `admin.token` files self-heal (bounded wait for a concurrent
  writer, then delete and regenerate) instead of throwing forever.
  The stale `EnsureToken_EmptyFile_Throws` test now pins regeneration.

### Follow-up fixes (same release)

- `Channel.Msg` drops delivery when a delete lands between the listener
  snapshot and the fan-out, and `BuildSaveOperation` re-dirties when the
  mutation generation moved between the history snapshot and the flag
  clear, so concurrent sends never ghost-deliver nor lose a checkpoint
  entry.
- `Channel.AddListener`/`RemoveListener`/`ReplaceListener` snapshot the peer
  id before taking the channel lock. `Id` is a locked property, so reading
  it under `_histLock` nested channel -> peer — the inverse of the fixed
  object -> channel order (`Delete` detaches under the peer lock) — and
  Subscribe-vs-Delete churn ABBA-deadlocked (caught via dump stacks: one
  worker in `RemoveListener` holding `_histLock` wanting the peer read
  lock, the other in detach holding the peer write lock wanting
  `_histLock`). The snapshot loses nothing (`_histLock` never protected
  the peer id). The churn test free-runs both sides instead of rendezvous
  per round: the barrier phase-locked the workers and masked the overlap.
  The save churn test asserts the converged history from a fresh snapshot
  instead of the last drain output (the concurrent saver's final clearing
  save may already have persisted everything and quiesced the flag,
  leaving the drain with nothing — correct behavior, not loss), and a new
  structural pin covers the generation re-dirty the window depends on.
- `Session.SecondsPlayed` accumulates under the session lock from a
  lock-snapshotted `ConnTime`, and login sends snapshot session then
  connection under that session's lock.
- `SetCommand` PC renames re-check the duplicate-name gate inside a
  dedicated rename lock, so two concurrent renames to one fresh name
  leave a single holder.
- `DoReload` resolves its ticker once and reuses it across every step
  instead of resolving per step.
- Wanderer spawns verify the picked node is still registered after the
  move and fall back to the spawner's room.
- Map-edit apply refusals reach the client as `moves_denied` (which the
  draw editor rolls back) instead of only a server log line.
- Connection-screen hints evaluate the dispatch gate against one
  captured settings pair instead of two live reads; the dispatcher
  exposes a single-generation gate overload plus a test settings seam.
- Ban account lookup, host lookup, and the kick loop snapshot
  session/connection atomically and skip (reported, offline stays silent)
  when the target re-homed mid-kick.
- `StopHandler` publishes its effective-settings cache under a lock with
  a volatile fast path, and `StopAsync` reads port/secret/savedir from
  one snapshot.
- `ServerHost` shutdown registration replaces the previous owner's hooks
  instead of stacking duplicate shutdown/pid/token handlers.
- `create_account` runs `AtCharCreate` on the pool with a 60 s watchdog
  instead of parking a Kestrel worker inline.
- `reset` holds the DB write gate across the close/wipe/reopen sandwich
  (bounded 30 s, fail-closed) so concurrent savers park instead of
  throwing "database is closed".
- `FindTokenFile` snapshots CWD once per call instead of per loop
  iteration; `new --overwrite` re-probes liveness immediately before the
  deletes.
- `GameObjectDtoConverter.FromDto` loads through a record copy instead
  of swapping and restoring the caller's `Extra`, so concurrent loads of
  one DTO instance never strip each other's subtype markers.
- Telnet `ScheduleWrite` sheds past 32 parked writes per connection
  (drops with limiter release) instead of parking unbounded tasks on
  wedged peers.
- `BaseConnection.RetryDrain` never arms past dispose.
- The `/` route serves the landing template (`web/templates/index.html`,
  game folder winning over the shipped copy, matching the original server)
  instead of a static `wwwroot/index.html`: a stale Draw build committed at
  that path was served at `/` in place of the landing page, so the stray
  file is deleted and a static root index is never consulted. Pinned by
  `StaticFile_Root_IgnoresStaleWwwrootIndex_ServesLanding` plus the
  `Wwwroot_HasNoRootIndexHtml` source guard against re-committing it; the no-files
  test now asserts the landing page (`Play`, no `Atheriz Draw`) instead of
  the template-less inline fallback.
- `IsExcludedAssembly` skips null snapshot slots from torn legacy
  raw-write races instead of throwing.
- `PutCommand`'s loop pre-check is documented advisory: `MoveTo`'s
  under-lock cycle guard is the enforcing backstop. `door create`
  documents sequential (not all-or-none) direction semantics: a missing
  destination refuses its direction without ever referencing a missing
  node. The `GameArgumentParser` snapshot comment documents why shared
  def instances are throw-free.
- New concurrency regression suites `ThreadSafetyRegression11Tests` through
  `ThreadSafetyRegression14Tests` pin the above (depth-2 unwire
  coherence, delete-vs-send behavior, registry single-publish,
  serialized puppet attach, single-count disconnect, rename
  single-holder, spawn vs remove, put vs rewire acyclicity, reload
  tickable identity, denied-move surfacing, hint/gate agreement, door vs
  delete safety, settings-cache stability, shutdown-hook replacement,
  racing-shutdown readiness, foreign-pid refusal, token walk, overwrite
  double check, shared-DTO subtype loads, concurrent migration setups,
  write shedding, route/gate scans, door-dirty retention,
  fresh-transition survival, real-Delete refusal, raw-write tolerance,
  ban-kick snapshot, drain-retry guard).

## [0.18.0.0] - 2026-09-25

### Dice and text functions

- `DiceRoll` handles `int.MaxValue` faces via a long random bound instead
  of throwing on the `faces + 1` overflow (same trap `DiceRollAverage`
  already avoided).
- `$random`/`$randint` use long bounds, so a maximum-int bound rolls
  instead of throwing (quiet parses render the roll instead of echoing
  the raw call).
- `$You(..., capitalize=false)` renders `you`: the explicit kwarg now wins
  over the capital stanza, matching `$your`.
- Arithmetic that overflows to `Infinity`/`NaN` renders empty (throwing
  `arithmetic overflow` when raising) instead of leaking `Infinity` to chat.
- `$conj`/`$pconj` with an explicit actor key missing from a present
  mapping echo the raw call (throwing `unknown actor` when raising)
  instead of silently conjugating for the caller.

### Registry and commands

- `ObjectRegistry.RemoveObject` only removes the live entry when it is the
  passed object, and `IndexInsert` drops a displaced occupant's reverse
  entry, so overwrite-then-remove no longer evicts a live same-id occupant.
- `#id` references accept digits only: `# 123`, `#+12`, and `#-5` report
  invalid format instead of parsing.
- Unknown `nargs` strings throw `ArgumentException` instead of silently
  compiling to single-value.
- `float`/`double` argument conversion rejects comma-grouped input like
  the `int` branch does.
- Single-value options error with `expected one argument` when the next
  token is a known optional or `-flag` instead of swallowing it as the
  value; negative numbers still parse as values.
- `AddArgument()` with no names (or a null/empty name) throws
  `ArgumentException` (naming the parameter) instead of a bare
  `InvalidOperationException`.
- `ReturnAppearance` omits the template colon when desc is empty (`Bare`,
  not `Bare:`).
- `FollowCommand` takes the follower/leader write locks in Id order
  (`FollowLockOrder`, shared by both directions) instead of nesting the
  `Following` setter under the target lock, so opposite simultaneous
  follows cannot deadlock.
- `CmdSet.RemoveByTag` collects and deletes under a single lock, so keys
  added with the tag mid-pass cannot survive that eviction.

### Sound, throttle, and map editing

- `AtEmitSound` fans out with the node gate's emitter (not the pre-gate
  one) when the gate swaps it, for both room broadcast and BFS propagation.
- `ThrottleWindow` fully scans small dicts and probes a rotating window in
  larger ones, so expired tail entries drain instead of lingering to the cap.
- Malformed map-edit cells send `map_edit_reject` with the failing index
  instead of dropping the edit silently.
- Legend entries keep validated `[r,g,b]` colors in new `FgRgb`/`BgRgb`
  fields (persisted through `LegendEntryDto`) instead of flattening them
  to the default hue; scalar hues are unchanged.
- Legend coords accept `long` values like map-edit cells do.

## [0.17.0.0] - 2026-09-25

### Commands

- Every `ParsedArgs` read goes through a `ParsedArgKeys` constant: the
  command-specific literals (`account_name`/`password`, direction and
  wall-style flags, `channel`/`object`/`reason`/`ip`/`name`, the six
  `is_*` flags, `recursive`, `value`, `count`) are now named constants
  alongside the shared keys, so a misspelled key is a compile error
  instead of a silently missing argument. Runtime keys are unchanged.

### Locking and policies

- `ObjectRegistry.AllLock` is a non-reentrant `Lock` instead of a
  recursive `ReaderWriterLockSlim`: every take was already a leaf-level
  hold (enter, touch the dictionaries, exit) with no re-entry path, and
  the lock is assembly-internal. `AddObjectUnique` predicates are
  restricted to reading object properties (a registry-touching predicate
  would now deadlock instead of merely racing).
- `LockPolicies` target-bound and target-free `TryResolve` overloads are
  switch expressions over the policy (same predicates, same
  allow/deny outcomes).

### Network and paths

- `TelnetSessionReader` lost its blocking sync `Read` override: the only
  production consumer (`ReadCappedLines`) reads through `ReadAsync`, so
  the sync-over-async bridge had no callers.
- `PathGuards.DenyRoot` computes containment with
  `Path.GetRelativePath` against the filesystem root (`.` is the root,
  a separator-free relative path is a direct child) instead of
  hand-trimming separators and comparing parent paths. Both refusal
  messages are unchanged.

### Persistence and objects

- The remaining persistence DTOs are sealed records like
  `GameObjectDto`/`DoorDto`: `NodeDto`, `NodeAreaDto`, `NodeGridDto`,
  `LockDefDto`, `AlarmDto`, `GameTimePersistDto`, `MapInfoPersistDto`,
  and `LegendEntryDto`. Wire shapes and null-backfill behavior are
  unchanged.
- `Door` state properties are `field`-backed (the 13 hand-written
  backing fields plus the generic `ReadProp`/`SetProp` pair are gone).
  Locking and dirty-mark behavior are unchanged: setters still take the
  lock and mark after release, and paired-endpoint publishes plus
  under-hold `Try*` writes suppress the per-write mark and fire one
  mark after release (marking takes the handler lock, which against the
  remap path's handler-to-door order must never happen under the door
  hold).

## [0.16.0.0] - 2026-09-25

### Tests

- One test connection: `FakeConnection`/`ConcreteConn`/`TestConn`/`BareConn` aliases and the parallel
  bag/string/queue stores are deleted. Every consumer reads the single tuple `Sent` store plus the `Received`
  queue on `TestConnection`; delivery-failure paths use the `ThrowOnSend` flag. Assertion predicates that read
  the old JSON bag now match command/args/kwargs directly.
- `SaltProvider.CurrentSaltForTests` replaces the last production reflection poke (lock-free volatile read);
  ticker/pool already resolve through `TryGet` methods.
- `Features/Simplify/*` merged 87 files into 11 feature files, methods byte-identical.
- Blocking waits converted to `await` throughout the suite (including `Task.WaitAll` → `WhenAll`+`WaitAsync`,
  negative-timeout proofs, and `AggregateException`-unwrap sites); the redundant `EnterAsync().Wait()` line is
  deleted. The global xUnit1031 suppression is removed — file-level pragmas remain only in `TestFixture.cs`,
  `Fakes.cs`, and the two thread-affine lock tests where blocking is load-bearing (awaiting across a held
  `ReaderWriterLockSlim` hops threads and breaks affinity).
- The lifecycle test polls for the parent banner text (bounded) after the health gate: health needs only the
  port while banners wait for the pid claim, and under suite IO load the claim lags the bind.

### GameTemplate

- The five behavior-empty `Custom*` subclasses are deleted from the template and the scaffold (emitters, kind
  table, hook generator, and banner line go with them — all verified caller-free). `GameSettings` keeps only
  overridden values (`ServerName`, `WebclientSyncCheck`); `SavePath`/`SecretPath` duplicates of the engine
  defaults are gone from the checked-in file and the emitter. Template README updated.

### Build and docs

- `<WarningWave>latest</WarningWave>` added (zero new warnings); `global.json` rolls forward by patch, not
  feature band; the `telnet_cs` pin rationale moves from the props file to `README.md`; launcher scripts
  report the 10.0.100 SDK floor.
- `upgrade.md` archived to `docs/upgrade-10-done.md` (status only); engine and webclient changelogs split
  (`webclient/CHANGELOG.md`).
- `deploy.py` no longer string-patches built HTML: `vite.config.ts base: '/static/'` already emits prefixed
  paths (verified against a fresh `npm run build` and the staged copies — all 12 patterns match nothing).

## [0.15.0.0] - 2026-09-24

### Settings and setup

- `AtherizSettings.Global` is a lock-free volatile publish (the
  per-access lock bought nothing: nobody does atomic read-modify-write
  on the slot). The shared-mutable `Default` singleton is deleted;
  call sites take `new AtherizSettings()`.
- The validator is plain conditionals with unchanged messages (no
  `FailWhen`/`CollectGuard` closure framework, no throw-to-collect).
  Certificate validation is file-exists-only; the crypto load happens
  once at host startup, fail-fast there.
- World creation takes a `SetupOptions` record (no five-positional-string
  signatures on `IGameSetup`/`RunSetup`/`DoSetup`) and runs in named
  stages: directories, salt, registry reset, `BuildLimboWorld`,
  `ResolveCredentials`, `PersistSeed`/`SeedAccount` in one transaction.
  Prompts, messages, and checkpoint atomicity are unchanged.
- `GameUtils.MakeIter` is a single generic `MakeIter<T>(T)` (sequences
  need explicit `T`); stringly `IsInGameFolder`/`ExistsExact` `osName`
  flags are gone — production asks the OS, tests drive the Windows
  branch through a typed internal seam. `TlsCertLoader` is a single
  attempt (the file/platform-loader retries could never succeed where
  the in-memory parse failed); combined-PEM, chain bundling, and
  fail-closed behavior are unchanged.

### Menu, logger, clock

- One async menu system: `MenuEngine` with `HandleInputAsync`/`RunAsync`
  (explicit render, single `Choice` shape with task goto/callback).
  `Menu`, `MenuRunner`, sync paths, and `Task.Run` wraps are deleted;
  display text, key normalization, and stay/close semantics unchanged.
- The log factory carries a real sink provider (echo + file in one
  `Log` call) instead of the null pair plus log-then-echo; echo and
  file bytes are identical, including the foreign-factory path.
  `Write()` routes through a per-category logger so call-site
  categories (e.g. `Node:` context lines) reach the output — the
  default-category shortcut that stamped every line `atheriz:` is gone.
- The static clock is `GameClock`, ending the BCL `TimeProvider`
  collision (and its `global::` qualifications and per-file aliases);
  the fake-clock seam and all readings are unchanged.

### Utils

- `GameUtils` regexes are source-generated (`[GeneratedRegex]`, AOT-safe,
  zero startup compile). `CompressWhitespace` is a two-pass span collapse
  with no regex and no pattern cache; blank-gap, `(?<=\S)`-gate, and
  zero-cap quirks are preserved and pinned.
- `CryptoRandom` uses `Base64Url.EncodeToString` and
  `Convert.ToHexStringLower` instead of hand base64url/hex (same lengths
  and alphabets). `StringDistance` rents its DP rows from `ArrayPool`
  instead of allocating per call. `FsUtil` exposes one
  `TryChmod(path, mode)` API; the named wrappers and aliases delegate to
  it.
- The verb table ships embedded-only (the output-dir copy that shadowed
  it is gone); `Conjugate` lookup tables are frozen dictionaries built
  from the insertion-ordered load, so last-wins lemma collisions resolve
  exactly as before.

### Small core types

- `MenuPrompt` waits with `WaitAsync(timeout)` instead of a
  `Task.WhenAny` + delay pair; timeout still cancels the owning prompt.
- The connection screen resolves the assembly version once and reuses
  it; rendered output is unchanged.
- `PathNode` is a record with computed `F` over immutable scores, and
  the A* queue takes integer `F` priorities; ordering semantics and
  paths are unchanged.

### FuncParser

- Game callables use the `ParserCallable` shape directly: the two
  generic-`Delegate` constructors, the `GetParameters` signature sniff,
  `ValidateGenericCallables`, and the merged-kwargs builder are deleted
  (no plain-`Delegate` callers exist in production).
- One call engine (`ExecuteCall`) behind the public `Execute` and the
  parser loop (the two merge/invoke copies are gone); `ParsedFunc`
  keeps the echo pair plus name/args/kwargs (`DoubleQuoted`,
  `CurrentKwarg`, `OpenL*` counters deleted). The nesting cap stays as a
  pinned safety bound (enforced once, in `ParseFrame`), and the static
  `Parse` entry points stay as the production public API. Separator
  finalization is a named helper; the frame/echo machine itself is
  logic-identical (unknown-function echo, nesting cap, raising
  semantics, and the two documented `returnStr:false` divergences all
  pinned by the untouched characterization suites).
- `SafeConvertToTypes` takes typed `Func<object?, object?>` converters
  (the `"py"` literal/arithmetic converter is the named
  `PyConverter`) instead of the tuple spec with `Type`/`Delegate`
  sniffing and `Convert.ChangeType` boxing; in-place ownership
  semantics are unchanged.
- `SafeFormatMap`'s `{key}` pattern is source-generated.

### Plugins and hosting

- Plugin dependency resolution lives in a dedicated
  `GamePluginLoadContext` (`AssemblyLoadContext` subclass) using the
  BCL `AssemblyDependencyResolver`: managed deps, natives, and
  resources resolve from beside the plugin dll, while engine/framework
  assemblies unify with the default context (a vendored second
  `Atheriz.Core` can never shadow the live one). The hand `Resolving`
  probe is deleted.
- `IGameSetup` has no static slot: `PluginLoader.Load` returns the
  discovered entry, the boot load hands it back (last loaded wins, as
  the slot did), and `reset`/`new` thread it explicitly into
  `RunSetup(options, game)`. Dispatch tests pass stubs directly with
  no global save/restore.
- `KestrelConfig.ConfigureKestrel` takes the already-bound
  `AtherizSettings` instead of re-binding `IConfiguration` itself
  (binding happens once in `ServerHost`); fail-fast bad host,
  fail-closed TLS, and dual-stack policy are byte-identical.
- `Coord`'s two grammars share one span comma-splitter (no `Split`
  arrays anywhere); accepted inputs and failure contracts unchanged.
- Hot-reload migration is explicit where the game opts in:
  `IMigrateFrom<T>` (`Plugins/IMigrateFrom.cs`) — a replacement
  implementing it constructs normally (all initializers run) and pulls
  state via `MigrateFrom`, instead of the ctor-bypassing field copy.
  Transient session/lock state is engine-restored on both paths, and
  both share one registration tail (id, unlock-before-registry,
  AddObject, rewire) so the audited lock order cannot regress. A
  declared-but-broken contract keeps the old instance live and loud;
  types without the contract keep the legacy copy (the only mechanism
  for unknown game subclasses). Also from the deadlock audit:
  `ReloadCoreAsync` extracted so the skip-gate is held once per
  operation (reentrancy retained as defense-in-depth).

## [0.14.0.0] - 2026-09-24

### CLI

- Commands run on a `System.CommandLine` tree with typed options and
  positional arguments; handlers return `Task<int>` exit codes. Deleted
  the hand matcher, glued/port-strip helpers, exit-code static, help
  table, `NullWebServer`, and test-literal block. `--help` and parse errors
  come from the framework. Glued `-p1234` is normalized before parsing
  and the telnet env fallback is preserved.
- `start`/`new`/`restart`/`reset` background by default, each with
  `--foreground`/`-f` for in-process (`new`/`restart`/`reset` gain it;
  `start`'s previously ignored `-f` is now honored).

### Processes

- Deleted the bash/nohup spawner, `$!` pid parsing, `lsof`/`ss`
  subprocesses, and port-to-pid discovery: `stop` never signals a
  process it cannot verify. Kept the atomic pid claim plus the
  Linux-only per-pid listener check as the kill gate. Kills are quiet
  bounded escalations (`WaitForExitAsync`, no dot output).
- The replacement spawner is fully managed (argument list, never a shell
  string; inherited stdio, never pipes): the parent launches
  `start --foreground` in the absolute game folder, waits boundedly for
  the pid claim plus the port, then prints the banners itself and exits —
  child pid and log tail on failure. The child detaches itself
  (`Cli/DaemonDetach.cs`, the only file with native declarations):
  `setsid` plus SIGHUP-ignore plus stdio to `/dev/null` on Unix, console
  release on Windows, each OS-guarded with a logged run-attached
  fallback. Child banners are skipped and unhandled exceptions append to
  `save/server.log`, so closing the terminal no longer stops the server
  on any OS.

### Hosting

- Protocols register through DI (`AddAtherizProtocols` hosted service)
  instead of the string allowlist plus `Setup(object)` casts; the
  test-only websocket/telnet app doubles are gone with it. No-HTTP
  servers boot a generic host instead of a null web server. Static
  files serve from a single web root (template fallback and
  first-existing helper deleted) with a source-generated hash pattern;
  entry routes, health shapes, and cache headers are unchanged. Banner
  text comes from one shared formatter used by the foreground server
  and the background parent alike. The startup-loaded TLS cert is served
  through a small selector (`KestrelCertSelector`) instead of a direct
  `UseHttps` handoff; load-time fail-closed behavior is unchanged.

### Logging and admin auth

- Deleted the unused file-logger provider; the shared logger encodes
  once per line (same array feeds rotation check and write) and
  recreates deleted directories on the next write. Admin endpoints
  authorize through an `Admin` authentication policy with typed
  `AdminResult` JSON and 401 failures in the same body shape; the
  token compare is one `FixedTimeEquals` call. Per-request token reads
  and the full `127/8` loopback check are unchanged.

## [0.13.0.0] - 2026-09-24

### Commands

- All concrete logged-in verbs now run through the `LoggedInCommand`
  template base (`Run(CommandContext)` resolves the puppet once and splits
  parsed vs raw input into `RunPuppet`/`RunPuppetRaw`) instead of
  repeating the puppet/parsed-args guards in every `Run`. Builder-only
  verbs sit on `BuilderCommand` with its single sealed `IsBuilder`
  gate; superuser (`reload`/`save`/`shutdown`/`spam`) and raw-privilege
  (`quell`/`unquell`) verbs keep their own gates on `LoggedInCommand`.
  `help`/`none`/`quit`, room exits, and the map editor stay on the raw
  base (unpuppeted callers or non-standard puppet resolution). All
  user-visible messages are unchanged, including `put`'s usage denial
  and `examine`-here/`look`-here/`follow`-who null-arg behavior.
- `Command.Parser` is virtual so decorator commands can forward it;
  fixed tuple deconstruction in argument splitting and definite
  assignment in `move` coordinate parsing.
- Argument splitting runs on one escape-aware tokenizer: `SplitArgs`
  keeps its unbalanced-quote contract and the `create`/`guest`/`new`
  stubs take verbatim password/description tails via `SplitHeadTail`.
  Coordinate parsing is centralized in `Coord` (space and comma-only
  forms); `move` accepts the same inputs with the same messages.
- `DispatchLoggedIn` is a flat resolve chain (global, glued-quote,
  local, auto-alias, none-fallback). `#id` lookup is shared via
  `TargetResolution.ResolveById` and `to`/`from`/`in`/`into` keyword
  splits via `SplitOnFirstKeyword` (`give`/`get`/`put`/`ban`/`puppet`
  behave as before).
- `examine` renders via a value/hint tuple switch, `group`
  subcommands are `IGroupOp` operations, and channel verbs parse once
  into a `ChannelAction` (list beats unsubscribe beats subscribe beats
  replay beats send). No output text changed.
- The logged-in registry is built from per-family builders
  (admin/building/communication/social/item/movement/info) with the
  same verbs; `ScreenReaderCommand` lives in `Commands/Common` with a
  subclass kept at the old name.
- `Run(CommandContext)` is the primary command entry: the untyped
  `Run(caller, args)` adapter forwards to it (parsed args run
  `RunParsed`, otherwise `RunRaw`), so direct `Run(caller, pa-or-string)`
  calls keep working. `LoggedInCommand` resolves the puppet there, and
  the remaining untyped verbs (`help`/`none`/`quit`, room exits, the map
  editor, the unlogged-in stubs) moved onto the context. No output text
  changed.
- `move` parses into a `MoveOptions(Coord Dest)` record and moves via
  `RunMove` (same inputs, same two diagnostics, same move messages).
- The dispatch auto-alias tail is `TryResolveAutoAlias` (single-char
  no-alias refusal still aborts before the none-fallback); resolution
  order and messages are unchanged.
- Name search behind `ban` and `puppet` runs through
  `TargetResolution.ResolveObject` (`#id` first with verbatim errors,
  then the shared fallback search with the caller's filter, not-found
  text, and multi-match shape). Ban keeps its player-only world-exact
  scope and per-line id list; puppet keeps its local search and
  single-line id list. All four messages are unchanged.
- Settable properties dispatch through `ISettable.TrySetProperty`
  (explicit per-type switch on the owning type: `GameObject` plus
  `Node`/`Channel`/`Account`/`Script` overrides) instead of the
  triple-spelling map plus `Convert.ChangeType`. Accepted inputs,
  conversion errors, and read-only refusals are unchanged; unknown
  names still land in the extras store. `SetHelper` keeps the
  protected/move gates and delegates the rest.
- Session helpers live in `Commands/SessionLifecycle.cs`
  (`ConnectionHelper`, `CreationCooldownHelper`) and message/help
  formatting in `Commands/Formatting.cs` (`HelpHelper`, the
  `CommandHelpers` message-dialect half; the guard/search half stays in
  `Commands/CommandHelpers.cs` under the same namespace); pure
  relocation, same namespaces, types, and messages.
- The `RunParsed`/`RunRaw` defaults send help instead of recursing
  when a single-override command receives the other input shape (the
  untyped adapter routes back through the context entry, which would
  otherwise ping-pong until the stack overflows); normal and decorator
  forwarding is unchanged.
- `ToBool`/`ToInt`/`ToDouble` accept the widened numerics (plus
  `bool` for int/double targets) the old `Convert.ChangeType` path
  accepted, with the same `OverflowException` on overflow.
- Logged-in verbs live one file per registry family
  (`AdminCommands`, `BuildingCommands`, `CommunicationCommands`,
  `SocialCommands`, `ItemCommands`, `MovementCommands`,
  `InfoCommands`, in registry-builder order); helpers keep their own
  files. Type names, namespaces, and messages are unchanged.

### Formatting and validation

- The help table sizes its columns from the data and truncates with
  ranges instead of fixed widths and `Substring`; pinned table outputs
  are unchanged and over-long keys no longer overflow.
- The name-validation regex is source-generated and there is a new
  `ValidateNameOrThrow` whose messages match `ValidateName`.

### Async and channels

- `Session.Prompt` accepts a `CancellationToken` (cancel ends the
  prompt quietly); the account/character wizards take an optional token
  and a disconnect now cancels the in-flight wizard instead of leaking
  it. `Session` implements `IMessageTarget` (itself, plus close).
- New `RunAsync(CommandContext, CancellationToken)` entry surface
  (sync dispatch unchanged): `create`/`guest`/`new` delegate to their
  existing connection wizards, `connect` awaits character selection
  under the passed token linked with the connection lifetime; the
  default runs the sync body inline.
- `BaseChannelCommand` gains a constructor capturing its channel (used
  by `Channel.GetCommand()`); the mutable fields, setters, and lazy
  lookup still work. `IMessageTarget` has default `Session`/`Close`
  members and `ConnectionHelper` calls the interface; unknown caller
  shapes stay a silent noop.

## [0.12.0.0] - 2026-09-23

### Compatibility

- Old save files from 0.11.x may not load: map legend entries are JSON
  elements, DTOs are records, and save/delete operations are typed
  records. External game code using the deleted aliases below must
  switch to the canonical names.

### Singletons

- Central singletons are `Lazy<T>` slots; the reader/writer lock, the
  generic get-or-create core, the public singleton-lock exposure, and
  the map-handler/ticker twin shims are gone. Settings overloads keep
  first-creation-wins. A failed creation no longer wedges the slot:
  the next caller retries.

### Typing

- Flag setters take getter/setter delegates; string-literal `TrySet`
  is deleted.
- Legend, door, and game-object DTOs are sealed records; map-edit
  legend payloads are JSON elements (wire bytes unchanged); the dict
  payload methods are deleted.
- Save paths use `SaveOperation`/`DeleteOperation` records; `Delete`
  returns carry the record lists.
- Load paths use `CreateForLoad` factories; the skip-id marker types
  are deleted.
- Search depth is a `DefaultMaxSearchDepth` constant plus a per-call
  `maxDepth` threaded through deletes; the mutable statics and the
  settings knob are deleted.
- Delete-path dispatch is virtual (`SyncDeletedGuard`,
  `RemoveListener`, `Account.Delete` override). Lock-policy parsing is
  a switch expression. `Display` is a property; node lookups return
  lazy enumerables.

### Structure

- One type per file across providers, row types, DTOs, helpers,
  command helpers, map-edit/map types, and the input-func table; the
  vacated containment file is removed.
- Locks and hooks live in `LockTable`/`HookRegistry`; bans, creation
  cooldowns, and the bounded dictionary are top-level stores with thin
  forwarders behind. The reload-patch transient list tracks the hook
  storage rename.

## [0.11.0.1] - 2026-09-23

### Cleanup

- Deleted the `_is_thread_safe` patch-marker flags from all five entity
  types. Thread safety comes from each instance's `SyncRoot` lock, so the
  markers were write-only state nothing read. Tests that pinned the
  markers now pin the lock surface instead (marker absence,
  `SyncRoot` inheritance, live lock round-trip, loaded instances
  carrying locks). `EnsureThreadSafe` stays as the documented no-op.

## [0.11.0.0] - 2026-09-23

### Singletons

- The singleton lock no longer allows recursion; the ticker fetches its
  pool before taking its own slot, so nested acquisition is gone.
- The map-handler and ticker shims are thin forwarders to the central
  singletons with no second cache, so setting a handler is visible to
  move stamps, door paint, and tick wiring immediately. Stale-cache pins
  updated to assert the forwarder shape.
- Central singletons gained typed ticker/pool setters for test and boot
  wiring.

### Typing

- Moves gained typed front doors (`GameObject`/`Coord` overloads,
  `MoveToNowhere`, `MoveToLocation`); the untyped core is unchanged.
- Broadcasts dispatch virtually (`BroadcastToContents`, overridden by
  nodes); the location helper no longer tests the runtime type, and both
  delivery contracts (node catch-all vs object parse-error-only) are
  unchanged.
- DTO kind dispatch goes through a single `EntityKind` classify point;
  the `Type` string plus `IsNode` persisted shape is unchanged.
- Script lookups gained generic `HasScriptType<T>`/`GetScriptsByType<T>`;
  the string forms stay for dynamic names.
- Lock policies gained a typed `Evaluate` switch; persisted policy names
  and lock behavior are unchanged.
- Legend entries serialize directly to JSON; the dict payload methods
  stay for compat.
- Content queries accept the most general sequence type; the depth limit
  is now a per-call option defaulting to the shared static.
- Ticker slots are millisecond-quantized so near-identical intervals
  share one slot; pools and tickers accept cancellable work items.

### Cleanup

- Flags are plain auto-properties; the multi-spelling name setter stays
  for compat but is now a switch expression.
- Nullability suppressions removed at the hook snapshot, session pop
  (now nullable out), and node type-name registration.
- `GetOrAddGrid` is the canonical grid accessor; `GetOrCreateGrid`
  forwards to it.
- `NodeLink`, the node exit command, the ticker holder, the lock DTO,
  and the DTO serializer each live in their own file; the node exit
  command also uses collection expressions. A side-count queue
  optimization was reverted: the queue stays the single source of truth
  for counts.
- The moon-phase ladder is a switch expression; output strings
  unchanged.

## [0.10.0.0] - 2026-09-23

### Typing

- Input handlers are a typed delegate alias end to end (queue items,
  registration, dispatch); registering a wrong-shaped handler throws at
  registration instead of failing at dispatch.
- Parsed command-argument keys are named constants shared by writers and
  readers.
- Coordinate helpers only accept coordinates and tuples; the untyped
  list-taking overloads are gone.
- The puppet-restore snapshot is a two-field record with value equality;
  the int-form privilege arm is gone.
- The base ban-reason property reads only its canonical key; a stale
  legacy spelling in extras is ignored (account storage is unchanged).
- Locks live in a single per-name entry list pairing policy with
  predicate; persisted lock rows carry a policy list, with unknown names
  denied closed and logged; the policy-name helpers also resolve the new
  denied marker.
- Hook names are an enum with attach-time signature checks: a wrong-shaped
  hook is refused with an error log and never runs; unknown names still
  attach unchecked for custom hooks. Three previously literal-only hook
  names gained constants.

### Logging

- Plugin loader diagnostics route through the logger (still echoed to
  stderr and the server log); all message texts unchanged.

## [0.9.0.0] - 2026-09-23

### Cleanup

- Comments: dropped port-of citations across library sources (behavioral
  notes kept); one test anchor updated to match the new text.
- Exceptions: narrowed catches to the specific errors each site handles;
  deleted helpers that only logged swallowed exceptions; session reads go
  direct; user-visible messages unchanged.
- Locks: the connection map is now a concurrent dictionary (custom
  comparer deleted); two private locks converted to non-reentrant locks
  after auditing every hold site; public lock surfaces unchanged.
- Async: the telnet writer gained awaited write APIs; connections dispose
  asynchronously with bounded drain waits; retry loops carry cancellation;
  the telnet accept loop runs as a hosted service; pid helpers read
  asynchronously with kill-on-timeout; daemon spawn drains stderr.
- Logging: library diagnostics route through the logger (still echoed to
  stderr and the server log); logger-failure fallbacks, CLI output, and
  all message texts unchanged.
- Typing: hook names are compile-time constants; input-queue items are a
  record; lock policies gained an enum front door with persisted names
  unchanged.

## [0.8.0.0] - 2026-09-23

### Commands

- Per-channel shortcuts accept multi-word messages like the main channel
  verb; `spam` skips taken character names; sync `guest` keeps the
  description words; `group add` refuses targets already in a group;
  pre-login `none` only suggests usable verbs; `$your(x, capitalize=1)`
  capitalizes like `$you`; `already open` notifies the opener's room only.
- A menu choice with an async callback where a sync one is required now
  throws to the caller instead of being swallowed and logged.
- Losing the new-character name race on a fresh account removes the
  just-created account instead of orphaning it.

### Engine

- `GetTimespan` renders "1 minute"; alarm removal drops emptied buckets;
  fresh connections start "never busy"; `Autosave.Reset` also unregisters
  explicit-ticker registrations.
- `CompressWhitespace` preserves a single blank line by default and
  honors limits above 2.
- Tick re-registration registers each tickable node once; `SetMapHandler`
  publishes to the cached singleton; static clocks honor a swapped
  `TimeProvider.Default` (system path stays fast).
- Over-length strings compare on capped prefixes so best-match orders by
  content; zero-byte `Track` plants no entry; disposing a stale-fork
  adopted write lease clears the flow's hold mark.

## [0.7.0.0] - 2026-09-22

### Concurrency and snapshots

- Live collection properties now return copies under the lock, so
  readers never observe a concurrent mutation: `Node.Links`/`Nouns`,
  `NodeGrid.Nodes`/`Data`, `NodeArea.Grids`/`Data`, and the `MapInfo`
  pre/post grids, legend entries, objects, and listeners. Their
  setters copy the input and mark the owner modified.
- `MapInfo` gained locked single-scope mutators for the writers that
  used to touch the live dicts directly: `SetPreCell`/`RemovePreCell`/
  `SetPostCell`, `PaintSymbol` (door glyph repaint), `ReplaceLegendEntries`
  (legend bulk swap), `ReplaceEntry` (hot-reload rewire), and
  `AddListenerAndMapable` (same-map move).
- `ApplyMoves` rewrites rewritten link coordinates in place instead of
  swapping link instances, preserving link identity for concurrent
  readers.
- `NodeLink` and `ExitCommand` alias lists are copied on the way in
  and out.
- The command dispatcher snapshots the lag check before invoking it,
  so a concurrent settings swap cannot split the null test from the call.
- `Channel.Command` rebuilds on rename instead of serving a stale
  cached key.
- `AsyncThreadPool.Dispose` is guarded against concurrent double
  dispose.

### Sessions and prompts

- A session records that it is closed at disconnect (cleared on
  connect): a closed session refuses puppet attach and drops late
  text input instead of acting on a dead session.
- Prompt cancellation targets the exact prompt via a token returned
  with the prompt task, so a newer prompt can no longer cancel (or be
  cancelled by) an older one.

### Logging

- Malformed/oversize/overlong log throttles are per manager,
  protocol, or connection instead of process-wide, so one noisy
  connection no longer silences warnings for the rest.

### Persistence

- The ambient-transaction path invokes the rollback hook when the
  work throws, matching the owned-transaction contract.
- Disconnect snapshots the connection host inside the manager write
  lock instead of before it.

## [0.6.0.0] - 2026-09-22

### Commands

- `group add`/`kick` accept multi-word names (the full remainder after
  the subcommand, not just the first word).
- `door` accepts a bare direction (`n`/`north`, etc.) like its sibling
  direction command, instead of only flag syntax.
- `look` with no location always reports "You are nowhere." even when
  the puppet has a description set.
- Channel `-s`/`-r`/send/history report an unviewable channel as
  not-found (same as `-u`); sending needs both view and send rights,
  and the send-locked message is kept when view passes.
- `puppet #id` reports deleted targets as not available.
- Unknown-command suggestions now skip hidden commands and commands
  the requester cannot access, matching `help`.
- `set name` validates the new name and blocks duplicates across PCs;
  renaming to the current name still works, and the non-superuser gate
  is unchanged.
- `create` password and new-character desc keep raw spacing after the
  name instead of shlex re-joining.

### Messaging and hearing

- Offline PCs are invisible to regular players: they appear in no room
  lists, resolve in no search, and render as `Someone` even where `view`
  otherwise passes. Builders and above keep sight of them (room lists,
  search, examine) with a `Name (offline)` suffix.
- The hearing check gates on `CanHear` instead of the PC flag, so
  non-PC listeners hear and deaf PCs do not.
- `Channel.Msg` forwards the sender to `listener.Msg`, preserving
  `from` for per-listener personalization.

### Expressions and appearance

- `Node.AddScript` no longer records non-script objects in the script
  id list.
- Dice average uses long/double math (no int overflow) and rejects
  faces below 1.
- Unary minus binds outside `**` (`-2**2` is `-4`); `%` is floored
  (`-7%3` is `2`).
- The names `me`, `here`, and `all` (any case) are reserved.
- Node appearance runs the `return_appearance` hook like the base
  implementation.

### Persistence and locks

- Locks whose policy cannot be resolved restore as deny-all under the
  original name instead of being dropped open; `custom` locks are
  still dropped loudly.
- Account deletion tears down follows, channels, sessions, and ticker
  state and journals the row for the next save checkpoint instead of
  writing the database mid-game.

### Time

- Season boundaries derive from `MonthsPerYear` (identical bands for
  the default 12-month calendar).
- Ordinal suffixes use the last two digits (`111th`, not `111st`).
- The ticker interval is cached at start and reused at stop (invalid
  values clamp to 1s); the settings validator rejects non-positive
  intervals.

### Network

- The telnet byte budget is measured after line-ending normalization
  (text, prompt, and masked prompt), so `a\nb` counts its wire bytes.
- Oversize/malformed logs report UTF-8 byte counts, not char counts.
- Queue-full (dropped) and queued-retrying busy signals are now
  distinct messages.

### Ticks and plugins

- Stuck async tick holds expire (default 5 minutes) with a warning
  instead of pinning the slot forever.
- Plugin reload evicts ticks targeting any game object, not just live
  tickables, so deleted objects leave no zombie ticks.

### Doors and settings

- Doors honor `KeyId`: locking/unlocking needs the key in inventory
  (doors without a key are unaffected); `DoorDesc` renders in the
  door description.
- A settings key file without a certificate now fails validation.

### Tooling

- The webclient sync checker compares against the `wwwroot/webclient`
  static baseline instead of reporting clean when no engine `web/`
  tree exists.
- The `create` CLI exits 1 on failure, like `reload`.
- Four bare `catch` blocks in `NodeHandler` now name the exception
  type (identical semantics; satisfies the no-bare-catch rule).

## [0.5.0.0] - 2026-09-21

### Server boot loads the game assembly

- `start` now discovers + loads the game assembly and patches the world to
  its types (`PluginReloader.LoadGameAssembliesAtBoot`), so game verbs work
  without a manual `reload`. Failures log and boot continues engine-only.
- `PatchSingleObject` pairs fields by name and assignability, so a game
  field shadowing a base field with an incompatible type no longer
  starves the true slot (which threw on every flag read of patched
  instances and broke the connection screen).
- `reload` now evicts command instances from the replaced plugin load
  (`PluginReloader.EvictStaleCommands`), so the reinstall re-adds fresh
  ones. Previously the registry kept the dead instances (duplicate-key
  collision) and new command code never ran until a restart.
- `reload` skips object patch and command eviction when the world was
  already converted by a previous load (exact-match patching is blind to
  those instances, so converting again matches nothing while eviction
  destroys the working commands). It logs this loudly instead of
  silently gutting the command registry; a restart picks up new code.
- World creation (`reset`/`new`) now dispatches to the loaded game's setup
  through a new engine `IGameSetup` slot (game self-registers via module
  initializer; engine template otherwise), so a reset rebuilds the game's
  world instead of the template's. Boot discovery no longer counts `obj/`
  `bin` outputs or test sources toward dll staleness (which booted
  engine-only after any test build). `reset` logs its target path and
  refuses setup over a half-wiped save.
- Games now register their persisted `GameObject` subtypes at load/setup,
  so saves carry the subtype marker instead of loud-logging "Unregistered
  object subtype" per row, and subtype overrides (e.g. alarm behavior)
  survive restarts. Daemon boot loads the game assembly before the world
  (was: after), so game rows convert directly instead of loading as base
  objects with "Unknown __object_type" noise. Setup discovery only
  accepts public `IGameSetup` classes (a private nested helper could
  previously win the scan and hijack world creation).

## [0.4.0.0] - 2026-09-21

### Game-folder launchers

- `atheriz.sh new` now scaffolds per-game `atheriz.sh` / `atheriz.cmd`
  launchers inside the new game folder: they forward every command to the
  engine launcher without changing directory, so `./atheriz.sh start` (and
  `stop`/`restart`/`reload`/`create`/`reset`/`test`) works from inside the
  game. Engine resolution is `ATHERIZ_ROOT` first, then an upward search
  for the engine checkout, then the absolute path baked in at creation
  time. `new --overwrite` refreshes them like the other template files,
  and the scaffold README documents `./atheriz.sh start`.
- Pinned by `GameLauncherScaffoldTests` (emission + content, overwrite
  refresh, POSIX executable bit; sensitivity-proven).

### Game-folder builds

- `atheriz.sh new` now also scaffolds per-game `build.sh` / `build.cmd`
  wrappers: from inside the game, `./build.sh` rebuilds the game plugin
  (`dotnet build -c Release`, which is what hot-reload discovers) and
  redeploys the webclient into that game's `web/` via the engine's
  `deploy.py`. No flags runs both steps; `--no-web` is plugin-only,
  `--web` is web-only (the two together are rejected), and `--reload`
  forwards to the sibling per-game launcher so a running server hot-loads
  the fresh dll — failing loudly instead of masking a stopped server.
  The engine path is baked in as a path relative to the game folder,
  with the same `ATHERIZ_ROOT` → upward-search resolution as the
  launchers. `--overwrite` refreshes them, and the scaffold README
  documents the new commands.
- Pinned by `GameBuildScaffoldTests` (emission + content for both twins,
  relative-path resolution to a real checkout, executable bit, plus a
  no-doubled-backslash guard on the batch file; sensitivity-proven).
  Live-verified on a scratch game: plugin build produced the Release dll,
  web redeploy staged into the game's `web/static`, and
  `./build.sh --no-web --reload` hot-loaded with "Reloaded 1 modules.
  Patched 734 objects. Errors: 0".

## [0.3.0.0] - 2026-09-20

Threading fixes from the `race.md` audit (R1–R3, R5, R6; notes N1–N2).
Every fix is pinned by a regression test in
`tests/Atheriz.Core.Tests/Features/Concurrency/RaceFixRegressionTests.cs`
(sensitivity-proven: each pin fails with its fix neutered and passes with
it restored). Full suite: 5012/5012 green.

### Concurrency / locking

- `Command.Parser` publication is now `volatile`, so threads racing first
  access on a shared command instance always observe fully-constructed
  parser state (R1).
- `GameArgumentParser` def-map cache is built under a dedicated lock, so
  concurrent parses sharing one parser cannot observe a half-built map
  (R2, latent in production where `Command.Execute` already serializes).
- `GameObject.Subscribe` re-validates the subscription under the peer read
  lock after installing the channel command and rolls the install back on
  mismatch, so a channel `Delete`/`Unsubscribe` landing between peer-add
  and install no longer orphans a live command for a dead channel (R3).
- `Channel.Msg` on a deleted channel is now a no-op instead of appending
  to the dead channel's history, and `ChannelCommand` re-checks the found
  channel's deleted flag under `CacheLock` before caching it (R5).
- `GameObject.MoveTo` snapshots `Location` before the pre-move gates and
  re-compares it inside the ordered write-lock hold (both the normal and
  the null-destination paths), so the loser of two concurrent moves of the
  same object aborts instead of leaving a ghost membership in the stale
  room (R6).
- `AsyncThreadPool` relief-sentinel exit mirrors the relief-thread
  accounting of every sibling path (N1, unreachable in production, now
  consistent), and the `AutosaveTick` settings overload reads the cached
  settings via `Volatile.Read` like the map section already does (N2).

## [0.2.0.0] - 2026-09-20

Security, concurrency, and persistence hardening across the engine and
host. Every fix below is pinned by a regression test (22 new tests across
`FuncParserTests`, `GiveCommandTests`, `MoveCommandTests`,
`WanderCommandTests`, `DoorLockPersistenceTests`, `ChannelUnsubscribeTests`,
`MoveToSwapDeadlockTests`, `MoveToGraftedOriginTests`, `NodeGridApplyMovesTests`,
`AccountLoginReverifyTests`, `PuppetCommandIdTests`, `AutoAliasAccessTests`,
`PendingLimiterReleaseTests`, `HostingRegressionTests`, `WebSocketHandlerTests`,
`RetryDrainTests`, `NodeSubtypeNameTests`, and `BuildSaveJsonFlagRestoreTests`).

### Concurrency / locking

- `GameObject.MoveTo` acquires the mover, source, and destination locks in
  a single globally-ordered set, closing an ABBA deadlock between two
  characters swapping rooms concurrently.
- `ConnectionManager.SweepOrphanedConnections` reads session state under
  the session lock and re-validates immediately before disconnect, so a
  login racing the orphan sweep is no longer disconnected out from under
  itself.
- `Account.Login` re-verifies when the account name changes mid-verify (in
  addition to hash rotation), so a rename racing authentication cannot log
  in under the stale name.

### World / movement

- `NodeGrid.ApplyMoves` remaps members whose `CoordLocation` equals the
  moved node's old coordinate, so contents follow their node instead of
  being left behind as ghosts.
- `GameObject.MoveTo` compares locations by id instead of by reference
  when re-verifying after the pre-leave hook, so a hook that legitimately
  grafts a fresh instance (same id) no longer aborts the move.

### Locks / permissions

- `Door.FromDto` no longer silently drops named lock policies it cannot
  resolve; unresolvable named policies install a deny-all predicate (fail
  closed), while ad-hoc `custom` lambdas keep the established
  drop-with-loud-log behavior.
- `ChannelCommand -u` (unsubscribe) now requires `view` permission on the
  channel; denied channels read as "not found", matching unknown names.
- `CommandDispatcher.AutoAlias` skips commands the caller cannot access,
  so near-miss suggestions no longer leak the existence of commands the
  caller can't use (gibberish input still gets no suggestion).
- `GiveCommand`'s pre-give hook now checks the giver's `give` lock instead
  of the receiver's, so giver-scoped give locks actually gate the transfer.

### Scripting / parsing

- `FuncParser` `$div` by zero returns empty string (or throws
  `ParsingError` when raising) instead of emitting `Infinity`/`NaN`.

### Persistence

- `NodeDtos.HydrateNode` always restores the persisted `Name` for custom
  node subtypes, so a renamed room keeps its name across save/load.
- `GameObjectDtoConverter.BuildSaveJson` restores the live dirty flag
  (instead of a stale snapshot) when encoding fails, so a failed save no
  longer clears the pending-save marker.

### Host / networking

- `WebSocketHandler` disposes the socket when no connection manager is
  installed and disposes the refused connection when registration is
  denied, closing two socket leaks.
- `BaseConnection.RetryDrain` terminates the retry chain (instead of
  rescheduling forever) when the worker pool is stopped, so a stopped
  pool no longer spins the retry gauge.
- `PendingLimiter.Release(Task)` ignores over-release with a warning
  (matching `ReleaseSync`), and a new `ReleaseAttachFailure` performs
  exactly one release for either a tracked or an untracked reservation —
  the websocket send path uses it, closing a double-subtract that drove
  the byte gauge negative.

### NPCs

- `WanderCommand` probes unique wanderer names sequentially from a random
  start, so mass spawns no longer silently produce fewer NPCs than
  requested.

## [0.1.0.0] - 2026-09-19

Correctness and robustness pass over the engine, host, and template.
Every fix below is pinned by a regression test (39 new tests across
`CorrectnessBatchATests`, `CorrectnessBatchBTests`, `CorrectnessBatchCTests`,
`CorrectnessBatchDTests`).

### World / objects

- `NodeGrid.ValidateMoves` now rejects moves that share a destination node;
  swaps and chains (distinct destinations) still pass.
- `GameObject` hash codes are snapshotted at creation, so changing `Id` no
  longer loses set/dictionary members; load paths re-key via `SetIdRaw`.
  Redundant id draws in `GameObject`/`Account`/`Channel.Create`,
  `FollowCommand`, `WanderCommand`, and `InitialSetup` were removed.
- `GameObject.Delete` (plus `Node`/`Channel` deletes) is journaled through
  `ObjectRegistry` and drained in `SaveObjects`, so deleted rows stay deleted
  instead of being resurrected by upsert.
- `GameTime.OnTick` clones alarm `Data` per firing so handler mutations no
  longer leak into later ticks.

### Scripting / parsing

- `SafeArith` division, modulo, and floordiv by zero throw instead of
  producing `Inf`/`NaN`; callers render the raw input.
- `FuncParser` oversize input honors `raiseErrors: false` (echoes raw text)
  in both instance and static `Parse`.
- Argument-parser `append` now converts values and enforces choices; new
  `GetObjList`/`GetList` accessors for multi-value arguments.

### Messaging / commands

- `all_receivers` location messages reuse the speaker-perspective rendering
  instead of `ToString()`.
- `give`/`get` accept `ALL` case-insensitively.
- `set` accepts multi-word values, still errors on a missing value.
- `MenuEngine` null input keeps the current menu instead of throwing.
- `PendingLimiter` over-release warns and preserves the debt instead of
  zeroing both counters.

### Accounts / auth

- `Account.FromDto` skips non-integer character ids instead of crashing.
- Setup passwords are used verbatim (no trim lockout); usernames are still
  trimmed.
- `Account.Login` re-verifies under the write lock when the stored hash
  rotates mid-verify.

### Paths / persistence

- `DenyRoot` also denies direct children of the game root.
- `GuardWipePath` force bypass is contained to the CWD game folder; foreign
  paths still fail closed.
- Malformed legend coordinates throw/skip loudly instead of being silently
  truncated.
- `MapEdit.Consume` resolves before evicting, fixing wrong-key processing
  after cap shrink.

### Setup / templates

- Template generation validates credentials up front and returns `false`
  (no success banner) on setup failure.
