using System.Text.RegularExpressions;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Utils;
using Atheriz.Core.Commands;

namespace Atheriz.Core.Commands.LoggedIn;

public sealed class DrawCommand : Command
{
    public override string Key => "mapedit";
    public override string Desc => "Open the AtheriZ map editor in a new browser tab.";
    public override bool UseParser => false;
    public override bool Access(IMessageTarget caller) => CommandPermissions.IsBuilder(caller);

    public override void Run(IMessageTarget caller, object? args)
    {
        // Typed (F001): GameObject carries Location/Session; Session.Connection is
        // typed BaseConnection (ClientHost/SendCommand). A raw BaseConnection caller
        // resolves via its session puppet. No reflection.
        GameObject? go = caller as GameObject;
        if (go == null && caller is Atheriz.Core.Network.BaseConnection bc0 && bc0.Session.Puppet is GameObject pgo)
            go = pgo;
        Node? loc = go?.ResolveLocationObject() as Node;
        if (loc == null)
        {
            caller.Msg("You must be in a valid location to open the map editor.");
            return;
        }
        // session/connection
        Session? session = go?.Session;
        Atheriz.Core.Network.BaseConnection? conn = session?.Connection;
        if (conn == null)
        {
            caller.Msg("No active connection.");
            return;
        }
        string area = loc.Coord.Area;
        int z = loc.Coord.Z;
        MapHandler mh;
        try { mh = GlobalServices.GetMapHandler(); } catch { mh = new MapHandler(autoLoad:false); }
        var mi = mh.GetMapInfo(area, z);
        if (mi == null)
        {
            mi = new MapInfo(area);
            mh.SetMapInfo(area, z, mi);
        }
        string ip = conn.ClientHost ?? "?";

        string key = MapEdit.Grant(ip, area, z, go?.Session);
        string rawSym = go?.Symbol ?? "X";
        if (string.IsNullOrEmpty(rawSym)) rawSym = "X";
        string plain = GameUtils.StripAnsi(rawSym);
        plain = plain.Trim();
        if (string.IsNullOrEmpty(plain)) plain = "X";
        if (plain.Length > 2) plain = plain.Substring(0, 2);

        var payload = new Dictionary<string, object?>
        {
            ["area"] = area,
            ["z"] = z,
            ["grid"] = new List<List<object?>>(),
            ["rooms"] = new List<Dictionary<string, object?>>(),
            ["legend"] = new List<Dictionary<string, object?>>(),
            ["playerSymbol"] = plain
        };
        if (mi.PreGrid.Count > 0)
        {
            mi.PreRender();
        }
        List<KeyValuePair<(int X,int Y), string>> gridSnap;
        mi.Lock.EnterReadLock();
        try { gridSnap = mi.PostGrid.ToList(); }
        finally { mi.Lock.ExitReadLock(); }
        NodeHandler? nh = null;
        try { nh = NodeHandler.GetCurrent() ?? GlobalServices.GetNodeHandler(); } catch { }
        NodeArea? areaObj = nh?.GetArea(area);
        NodeGrid? nodeGrid = areaObj?.GetGrid(z);

        // helper to build room payload
        Dictionary<string, object?> RoomPayload(Node node)
        {
            var exits = new List<Dictionary<string, object?>>();
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

        var seen = new HashSet<(int,int)>();
        var gridList = (List<List<object?>>)payload["grid"]!;
        var roomsList = (List<Dictionary<string, object?>>)payload["rooms"]!;
        foreach (var kv in gridSnap)
        {
            gridList.Add(new List<object?>{ kv.Key.X, kv.Key.Y, kv.Value });
            if (nodeGrid == null) continue;
            var node = nodeGrid.GetNode(kv.Key);
            if (node == null) continue;
            seen.Add(kv.Key);
            roomsList.Add(RoomPayload(node));
        }
        if (nodeGrid != null)
        {
            nodeGrid.Lock.EnterReadLock();
            List<( (int X,int Y) coord, Node node)> extra = new();
            try
            {
                foreach (var kv in nodeGrid.Nodes)
                    if (!seen.Contains(kv.Key)) extra.Add((kv.Key, kv.Value));
            }
            finally { nodeGrid.Lock.ExitReadLock(); }
            foreach (var (coord, node) in extra)
                roomsList.Add(RoomPayload(node));
        }
        mi.Lock.EnterReadLock();
        try
        {
            var legendList = (List<Dictionary<string, object?>>)payload["legend"]!;
            foreach (var e in mi.LegendEntries)
                legendList.Add(e.ToPayload());
        }
        finally { mi.Lock.ExitReadLock(); }

        try
        {
            var argsList = new List<object?> { key, payload };
            var kw = new Dictionary<string, object?>();
            conn.SendCommand("launch_draw", argsList, kw);
        }
        catch { }
        caller.Msg("Opening AtheriZ Draw in a new tab.");
    }
}