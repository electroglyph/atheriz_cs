using Atheriz.Core.Commands.UnloggedIn;
using Atheriz.Core.Network;
using Atheriz.Core.Persistence.Dto;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text;

namespace Atheriz.Core.Commands.LoggedIn;

public sealed class DrawCommand : Command
{
    public override string Key => "mapedit";
    public override string Desc => "Open the AtheriZ map editor in a new browser tab.";
    public override string Category => "Building";
    public override bool UseParser => false;
    public override bool Access(IMessageTarget caller) => CommandPermissions.IsBuilder(caller);

    // Wire options for legend entries: the shared persistence options drop
    // nulls, but the launch_draw payload historically carries explicit nulls.
    private static readonly JsonSerializerOptions LegendWireOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public override void Run(CommandContext ctx)
    {
        var caller = ctx.Caller;
        // Typed (F001): GameObject carries Location/Session; Session.Connection is
        // typed BaseConnection (ClientHost/SendCommand). A raw BaseConnection caller
        // resolves via its session puppet. No reflection.
        GameObject? go = caller as GameObject;
        if (go is null && caller is Atheriz.Core.Network.BaseConnection bc0 && bc0.Session.Puppet is GameObject pgo)
            go = pgo;
        Node? loc = go?.ResolveLocationObject() as Node;
        if (loc is null)
        {
            caller.Msg("You must be in a valid location to open the map editor.");
            return;
        }
        // session/connection — prefer the caller's own session (a connection
        // caller stays live even when its puppet's session link is stale),
        // falling back to the resolved puppet's session.
        Session? session = (caller as ISessionProvider)?.Session ?? go?.Session;
        Atheriz.Core.Network.BaseConnection? conn = session?.Connection;
        if (conn is null)
        {
            caller.Msg("No active connection.");
            return;
        }
        string area = loc.Coord.Area;
        int z = loc.Coord.Z;
        MapHandler mh;
        try { mh = GlobalServices.GetMapHandler(); } catch { mh = new MapHandler(autoLoad:false); }
        var mi = mh.GetMapInfo(area, z);
        if (mi is null)
        {
            mi = new MapInfo(area);
            mh.SetMapInfo(area, z, mi);
        }
        string ip = conn.ClientHost ?? "?";

        string key = MapEdit.Grant(ip, area, z, session);
        string rawSym = go?.Symbol ?? "X";
        if (string.IsNullOrEmpty(rawSym)) rawSym = "X";
        string plain = GameUtils.StripAnsi(rawSym);
        plain = plain.Trim();
        if (string.IsNullOrEmpty(plain)) plain = "X";
        if (plain.Length > 2) plain = plain.Substring(0, 2);

        Dictionary<string, object?> payload = new()
        {
            ["area"] = area,
            ["z"] = z,
            ["grid"] = new List<List<object?>>(),
            ["rooms"] = new List<Dictionary<string, object?>>(),
            ["legend"] = new List<JsonElement>(),
            ["playerSymbol"] = plain
        };
        if (mi.PreGrid.Count > 0)
        {
            mi.PreRender();
        }
        // Snapshot copy under the info read lock — safe to enumerate lock-free.
        List<KeyValuePair<(int X,int Y), string>> gridSnap = mi.PostGrid.ToList();
        NodeHandler? nh = null;
        try { nh = NodeHandler.GetCurrent() ?? GlobalServices.GetNodeHandler(); } catch (Exception) { }
        NodeArea? areaObj = nh?.GetArea(area);
        NodeGrid? nodeGrid = areaObj?.GetGrid(z);

        // helper to build room payload
        Dictionary<string, object?> RoomPayload(Node node)
        {
            List<Dictionary<string, object?>> exits = [];
            foreach (var link in node.GetLinks())
            {
                if (link.Coord.Equals(default)) continue;
                // link.Coord may be default if null? Check
                exits.Add(new Dictionary<string, object?>
                {
                    ["name"] = link.Name,
                    ["aliases"] = new List<string>(link.Aliases),
                    ["coord"] = new List<object?>{ link.Coord.Area, link.Coord.X, link.Coord.Y, link.Coord.Z }
                });
            }
            return new Dictionary<string, object?>
            {
                ["x"] = node.Coord.X,
                ["y"] = node.Coord.Y,
                // The editor's name field, empty when unset — never the
                // coord-derived Name, so blank stays blank on screen.
                ["name"] = node.DisplayName,
                ["desc"] = node.Desc,
                ["exits"] = exits
            };
        }

        HashSet<(int,int)> seen = [];
        var gridList = (List<List<object?>>)payload["grid"]!;
        var roomsList = (List<Dictionary<string, object?>>)payload["rooms"]!;
        foreach (var kv in gridSnap)
        {
            gridList.Add(new List<object?>{ kv.Key.X, kv.Key.Y, kv.Value });
            if (nodeGrid is null) continue;
            var node = nodeGrid.GetNode(kv.Key);
            if (node is null) continue;
            seen.Add(kv.Key);
            roomsList.Add(RoomPayload(node));
        }
        if (nodeGrid is not null)
        {
            // Snapshot copy under the grid read lock — safe to enumerate lock-free.
            List<( (int X,int Y) coord, Node node)> extra = new();
            foreach (var kv in nodeGrid.Nodes)
                if (!seen.Contains(kv.Key)) extra.Add((kv.Key, kv.Value));
            foreach (var (coord, node) in extra)
                roomsList.Add(RoomPayload(node));
        }
        // The record serializes directly: camelCase keys plus explicit nulls
        // match the old hand-unrolled dict byte-for-byte on the wire, and the
        // transport writes JsonElement values raw.
        var legendList = (List<JsonElement>)payload["legend"]!;
        foreach (var e in mi.LegendEntries)
            legendList.Add(JsonSerializer.SerializeToElement(e, LegendWireOptions));
        // Previously saved editor settings ride the grant so a fresh editor
        // tab restores colors, slots, palette, font, and tool modes. Stored
        // JSON was normalized at save time; re-validate before embedding so
        // a corrupt account value can never poison the client. Corrupt or
        // absent settings simply open a default editor.
        string? savedSettings = session?.Account?.MapEditorSettingsJson;
        if (!string.IsNullOrEmpty(savedSettings))
        {
            try
            {
                using var settingsDoc = JsonDocument.Parse(savedSettings);
                if (Atheriz.Core.Network.MapEditorSettings.TryParse(settingsDoc.RootElement, out _, out _))
                    payload["editorSettings"] = settingsDoc.RootElement.Clone();
            }
            catch (JsonException) { }
        }

        try
        {
            var argsList = new List<object?> { key, payload };
            Dictionary<string, object?> kw = [];
            conn.SendCommand("launch_draw", argsList, kw);
            caller.Msg("Opening AtheriZ Draw in a new tab.");
        }
        catch (Exception) { caller.Msg("Could not open the map editor."); }
    }
}

/// <summary>
/// Mirrors <c>atheriz/commands/loggedin/help.py:HelpCommand</c> (116 LOC).
/// </summary>
public sealed class HelpCommand : Command
{
    public override string Key => "help";
    public override IReadOnlyList<string> Aliases => ["?"];
    public override string Desc => "Show help for commands.";
    public override bool UseParser => true;

    protected override void SetupParser(GameArgumentParser p) { p.AddArgument(ParsedArgKeys.Command, nargs: "?", help: "Command to get help on"); }
    public override void Run(CommandContext ctx)
    {
        var caller = ctx.Caller;
        var pa = ctx.Args;
        string? query = pa?.GetString(ParsedArgKeys.Command)?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(query)) query = ctx.RawText.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(query))
        {
            // mirror help.py:30-38 term_width and screenreader from session
            bool sr = false;
            int tw = 80;
            if (caller is Objects.GameObject goc)
            {
                try { sr = goc.Session?.ScreenReader ?? false; } catch (Exception) { }
                try { tw = (goc.Session?.TermWidth ?? 80) - 2; if (tw < 20) tw = 20; } catch { tw = 80; }
            }
            var all = CommandRegistry.LoggedIn.GetAll().Where(c => !c.Hide && c.Access(caller)).ToList();
            var sb = new StringBuilder("\n" + HelpFormatter.Format(all, sr, tw + 2));
            // local commands from location/inventory (single session lookup above)
            if (caller is Objects.GameObject go)
            {
                // Order-preserving dedup (set for membership, list for order):
                // the default comparer is reference equality here, matching
                // the old ReferenceEquals scan exactly.
                List<Command> locals = [];
                HashSet<Command> seenLocals = [];
                foreach (var set in CommandHelpers.LocalVerbSets(go))
                    foreach (var lc in set.GetAll())
                        if (!lc.Hide && lc.Access(go) && seenLocals.Add(lc))
                            locals.Add(lc);
                if (locals.Count > 0)
                {
                    sb.AppendLine("\nLocal commands:");
                    sb.Append(HelpFormatter.Format(locals, sr, tw + 2));
                }
            }
            sb.AppendLine();
            sb.AppendLine("help <command> for more information");
            caller.Msg(sb.ToString());
            return;
        }
        if (HelpHelper.TryShowGlobal(CommandRegistry.LoggedIn, caller, query!)) return;
        // search local
        if (caller is Objects.GameObject go2)
        {
            foreach (var set in CommandHelpers.LocalVerbSets(go2))
            {
                var c = set.Get(query!);
                if (c is not null && c.Access(go2) && !c.Hide) { caller.Msg(HelpHelper.FormatFor(c)); return; }
                foreach (var cc in set.GetAll()) if (cc.Aliases.Any(a => a.Equals(query, StringComparison.OrdinalIgnoreCase)) && cc.Access(go2) && !cc.Hide) { caller.Msg(HelpHelper.FormatFor(cc)); return; }
            }
        }
        caller.Msg("Command not found.");
    }
}

/// <summary>
/// Mirrors <c>atheriz/commands/loggedin/look.py:LookCommand</c> (72 LOC).
/// </summary>
public sealed class LookCommand : LoggedInCommand
{
    public override string Key => "look";
    public override IReadOnlyList<string> Aliases => ["l"];
    public override string Desc => "Look at your current location or an object.";
    public override string Category => "General";
    protected override bool AllowMissingArgs => true;

    protected override void SetupParser(GameArgumentParser parser)
    {
        parser.AddArgument(ParsedArgKeys.Target, help: "Object to look at.", nargs: "REMAINDER");
    }

    protected override void RunPuppet(GameObject puppet, GameArgumentParser.ParsedArgs parsed, CancellationToken ct)
    {
        var targets = parsed.GetList(ParsedArgKeys.Target);
        if (targets.Count == 0)
        {
            ShowLocation(puppet);
            return;
        }
        var targetName = string.Join(" ", targets);
        // SearchWithFallback covers caller + loc fallback and global #id
        // (same resolution as Exam/Delete via ResolveObject, minus messaging).
        var found = CommandHelpers.SearchWithFallback(puppet, targetName);
        if (found.Count == 0)
        {
            var loc = puppet.ResolveLocationObject();
            if (loc is not null && loc.Access(puppet, "view"))
            {
                // noun/link fallback (not part of SearchWithFallback)
                if (loc is Node node)
                {
                    var resolved = node.TryResolveLookTarget(targetName, puppet);
                    if (resolved is not null) { puppet.Msg(resolved); return; }
                }
                CommandHelpers.MsgNoMatchFound(puppet, targetName);
                return;
            }
            else
            {
                CommandHelpers.MsgNoMatchFound(puppet, targetName);
                return;
            }
        }
        if (found.Count > 1) { CommandHelpers.MsgMultipleMatches(puppet, targetName); return; }
        // Defense in depth : AtLook gates internally, but the call
        // site checks first so a denied target never reaches hooks/rendering.
        if (!found[0].Access(puppet, "view")) { puppet.Msg("You can't see anything."); return; }
        puppet.Msg(puppet.AtLook(found[0]));
    }

    private static void ShowLocation(GameObject puppet)
    {
        var loc = puppet.ResolveLocationObject();
        if (loc is null)
        {
            CommandHelpers.MsgNowhere(puppet);
            return;
        }
        // Single shared gate + render for Node and non-Node locations alike.
        // The old non-Node branch substituted the VIEWER's desc when the
        // appearance was a bare "name:" (empty desc) — echoing self; the port
        // shows caller.at_look(loc) in both tails (look.py:20-30,66-72), so
        // the two gated tails were already identical. Gated like the other
        // paths: a denied container never reaches hooks/rendering.
        if (!loc.Access(puppet, "view")) { puppet.Msg("You can't see anything."); return; }
        puppet.Msg(puppet.AtLook(loc));
    }
}

public sealed class MapCommand : LoggedInCommand
{
    public override string Key => "map";
    public override string Desc => "Toggle map display.";
    public override bool UseParser => false;
    protected override void RunPuppetRaw(GameObject go, string raw, CancellationToken ct)
    {
        bool enabled = go.ToggleMapEnabled();
        if (enabled)
        {
            go.Msg("Map enabled.");
            try { go.Session?.Connection?.SendCommand("map_enable", new List<object?> { "" }, null); } catch (Exception) { }
            if (AtherizSettings.Global.MapEnabled)
            {
                var loc = go.ResolveLocationObject() as Node;
                if (loc is not null)
                {
                    try
                    {
                        var mh = GlobalServices.GetMapHandler();
                        var mi = mh.GetMapInfo(loc.Coord.Area, loc.Coord.Z);
                        mi?.Render(true);
                    }
                    catch (Exception) { }
                }
            }
        }
        else
        {
            go.Msg("Map disabled.");
            try { go.Session?.Connection?.SendCommand("map_disable", new List<object?> { "" }, null); } catch (Exception) { }
        }
    }
}

/// <summary>
/// Fallback unknown command. Mirrors <c>atheriz/commands/loggedin/none.py:NoneCommand</c> (62 LOC).
/// </summary>
public sealed class NoneCommand : Command
{
    public override string Key => "none";
    public override bool Hide => true;
    public override string Desc => "Fallback for unknown commands.";
    // Same parsed shape as the unlogged fallback: identical inputs suggest
    // identically pre/post login (quotes, dash-input) instead of raw-vs-parsed.
    protected override void SetupParser(GameArgumentParser p) { p.AddArgument(ParsedArgKeys.None, nargs: "*", help: "Fallback for unknown commands."); }

    public override void Run(CommandContext ctx)
    {
        var caller = ctx.Caller;
        var pa = ctx.Args;
        string text = "";
        if (pa is not null) text = string.Join(" ", pa.GetList(ParsedArgKeys.None));
        else text = ctx.RawText.Trim();
        if (string.IsNullOrEmpty(text)) { caller.Msg("Command not found."); return; } // none.py:25
        var ignored = Atheriz.Core.Settings.AtherizSettings.Global.AutoAliasIgnoredKeys;
        // hidden or inaccessible commands are never suggested (otherwise a
        // typo oracle leaks their names to callers who cannot use them).
        List<string> choices = [];
        // Order-preserving dedup (set for membership, list for order):
        // insertion order feeds BestMatch's stable tie-breaks.
        HashSet<string> seen = new(StringComparer.Ordinal);
        if (caller is Objects.GameObject go && go.InternalCmdSet is not null)
            foreach (var c in go.InternalCmdSet.GetAll())
                if (!ignored.Contains(c.Key) && !c.Hide && c.Access(caller)) CommandHelpers.TryAddChoice(choices, seen, c.Key);
        foreach (var c in CommandRegistry.LoggedIn.GetAll())
            if (!ignored.Contains(c.Key) && !c.Hide && c.Access(caller)) CommandHelpers.TryAddChoice(choices, seen, c.Key);
        if (caller is Objects.GameObject go2)
        {
            try
            {
                foreach (var set in CommandHelpers.LocalVerbSets(go2))
                    foreach (var c in set.GetAll())
                        if (!ignored.Contains(c.Key) && !c.Hide && c.Access(go2)) CommandHelpers.TryAddChoice(choices, seen, c.Key);
            }
            catch (Exception logEx) { Atheriz.Core.AtherizLogger.LogDebug("Suppressed NoneCommand externals: " + logEx.Message, "NoneCommand"); }
        }
        if (choices.Count > 0)
        {
            var best = StringDistance.BestMatch(text, choices);
            caller.Msg($"Command \"{text}\" not found, did you mean: \"{best}\"?");
        }
        else caller.Msg($"Command \"{text}\" not found.");
    }
}

public sealed class QuitCommand : Command
{
    public override string Key => "quit";
    // "exit" alias is verbatim Python quit.py:14. It never collides live with the room-exit
    // command: room exits are per-direction ExitCommands in the puppet's InternalCmdSet,
    // which CommandDispatcher consults BEFORE the global registry — so in-room "exit"
    // (if ever keyed literally) wins, otherwise this quit alias fires. Do not remove.
    public override IReadOnlyList<string> Aliases => ["exit", "logout", "disconnect"];
    public override string Desc => "Quit.";
    public override bool UseParser => false;
    public override void Run(CommandContext ctx)
    {
        ctx.Caller.Msg("Goodbye!");
        ConnectionHelper.CloseQuietly(ctx.Caller);
    }
}

public sealed class TimeCommand : LoggedInCommand
{
    public override string Key => "time";
    public override IReadOnlyList<string> Aliases => ["uptime"];
    public override string Desc => "Show the current time.";
    public override bool UseParser => false;
    protected override void RunPuppetRaw(GameObject go, string raw, CancellationToken ct)
    {
        try
        {
            var gt = GlobalServices.GetGameTime();
            var info = gt.GetTime();
            go.Msg(info.Formatted);
        }
        catch { go.Msg("Time system unavailable."); return; }
        // Evennia time also reports uptime: process start is the closest
        // equivalent (nothing tracks server boot). Failure falls back to the
        // old game-time-only line above.
        var (ok, up, started) = TryGetUptime();
        if (!ok) return;
        go.Msg($"Uptime: {FormatUptime(up)}");
        go.Msg($"Started: {started:yyyy-MM-dd HH:mm:ss} UTC");
    }

    // Shared with the info verb: process start time as server boot.
    internal static (bool Ok, TimeSpan Up, DateTime StartedUtc) TryGetUptime()
    {
        try
        {
            var started = System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime();
            var up = DateTime.UtcNow - started;
            if (up < TimeSpan.Zero) return (false, TimeSpan.Zero, started);
            return (true, up, started);
        }
        catch { return (false, TimeSpan.Zero, DateTime.MinValue); }
    }

    internal static string FormatUptime(TimeSpan up)
        => $"{(int)up.TotalDays}d {up.Hours:D2}:{up.Minutes:D2}:{up.Seconds:D2}";
}

/// <summary>
/// Mirrors <c>evennia/commands/default/general.py:CmdHome</c>.
/// </summary>
public sealed class HomeCommand : LoggedInCommand
{
    public override string Key => "home";
    public override string Desc => "Teleport to your home location.";
    public override bool UseParser => false;
    protected override void RunPuppetRaw(GameObject go, string raw, CancellationToken ct)
    {
        var home = go.Home;
        if (home is LocationRef.NullLocation) { go.Msg("You have no home."); return; }
        // LocationRef is a record: value equality detects already-home.
        if (go.Location.Equals(home)) { go.Msg("You are already home."); return; }
        if (!go.MoveToLocation(home, go)) go.Msg("You cannot go home.");
    }
}

/// <summary>
/// Mirrors <c>evennia/commands/default/system.py:CmdAbout</c>.
/// </summary>
public sealed class AboutCommand : LoggedInCommand
{
    public override string Key => "about";
    public override IReadOnlyList<string> Aliases => ["version"];
    public override string Desc => "Show server version and runtime info.";
    public override bool UseParser => false;
    protected override void RunPuppetRaw(GameObject go, string raw, CancellationToken ct)
    {
        go.Msg($"{AtherizSettings.Global.ServerName} {VersionText()} on {System.Runtime.InteropServices.RuntimeInformation.OSDescription.Trim()} ({System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}).");
    }

    // Shared with the info verb so the two agree.
    internal static string VersionText()
        // Same assembly-version source as the connection screen.
        => System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
}

/// <summary>
/// Merges <c>evennia/commands/default/general.py:CmdAccess</c> (bare display)
/// with <c>evennia/commands/default/building.py:CmdLock</c> (object locks).
/// One verb, split on arguments: dispatch holds one command per key and
/// Access never sees args, so the verb stays player-visible and the
/// object-targeting branch checks IsBuilder at runtime.
/// </summary>
public sealed class AccessCommand : LoggedInCommand
{
    public override string Key => "access";
    public override IReadOnlyList<string> Aliases => ["groups", "hierarchy"];
    public override string Desc => "Show your permissions, or manage locks on an object.";
    public override string ExtraDesc => "Use 'access' alone to see the permission hierarchy and your own groups, or 'access <object> [= <lock>:<policy>[; ...]]' to view or set that object's locks (builders only). An empty policy clears that lock: 'access <object> = <lock>:' .";
    public override bool UseParser => false;
    protected override void RunPuppetRaw(GameObject go, string raw, CancellationToken ct)
    {
        string text = (raw ?? "").Trim();
        if (text.Length == 0) { ShowSelf(go); return; }
        // Runtime builder gate for the object branch (see the class note).
        if (!go.IsBuilder) { go.Msg("You can't do that."); return; }
        int eq = text.IndexOf('=');
        string targetPart = (eq < 0 ? text : text[..eq]).Trim();
        string? lockPart = eq < 0 ? null : text[(eq + 1)..].Trim();
        // Builder verb: resolve world-wide by exact name (the `_ => true`
        // filter unions the global exact-name index in), not just nearby.
        // `#id` already resolves globally through SearchWithFallback.
        var (target, error) = TargetResolution.ResolveObject(go, targetPart, _ => true, CommandHelpers.FormatNoMatchFound(targetPart));
        if (error is not null) { go.Msg(error); return; }
        if (target is null) return;
        if (lockPart is null) { ShowLocks(go, target); return; }
        string help = PrintHelp();
        if (lockPart.Length == 0) { go.Msg(help); return; }
        SetLocks(go, target, lockPart, help);
    }

    private static void ShowSelf(GameObject go)
    {
        string chain = string.Join(" < ", Enum.GetValues<Privilege>());
        var flags = new List<string>();
        if (go.IsBuilder) flags.Add("builder");
        if (go.IsSuperUser) flags.Add("superuser");
        string self = $"You are: {go.PrivilegeLevel}" + (flags.Count > 0 ? $" ({string.Join(", ", flags)})" : "");
        go.Msg($"Permission hierarchy: {chain}\n{self}");
    }

    private static void ShowLocks(GameObject go, GameObject target)
    {
        var snap = target.GetLockPoliciesSnapshot();
        if (snap.Count == 0) { go.Msg($"{target.Name} has no locks."); return; }
        var lines = snap
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => $"{kv.Key}: {string.Join(", ", kv.Value)}");
        go.Msg($"{target.Name} locks:\n{string.Join("\n", lines)}");
    }

    private static void SetLocks(GameObject go, GameObject target, string lockPart, string help)
    {
        // Validate everything before applying anything: a bad entry aborts
        // the whole update instead of half-applying it.
        var parsed = new List<(string Name, string Policy, Func<GameObject, bool> Check)>();
        var cleared = new List<string>();
        foreach (var chunk in lockPart.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int colon = chunk.IndexOf(':');
            if (colon < 0) { go.Msg($"Bad lock entry '{chunk}'. Use <lock>:<policy>."); return; }
            string lname = chunk[..colon].Trim();
            string policy = chunk[(colon + 1)..].Trim().ToLowerInvariant();
            if (lname.Length == 0) { go.Msg($"Bad lock entry '{chunk}'. Use <lock>:<policy>."); return; }
            if (policy.Length == 0) { cleared.Add(lname); continue; }
            if (policy == LockPolicies.Custom) { go.Msg("Custom locks need code and cannot be set here."); return; }
            if (!LockPolicies.TryResolve(policy, target, out var pred)) { go.Msg($"Unknown lock policy '{policy}'."); return; }
            parsed.Add((lname, policy, pred));
        }
        if (parsed.Count == 0 && cleared.Count == 0) { go.Msg(help); return; }
        foreach (var lname in cleared) target.ClearLocksByName(lname);
        foreach (var (lname, policy, check) in parsed)
        {
            target.ClearLocksByName(lname);
            target.AddLock(lname, check, policy);
        }
        var bits = parsed
            .Select(p => $"{p.Name}:{p.Policy}")
            .Concat(cleared.Select(c => $"{c}: cleared"));
        go.Msg($"Locks updated on {target.Name}: {string.Join(", ", bits)}.");
    }
}

/// <summary>
/// Mirrors <c>evennia/commands/default/general.py:CmdNick</c> (inputline scope).
/// Object/account scopes store alongside but only inputline nicks expand at
/// dispatch — there is no object/account input line to expand on.
/// </summary>
public sealed class NickCommand : LoggedInCommand
{
    public override string Key => "nick";
    public override IReadOnlyList<string> Aliases => ["nickname", "nicks"];
    public override string Desc => "Manage your personal input nicks (aliases).";
    public override string ExtraDesc => "Use 'nick' to list, 'nick <key> = <replacement>' to set, 'nick <type> <key> = <replacement>' for a scoped nick (inputline, object, account), 'nick <key>' to show one, 'nick delete [<type>] <key>' to delete, 'nick clear' to delete all.";
    public override bool UseParser => false;

    // Extras key holding the caller's nick table: a JSON object of
    // lowercased "type:key" to replacement. Lives on the account so nicks
    // follow the player across puppets.
    internal const string NicksExtraKey = "nicks";
    private static readonly string[] Scopes = ["inputline", "object", "account"];

    protected override void RunPuppetRaw(GameObject go, string raw, CancellationToken ct)
    {
        var acc = go.Session?.Account;
        if (acc is null) { go.Msg("No account is attached to your session."); return; }
        string help = PrintHelp();
        string text = (raw ?? "").Trim();
        if (text.Length == 0) { ListNicks(go, ReadNicks(acc)); return; }
        string head, tail;
        int sp = text.IndexOfAny([' ', '\t']);
        if (sp < 0) { head = text; tail = ""; }
        else { head = text[..sp]; tail = text[(sp + 1)..].Trim(); }
        if (head.Equals("clear", StringComparison.OrdinalIgnoreCase) && tail.Length == 0)
        {
            WriteNicks(acc, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
            go.Msg("All nicks cleared.");
            return;
        }
        if (head.Equals("delete", StringComparison.OrdinalIgnoreCase)) { DeleteNick(go, acc, tail, help); return; }
        int eq = text.IndexOf('=');
        if (eq < 0) { ShowNick(go, ReadNicks(acc), text, help); return; }
        string left = text[..eq].Trim();
        string replacement = text[(eq + 1)..].Trim();
        if (replacement.Length == 0) { go.Msg(help); return; }
        var words = left.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string scope = "inputline";
        string key;
        if (words.Length == 1) key = words[0];
        else if (words.Length == 2 && IsScope(words[0])) { scope = words[0].ToLowerInvariant(); key = words[1]; }
        else { go.Msg(help); return; }
        var nicks = ReadNicks(acc);
        nicks[$"{scope}:{key.ToLowerInvariant()}"] = replacement;
        WriteNicks(acc, nicks);
        go.Msg($"Nick '{key}' set.");
    }

    private static bool IsScope(string word) =>
        word.Equals("inputline", StringComparison.OrdinalIgnoreCase)
        || word.Equals("object", StringComparison.OrdinalIgnoreCase)
        || word.Equals("account", StringComparison.OrdinalIgnoreCase);

    private static void ListNicks(GameObject go, Dictionary<string, string> nicks)
    {
        if (nicks.Count == 0) { go.Msg("You have no nicks."); return; }
        var lines = nicks
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => $"{kv.Key} = {kv.Value}");
        go.Msg($"Your nicks:\n{string.Join("\n", lines)}");
    }

    private static void ShowNick(GameObject go, Dictionary<string, string> nicks, string query, string help)
    {
        var words = query.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string scope = "inputline";
        string key;
        if (words.Length == 1) key = words[0];
        else if (words.Length == 2 && IsScope(words[0])) { scope = words[0].ToLowerInvariant(); key = words[1]; }
        else { go.Msg(help); return; }
        if (nicks.TryGetValue($"{scope}:{key.ToLowerInvariant()}", out var replacement))
            go.Msg($"{key} = {replacement}");
        else go.Msg($"No such nick '{key}'.");
    }

    private static void DeleteNick(GameObject go, Account acc, string tail, string help)
    {
        var words = tail.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string? scope = null;
        string key;
        if (words.Length == 1 && words[0].Length > 0) key = words[0];
        else if (words.Length == 2 && IsScope(words[0])) { scope = words[0].ToLowerInvariant(); key = words[1]; }
        else { go.Msg(help); return; }
        var nicks = ReadNicks(acc);
        string suffix = ":" + key.ToLowerInvariant();
        var doomed = nicks.Keys
            .Where(k => scope is not null ? k.Equals($"{scope}:{key.ToLowerInvariant()}", StringComparison.OrdinalIgnoreCase) : k.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (doomed.Count == 0) { go.Msg($"No such nick '{key}'."); return; }
        foreach (var k in doomed) nicks.Remove(k);
        WriteNicks(acc, nicks);
        go.Msg($"Nick '{key}' deleted.");
    }

    internal static Dictionary<string, string> ReadNicks(Account acc)
    {
        var table = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!acc.TryGetExtraJson(NicksExtraKey, out var el)) return table;
        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(el.GetRawText());
            if (parsed is null) return table;
            foreach (var kv in parsed) table[kv.Key] = kv.Value;
        }
        catch (JsonException) { }
        return table;
    }

    internal static void WriteNicks(Account acc, Dictionary<string, string> nicks)
        => acc.SetExtraJson(NicksExtraKey, JsonSerializer.SerializeToElement(nicks));

    // Single-pass inputline expansion for the dispatch path: rewrites the raw
    // line when its first word is a nick and not an existing command.
    // Existing commands and single-char keys always win — no shadowing, and no
    // hijacking the glued single-char movement refusal. The rewritten line is
    // parsed once downstream, so nicks never chain.
    public static string ExpandNicks(GameObject go, string text)
    {
        var parsed = CommandDispatcher.ParseRaw(text);
        if (parsed is null) return text;
        string first = parsed.RawCmdKey;
        if (first.Length <= 1) return text;
        if (CommandRegistry.LoggedIn.Get(first) is not null) return text;
        if (go.InternalCmdSet?.Get(first) is not null) return text;
        var acc = go.Session?.Account;
        if (acc is null) return text;
        if (!ReadNicks(acc).TryGetValue("inputline:" + first, out var replacement)) return text;
        if (string.IsNullOrWhiteSpace(replacement)) return text;
        string rest = parsed.CmdArgs;
        return rest.Length == 0 ? replacement : replacement + " " + rest;
    }
}

/// <summary>
/// Mirrors <c>evennia/commands/default/account.py:CmdWho</c>.
/// The <c>doing</c> alias hides session details.
/// </summary>
public sealed class WhoCommand : LoggedInCommand
{
    public override string Key => "who";
    public override IReadOnlyList<string> Aliases => ["doing"];
    public override string Desc => "List who is online.";
    public override string ExtraDesc => "Use 'who' for the full session list, or 'doing' for names only.";

    // Parser with no arguments: Execute stamps ParsedArgs.CmdString with the
    // invoked alias (who vs doing), which the raw path can never see.
    protected override void SetupParser(GameArgumentParser p) { }
    protected override void RunPuppet(GameObject go, GameArgumentParser.ParsedArgs pa, CancellationToken ct)
        => ShowWho(go, detailed: !pa.CmdString.Equals("doing", StringComparison.OrdinalIgnoreCase));
    protected override void RunPuppetRaw(GameObject go, string raw, CancellationToken ct)
        => ShowWho(go, detailed: true);

    private static void ShowWho(GameObject go, bool detailed)
    {
        var conns = GlobalServices.GetConnectionManager().GetAllConnections();
        if (conns.Count == 0) { go.Msg("Nobody is online."); return; }
        var lines = new List<string>();
        foreach (var conn in conns)
        {
            string acc = conn.Session.Account?.Name ?? "(none)";
            string puppet = conn.Session.Puppet?.Name ?? "(none)";
            lines.Add(detailed
                ? $"{acc} ({puppet}) [{ProtocolName(conn)} {conn.ClientHost} {conn.SessionId}]"
                : $"{acc} ({puppet})");
        }
        go.Msg($"Online ({conns.Count}):\n{string.Join("\n", lines)}");
    }

    internal static string ProtocolName(BaseConnection conn) => conn switch
    {
        TelnetConnection => "telnet",
        WebSocketConnection => "web",
        _ => "other",
    };
}

/// <summary>
/// Mirrors <c>evennia/commands/default/account.py:CmdSessions</c>.
/// Callers see only their own sessions.
/// </summary>
public sealed class SessionsCommand : LoggedInCommand
{
    public override string Key => "sessions";
    public override string Desc => "List your own sessions.";
    public override bool UseParser => false;
    protected override void RunPuppetRaw(GameObject go, string raw, CancellationToken ct)
    {
        var acc = go.Session?.Account;
        if (acc is null) { go.Msg("No account is attached to your session."); return; }
        var mine = GlobalServices.GetConnectionManager().GetAllConnections()
            .Where(c => ReferenceEquals(c.Session.Account, acc))
            .ToList();
        if (mine.Count == 0) { go.Msg("You have no sessions."); return; }
        var lines = mine.Select(c =>
        {
            string puppet = c.Session.Puppet?.Name ?? "(none)";
            string loc = c.Session.Puppet?.ResolveLocationObject()?.Name ?? "(nowhere)";
            return $"{c.SessionId} [{WhoCommand.ProtocolName(c)} {c.ClientHost}] puppet: {puppet} at {loc}";
        });
        go.Msg($"Your sessions ({mine.Count}):\n{string.Join("\n", lines)}");
    }
}

/// <summary>
/// Mirrors <c>evennia/commands/default/account.py:CmdColorTest</c>.
/// Dependency-free ANSI charts for client debugging.
/// </summary>
public sealed class ColorCommand : LoggedInCommand
{
    public override string Key => "color";
    public override string Desc => "Show ANSI color test charts.";
    public override bool UseParser => false;
    protected override void RunPuppetRaw(GameObject go, string raw, CancellationToken ct)
    {
        var basic = new List<string>();
        for (int i = 0; i < 8; i++) basic.Add($"\x1b[{30 + i}mSample {30 + i}\x1b[0m");
        for (int i = 0; i < 8; i++) basic.Add($"\x1b[{90 + i}mSample {90 + i}\x1b[0m");
        go.Msg("Standard 16:\n" + string.Join(" ", basic));
        int[] cube = [16, 52, 88, 124, 160, 196, 21, 51, 87, 231];
        go.Msg("256 colors:\n" + string.Join(" ", cube.Select(v => $"\x1b[38;5;{v}mSample {v}\x1b[0m")));
        (int R, int G, int B)[] rgb = [(255, 0, 0), (0, 255, 0), (0, 0, 255), (255, 255, 0)];
        go.Msg("Truecolor:\n" + string.Join(" ", rgb.Select(c => $"\x1b[38;2;{c.R};{c.G};{c.B}mSample\x1b[0m")));
    }
}

/// <summary>
/// Mirrors <c>evennia/commands/default/unloggedin.py:CmdUnconnectedInfo</c>
/// for the logged-in side: MudConnector-style server info block.
/// </summary>
public sealed class InfoCommand : LoggedInCommand
{
    public override string Key => "info";
    public override string Desc => "Show server info.";
    public override bool UseParser => false;
    protected override void RunPuppetRaw(GameObject go, string raw, CancellationToken ct)
        => go.Msg(BuildInfoBlock());

    // Shared with the unlogged-in twin so both sets report identically.
    internal static string BuildInfoBlock()
    {
        var (ok, up, _) = TimeCommand.TryGetUptime();
        int count;
        try { count = GlobalServices.GetConnectionManager().ConnectionCount; }
        catch { count = 0; }
        return "## BEGIN INFO\n"
            + $"Name: {AtherizSettings.Global.ServerName}\n"
            + $"Version: {AboutCommand.VersionText()}\n"
            + $"Uptime: {(ok ? TimeCommand.FormatUptime(up) : "unknown")}\n"
            + $"Connected: {count}\n"
            + "## END INFO";
    }
}

/// <summary>
/// Mirrors <c>evennia/commands/default/account.py:CmdPassword</c>:
/// self-service password change (companion to the superuser-only
/// <c>userpassword</c> verb).
/// </summary>
public sealed class PasswordCommand : LoggedInCommand
{
    public override string Key => "password";
    public override string Desc => "Change your own password.";
    public override string ExtraDesc => "Use 'password <old> = <new>'.";
    public override bool UseParser => false;
    protected override void RunPuppetRaw(GameObject go, string raw, CancellationToken ct)
    {
        var acc = go.Session?.Account;
        if (acc is null) { go.Msg("No account is attached to your session."); return; }
        string text = (raw ?? "").Trim();
        int eq = text.IndexOf('=');
        string old = (eq < 0 ? "" : text[..eq]).Trim();
        string fresh = eq < 0 ? "" : text[(eq + 1)..].Trim();
        if (old.Length == 0 || fresh.Length == 0) { go.Msg(PrintHelp()); return; }
        var pwErr = Validation.ValidatePassword(fresh, AtherizSettings.Global);
        if (pwErr is not null) { go.Msg(pwErr); return; }
        if (!acc.ChangePassword(old, fresh)) { go.Msg("Current password is wrong."); return; }
        go.Msg("Password changed.");
    }
}

/// <summary>
/// Mirrors <c>evennia/commands/default/account.py:CmdCharDelete</c>.
/// Self-service deletion of one of your own characters. The guard is
/// ownership, not privilege. Stateless two-step: the first call names the
/// price, the <c>--confirm</c> call pays it.
/// </summary>
public sealed class ChardeleteCommand : LoggedInCommand
{
    public override string Key => "chardelete";
    public override string Desc => "Permanently delete one of your characters.";
    public override string ExtraDesc => "Use 'chardelete <name>', then repeat with '--confirm' to delete.";
    public override bool UseParser => false;
    protected override void RunPuppetRaw(GameObject go, string raw, CancellationToken ct)
    {
        var acc = go.Session?.Account;
        if (acc is null) { go.Msg("No account is attached to your session."); return; }
        string text = (raw ?? "").Trim();
        bool confirm = false;
        if (text.StartsWith("--confirm ", StringComparison.OrdinalIgnoreCase))
        {
            confirm = true;
            text = text[10..].Trim();
        }
        else if (text.EndsWith(" --confirm", StringComparison.OrdinalIgnoreCase))
        {
            confirm = true;
            text = text[..^10].Trim();
        }
        else if (text.Equals("--confirm", StringComparison.OrdinalIgnoreCase)) text = "";
        if (text.Length == 0) { go.Msg(PrintHelp()); return; }
        GameObject? target = null;
        foreach (int id in acc.Characters)
        {
            var c = ObjectRegistry.GetSingle(id);
            if (c is not null && !c.IsDeleted && c.Name.Equals(text, StringComparison.OrdinalIgnoreCase)) { target = c; break; }
        }
        if (target is null) { CommandHelpers.MsgCouldNotFind(go, text); return; }
        if (target == go) { go.Msg("You cannot delete the character you are playing."); return; }
        if (!confirm) { go.Msg($"To delete {target.Name}, run 'chardelete --confirm {target.Name}'."); return; }
        acc.RemoveCharacter(target);
        target.Delete(go);
        go.Msg($"Deleted character {target.Name}.");
    }
}
