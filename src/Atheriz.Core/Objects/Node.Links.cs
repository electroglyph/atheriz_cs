namespace Atheriz.Core.Objects;

public partial class Node
{
    public void AddNoun(string key, string desc)
    {
        SyncRoot.EnterWriteLock();
        try
        {
            // No case-variant hunt: the OrdinalIgnoreCase dict cannot hold two
            // keys differing only by case, and the indexer overwrites anyway.
            _nouns[key.ToLowerInvariant()] = desc;
            IsModified = true;
        }
        finally { SyncRoot.ExitWriteLock(); }
    }
    // Atomic insert-if-absent core for the noun command: the check and the
    // insert share one write hold, so concurrent adds of the same new noun
    // cannot both report "Added". Returns true when it inserted (absent),
    // false when the noun already exists (existing text kept — the caller
    // overwrites via AddNoun and reports "Updated"). Same lowered-key storage
    // as AddNoun, so bytes match either path.
    public bool AddNounIfAbsent(string key, string desc)
    {
        SyncRoot.EnterWriteLock();
        try
        {
            var lowered = key.ToLowerInvariant();
            if (_nouns.ContainsKey(lowered)) return false;
            _nouns[lowered] = desc;
            IsModified = true;
            return true;
        }
        finally { SyncRoot.ExitWriteLock(); }
    }
    public void RemoveNoun(string key)
    {
        SyncRoot.EnterWriteLock();
        try
        {
            _nouns.Remove(key.ToLowerInvariant());
            IsModified = true;
        }
        finally { SyncRoot.ExitWriteLock(); }
    }
    public string? GetNoun(string key)
    {
        SyncRoot.EnterReadLock();
        try
        {
            if (_nouns.TryGetValue(key.ToLowerInvariant(), out var v)) return v;
            // the OrdinalIgnoreCase dict makes a post-TryGetValue
            // manual scan unreachable — deleted.
            return null;
        }
        finally { SyncRoot.ExitReadLock(); }
    }
    public override string ToString() => $"Node: {Coord}";

    public override List<GameObject> Search(string query, bool recursive = true, GameObject? looker = null)
        => ContentUtils.Search(this, query, id => ObjectRegistry.Get(id).FirstOrDefault(), recursive, looker ?? this);

    public List<NodeLink> GetLinks()
    {
        SyncRoot.EnterReadLock();
        try { return _links.ToList(); }
        finally { SyncRoot.ExitReadLock(); }
    }
    // existence guards must agree with lookups.
    public bool HasLinkName(string name) => FindLink(name) is not null;
    public NodeLink? GetLinkByName(string name) => FindLink(name);
    // Shared alias-including lookup for HasLinkName/GetLinkByName only.
    // AddLinkIfAbsent guards stay name-only (alias collisions must NOT block
    // link creation), so they must not route through this.
    private NodeLink? FindLink(string name)
    {
        SyncRoot.EnterReadLock();
        try { return _links.FirstOrDefault(l => l.Name.Equals(name, StringComparison.OrdinalIgnoreCase) || l.Aliases.Any(a => a.Equals(name, StringComparison.OrdinalIgnoreCase))); }
        finally { SyncRoot.ExitReadLock(); }
    }
    // Name-only guard shared by the two AddLinkIfAbsent checks (same lock-held
    // shape, no alias matching).
    private bool HasLinkNameNoLock(string name)
        => _links.Any(l => l.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    public NodeLink? GetLink(string name) => GetLinkByName(name);
    // Look-command noun/link fallback (moved out of LookCommand.Run intact):
    // noun text first, then the linked node's appearance. Null when neither
    // matches (a found link with no node falls through to "no match" at the
    // call site, as before). The view gate stays at the call site — gate
    // before resolve, never after.
    internal string? TryResolveLookTarget(string name, GameObject looker)
    {
        var noun = GetNoun(name);
        if (noun is not null) return noun;
        var link = FindLink(name);
        if (link is null) return null;
        var ln = NodeHandler.GetCurrent()?.GetNode(link.Coord);
        return ln?.ReturnAppearance(looker);
    }
    public NodeArea? Area => NodeHandler.GetCurrent()?.GetArea(Coord.Area);
    public NodeGrid? Grid
    {
        get
        {
            var nh = NodeHandler.GetCurrent();
            var a = nh?.GetArea(Coord.Area);
            return a?.GetGrid(Coord.Z);
        }
    }
// coord-derived, read-only in Python (no setter).
    // Override (not new) so GameObject-typed refs see the same value as Node-typed refs.
    // The setter is intentionally a no-op: Python's __setstate__ raw-dict restore writes
    // 'name' into the instance dict where it is shadowed by the read-only data
    // descriptor, and JSON round-trips write the coord string back. A throwing setter
    // would break deserialization and the ported regression tests, so the write is
    // accepted and ignored (never observable via the getter).
    public override string Name { get => Coord.ToString(); set { } }

// int | Script accepted (same as base AddScript
    // overloads); records into the shared base scripts set.
    public void AddScript(object script)
    {
        int id = script is int i ? i : script is GameObject go ? go.Id : -1;
        if (id == -1) return;
        var objs = ObjectRegistry.Get(id);
        if (objs.Count == 0) return;
        // The id joins the scripts set only for a real Script: recording a
        // non-script (or missing) id would persist a junk entry that
        // RemoveScript can never unhook.
        if (objs[0] is Script s) { s.InstallHooks(this); AddScriptId(id); }
    }
    public void RemoveScript(object script)
    {
        int id = script is int i ? i : script is GameObject go ? go.Id : -1;
        if (id == -1) return;
        var objs = ObjectRegistry.Get(id);
        if (objs.Count > 0 && objs[0] is Script s) s.RemoveHooks(this);
        RemoveScriptId(id);
    }
    public NodeLink? GetRandomLink()
    {
        // Snapshot copy under the read lock: callers read Name/Coord
        // off-lock, which tore against a concurrent transition remap. The
        // copy observes one full generation; identity with the live link is
        // not preserved (compare by value).
        SyncRoot.EnterReadLock();
        try
        {
            if (_links.Count == 0) return null;
            var live = _links[Random.Shared.Next(_links.Count)];
            return new NodeLink(live.Name, live.Coord, live.Aliases);
        }
        finally { SyncRoot.ExitReadLock(); }
    }
    // Insert core shared with AddLinkIfAbsent: caller holds the write lock.
    private bool AddLinkRawNoLock(NodeLink link)
    {
        if (_links.Count > 0 && _links.Contains(link)) return false;
        if (_links.Count > 0 && HasLinkNameNoLock(link.Name)) return false;
        if (_links.Count == 0) _links = [link];
        else _links.Add(link);
        IsModified = true;
        return true;
    }
    // Coordinate rewrite core for NodeGrid.ApplyMoves: caller holds the node
    // write lock. Rewrites each link Coord through oldToNew in one pass and
    // marks modified when anything moved.
    internal bool RemapLinkCoordsNoLock(Dictionary<Coord, Coord> oldToNew)
    {
        bool changed = false;
        foreach (var l in _links)
        {
            if (oldToNew.TryGetValue(l.Coord, out var nc) && !nc.Equals(l.Coord))
            {
                l.Coord = nc;
                changed = true;
            }
        }
        if (changed) IsModified = true;
        return changed;
    }
    // Publish tail shared with AddLinkIfAbsent: runs lock-free after release.
    // logContext keeps the per-caller label.
    private void PublishLinkAdded(NodeLink link, string logContext)
    {
        foreach (var o in GetContents()) try { AddExits(o); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed " + logContext + ": " + logEx.Message, "Node"); }
        if (link.Coord.Area != Coord.Area)
        {
            var nh = NodeHandler.GetCurrent();
            nh?.AddTransition(new Transition(Coord, link.Coord, link.Name));
        }
    }
    public void AddLink(NodeLink link)
    {
        bool added;
        SyncRoot.EnterWriteLock();
        try { added = AddLinkRawNoLock(link); }
        finally { SyncRoot.ExitWriteLock(); }
        if (!added) return;
        PublishLinkAdded(link, "Node.AddLink");
    }
    public bool AddLinkIfAbsent(string name, Func<NodeLink> factory)
    {
        // Guards fold case like HasLinkName/GetLinkByName — ordinal guards let
        // AddLinkIfAbsent("North") + AddLinkIfAbsent("north") install a shadowed
        // unreachable link.
        SyncRoot.EnterReadLock();
        try { if (HasLinkNameNoLock(name)) return false; }
        finally { SyncRoot.ExitReadLock(); }
        var link = factory();
        bool added;
        SyncRoot.EnterWriteLock();
        try
        {
            if (HasLinkNameNoLock(name)) return false;
            // inline add_link logic but avoid double lock
            added = AddLinkRawNoLock(link);
        }
        finally { SyncRoot.ExitWriteLock(); }
        if (!added) return false;
        PublishLinkAdded(link, "Node.AddLinkIfAbsent");
        return true;
    }
    public void RemoveLink(string name)
    {
        NodeLink? found = null;
        SyncRoot.EnterWriteLock();
        try
        {
            // RemoveLink folds case like the lookups — RemoveLink("NORTH") must
            // find the "north" that GetLinkByName finds.
            var idx = _links.FindIndex(l => l.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0) { found = _links[idx]; _links.RemoveAt(idx); IsModified = true; }
        }
        finally { SyncRoot.ExitWriteLock(); }
        if (found is not null && Coord.Area != found.Coord.Area)
        {
            var nh = NodeHandler.GetCurrent();
            nh?.RemoveTransition(Coord, found.Coord);
        }
        // also remove exits from occupants
        if (found is not null)
            foreach (var o in GetContents()) try { o.InternalCmdSet?.RemoveByTag("exits"); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed Node.RemoveLink: " + logEx.Message, "Node"); }
    }

    public void AddExits(GameObject obj)
    {
        obj.InternalCmdSet?.RemoveByTag("exits");
        List<NodeLink> snap;
        SyncRoot.EnterReadLock();
        try { snap = _links.ToList(); }
        finally { SyncRoot.ExitReadLock(); }
        if (snap.Count == 0) return;
        List<Command> cmds = [];
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
        if (set is null) { set = new CmdSet(); obj.InternalCmdSet = set; }
        try { set.Adds(cmds); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed Node.AddExits: " + logEx.Message, "Node"); }
    }
    public new void AddExitsForObject(GameObject obj) => AddExits(obj);

    public void AddObjects(List<GameObject> objs)
    {
        SyncRoot.EnterWriteLock();
        try
        {
            // Same deleted-parent refusal as the singular path: a deleted
            // node accepts no new contents.
            if (IsDeleted) return;
            foreach (var o in objs) AddContent(o.Id);
            IsModified = true;
            foreach (var o in objs) { o.IsModified = true; }
        }
        finally { SyncRoot.ExitWriteLock(); }
        foreach (var o in objs) try { o.Location = Persistence.Dto.LocationRef.FromCoord(Coord); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed Node.AddObjects: " + logEx.Message, "Node"); }
        foreach (var o in objs) try { AddExits(o); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed Node.AddObjects: " + logEx.Message, "Node"); }
    }
    public new void AddObject(GameObject obj)
    {
        SyncRoot.EnterWriteLock();
        try
        {
            // Same deleted-parent refusal as the container path: a deleted node
            // accepts no new contents (membership would point at a gone node).
            if (IsDeleted) return;
            AddContent(obj.Id); obj.IsModified = true; IsModified = true;
        }
        finally { SyncRoot.ExitWriteLock(); }
        // Like MoveTo into a node, membership implies the node's coord.
        try { obj.Location = Persistence.Dto.LocationRef.FromCoord(Coord); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed Node.AddObject: " + logEx.Message, "Node"); }
        try { AddExits(obj); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed Node.AddObject: " + logEx.Message, "Node"); }
    }
    public new void RemoveObject(GameObject obj)
    {
        SyncRoot.EnterWriteLock();
        try { RemoveContent(obj.Id); IsModified = true; }
        finally { SyncRoot.ExitWriteLock(); }
        try
        {
            if (obj.Location is Persistence.Dto.LocationRef.CoordLocation cl && cl.Coord.Equals(Coord))
                obj.Location = Persistence.Dto.LocationRef.NullLocation.Instance;
        }
        catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed Node.RemoveObject: " + logEx.Message, "Node"); }
        try { obj.InternalCmdSet?.RemoveByTag("exits"); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed Node.RemoveObject: " + logEx.Message, "Node"); }
    }

    // Broadcast loop shared with the base overload via
    // ContentUtils.EmitToContents; only the receiver source (live contents)
    // stays here. Node delivery keeps catch-all fallback semantics
    // (nodeSemantics: true): any parser failure falls back to raw text and
    // raiseErrors never throws — unlike the base overload.
    public void MsgContents(string? text, IEnumerable<GameObject>? exclude = null, GameObject? fromObj = null, IDictionary<string, object?>? mapping = null, bool raiseErrors = false, string? msgType = null)
        => BroadcastToContents(text, fromObj, mapping, exclude, raiseErrors, msgType);

    public override void BroadcastToContents(string? text, GameObject? fromObj = null, IDictionary<string, object?>? mapping = null, IEnumerable<GameObject>? exclude = null, bool raiseErrors = false, string? msgType = null, Func<int, GameObject?>? resolver = null)
    {
        _ = resolver;
        ContentUtils.EmitToContents(GetContents(), this, text, fromObj, mapping, exclude, msgType, raiseErrors, nodeSemantics: true);
    }

    public override string GetDisplayThings(GameObject? looker = null)
    {
        var contents = GetContents();
        var things = contents.Where(x => x.IsItem && x.Access(looker, "view")).ToList();
        var names = ContentUtils.GroupByName(things, looker);
        return !string.IsNullOrEmpty(names) ? $"{GameUtils.WrapXterm256("You see:", fg: 15, bold: true)} {names}\n" : "";
    }
    public string GetDisplayCharacters(GameObject? looker = null)
    {
        if (looker is null) return "";
        var contents = GetContents();
        var chars = contents.Where(x => (x.IsPc || x.IsNpc) && x != looker && x.Access(looker, "view")).ToList();
        var names = ContentUtils.GroupByName(chars, looker);
        return !string.IsNullOrEmpty(names) ? $"{GameUtils.WrapXterm256("Characters:", fg: 15, bold: true)} {names}\n" : "";
    }
    public string GetDisplayExits(GameObject? looker = null)
    {
        string names;
        SyncRoot.EnterReadLock();
        try { names = string.Join(", ", _links.Select(l => l.Name)); }
        finally { SyncRoot.ExitReadLock(); }
        return !string.IsNullOrEmpty(names) ? $"{GameUtils.WrapXterm256("Exits:", fg: 15, bold: true)} {names}\n" : "";
    }
    public string GetDisplayDoors(GameObject? looker = null)
    {
        var header = $"{GameUtils.WrapXterm256("Doors:", fg: 15, bold: true)} ";
        var nh = NodeHandler.GetCurrent();
        var d = nh?.GetDoors(Coord);
        if (d is null || d.Count == 0) return "";
        List<string> parts = [];
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
    public string GetDisplayDesc(GameObject? looker = null)
    {
        SyncRoot.EnterReadLock();
        try { return !string.IsNullOrEmpty(Desc) ? Desc + "\n" : "You see nothing special.\n"; }
        finally { SyncRoot.ExitReadLock(); }
    }
    public override string GetDisplayName(GameObject? looker = null)
    {
        // read the looker's flag BEFORE taking the node lock (self->looker
        // nesting under concurrency). The builder bit decides everything.
        if (looker is null || !looker.IsBuilder) return "";
        SyncRoot.EnterReadLock();
        try
        {
            return GameUtils.WrapTruecolor($"({Coord.Area},{Coord.X},{Coord.Y},{Coord.Z})\n", fg: 170);
        }
        finally { SyncRoot.ExitReadLock(); }
    }
    public override string ReturnAppearance(GameObject? looker = null)
    {
        if (looker is null) return "You see nothing here.";
        if (IsDeleted) return "You see nothing here.";
        // Hookable like the base: game code overriding return_appearance must
        // see node renders too.
        return Hookable(HookName.ReturnAppearance, () =>
        {
            // Parts concatenate with no separators; each part emits literally, so
            // a placeholder token inside user-controlled text (e.g. "{desc}" in a
            // name) stays as-is instead of being swallowed by a later pass.
            return string.Concat(GetDisplayName(looker), GetDisplayDesc(looker), GetDisplayDoors(looker), GetDisplayExits(looker), GetDisplayCharacters(looker), GetDisplayThings(looker));
        }, looker);
    }
}
