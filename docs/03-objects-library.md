# Objects: creating and using them

> Covers: `src/Atheriz.Core/Objects/GameObject*.cs` (7 partials), `ContentUtils.cs`, `Flags.cs`, `ISettable.cs`, `IGenderProvider.cs`, `Account.cs`, `Session.cs`, `Channel.cs`, `Door.cs`, `Globals/ObjectRegistry.cs`. You learn how to create, find, move, and message objects from game code; save mechanics live in [Saves and backups](10-persistence-saves.md).

## 1. The model in one page

Everything in the world is a `GameObject`: rooms, players, items, exits, accounts, channels, doors. Each object has an id, a name, a description, aliases, tags, contents (other objects inside it), a location (the object containing it), scripts, and a lock table.

The one rule to internalize first: the world lives in memory. `GameObject.Create()` mutates memory; the database is written only on save (`ObjectRegistry.SaveObjects()` at checkpoints and shutdown). Never read the database mid-game — your code always touches live objects. Full save mechanics are in [Saves and backups](10-persistence-saves.md).

Nodes are rooms: `Node` (in `Objects/Node.cs`) is a `GameObject` subclass with coordinates and links. Accounts own characters. Channels carry chat. Doors gate exits. The partial-file map:

- `GameObject.Containment.cs` — contents, put/get/give mechanics
- `GameObject.Look.cs` — look/examine rendering
- `GameObject.Move.cs` — `MoveTo` overloads and guards
- `GameObject.Hear.cs` — sound/hearing hooks
- `GameObject.Delete.cs` — delete teardown
- `GameObject.Puppet.cs` — puppet/unpuppet
- `GameObject.Settable.cs` — property setting (`ISettable`)
- `Node.cs` + `Node.Links.cs` + `Node.Settable.cs` (`NodeArea`/`NodeGrid` are separate types, not partials)
- `IGenderProvider.cs` — gendered-pronoun provider interface
- Hooks come as both a `HookName` enum and a `HookNames` static class (both exist; full detail in [Hooks, scripts, and text](05-hooks-scripts-text.md))

## 2. Example: create a room and a sword

Use `GameObject.Create(string name, string desc = "", ...)`. This code lives in your setup/seed code or in a builder command — anywhere you have access to the live world.

```csharp
// Town Square: the starter room from the tutorial.
var square = GameObject.Create("Town Square", "A cobbled square with a dry fountain at its center.");
square.AddTags(["town", "outdoors"]);

// A sword lying in the square.
var sword = GameObject.Create("rusty sword", "A rusty sword. Still sharp enough.", aliases: ["sword"]);
sword.MoveTo(square);
ObjectRegistry.SaveObjects();
```

Verify in-game with `look`:

```text
> look
Town Square
A cobbled square with a dry fountain at its center.
You see: rusty sword
```

`AddTags`/`RemoveTags`/`HasTags` manage lookup tags. `HasTag(tag)` checks one; `HasTags(tags, all: true)` requires all of them. `Aliases` is a settable list used by `Search` alongside the name.

## 3. Example: containment — put the sword in a chest

Standard item verbs (`get`, `put`, `give`, `drop`) already handle put/get/give with no custom code. For code-driven placement, move the object into its container:

```csharp
var chest = GameObject.Create("wooden chest", "A sturdy wooden chest with iron bands.");
chest.IsContainer = true;
chest.MoveTo(square);

var sword = square.Search("rusty sword").FirstOrDefault();
if (sword is not null)
    sword.MoveTo(chest);
```

```text
> look chest
A sturdy wooden chest with iron bands.
Inside you see: rusty sword
> get sword from chest
You picked up: rusty sword.
```

`#id` references are plain-only: `get #12` resolves object 12, but signed or spaced forms do not resolve. Containment nests arbitrarily (a pouch in a chest in a room), and `Contents` on each object lists what is directly inside it.

## 4. Example: find things

`Search(string query, bool recursive = true, GameObject? looker = null)` returns a `List<GameObject>` from the caller's point of view. `ContentUtils.Search()` implements the order/term-split: it splits the query into terms and matches name, then singular/plural forms, then aliases, searching local contents before widening.

```csharp
// What "get sword" does under the hood, roughly:
List<GameObject> hits = room.Search("sword", looker: character);
if (hits.Count == 0)
    character.Msg(CommandHelpers.FormatCouldNotFind("sword"));
else if (hits.Count > 1)
    character.Msg($"Multiple matches for 'sword'.");
else
    hits[0].MoveTo(character);
```

Expected in-game output on a miss:

```text
> get halberd
Could not find 'halberd'.
```

The string comes from `CommandHelpers.FormatCouldNotFind`, which returns `Could not find '{name}'.`. A different helper, `CommandHelpers.FormatNoMatchFound`, returns `No match found for '{name}'.` — used where the match step (not the search step) fails. Keep the messages byte-identical; game code and tests pin them.

## 5. Example: move a character

`MoveTo` has overloads for `GameObject?`, `Coord`, `LocationRef`, and `object?` destinations (there is no `MoveToNode` — pass the node object to the `GameObject?` overload). Guards surface as user outcomes:

```csharp
// North Road: the second tutorial room, north of Town Square.
var road = GameObject.Create("North Road", "A dusty road leading north out of town.");
road.MoveTo(square); // placeholder placement; real rooms are nodes (see next doc)

// Walk the character north through a linked exit.
bool moved = character.MoveTo(road, caller: character, announce: true);
if (!moved)
    character.Msg("You cannot go that way.");
```

Expected output on success (room announce fan-out notifies both sides):

```text
> north
North Road
A dusty road leading north out of town.
```

Guards explained as outcomes: a blocked exit stops the move with a message; grafted-origin (object mid-move bookkeeping) aborts rather than duplicating; cross-area moves between map areas are refused. Direction lookup on doors and links is case-insensitive (`OrdinalIgnoreCase`), so `North`, `NORTH`, and `north` all match.

## 6. Example: talk — Msg and Say

`Msg(string? text, ...)` sends to one object. `MsgContents(string? text, ...)` sends to everything inside a location (with an optional exclude list) — it is the unified fan-out behind room announcements. `Say` reaches the room; whisper reaches one target.

```csharp
// Greet everyone in the square except yourself.
square.MsgContents($"{character.Name} waves hello!", exclude: [character]);

// A torch flicker only the holder sees.
torch.Msg("Your torch gutters in the wind.");
```

```text
> say Hello, town!
You say, "Hello, town!"
> wave
Bryn waves hello!
```

`$func()` interpolation (actor names, widths) works inside these strings; full detail is in [Hooks, scripts, and text](05-hooks-scripts-text.md).

## 7. Tags, dirty flags, persistence of your objects

Set state through properties so saves pick changes up. `Flags` tracks dirtiness (`IsModified`); `ISettable` is the interface builder `set` verbs use to assign properties by name. Custom game data goes through extras:

```csharp
// Persisted custom field on a torch: hours of oil left.
torch.SetExtraJson("oilHours", System.Text.Json.JsonSerializer.SerializeToElement(6));
if (torch.TryGetExtraJson("oilHours", out var oil))
    torch.Msg($"Oil left: {oil.GetInt32()} hours.");
```

```text
Oil left: 6 hours.
```

`ChannelsSnapshot` returns a copy of the channel ids an object is subscribed to. `ToDto()`/`GetSaveOperation()` produce the persistence form via the subtype factory (`RegisterPersistedSubtype` lets game code register subclasses for round-trip). What of your object gets saved — and when — is covered in [Saves and backups](10-persistence-saves.md).

## 8. Accounts, characters, puppets

The `create` → login → select/puppet flow from the dev's side: an `Account` owns up to `MaxCharacters` characters. A `Session` attaches to a connection; puppeting binds the session to a character so input reaches it. The API is `GameObject.Puppet(Session session, GameObject npc)` with `Unpuppet(Session session)` to release.

```csharp
// Puppet an NPC to speak through it (e.g. a town crier script).
// The owner puppets the target: self-puppeting is refused.
character.Puppet(session, crier);
crier.MsgContents("Hear ye! The north gate is open!");
character.Unpuppet(session);
```

Owner policy: only the owning account (or a superuser) may puppet a character; puppeting the same character twice is guarded by a same-id check; deleting a character unpuppets its sessions first. Session attach/detach is one paragraph of mechanics: attach binds session→character and announces arrival; detach (quit, disconnect, unpuppet) announces departure and parks the character.

## 9. Channels and doors

Player view of channels: subscribe, send, replay history, unsubscribe. Each channel also installs a command named for the channel (lowercased), so `town <text>` sends to the town channel. History is kept per channel (`ChannelHistoryEntry`).

```csharp
// Say to a channel from code (e.g. a town bulletin script).
var town = ObjectRegistry.FilterBy(o => o.IsChannel && o.Name == "town")
    .OfType<Channel>().FirstOrDefault();
town?.Send("The market is open!", speaker);
```

```text
> channel -s -c town
> town Hello?
(town) [04 October, 2026 12:00:00] Bryn: Hello?
> town -r
(town) [04 October, 2026 12:00:00] Bryn: Hello?
> channel -u -c town
```

Subscribe and unsubscribe are silent; sends render as `(name) [<timestamp>] <sender>: <text>`. `channel -l` lists visible channels; `town -r` replays history (`No history available.` when empty).

Doors gate exits with open/locked/remove flows; direction names on doors are case-insensitive like links. `open north`, `unlock north with iron key`, `lock north`, and builder `remove door north` cover the lifecycle.

## 10. Give, drop, and containers in play

```csharp
// Hand the torch to another character; drop it; pick it back up.
var bryn = square.Search("Bryn", looker: character).FirstOrDefault();
if (bryn is not null)
    torch.MoveTo(bryn, caller: character);
```

```text
> give torch to Bryn
You give torch to Bryn.
> drop torch
You dropped: torch.
> get torch
You picked up: torch.
```

Containers nest: a pouch inside the chest still resolves (`get coin from pouch`), and `IsContainer = false` refuses placement with a message rather than losing the object.

## 11. Examine output and descriptions

`Look` renders name, description, visible contents, and exits for rooms. Long descriptions wrap; empty descriptions show just the name line. Gendered text uses the object's gender provider (`IGenderProvider`):

```csharp
crier.Gender = "male";
square.MsgContents($"{crier.Name} rubs his eyes.");
```

```text
Town Crier rubs his eyes.
```

## 12. Delete and teardown

```csharp
// Deleting returns null when a hook refuses (or nothing to delete); otherwise a
// teardown snapshot: contents are re-homed first.
var result = torch.Delete(character);
if (result is null)
    character.Msg("Deletion aborted.");
```

```text
> delete torch
Deleted torch.
```

A delete hook may refuse (the object stays, the caller sees `Deletion aborted.`). Deleting a character unpuppets its sessions first; deleting a node re-homes its contents rather than stranding them. Tombstones remember deleted ids so stale `#id` references fail with `Object not found.` instead of resolving to a recycled object.

## 13. Common mistakes

- Reading the database mid-game: there is no load path outside startup/seed — use the live object.
- Setting fields instead of properties: dirtiness is tracked in property setters, so raw field writes are invisible to the next save.
- Forgetting the reverse link: `AddLink` publishes one direction only; walk back requires the explicit reverse.
- Assuming `#id` forms: only the plain `#12` resolves; signed or spaced forms do not.
- Re-adding removed fallbacks or paraphrasing messages: keep user-visible strings byte-identical.

## 14. Session attach and detach transcript

```csharp
// What login does: character select attaches the session to the chosen
// character atomically (session lock + character lock, one puppet each
// way) via SessionPuppetHelper.TryAttach(connection, character).
bool attached = SessionPuppetHelper.TryAttach(conn, character);
```

```text
> connect bryn s3cretPass
Welcome bryn.
Please select a character to play:
0. Bryn
Enter your choice: 0
Town Square
A cobbled square with a dry fountain at its center.
> quit
Goodbye!
```

Detach reverses it: announce departure, unbind, park the character in its room. A dropped connection runs the same path (drain-on-disconnect), so a crash-close never leaves a ghost puppet behind — re-login re-attaches to the same character.

## 15. Channel history and membership transcript

```csharp
// Subscribe, check membership, read recent history.
character.Subscribe(townChannel);
IReadOnlyList<int> memberOf = character.ChannelsSnapshot;
character.Unsubscribe(townChannel);
```

```text
> channel -s -c town
> town Is the market open?
(town) [04 October, 2026 12:00:00] Bryn: Is the market open?
> town -r
(town) [04 October, 2026 12:00:00] Bryn: Is the market open?
> channel -u -c town
```

History keeps the last N entries per channel (`ChannelHistoryEntry`); leaving does not erase history, rejoining replays nothing automatically — ask with replay (see below). Lock order inside channel code: channel list lock first, then member object locks, never the reverse — game code sending to channels inherits this ordering for free by calling `Send`.

## 16. Tag lookup in play

```csharp
// Tag rooms by region, then find them all.
square.AddTags(["town"]);
road.AddTags(["town"]);
var townRooms = ObjectRegistry.FilterBy(o => o.HasTag("town"));
character.Msg($"Town rooms: {townRooms.Count}.");
bool anyOutdoor = townRooms.Any(o => o.HasTag("outdoors"));
```

```text
Town rooms: 2.
```

Tags are plain strings: add/remove in code or via builder verbs, query with `HasTag`/`HasTags`. They never gate access (that is locks) and never render (that is legend) — they are lookup labels only.

## 17. Door flows transcript

```csharp
var gate = new Door(squareCoord, roadCoord, "north", "south", closed: true, locked: true);
var key = GameObject.Create("iron key", "A heavy iron key.");
key.MoveTo(character);
gate.KeyId = key.Id;
```

```text
> north
You try to open the door, but it won't budge.
> unlock north with iron key
You unlock the door.
> open north
You open the door.
> north
North Road
A dusty road leading north out of town.
```

Lock, unlock (needs the key object), open, close, and builder remove cover the lifecycle; every step announces to the room. Direction names match case-insensitively throughout.

## 18. Under the hood

`ObjectRegistry` mechanics: an id→object map with `Add`/`Remove`, `FilterBy` predicates, bulk fetch by id set, tombstones (deleted ids remembered so stale references fail cleanly) plus resurrection (re-register under a tombstoned id during reload patching), a delete/teardown snapshot (delete runs hooks and can be refused; teardown captures contents to re-home), and `Load`/`SaveObjects` call sites at boot and checkpoint only — never mid-game.

`Flags` dirty tracking plus `ISettable` give the persistence layer its signal: only objects marked modified are written. DTO round-trip shape is `GameObjectDto` plus the subtype factory plus save ops (mechanics in [Saves and backups](10-persistence-saves.md)).

`ContentUtils.Search()` order/term-split: split the query on whitespace, try full-name match, then singular/plural folding, then alias match, local contents first, then recursive contents, then (for `#id`) `ResolveById`/`ResolveObject` from `TargetResolution`. `MoveTo` guards: grafted-origin abort, cross-area refusal, destination resolution through the same resolvers.

Link directedness: `Node.AddLink(NodeLink)` publishes one direction; the reverse must be added explicitly (or use `AddLinkIfAbsent(name, factory)` for idempotent seeding). The link table dedups case-insensitively — adding `North` when `north` exists is refused, not doubled.

Puppet policy: same-id guard (already puppeted → no-op success), owner policy (account owns character or caller is superuser), unpuppet-after-delete (delete tears down puppet bindings first). Channel lock order: channel list lock before member object locks, never the reverse. Node delete-hook-failure rule: if a delete hook refuses, the node and its contents stay put and the caller gets `Deletion aborted.`.

---
Next: [World and maps](04-world-maps.md)
