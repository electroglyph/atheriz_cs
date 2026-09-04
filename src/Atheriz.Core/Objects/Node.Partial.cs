using Atheriz.Core.Globals;
using Atheriz.Core.Utils;
using Atheriz.Core.Commands;
using System.Text.Json;
namespace Atheriz.Core.Objects;

public partial class Node
{
    // Port of nodes.py:499 add_noun
    public void AddNoun(string key, string desc)
    {
        _nodeLock.EnterWriteLock();
        try
        {
            var low = key.ToLowerInvariant();
            foreach (var k in Nouns.Keys.ToList()) if (k.ToLowerInvariant() == low && k != low) Nouns.Remove(k);
            Nouns[low] = desc;
            IsModified = true;
        }
        finally { _nodeLock.ExitWriteLock(); }
    }
    // Port of nodes.py:516 remove_noun
    public void RemoveNoun(string key)
    {
        _nodeLock.EnterWriteLock();
        try
        {
            Nouns.Remove(key.ToLowerInvariant());
            foreach (var k in Nouns.Keys.ToList()) if (k.ToLowerInvariant() == key.ToLowerInvariant()) Nouns.Remove(k);
            IsModified = true;
        }
        finally { _nodeLock.ExitWriteLock(); }
    }
    // Port of nodes.py:531 get_noun
    public string? GetNoun(string key)
    {
        _nodeLock.EnterReadLock();
        try
        {
            if (Nouns.TryGetValue(key.ToLowerInvariant(), out var v)) return v;
            foreach (var kv in Nouns) if (kv.Key.Equals(key, StringComparison.OrdinalIgnoreCase)) return kv.Value;
            return null;
        }
        finally { _nodeLock.ExitReadLock(); }
    }
    public override string ToString() => $"Node: {Coord}"; // Port of nodes.py:551

    // Port of nodes.py:554 search
    public override List<GameObject> Search(string query, bool recursive = true, GameObject? looker = null)
        => ContentUtils.Search(this, query, id => ObjectRegistry.Get(id).FirstOrDefault(), recursive, looker ?? this);

    // Port of nodes.py:569
    public List<NodeLink> GetLinks()
    {
        _nodeLock.EnterReadLock();
        try { return Links.ToList(); }
        finally { _nodeLock.ExitReadLock(); }
    }
    // Port of nodes.py:579
    public bool HasLinkName(string name)
    {
        _nodeLock.EnterReadLock();
        try { return Links.Any(l => l.Name == name); }
        finally { _nodeLock.ExitReadLock(); }
    }
    // Port of nodes.py:590
    public NodeLink? GetLinkByName(string name)
    {
        var low = name.ToLowerInvariant();
        _nodeLock.EnterReadLock();
        try { return Links.FirstOrDefault(l => l.Name.ToLowerInvariant() == low || l.Aliases.Any(a => a.Equals(low, StringComparison.OrdinalIgnoreCase))); }
        finally { _nodeLock.ExitReadLock(); }
    }
    public NodeLink? GetLink(string name) => GetLinkByName(name);
    // Port of nodes.py:598
    public NodeArea? Area => NodeHandler.GetCurrent()?.GetArea(Coord.Area);
    // Port of nodes.py:604
    public NodeGrid? Grid
    {
        get
        {
            var nh = NodeHandler.GetCurrent();
            var a = nh?.GetArea(Coord.Area);
            return a?.GetGrid(Coord.Z);
        }
    }
    // Port of nodes.py:611 name
    public new string Name { get => Coord.ToString(); set { } }

    // Port of nodes.py:616 add_script
    public void AddScript(object script)
    {
        int id = script is int i ? i : script is GameObject go ? go.Id : -1;
        if (id == -1) return;
        var objs = ObjectRegistry.Get(id);
        if (objs.Count == 0) return;
        if (objs[0] is Script s) s.InstallHooks(this);
        _nodeLock.EnterWriteLock();
        try { _nodeScripts.Add(id); IsModified = true; }
        finally { _nodeLock.ExitWriteLock(); }
    }
    // Port of nodes.py:632
    public void RemoveScript(object script)
    {
        int id = script is int i ? i : script is GameObject go ? go.Id : -1;
        if (id == -1) return;
        var objs = ObjectRegistry.Get(id);
        if (objs.Count > 0 && objs[0] is Script s) s.RemoveHooks(this);
        _nodeLock.EnterWriteLock();
        try { _nodeScripts.Remove(id); IsModified = true; }
        finally { _nodeLock.ExitWriteLock(); }
    }
    // Port of nodes.py:648
    public NodeLink? GetRandomLink()
    {
        _nodeLock.EnterReadLock();
        try { return Links.Count == 0 ? null : Links[Random.Shared.Next(Links.Count)]; }
        finally { _nodeLock.ExitReadLock(); }
    }
    // Port of nodes.py:657 add_link
    public void AddLink(NodeLink link)
    {
        _nodeLock.EnterWriteLock();
        try
        {
            if (Links.Count > 0 && Links.Contains(link)) return;
            if (Links.Count == 0) Links = [link];
            else Links.Add(link);
            IsModified = true;
        }
        finally { _nodeLock.ExitWriteLock(); }
        // notify occupants
        foreach (var o in GetContents()) try { AddExits(o); } catch { }
        if (link.Coord.Area != Coord.Area)
        {
            var nh = NodeHandler.GetCurrent();
            nh?.AddTransition(new Transition(Coord, link.Coord, link.Name));
        }
    }
    // Port of nodes.py:677
    public bool AddLinkIfAbsent(string name, Func<NodeLink> factory)
    {
        _nodeLock.EnterReadLock();
        try { if (Links.Any(l => l.Name == name)) return false; }
        finally { _nodeLock.ExitReadLock(); }
        var link = factory();
        _nodeLock.EnterWriteLock();
        try
        {
            if (Links.Any(l => l.Name == name)) return false;
            // inline add_link logic but avoid double lock
            if (Links.Count > 0 && Links.Contains(link)) return false;
            if (Links.Count == 0) Links = [link];
            else Links.Add(link);
            IsModified = true;
        }
        finally { _nodeLock.ExitWriteLock(); }
        foreach (var o in GetContents()) try { AddExits(o); } catch { }
        if (link.Coord.Area != Coord.Area)
        {
            var nh = NodeHandler.GetCurrent();
            nh?.AddTransition(new Transition(Coord, link.Coord, link.Name));
        }
        return true;
    }
    // Port of nodes.py:688
    public void RemoveLink(string name)
    {
        NodeLink? found = null;
        _nodeLock.EnterWriteLock();
        try
        {
            var idx = Links.FindIndex(l => l.Name == name);
            if (idx >= 0) { found = Links[idx]; Links.RemoveAt(idx); IsModified = true; }
        }
        finally { _nodeLock.ExitWriteLock(); }
        if (found != null && Coord.Area != found.Coord.Area)
        {
            var nh = NodeHandler.GetCurrent();
            nh?.RemoveTransition(found.Coord);
        }
        // also remove exits from occupants
        if (found != null)
            foreach (var o in GetContents()) try { o.InternalCmdSet?.RemoveByTag("exits"); } catch { }
    }

    // Port of nodes.py:711 add_exits
    public void AddExits(GameObject obj, bool internalCall = false)
    {
        obj.InternalCmdSet?.RemoveByTag("exits");
        List<NodeLink> snap;
        if (internalCall)
        {
            snap = Links; // direct reference when already locked? keep snapshot
        }
        else
        {
            _nodeLock.EnterReadLock();
            try { snap = Links.ToList(); }
            finally { _nodeLock.ExitReadLock(); }
        }
        if (snap.Count == 0) return;
        var cmds = new List<Command>();
        foreach (var n in snap)
        {
            var ec = new ExitCommand();
            ec.SetKey(n.Name);
            ec.CallerId = obj.Id;
            ec.Location = Coord;
            ec.Destination = n.Coord;
            ec.ExitName = n.Name;
            ec.SetAliases(n.Aliases);
            ec.Tag = "exits";
            cmds.Add(ec);
        }
        var set = obj.InternalCmdSet;
        if (set == null) { set = new CmdSet(); obj.InternalCmdSet = set; }
        try { set.Adds(cmds); } catch { }
    }
    public new void AddExitsForObject(GameObject obj) => AddExits(obj);

    // Port of nodes.py:734 add_objects
    public void AddObjects(List<GameObject> objs)
    {
        _nodeLock.EnterWriteLock();
        try
        {
            foreach (var o in objs) AddContent(o.Id);
            IsModified = true;
            foreach (var o in objs) { o.IsModified = true; }
        }
        finally { _nodeLock.ExitWriteLock(); }
        foreach (var o in objs) try { AddExits(o); } catch { }
    }
    // Port of nodes.py:747 add_object
    public new void AddObject(GameObject obj)
    {
        _nodeLock.EnterWriteLock();
        try { AddContent(obj.Id); obj.IsModified = true; IsModified = true; }
        finally { _nodeLock.ExitWriteLock(); }
        try { AddExits(obj); } catch { }
    }
    // Port of nodes.py:759 remove_object
    public new void RemoveObject(GameObject obj)
    {
        _nodeLock.EnterWriteLock();
        try { RemoveContent(obj.Id); IsModified = true; }
        finally { _nodeLock.ExitWriteLock(); }
        try { obj.InternalCmdSet?.RemoveByTag("exits"); } catch { }
    }

    // Port of nodes.py:770 msg_contents
    public void MsgContents(string? text, List<GameObject>? exclude = null, GameObject? fromObj = null, Dictionary<string, object?>? mapping = null, bool raiseErrors = false, string? msgType = null)
    {
        if (text == null) text = "";
        mapping ??= new Dictionary<string, object?>();
        var you = fromObj ?? this;
        if (!mapping.ContainsKey("you")) mapping["you"] = you;
        var contents = GetContents();
        HashSet<GameObject>? excl = exclude != null ? new HashSet<GameObject>(exclude) : null;
        foreach (var receiver in contents)
        {
            if (excl != null && excl.Contains(receiver)) continue;
            string outMsg;
            try { outMsg = FuncParser.Parse(text, you, receiver, mapping, raiseErrors); } catch { outMsg = text; }
            if (!string.IsNullOrEmpty(outMsg))
            {
                try
                {
                    var formatted = outMsg;
                    // safe format map handled inside Parse already for {you}
                    receiver.Msg(formatted, fromObj, null, false, msgType);
                }
                catch { }
            }
            else receiver.Msg("", fromObj, null, false, msgType);
        }
    }

    // Port of nodes.py:828 get_display_things
    public override string GetDisplayThings(GameObject? looker = null)
    {
        var contents = GetContents();
        var things = contents.Where(x => x.IsItem && x.Access(looker, "view")).ToList();
        var names = ContentUtils.GroupByName(things, looker);
        return !string.IsNullOrEmpty(names) ? $"{GameUtils.WrapXterm256("You see:", fg: 15, bold: true)} {names}\n" : "";
    }
    // Port of nodes.py:843 get_display_characters
    public string GetDisplayCharacters(GameObject? looker = null)
    {
        if (looker == null) return "";
        var contents = GetContents();
        var chars = contents.Where(x => (x.IsPc || x.IsNpc) && x != looker && x.Access(looker, "view")).ToList();
        var names = ContentUtils.GroupByName(chars, looker);
        return !string.IsNullOrEmpty(names) ? $"{GameUtils.WrapXterm256("Characters:", fg: 15, bold: true)} {names}\n" : "";
    }
    // Port of nodes.py:863 get_display_exits
    public string GetDisplayExits(GameObject? looker = null)
    {
        if (Links == null) return "";
        string names;
        _nodeLock.EnterReadLock();
        try { names = string.Join(", ", Links.Select(l => l.Name)); }
        finally { _nodeLock.ExitReadLock(); }
        return !string.IsNullOrEmpty(names) ? $"{GameUtils.WrapXterm256("Exits:", fg: 15, bold: true)} {names}\n" : "";
    }
    // Port of nodes.py:884 get_display_doors
    public string GetDisplayDoors(GameObject? looker = null)
    {
        var header = $"{GameUtils.WrapXterm256("Doors:", fg: 15, bold: true)} ";
        var nh = NodeHandler.GetCurrent();
        var d = nh?.GetDoors(Coord);
        if (d == null || d.Count == 0) return "";
        var parts = new List<string>();
        int idx = 0;
        foreach (var door in d.Values)
        {
            var s = door.Desc(Coord);
            if (idx != 0) s = s.ToLowerInvariant();
            parts.Add(s);
            idx++;
        }
        return header + string.Join(", ", parts) + "\n";
    }
    // Port of nodes.py:912 get_display_desc
    public string GetDisplayDesc(GameObject? looker = null)
    {
        _nodeLock.EnterReadLock();
        try { return !string.IsNullOrEmpty(Desc) ? Desc + "\n" : "You see nothing special.\n"; }
        finally { _nodeLock.ExitReadLock(); }
    }
    // Port of nodes.py:926 get_display_name
    public override string GetDisplayName(GameObject? looker = null)
    {
        _nodeLock.EnterReadLock();
        try
        {
            if (looker != null && looker.IsBuilder)
                return GameUtils.WrapTruecolor($"({Coord.Area},{Coord.X},{Coord.Y},{Coord.Z})\n", fg: 170);
        }
        finally { _nodeLock.ExitReadLock(); }
        return "";
    }
    // Port of nodes.py:945 return_appearance
    public override string ReturnAppearance(GameObject? looker = null)
    {
        if (looker == null) return "You see nothing here.";
        const string tmpl = "{name}{desc}{doors}{exits}{characters}{things}";
        return tmpl.Replace("{name}", GetDisplayName(looker))
                   .Replace("{desc}", GetDisplayDesc(looker))
                   .Replace("{doors}", GetDisplayDoors(looker))
                   .Replace("{exits}", GetDisplayExits(looker))
                   .Replace("{characters}", GetDisplayCharacters(looker))
                   .Replace("{things}", GetDisplayThings(looker));
    }
}
