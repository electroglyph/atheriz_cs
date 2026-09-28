# Hooks, scripts, and text

> Covers: `src/Atheriz.Core/Objects/Hooks.cs`, `HookName.cs`, `HookNames.cs`, `HookMarkers.cs`, `HookRegistry.cs`, `DelegateInvoker.cs`, `Script.cs`, `FollowScript.cs`, `LockTable.cs`, `LockEntry.cs`, `LockPolicies.cs`, `FuncParser/` (2 files), `VerbConjugation/Conjugate.cs`, `Pronouns.cs`, `Messaging/GameObjectMessaging.cs`, `Menu.cs` (at the `Atheriz.Core` root), `Root: Privilege.cs`. You learn to react to events, run tick code, gate access, format text, and build multi-step menus; the verbs that check locks are cataloged in [Commands](06-commands.md).

## 1. Example: react to entering a room

Hook sites fire at fixed points (receiving/leaving on move, rendering on look, teardown on delete, speech on say). Entering and leaving run through the receive/leave pair: the destination fires `AtObjectReceive(source, fromExit)` and the mover fires `AtObjectLeave(destination, toExit)` — the handler takes what the site passes (the other side plus the exit name), and the object itself is implicit (the one you installed on). Register a handler on the object with `InstallHook`; query with `HasHook`. There is no per-delegate uninstall — hook removal is script-scoped (`Script.RemoveHooks`, which unwires that script's marker-attributed methods) or whole-registry (`Clear` at delete teardown). A script that no longer applies removes itself the same way `FollowScript` does when its leader is gone.

```csharp
// Greet anyone entering Town Square. Installed on the room: the site calls
// back with (source, fromExit), so the room is implicit, not an argument.
square.InstallHook(HookName.AtObjectReceive, (Action<GameObject?, string?>)GreetOnEnter);

static void GreetOnEnter(GameObject? source, string? fromExit)
{
    square.MsgContents($"The fountain splashes as {source?.Name} arrives.");
    source?.Msg("The fountain splashes a greeting.");
}
```

```text
> north
North Road
A dusty road leading north out of town.
> south
Town Square
A cobbled square with a dry fountain at its center.
The fountain splashes a greeting.
```

Move/look hook site table: which hooks fire when —

| Site | When it fires | Typical use |
|---|---|---|
| `AtObjectReceive` / `AtObjectLeave` | object arrives into / departs a location | greetings, traps, tolls |
| `AtPreObjectReceive` / `AtPreObjectLeave` | before the receive/leave commits (bool sites) | refuse an arrival, log travel |
| `AtPreMove` / `AtPostMove` | before/after `MoveTo` commits | refuse a move, log travel |
| `AtLook` / `AtDesc` | object is looked at / its description renders | dynamic descriptions |
| `AtDelete` | object is being deleted | refuse, rescue contents |
| `AtSay` / `AtPreSay` | object speaks | log speech, transform text |

Remove-by-identity note: keep the delegate reference and uninstall that same instance; a freshly constructed equal-looking lambda will not match. Hook name sources come in two parallel forms — the `HookName` enum and the `HookNames` static class — and both resolve to the same sites.

## 2. Example: a ticking object (torch that burns down)

`Script` binds tick code to an object; lookup finds scripts by type. The torch from the tutorial burns one oil-hour per tick and gutters out at zero.

```csharp
public sealed class BurningTorch : Script
{
    public override void AtTick()
    {
        // Child is the object this script is installed on (the torch):
        // AddScript wires InstallHooks, which records it.
        var torch = Child;
        if (torch is null) return;
        int oil = torch.TryGetExtraJson("oilHours", out var el) ? el.GetInt32() : 6;
        oil--;
        torch.SetExtraJson("oilHours", System.Text.Json.JsonSerializer.SerializeToElement(oil));
        if (oil <= 0)
        {
            torch.ResolveLocationObject()?.MsgContents($"{torch.Name} gutters out.");
            torch.Delete();
        }
    }
}

torch.AddScript(new BurningTorch());
```

```text
> look torch
A sputtering torch. The flame is low.
... (ticks pass) ...
Your torch gutters out.
```

Follow-script pointer: `FollowScript` is the specialized script that moves an object after its target (pets, guards, trailing NPCs). See [Commands](06-commands.md) for the player-facing follow verbs.

## 3. Locks on your objects

Named locks gate per-object access; declarative policies ship common shapes in one table. The one-gate rule, quoted: builder-only commands get ONE privilege gate, no per-object layers. Builders are trusted — one gate per command, keep it simple:

```csharp
// Anyone may look; only builders may take the display sword.
displaySword.AddLock("get", accessing => accessing.IsBuilder, LockPolicies.LockPolicy.Builder);
```

| Policy | Meaning |
|---|---|
| `PcView` | connected players may view |
| `Builder` | builders and above |
| `Custom` | your predicate decides |

Which lock names the built-in verbs check (`get`, `view`, `control`, …) is listed in the [Commands](06-commands.md) catalog, not duplicated here — check there before inventing a new lock name.

## 4. Example: $func() text and verbs

`$func()` interpolation renders actor-aware text inside `Msg` strings. The registered funcs are the text helpers (`eval`, `an`, `pad`, `crop`, `pluralize`, `random`, …) plus the actor stances (`$you`, `$your`, `$conj`, `$pconj`, `$pron`), which render relative to the speaker and the receiver — outcomes, not internals:

```csharp
// "wave" social: actor sees one string, the room sees another.
// $You(actor) / $conj(wave) render per receiver: "You wave" for the actor,
// "<name> waves" for everyone else (same shape as the follow announce).
actor.Msg("You wave your torch high.");
actor.ResolveLocationObject()?.MsgContents(
    "$You(actor) $conj(wave) a sputtering torch.",
    fromObj: actor,
    mapping: new Dictionary<string, object?> { ["actor"] = actor });
```

```text
> wave
You wave your torch high.
Bryn waves a sputtering torch.
```

`FuncParser` plus helpers resolve `actor`, width hints, and reserved-name precedence, with a safe arithmetic evaluator (`$eval(1+2*3)` renders `7`) under a depth cap. `Conjugate` handles tense/person normalization; `Pronouns` holds the case tables (`he/him/his`, `she/her`, `they/them`) behind the `$pron` stance, read from the speaker's gender provider (`IGenderProvider`).

## 5. Example: a multi-step menu

`MenuContext` carries per-run state (`State` dictionary), `Choice` is one option, `MenuEngine` drives the steps — all three live in `Menu.cs` at the `Atheriz.Core` root. Use menus for confirm-before-delete and other multi-step input.

```csharp
var menu = new MenuEngine(character, GreetNode);

static Task<(string, List<Choice>)> GreetNode(MenuContext ctx)
{
    var choices = new List<Choice>
    {
        new("1", "Ring the chapel bell", callback: async c =>
        {
            square.MsgContents("The chapel bell tolls.");
            await Task.CompletedTask;
        }),
        new("2", "Leave quietly"),
    };
    return Task.FromResult(("Ring the bell?", choices));
}

await menu.RunAsync(character);
```

```text
Ring the bell?
  [1] Ring the chapel bell
  [2] Leave quietly
> 1
The chapel bell tolls.
```

Connection banner plus `Privilege` ladder in one paragraph: on connect the server shows the banner, then login; privilege runs Guest → Player → Helper → Builder → Admin, and builder-only verbs check one gate against it.

## 6. Hook install/remove lifecycle

```csharp
// Install returns nothing; HasHook reports whether a site has handlers.
square.InstallHook(HookName.AtObjectReceive, (Action<GameObject?, string?>)GreetOnEnter);
bool had = square.HasHook(HookName.AtObjectReceive);
```

```text
> examine square
Town Square
```

A handler declaring the wrong number of arguments is refused at install (logged and skipped), not at fire time. Bool-returning sites honor replacement results where the site allows refusal (delete, pre-move); handlers are partitioned per object — one object's handlers never leak into another's. Game-defined custom hooks bypass install-time validation: name them with a plain string and define their own shapes.

## 7. Example: a follower (FollowScript)

Following is leader-side state, not a pet object: `follow <target>` records your id in the leader's follower set and installs one parameterless `FollowScript` on the leader, which moves every follower along on post-move. `FollowScript` is sealed — custom trailing behavior is a separate `Script` subclass on the leader using the same `[After] at_post_move` hook shape, reading `Child` (the leader it is installed on):

```csharp
// Installed on the leader alongside FollowScript. Child is the leader;
// FollowersSnapshot holds the follower ids that move along after it.
public sealed class GuardDogAnnounce : Script
{
    [After]
    public void at_post_move(GameObject? destination, string? toExit = null)
    {
        var leader = Child;
        if (leader is not null)
            leader.ResolveLocationObject()?.MsgContents($"{leader.Name}'s dog trots after them.");
    }
}
```

```text
> follow bryn
You start following Bryn.
> north
North Road
A dusty road leading north out of town.
A scruffy dog trots after you.
```

```text
> north
North Road
A dusty road leading north out of town.
A scruffy dog trots after you.
```

## 8. Deny-by-predicate and pronoun outcomes

A lock predicate returns false to deny — a predicate that throws also denies (the throw is swallowed, so keep predicates total and put player-facing text in the verb, not the predicate):

```csharp
chest.AddLock("get", accessing => accessing.IsBuilder, "custom");
```

```text
> get sword from chest
Could not find 'sword'.
```

(The `from` keyword splits target and container; a denied lock reads as absent from that angle.)

Pronoun outcomes by gender provider (`they` default), rendered through the `$pron` actor stance (backed by the `Pronouns` case tables, read from the speaker's gender provider via `IGenderProvider`):

```csharp
// "Bryn (they) drops their torch." vs "Crier (he) drops his torch."
// $pron takes the pronoun plus options; a trailing mapping key selects
// whose gender table applies (same convention as $conj's actor key).
actor.ResolveLocationObject()?.MsgContents(
    "$You(actor) $conj(drop, actor) $pron(they, actor) torch.",
    fromObj: actor,
    mapping: new Dictionary<string, object?> { ["actor"] = actor });
```

```text
Bryn drops their torch.
```

## 9. Connection banner transcript

```text
Welcome to MyGame!
connect <account> <password> | create <account> <char> <password> | guest
> connect bryn s3cretPass
Welcome Bryn.
Town Square
A cobbled square with a dry fountain at its center.
```

Privilege ladder: Guest (minimal verbs) → Player (standard play) → Helper → Builder (build/admin verbs, one gate each) → Admin (shutdown, spam). Builder-only verbs carry exactly one privilege gate — no per-object lock layers on top.

## 10. Under the hood

### Lock names the built-in verbs check

Do not invent a new lock name until you have read this table — the verbs check fixed names ([Commands](06-commands.md) owns the full catalog):

| Lock | Checked by | Deny outcome |
|---|---|---|
| `get` | get/take verbs | `Could not find` or a deny line |
| `view` | look/examine on concealed things | hidden from output |
| `control` | puppet/climb-into verbs | refused with a message |

### Menu validation and cancel transcript

```csharp
var confirm = new MenuEngine(character, ConfirmDeleteNode);

static Task<(string, List<Choice>)> ConfirmDeleteNode(MenuContext ctx)
{
    var choices = new List<Choice>
    {
        new("yes", "Delete it", callback: c =>
        {
            c.State["confirmed"] = true;
            return Task.CompletedTask;
        }),
        new("no", "Keep it"),
    };
    return Task.FromResult(("Delete the torch?", choices));
}

await confirm.RunAsync(character);
bool ok = confirm.Context.State.TryGetValue("confirmed", out _);
```

```text
Delete the torch?
  [yes] Delete it
  [no] Keep it
> yes
The torch is deleted.
> bogus
Delete the torch?
  [yes] Delete it
  [no] Keep it
```

`MenuContext.State` carries answers between steps; unknown input logs a warning and stays on the same node instead of aborting; closing the menu (or the session) ends it with no further side effects.

### FuncParser reserved names and arithmetic

Reserved names (`actor`, `target`, widths) take precedence over user variables — a game variable named `actor` never shadows the real actor. Arithmetic is a safe evaluator (no method calls, no property access) under a depth cap, so `$calc(1+2*3)` renders `7` and a pathological nesting errors instead of hanging.

### Script bind/lookup API

```csharp
torch.AddScript(new BurningTorch());
bool burning = torch.HasScriptType<BurningTorch>();
List<BurningTorch> all = torch.GetScriptsByType<BurningTorch>();
torch.RemoveScript(all[0]);
```

Bind by instance or id, look up by type or type-name string, remove by instance or id. Ticks re-register after load so saved torches keep burning across restarts.

### Hook markers and argument counts

| Marker | Meaning |
|---|---|
| 1-arg | site passes the subject only (look, cache) |
| 2-arg | site passes subject + other (enter, leave, move) |
| can-refuse | false/null-opinion respected (pre-move, delete) |

Installing a 1-arg handler on a 2-arg site is rejected at install with an explanatory error — the mismatch never surfaces mid-game.

### Broadcast mapping example

```csharp
// World broadcast reuses MsgContents delivery with a mapping function.
var online = ObjectRegistry.FilterBy(o => o.IsConnected && o.IsPc);
foreach (var pc in online)
    pc.Msg($"[spam] {announcement}", mapping: new Dictionary<string, object?> { ["from"] = square });
```

```text
[spam] The server restarts in 5 minutes.
```

One delivery path (`Msg` → `Hear` → announce fan-out) serves direct messages, room speech, channel sends, and world broadcasts; only the target set and the mapping differ.

### Privilege ladder transcript

```text
> set sword desc shiny
Command "set" not found.
```

Guests see the smallest verb set; players the standard set; helpers sit between; builders gain build/admin verbs behind their single gates; admins gain shutdown and broadcast. A verb the caller may not use is skipped at dispatch, so it reads exactly like an unknown verb instead of a permission lecture. One gate per command — a builder verb never stacks a lock check on top of its privilege check. `help` lists only verbs the caller may actually use: hidden or inaccessible commands are never suggested, so a typo cannot oracle their names.

Registration recap (the rule, quoted): game commands register with explicit `Add(new XxxCommand())` — never an assembly scan, and no `System.Reflection` in game code. Dispatch uses `switch`/`is` pattern matching and typed lists instead.

Threading recap (one line): hook and tick handlers run on pool/ticker threads — keep them short, never block or sleep, mutate the world only through the documented `GameObject` and command APIs. Locking rules live in [Engine internals](13-engine-internals.md).

Where to go next: [Commands](06-commands.md) owns the verb catalog and the permission call sites behind the one-gate rule; [World and maps](04-world-maps.md) owns tick re-registration and alarms; [Custom code and hot reload](11-custom-code-plugins.md) owns the never-touch list (no assembly scan, no production reflection, explicit `Add`).

Hook-site quick index (see §1 table for when each fires): `AtObjectReceive`, `AtObjectLeave`, `AtPreObjectReceive`, `AtPreObjectLeave`, `AtPreMove`, `AtPostMove`, `AtLook`, `AtDesc`, `AtDelete`, `AtSay`, `AtPreSay`, `AtTick`. Both the `HookName` enum and the `HookNames` static class name the same sites — use whichever reads better at the call site.

Conventions recap: `$func()` for actor-aware text, `Conjugate` for verbs, `Pronouns` for case, `MenuContext`/`Choice`/`MenuEngine` for multi-step input, `IMessageTarget` as the receiver contract. Text helpers never touch the database; they render from live objects only.

Small glossary: a hook *site* is where the engine fires; a *handler* is your function; *partition* means one object's handlers never leak into another's.

### Hood (continued)

`LockTable`/`LockEntry`/`LockPolicies`: named locks per object, declarative policies, deny-by-predicate (false — or a throw, which is swallowed — denies; player-facing text belongs in the verb), restore-on-load (locks persist via DTO and are rebuilt at load).

`HookName`/`HookNames`/`HookMarkers`/`HookRegistry`/`DelegateInvoker`: dispatch fan-out to installed handlers, install-time refusal of wrong-argument-count handlers (a handler declaring the wrong number of arguments for a known site is logged and skipped at install, never mid-game), partition (hooks partitioned per object so one object's handlers never leak into another's); hook sites: receive/leave, move, look/desc, say, delete, tick as tabulated in §1.

`GameObjectMessaging.Msg()` + `Hear`: delivery to one target, location-announce fan-out on enter/leave, `MsgContents` unification (single code path for "tell everyone here"), broadcast mapping (channel/world sends reuse the same delivery with a mapping function).

`FuncParser.cs` + helpers: actor/width/reserved-precedence resolution, safe arithmetic evaluation with a depth cap. `Conjugate.cs`/`Pronouns.cs`: tense/case tables, person normalization. `IMessageTarget` contract: anything that can receive a message (`Msg`) and be checked for privilege — connections, sessions, and game objects all implement it.

Threading note, one paragraph: game code runs on pool/ticker threads — keep handlers short, never block or sleep, and mutate the world only through the documented `GameObject`/command APIs. Locking rules and the never-touch list (no `Assembly`-scan, no `System.Reflection` in game code, explicit `Add` registration with `switch`/`is` dispatch) are detailed in [Custom code and hot reload](11-custom-code-plugins.md).

---
Next: [Commands: using and writing them](06-commands.md)
