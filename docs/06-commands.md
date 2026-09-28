# Commands: using and writing them

> Covers: `src/Atheriz.Core/Commands/Command.cs`, `CommandContext.cs`, `CommandDispatcher.cs`, `CommandRegistry.cs`, `CmdSet.cs`, `GameArgumentParser.cs` (+`ParsedArgs`), `ParsedArgKeys.cs`, `CommandError.cs`, `CommandHelpException.cs`, `CommandHelpers.cs`, `Formatting.cs`, `HelpFormatter.cs`, `LoggedInCommand.cs`, `BuilderCommand.cs`, `LagGateCommand.cs`, `BaseChannelCommand.cs`, `ChannelAction.cs`, `CommandPermissions.cs`, `TargetResolution.cs`, `SessionLifecycle.cs` (`ConnectionHelper`+`CreationCooldownHelper`), `IMessageTarget.cs`, `ISessionProvider.cs`, `ILagInfo.cs`, `UnloggedIn/` (12), `LoggedIn/` (12). First half: what you can type. Second half: how to add a verb. Room/link mechanics are in [World and maps](04-world-maps.md); the tutorial teaser is in [Creating a game](02-creating-a-game.md).

## 1. Connection-screen verbs

Before login you get the unlogged-in set. Name rules: at least 3 characters, at most 20 by default (`MaxAccountNameLength` / `MaxCharacterNameLength`); letters, digits, spaces, hyphens, underscores, apostrophes — no leading/trailing or consecutive spaces, and `me` / `here` / `all` are reserved. Passwords need at least 8 by default (`MinPasswordLength`). Failures arrive as plain messages (`Name must be at least 3 characters.`, `Password must be at least 8 characters.`, `That name is reserved.`).

| Command | Who | Args | What happens |
|---|---|---|---|
| `connect` | anyone | `<account> <password>` | logs in, shows character select |
| `create` | anyone | `<account> <password>` | creates the account, then starts character creation |
| `guest` | anyone | `<name>` | creates a guest character (`Guest {name} created.`) |
| `new` | logged-in account | `<name>` | adds a character (up to `MaxCharacters`) |
| `quit` | anyone | — | `Goodbye!`, closes the connection |
| `help` | anyone | `[topic]` | shows help text |
| `screenreader` | anyone | `on\|off` | toggles screen-reader-friendly output |

## 2. In-game groups

Info (look/help/map/mapedit):

| Command | Who | Args | What happens |
|---|---|---|---|
| `look` | player | `[target]` | shows the room (or examines a target); the room block ends with `Exits: …` |
| `help` | player | `[topic]` | shows help text |
| `map` | player | — | toggles your personal map display (`Map enabled.` / `Map disabled.`) |
| `mapedit` | builder | — | opens the AtheriZ Draw editor in a new browser tab |

There is no `exits` verb — the exit list comes from `look`. There is no `draw` verb — the editor opens via `mapedit`.

Movement (dynamic exit verbs, follow):

Each link installs its own one-word exit verb (a hidden per-direction command), so `north` works only where a `north` exit exists. Single letters `n/s/e/w/u/d` are in `NoAliasCommands` and always mean movement, never an alias.

| Command | Who | Args | What happens |
|---|---|---|---|
| `<exit-name>` | player | — | walks the linked exit |
| `follow` | player | `<who>` | follow them (`You start following Bryn.` to you, announced to the room) |
| `unfollow` | player | — | `You stop following.` (`You aren't following anyone.` when idle) |
| `nofollow` | player | — | toggles whether others may follow you |

Items (get/put/give/container):

| Command | Who | Args | What happens |
|---|---|---|---|
| `get` | player | `<thing> [from <container>]` | picks up (needs the `get` lock on the container/room; `You picked up: {name}`) |
| `put` | player | `<thing> in <container>` | places into a container (`You put {thing} in {container}.`) |
| `give` | player | `<thing> to <who>` | hands to another character (`You give {thing} to {who}.`) |
| `drop` | player | `<thing>` | drops into the room (`You dropped: {name}`) |
| `inventory` | player | — | `You are carrying: …` (`You are carrying nothing.` when empty) |

Socials:

| Command | Who | Args | What happens |
|---|---|---|---|
| `say` | player | `<text>` | `You say, "{text}"` to you, `{name} says, "{text}"` to the room |
| `emote` | player | `<text>` | `{name} {text}` to you and the room |
| `socials` | player | — | lists the social commands |
| `wave` | player | — | waves (the tutorial custom verb from [Creating a game](02-creating-a-game.md)) |

There is no `whisper` verb — one-target speech exists only as the `AtSayFull(…, whisper: true)` hook path for game code.

Communication (channels — there are no `join` / `leave` / `history` verbs):

| Command | Who | Args | What happens |
|---|---|---|---|
| `channel -s <name>` | player | — | subscribes (silent) |
| `channel -u <name>` | player | — | unsubscribes (silent) |
| `channel -l` | player | — | lists available channels |
| `channel -c` | player | — | shows your subscriptions |
| `<channel> <text>` | player | — | sends to the channel |
| `<channel> -r` | player | — | replays your recent entries (`No history available.` when empty) |

Building (entry points — the how is in [World and maps](04-world-maps.md)):

| Command | Who | Args | What happens |
|---|---|---|---|
| `build` | builder | `room\|road\|path …` + `-n -e -s -w -u -d -x …` | creates rooms, links, doors, nouns (full flags in [World and maps](04-world-maps.md)) |
| `mapedit` | builder | — | opens the AtheriZ Draw editor in a new browser tab |
| `set` | builder | `<target> <attr> <value>` | sets properties via `ISettable` (`Set {name}.{attr} = {json}`) |
| `examine` | builder | `<target>` | `Examining {name} (#{id}):` plus attribute rows |
| `delete` | builder | `<target>` | `Deleted {name}.` (a hook may refuse → `Deletion aborted.`) |
| `create` | builder | `<name>` | `Created object '{name}' (ID: {id}).` |
| `desc` / `noun` / `move` / `maze` | builder | … | describe, name, teleport, generate (see [World and maps](04-world-maps.md)) |
| `puppet` / `unpuppet` | builder | `<target>` | attach/detach a puppet |
| `quell` / `unquell` | builder | — | `You are now quelled.` / `You are now unquelled.` |
| `open` / `close` / `lock` / `unlock` | player | `<direction>` | work a door on an exit |

Admin (builder+ only — one-gate rule noted: each of these carries ONE privilege gate, no per-object layers):

| Command | Who | Args | What happens |
|---|---|---|---|
| `ban` | builder | `<name> [--reason …] [--account] [--ip]` | `Banned {name} ({character\|account}…).` |
| `unban` | builder | `<name>` | `Unbanned {name} ({scope}).` |
| `shutdown` | superuser | — | stops the server (saves first) |
| `reload` | superuser | — | announces `Server is reloading...`, hot-loads |
| `save` | superuser | — | checkpoints the world |
| `spam` | superuser | `<text>` | broadcasts to all sessions |

## 3. Targeting from the player's side

What `get sword`, `get #12`, directions, and channel names match: names match first, then singular/plural folding, then aliases; `#id` matches by object id (plain form only — signed or spaced forms do not resolve); a bare direction word walks the exit instead of matching an object; a leading channel name routes to the channel. On failure:

```text
> get halberd
Object not found.
```

That line is `CommandHelpers.MsgObjectNotFound` (`Object not found.`) — the get-miss message. Other verbs use their own miss lines: `CommandHelpers.FormatCouldNotFind` (`Could not find '{name}'.`) for search-step failures and `CommandHelpers.FormatNoMatchFound` (`No match found for '{name}'.`) for match-step failures. Keep each byte-identical.

## 4. Example: a "wave" command

The tutorial verb from [Creating a game](02-creating-a-game.md), repeated here as the authoring reference: a minimal `LoggedInCommand` with the parser off, help text on `Desc`, and explicit registration — `Add(new WaveCommand())`, NEVER an `Assembly`-scan.

```csharp
public sealed class WaveCommand : LoggedInCommand
{
    public override string Key => "wave";
    public override string Desc => "Wave at everyone in the room. Usage: wave";
    public override bool UseParser => false;

    protected override void RunPuppetRaw(GameObject puppet, string raw, CancellationToken ct)
    {
        puppet.Msg("You wave.");
        puppet.ResolveLocationObject()?.MsgContents($"{puppet.Name} waves.", exclude: [puppet]);
    }
}

// Registration: explicit, one line, in your command-set setup.
cmdSet.Add(new WaveCommand());
```

`UseParser => false` matters: with the default parser on, a missing `ParsedArgs` bag prints help instead of running. `ctx.Puppet` is only set on the parser path — `RunPuppetRaw` receives the resolved puppet directly. The room announce excludes the waver so they see only `You wave.` while everyone else sees `{name} waves.`

Try-it transcript:

```text
> wave
You wave.
```

Parser-args variant (a wave with an optional target): keep `UseParser` on, declare the argument in `SetupParser`, read it with `GetString` in `RunPuppet` (`ParsedArgs` has `GetString` / `GetList` / `GetBool` — no `Count`):

```csharp
protected override void SetupParser(GameArgumentParser p)
{
    p.AddArgument(ParsedArgKeys.Target).Nargs("?").Help("Who to wave at.");
}

protected override void RunPuppet(GameObject puppet, GameArgumentParser.ParsedArgs pa, CancellationToken ct)
{
    var who = pa.GetString(ParsedArgKeys.Target);
    puppet.Msg(who is null ? "You wave." : $"You wave at {who}.");
}
```

Subclass decision table, in 5 lines: subclass `Command` for anything (you handle access yourself); `LoggedInCommand` for verbs needing a puppeted character; `BuilderCommand` for builder-only verbs (ships the single `IsBuilder` gate); `LagGateCommand` wraps another command with a gate predicate (gated callers are silently dropped); `BaseChannelCommand` for channel verbs (ships channel precedence, parser reset, permission messages, silence handling).

## 5. Input lifecycle

One ASCII pipeline:

```text
raw text → tokenize (shlex) → dispatch → alias expand → parser → Run(ctx) → Msg
```

Tokenize splits respecting quotes; dispatch finds the command (global registry + per-object `CmdSet` sorted snapshot); alias expansion rewrites known aliases — except single-character `n/s/e/w/u/d`, which are in `NoAliasCommands` and always mean movement, never an alias; the parser fills `ParsedArgs` (dest derivation, nargs, keyword split, remainder drain, typed option pairs keyed by `ParsedArgKeys`); `Run` executes; `Msg` delivers. Argument parsing outcomes: missing required args print usage; unknown options print the error; `--help` throws `CommandHelpException` (a `CommandError` subclass) to show help instead of running.

## 6. Example: a builder-only "smite" command

Builder-only means ONE privilege gate (`BuilderCommand` ships the single `IsBuilder` check) and no per-object lock layers:

```csharp
public sealed class SmiteCommand : BuilderCommand
{
    public override string Key => "smite";
    public override string Desc => "Smite a target. Usage: smite <who>";

    protected override void SetupParser(GameArgumentParser p)
    {
        p.AddArgument(ParsedArgKeys.Target).Nargs("?").Help("Who to smite.");
    }

    protected override void RunPuppet(GameObject puppet, GameArgumentParser.ParsedArgs pa, CancellationToken ct)
    {
        var query = pa.GetString(ParsedArgKeys.Target);
        if (string.IsNullOrWhiteSpace(query))
            throw new CommandError("Smite whom?");
        var target = CommandHelpers.ResolveObject(puppet, query);
        if (target is null)
            throw new CommandError(CommandHelpers.FormatCouldNotFind(query));
        target.Msg("A bolt from above strikes you!");
    }
}

// Registration stays explicit.
cmdSet.Add(new SmiteCommand());
```

`CommandError` is an `Exception` subclass: dispatch catches it, messages the text, then prints usage. (`CommandHelpException` extends it for `--help`.) Target lookup goes through the public `CommandHelpers.ResolveObject` (null on miss) — the tuple-returning `TargetResolution.ResolveObject` beside it is internal to the engine.

```text
> smite goblin
A bolt from above strikes you!
> smite
Smite whom?
```

## 7. Example: channel send from the client

```text
> channel -s town
> town The market is open!
(town) [12:00:01] Bryn: The market is open!
> town -r
(town) [12:00:01] Bryn: The market is open!
```

Lines render as `({channel}) [{timestamp}] {speaker}: {text}`. History replays your own recent entries through the same format. A leading channel name routes to the channel even if an object shares the name. Permission messages are fixed strings (`You do not have permission to send to this channel.`, `You do not have permission to view this channel.`); subscribe/unsubscribe are silent.

## 8. Parser and error outcomes

`GameArgumentParser` fills `ParsedArgs` keyed by `ParsedArgKeys`: dest derivation (option name → key), nargs (how many values each option takes), keyword split (`SplitOnFirstKeyword` for `from`/`to`/`with`), remainder drain (leftover tokens stay positional), typed option pairs (`--count 3` arrives typed).

```csharp
var parser = new GameArgumentParser();
parser.AddArgument(ParsedArgKeys.Target).Help("Who to wave at").Nargs("?");
parser.AddArgument(ParsedArgKeys.Message).Help("What to say").Nargs("*");
```

`AddArgument` returns a builder (`.Help(…)` / `.Nargs("?")` / `.Action(…)` chain). Commands declare theirs in `SetupParser`; dispatch parses the raw tokens and hands `RunPuppet` a filled `ParsedArgs` (dest derivation, nargs, keyword split via `SplitOnFirstKeyword` for `from`/`to`/`with`, remainder drain, typed option pairs like `--count 3`).

```text
> wave --help
<the Desc-based help for wave: description, aliases, usage>
> get
<usage for get>
> help nosuchverb
Command not found.
```

Creation-cooldown outcomes (one line each): too-fast account creation is rejected with a wait-and-retry message (`CreationCooldownHelper` reserve/clear/apply around the creation path); quiet-close drops dead connections without a farewell (`ConnectionHelper`).

## 9. Connection-screen transcript

```text
Welcome to MyGame!
> create alice s3cretPass
Account alice created.
limbo
You are in a vast nothingness.
> quit
Goodbye!
> guest Gwen
Guest Gwen created.
limbo
You are in a vast nothingness.
```

`create` takes account + password only — the character is made in the flow that follows. `quit` always ends `Goodbye!`. The `limbo` room (name plus `LIMBO_DESC`) is the fresh-game start; the smoke-test oracle from [Getting started](01-getting-started.md).

Name/password rules in one paragraph: account and character names need at least 3 characters and at most 20 by default (letters, digits, spaces, hyphens, underscores, apostrophes; `me` / `here` / `all` reserved); passwords need at least 8 by default; too-fast repeated creation attempts hit the cooldown and must wait before retrying (`CreationCooldownHelper`).

## 10. Alias table and help output

| Input | Expands to | Note |
|---|---|---|
| `n/s/e/w/u/d` | movement | in `NoAliasCommands` — never an alias, always walks |
| `l` | `look` | alias |
| `i` | `inventory` | alias |
| `'` | `say` | alias |
| `:` | `emote` | alias |
| `?` | `help` | alias |
| `exam` / `ex` / `exa` | `examine` | aliases |
| `exit` / `logout` / `disconnect` | `quit` | aliases |
| `q` | `quell` | alias |
| `unq` | `unquell` | alias |

Help output (bare `help` prints the formatted command table; `help <verb>` prints that command's `Desc`-based help):

```text
> help wave
<wave's Desc, aliases, and usage>
> help
<formatted table of every visible command, plus "Local commands:" when present>
> help nosuchverb
Command not found.
```

Unknown verbs never reach help-text guessing: unlogged-in dispatch answers `Command "{text}" not found.` (with a did-you-mean suggestion when close), logged-in help answers `Command not found.` Object-search misses are a different layer: `CommandHelpers.FormatNoMatchFound` (`No match found for '{name}'.`) is the match-step failure (e.g. `look` at no such target), distinct from the search-step `Could not find '{name}'.`.

Exam/set/delete/ban transcripts (builder verbs, one gate each):

```text
> examine sword
Examining rusty sword (#12):
  desc: A rusty sword. Still sharp enough. (str)
> set sword desc A gleaming blade.
Set sword.desc = "A gleaming blade."
> delete goblin
Deleted goblin.
> ban goblin --reason griefing
Banned goblin (character, reason: griefing).
```

## 11. Lag gate and dispatch errors

```csharp
// Operators can install a lag check; gated commands are silently dropped.
CommandDispatcher.LagCheck = caller => serverTooLaggy;
```

```text
> north
<no reply when gated — the lag gate drops the command silently>
> frob
Command "frob" not found.
> get
<usage for get>
```

Dispatch error table: unknown verb → `Command "{text}" not found.` (plus did-you-mean when close); known verb with bad args → usage line; `--help` → help text via `CommandHelpException` (never runs `Run`); lag-gated → silent drop (`LagGateCommand` / `GlobalLagCheck` / `CommandDispatcher.LagCheck` all share the predicate shape); `quit` on a dead connection → quiet close (`ConnectionHelper`), no farewell. `CmdSet` precedence: per-object commands beat global ones at the same key, sorted snapshot per dispatch so mid-dispatch registration never corrupts the walk.

## 12. Movement and speech transcripts

`look` carries the exit list — there is no separate `exits` verb:

```text
> look
Town Square
A cobbled square with a dry fountain at its center.
Exits: north, east
> north
North Road
A dusty road leading north out of town.
Exits: south
> south
Town Square
A cobbled square with a dry fountain at its center.
```

(`wander` is not a walk verb — it is a builder command that spawns wandering NPCs: `Spawned {n} NPCs across area '{area}' in … milliseconds`.)

```text
> say Hello, town!
You say, "Hello, town!"
> emote grins.
Bryn grins.
> town The market is open!
(town) [12:00:01] Bryn: The market is open!
```

`say` reaches the room through `AtSay`/`AtSayFull` (self text vs room text, hookable); emote routes through the same entry with the literal `{name} {text}` for both audiences. A leading channel name routes to the channel instead. Item flows check the `get` lock and announce to the room:

```text
> get torch
You picked up: torch
> inventory
You are carrying: torch
> drop torch
You dropped: torch
> give torch to Bryn
You give torch to Bryn.
```

Follow and ban transcripts (helpers live beside the verbs):

```text
> follow Bryn
You start following Bryn.
> unfollow
You stop following.
> ban goblin --reason griefing
Banned goblin (character, reason: griefing).
```

Registration recap (the rule, quoted): game commands register with explicit `Add(new XxxCommand())` — never an assembly scan, and no `System.Reflection` in game code. Keep user-visible messages byte-identical; do not re-add removed fallbacks.

## 13. Under the hood

`Command.cs`: per-command parser setup + shlex-style tokenizing (mirrors `shlex.split`) + `Execute` → `Run(ctx)`; `CommandError` surfaces its message plus usage, `CommandHelpException` (a `CommandError`) shows help instead of running. `CommandContext`: `Caller`, `Puppet`, `Args`, `RawText`, `Ct`. `CommandDispatcher`: routing across registries + the `NoAliasCommands` single-char rule (`["n","s","e","w","u","d"]` — a one-character input matching these is always movement) + the `LagCheck` hook (`Func<IMessageTarget,bool>?`, silent drop; same predicate shape as `Command.GlobalLagCheck` / `LagGateCommand`, via `ILagInfo`).

`CommandRegistry` (global command table) + `CmdSet` (per-object sorted snapshot used for dispatch precedence). `GameArgumentParser` + `ParsedArgs` + `ParsedArgKeys`: dest derivation, nargs, keyword split, remainder drain, typed option pairs. `BaseChannelCommand`/`ChannelAction`: channel precedence (channel name beats object name), parser-reset, permission messages, silence handling. `CommandPermissions`: `IsBuilder` / `IsSuperUser` / `HasBuilderPrivilege` (raw level check behind the gates), plus hidden-target gating (unseen targets stay unusable). `TargetResolution`: internal name search behind the verbs — `ResolveById` (`#id` plain-only) / `ResolveObject` (name search from the caller), coord search, comma-coord parsing, global lookup, channel resolution, `SplitOnFirstKeyword` (split a token list on the first keyword occurrence). Game code calls the public `CommandHelpers.ResolveObject` (null on miss) instead.

`HelpFormatter` layout + `CommandHelpers`/`Formatting` message helpers. `CommandError`/`CommandHelpException` (help-as-exception flow). The `SessionLifecycle` file holds `ConnectionHelper` (quiet-close for raw connections) + `CreationCooldownHelper` (reserve/clear/apply around account creation to throttle spam). `ISessionProvider`/`ILagInfo` are the session/lag contracts commands program against.

Keep messages byte-identical; do not re-add removed fallbacks.

---
Next: [Running the server](07-server-lifecycle-cli.md)
