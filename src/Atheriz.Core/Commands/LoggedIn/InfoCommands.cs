using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text;

namespace Atheriz.Core.Commands.LoggedIn;

public sealed class DrawCommand : Command
{
    public override string Key => "mapedit";
    public override string Desc => "Open the AtheriZ map editor in a new browser tab.";
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
        go.MapEnabled = !go.MapEnabled;
        if (go.MapEnabled)
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
        catch { go.Msg("Time system unavailable."); }
    }
}
