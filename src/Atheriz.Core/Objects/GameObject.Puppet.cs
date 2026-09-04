using Atheriz.Core.Commands;
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Persistence.Dto;
using Atheriz.Core.Settings;
using Atheriz.Core.Utils;

namespace Atheriz.Core.Objects;

// Partial for puppet handling — Port of atheriz/commands/loggedin/puppet.py:110,138-142
// Wontfix document: snapshot only is_pc/privilege_level, quell/can_hear/is_mapable not part of snapshot by design.
public partial class GameObject
{
    // Port of target._puppet_restore: transient dict with is_pc/privilege_level only
    // Wontfix: puppet.py:110 restore_snapshot = {"is_pc": target.is_pc, "privilege_level": target.privilege_level}
    // quelled/can_hear/is_mapable are not part of the snapshot by design — documented here per AGENTS.md.
    internal Dictionary<string, object>? GetPuppetRestore()
    {
        _lock.EnterReadLock();
        try { return _puppetRestore != null ? new Dictionary<string, object>(_puppetRestore) : null; }
        finally { _lock.ExitReadLock(); }
    }

    internal void SetPuppetRestore(Dictionary<string, object> restore)
    {
        _lock.EnterWriteLock();
        try { _puppetRestore = new Dictionary<string, object>(restore); }
        finally { _lock.ExitWriteLock(); }
    }

    internal void ClearPuppetRestore()
    {
        _lock.EnterWriteLock();
        try { _puppetRestore = null; }
        finally { _lock.ExitWriteLock(); }
    }

    internal void RestorePuppetSnapshot(Dictionary<string, object> restore)
    {
        _lock.EnterWriteLock();
        try
        {
            if (restore.TryGetValue("is_pc", out var v) && v is bool b) _flags.IsPc = b;
            if (restore.TryGetValue("privilege_level", out var p) && p is Privilege priv) _privilege = priv;
            else if (restore.TryGetValue("privilege_level", out var p2) && p2 is int i) _privilege = (Privilege)i;
            _flags.IsModified = true;
            // Wontfix: do NOT restore quelled/can_hear/is_mapable per puppet.py:138-142
        }
        finally { _lock.ExitWriteLock(); }
    }

    /// <summary>
    /// Port of puppet handling: save snapshot (is_pc/privilege_level only) and wire session.
    /// Mirrors <c>atheriz/commands/loggedin/puppet.py:99-142</c> puppet command core.
    /// </summary>
    public bool Puppet(Session session, GameObject npc)
    {
        if (session == null || npc == null) return false;
        // Port of puppet.py:84-110 checks
        if (npc == this) return false;
        if (npc.IsAccount || npc.IsChannel || npc.IsNode) return false; // Port of _puppetable
        if (!npc.Access(this, "puppet")) return false; // Port of puppet.py:94
        Dictionary<string, object> snapshot;
        Privilege callerPriv;
        lock (session.Lock)
        {
            npc.SyncRoot.EnterReadLock();
            try
            {
                if (npc.Session != null && npc.Session != session) return false; // already puppeted
                if (npc.IsDeleted) return false;
                if (!npc.Access(this, "puppet")) return false;
                snapshot = new Dictionary<string, object> // Port of puppet.py:110
                {
                    ["is_pc"] = npc.IsPc,
                    ["privilege_level"] = npc.PrivilegeLevel
                };
                callerPriv = this.PrivilegeLevel;
            }
            finally { npc.SyncRoot.ExitReadLock(); }
        }
        // Port of puppet.py:112 caller.at_disconnect()
        try { this.AtDisconnect(); } catch { }
        lock (session.Lock)
        {
            npc.SyncRoot.EnterWriteLock();
            try
            {
                if (npc.Session != null && npc.Session != session) return false;
                if (npc.IsDeleted) return false;
                session.PuppetStack.Add((this, npc));
                npc.SetPuppetRestore(snapshot); // Port of puppet.py:138 target._puppet_restore = restore_snapshot
                npc.IsPc = true; // Port of puppet.py:139
                npc.PrivilegeLevel = callerPriv; // Port of puppet.py:140
                session.Puppet = npc;
                npc.Session = session; // Port of puppet.py:142
            }
            finally { npc.SyncRoot.ExitWriteLock(); }
        }
        try { npc.AtPuppet(this); } catch { }
        try { npc.AtPostPuppet(); } catch { }
        return true;
    }

    /// <summary>
    /// Port of unpuppet — mirrors <c>atheriz/commands/loggedin/puppet.py:164-192</c>.
    /// </summary>
    public bool Unpuppet(Session session)
    {
        if (session == null) return false;
        GameObject prev;
        GameObject target;
        lock (session.Lock)
        {
            if (session.PuppetStack.Count == 0) return false;
            var last = session.PuppetStack[session.PuppetStack.Count - 1];
            prev = last.Prev!;
            target = last.Target;
            session.PuppetStack.RemoveAt(session.PuppetStack.Count - 1);
        }
        var restore = target.GetPuppetRestore();
        try { target.AtUnpuppet(prev); } catch { }
        if (restore != null)
        {
            target.RestorePuppetSnapshot(restore);
            target.ClearPuppetRestore();
        }
        try { target.AtDisconnect(); } catch { }
        lock (session.Lock)
        {
            prev.SyncRoot.EnterWriteLock();
            try
            {
                session.Puppet = prev;
                prev.Session = session;
            }
            finally { prev.SyncRoot.ExitWriteLock(); }
        }
        try { prev.AtPostPuppet(); } catch { }
        return true;
    }

    // Hook stubs for puppet lifecycle — Port of base_obj.py:1447-1512
    // at_post_puppet, at_puppet, at_unpuppet
    public virtual void AtPostPuppet() // Port of base_obj.py:1447 at_post_puppet
    {
        // Port of base_obj.py:1447 at_post_puppet — verbatim faithful
        Hookable("at_post_puppet", () => 0);
        // Port of base_obj.py:1455 self.is_connected = True (outside lock per Python)
        IsConnected = true;
        // Port of base_obj.py:1456 self.session.connection.send_command("logged_in")
        try
        {
            var sess = Session;
            var conn = sess?.Connection;
            if (conn != null)
                conn.SendCommand("logged_in");
        }
        catch { }
        // Port of base_obj.py:1457-1460 with self.lock: for c in self.channels: if channel := get(c): channel[0].add_listener(self)
        try
        {
            List<int> channelsCopy;
            _lock.EnterReadLock();
            try { channelsCopy = new List<int>(_channels); }
            finally { _lock.ExitReadLock(); }
            foreach (var c in channelsCopy)
            {
                try
                {
                    var chObjs = ObjectRegistry.Get(c);
                    if (chObjs.Count > 0)
                    {
                        var ch = chObjs[0];
                        if (ch is Channel channelObj)
                            channelObj.AddListener(this);
                        else if (ch.IsChannel)
                        {
                            try { ((dynamic)ch).AddListener((dynamic)this); } catch { }
                        }
                    }
                }
                catch { }
            }
        }
        catch { }
        // Port of base_obj.py:1461-1462 if channel := get_server_channel(): channel.msg(f"{wrap_xterm256(self.name, fg=15, bold=True)} (#{self.id}) has logged in.")
        try
        {
            var serverChannel = GlobalServices.GetServerChannel();
            if (serverChannel != null)
            {
                var wrapped = GameUtils.WrapXterm256(Name ?? "", fg: 15, bold: true);
                serverChannel.Msg($"{wrapped} (#{Id}) has logged in.");
            }
        }
        catch { }
        // Port of base_obj.py:1463-1470 cs = get_loggedin_cmdset(); commands = [cmd.key for cmd in cs.get_all() if cmd.access(self) and not cmd.hide]; try: SOCIALS_DICT
        List<string> commands = new();
        try
        {
            var cs = GlobalServices.GetLoggedInCmdSet();
            foreach (var cmd in cs.GetAll())
            {
                try
                {
                    if (!cmd.Hide && cmd.Access(this))
                        commands.Add(cmd.Key);
                }
                catch { }
            }
        }
        catch { }
        try
        {
            foreach (var key in SocialsCommand.SocialsDict.Keys)
                commands.Add(key);
        }
        catch { }
        // Port of base_obj.py:1471 self.msg(player_commands=commands)
        try
        {
            var sess = Session;
            var conn = sess?.Connection;
            if (conn != null)
            {
                // Port of self.msg(player_commands=commands) -> connection.send_command("player_commands", commands)
                conn.SendCommand("player_commands", new List<object?> { commands }, null);
            }
        }
        catch { }
        // Port of base_obj.py:1472 self.msg(f"You become {wrap_xterm256(self.name, fg=15, bold=True)}.")
        try
        {
            var wrapped = GameUtils.WrapXterm256(Name ?? "", fg: 15, bold: true);
            Msg($"You become {wrapped}.");
        }
        catch { }
        // Port of base_obj.py:1473-1485 if self.location: map handling + move_to + map_enable + render
        try
        {
            LocationRef locRef;
            _lock.EnterReadLock();
            try { locRef = _location; }
            finally { _lock.ExitReadLock(); }
            bool hasLocation = locRef != null && !(locRef is LocationRef.NullLocation);
            if (hasLocation)
            {
                // Port of base_obj.py:1474-1478 if settings.MAP_ENABLED: mh.add_listener(self); if self.is_mapable: mh.add_mapable(self)
                bool mapEnabledSettings = false;
                try { mapEnabledSettings = AtherizSettings.Global.MapEnabled; } catch { }
                if (mapEnabledSettings)
                {
                    try
                    {
                        var mh = GlobalServices.GetMapHandler();
                        try { mh.AddListener(this); } catch { }
                        bool isMapable = false;
                        try { isMapable = IsMapable; } catch { }
                        if (isMapable)
                        {
                            try { mh.AddMapable(this); } catch { }
                        }
                    }
                    catch { }
                }
                // Port of base_obj.py:1479 self.move_to(self.location, announce=False)
                try
                {
                    var destObj = ResolveLocationObject();
                    object? destArg = null;
                    if (destObj != null)
                        destArg = destObj;
                    else if (locRef is LocationRef.CoordLocation cl)
                        destArg = cl.Coord;
                    else if (locRef is LocationRef.ObjectLocation)
                        destArg = locRef;
                    else
                        destArg = locRef;
                    if (destArg != null && !(destArg is LocationRef.NullLocation))
                    {
                        // announce=False to avoid "walks in" spam — test_puppet_announce expects no walk broadcast
                        MoveTo(destArg, announce: false);
                    }
                }
                catch { }
                // Port of base_obj.py:1480-1485 if settings.MAP_ENABLED and self.map_enabled: self.msg(map_enable=""); mh = get_map_handler(); mi = mh.get_mapinfo(...); if mi: mi.render(True)
                bool mapEnabled2 = false;
                bool selfMapEnabled = false;
                try { mapEnabled2 = AtherizSettings.Global.MapEnabled; } catch { }
                try { selfMapEnabled = MapEnabled; } catch { try { selfMapEnabled = IsMapable; } catch { } }
                if (mapEnabled2 && selfMapEnabled)
                {
                    try
                    {
                        var sess = Session;
                        var conn = sess?.Connection;
                        if (conn != null)
                        {
                            // Port of self.msg(map_enable="") -> connection.send_command("map_enable","")
                            conn.SendCommand("map_enable", new List<object?> { "" }, null);
                        }
                    }
                    catch { }
                    try
                    {
                        Coord? coord = null;
                        // Re-resolve after move — location may have been re-wired to same Node
                        _lock.EnterReadLock();
                        try { locRef = _location; }
                        finally { _lock.ExitReadLock(); }
                        if (locRef is LocationRef.CoordLocation cl2)
                            coord = cl2.Coord;
                        else
                        {
                            var locObj2 = ResolveLocationObject();
                            if (locObj2 is Node n)
                                coord = n.Coord;
                            else if (locObj2 != null)
                            {
                                var inner = locObj2.Location;
                                if (inner is LocationRef.CoordLocation icl)
                                    coord = icl.Coord;
                            }
                        }
                        if (coord.HasValue)
                        {
                            var mh2 = GlobalServices.GetMapHandler();
                            var mi = mh2.GetMapInfo(coord.Value.Area, coord.Value.Z);
                            if (mi != null)
                                mi.Render(true);
                        }
                    }
                    catch { }
                }
            }
        }
        catch { }
    }

    public virtual void AtPuppet(GameObject caller) // Port of base_obj.py:1488 at_puppet
    {
        Hookable("at_puppet", () => 0, caller);
    }

    public virtual void AtUnpuppet(GameObject caller) // Port of base_obj.py:1501 at_unpuppet
    {
        Hookable("at_unpuppet", () => 0, caller);
    }

    public virtual void AtDisconnect() // Port of base_obj.py:690 at_disconnect
    {
        Hookable("at_disconnect", () => 0);
        IsConnected = false;
        Session = null;
    }

    public virtual void AtCreate() // Port of base_obj.py:485 at_create
    {
        Hookable("at_create", () => 0);
    }

    public virtual bool AtDelete(GameObject caller) // Port of base_obj.py:467 at_delete
    {
        return Hookable("at_delete", () => Access(caller, "delete"), caller);
    }

    public virtual void AtTick() // Port of base_obj.py:676 at_tick
    {
        Hookable("at_tick", () => 0);
    }

    public virtual void AtSolarEvent(string message) // Port of time.py solar
    {
        Hookable("at_solar_event", () => 0, message);
    }
    public virtual void AtLunarEvent(string message) // Port of time.py lunar
    {
        Hookable("at_lunar_event", () => 0, message);
    }
    public virtual void AtAlarm(Globals.GameTime.GameTimeInfo time, Dictionary<string, System.Text.Json.JsonElement>? data) // Port of time.py alarm
    {
        Hookable("at_alarm", () => 0, time, data);
    }

    public virtual void AtInit() // Port of base_obj.py:669 at_init
    {
        Hookable("at_init", () => 0);
    }

    // Port of base_obj.py:467 delete + object deletion lifecycle — caller optional for Account parity
    public virtual (int count, List<object> ops)? Delete(GameObject? caller = null, bool recursive = false)
    {
        if (caller != null && !AtDelete(caller)) return null;
        // quick check already deleted
        _lock.EnterReadLock();
        try { if (_flags.IsDeleted) return null; }
        finally { _lock.ExitReadLock(); }

        var ops = new List<object>();
        var toDelete = new List<GameObject>();

        if (recursive)
        {
            // faithful port of base_obj.delete _collect_recursive with MAX_SEARCH_DEPTH
            int maxDepth = MaxSearchDepth;
            var seen = new HashSet<int>();
            var stack = new Stack<(GameObject obj, int depth)>();
            stack.Push((this, 0));
            var order = new List<GameObject>();
            var truncated = new List<GameObject>();
            while (stack.Count > 0)
            {
                var (obj, depth) = stack.Pop();
                if (!seen.Add(obj.Id)) continue;
                order.Add(obj);
                // snapshot contents safely
                List<int> contentIds;
                obj._lock.EnterReadLock();
                try { contentIds = new List<int>(obj._contents); }
                finally { obj._lock.ExitReadLock(); }
                foreach (var cid in contentIds)
                {
                    var cObjs = ObjectRegistry.Get(cid);
                    var content = cObjs.FirstOrDefault();
                    if (content == null) continue;
                    if (seen.Contains(content.Id)) continue;
                    if (truncated.Any(t => t.Id == content.Id)) continue;
                    if (depth + 1 >= maxDepth)
                    {
                        truncated.Add(content);
                        continue;
                    }
                    stack.Push((content, depth + 1));
                }
            }
            // reversed order for deletion (children first)
            order.Reverse();
            // Actually Python's order is collected then reversed: order is DFS pre-order, reversed gives children before parent.
            // Our order currently is pop order (which is DFS). Reversing gives leaves first? Let's mimic Python: it appends in visit order, then reversed iteration adds to to_delete.
            // We've added in pop order; reversing will give appropriate.
            foreach (var obj in order)
            {
                toDelete.Add(obj);
                bool isTemp;
                obj._lock.EnterReadLock();
                try { isTemp = obj._flags.IsTemporary; }
                finally { obj._lock.ExitReadLock(); }
                if (!isTemp)
                    ops.Add(obj.GetDelOps());
            }
            // handle truncated survivors: if survivor location's id is in seen, detach
            foreach (var survivor in truncated)
            {
                if (seen.Contains(survivor.Id)) continue;
                // get survivor's location ref
                LocationRef locRef;
                survivor._lock.EnterReadLock();
                try { locRef = survivor._location; }
                finally { survivor._lock.ExitReadLock(); }
                int? locId = null;
                GameObject? locObj = null;
                if (locRef is LocationRef.ObjectLocation ol) { locId = ol.ObjectId; locObj = ObjectRegistry.Get(ol.ObjectId).FirstOrDefault(); }
                else if (locRef is LocationRef.CoordLocation) { /* node case - not needed for container chain test */ }
                else { continue; }
                if (locId.HasValue && seen.Contains(locId.Value))
                {
                    try { locObj?.RemoveContent(survivor.Id); } catch {}
                    try
                    {
                        survivor._lock.EnterWriteLock();
                        try { survivor._location = LocationRef.NullLocation.Instance; survivor._flags.IsModified = true; }
                        finally { survivor._lock.ExitWriteLock(); }
                    }
                    catch { try { survivor.Location = LocationRef.NullLocation.Instance; } catch {} }
                }
            }
            // actually need to ensure toDelete contains order reversed already; truncated survivors stay alive
            // Now physically delete each in toDelete
            foreach (var obj in toDelete)
            {
                // mimic _delete_object minimal: remove from location, clear followers/channels, mark deleted, remove registry
                // detach from location if any
                try
                {
                    var loc = obj.ResolveLocationObject();
                    if (loc != null)
                    {
                        try { loc.RemoveContent(obj.Id); } catch {}
                    }
                } catch {}
                try
                {
                    obj._lock.EnterWriteLock();
                    try
                    {
                        if (!obj._flags.IsDeleted)
                        {
                            obj._flags.IsDeleted = true;
                            obj._flags.IsModified = true;
                        }
                        // clear location
                        // keep location as Null for survivors? For deleted ones, set to null as well but they are deleted anyway
                        // Don't override truncated handling for toDelete objects
                    }
                    finally { obj._lock.ExitWriteLock(); }
                } catch {}
                ObjectRegistry.RemoveObject(obj);
            }
            // ops already collected; return
            return (toDelete.Count, ops);
        }
        else
        {
            // non-recursive: move contents to self's location (Python _move_contents)
            GameObject? loc = null;
            try { loc = this.ResolveLocationObject(); } catch {}
            List<int> contentIds;
            _lock.EnterReadLock();
            try { contentIds = new List<int>(_contents); }
            finally { _lock.ExitReadLock(); }
            var contentObjs = contentIds.Select(id => ObjectRegistry.Get(id).FirstOrDefault()).Where(o => o != null).Cast<GameObject>().ToList();
            foreach (var content in contentObjs.ToList())
            {
                bool moved = false;
                try { moved = content.MoveTo(loc, force: false, announce: false); } catch { moved = false; }
                if (!moved)
                {
                    // if still located at this, detach
                    bool stillAtThis = false;
                    content._lock.EnterReadLock();
                    try
                    {
                        if (content._location is LocationRef.ObjectLocation ol2 && ol2.ObjectId == this.Id) stillAtThis = true;
                    }
                    finally { content._lock.ExitReadLock(); }
                    if (stillAtThis)
                    {
                        try { this.RemoveContent(content.Id); } catch {}
                        try
                        {
                            content._lock.EnterWriteLock();
                            try { content._location = LocationRef.NullLocation.Instance; content._flags.IsModified = true; }
                            finally { content._lock.ExitWriteLock(); }
                        } catch {}
                    }
                    // then collect recursively (delete content and its children)
                    var r = content.Delete(caller, true);
                    if (r != null) ops.AddRange(r.Value.ops);
                }
                else
                {
                    // moved successfully, ensure removed from this._contents (MoveTo already handled via destination add, but old loc removal already done)
                    // No delete
                }
            }
            // now delete self
            _lock.EnterWriteLock();
            try
            {
                if (_flags.IsDeleted) return null;
                _flags.IsDeleted = true;
                _flags.IsModified = true;
            }
            finally { _lock.ExitWriteLock(); }
            // detach from location
            try
            {
                var loc2 = this.ResolveLocationObject();
                if (loc2 != null) loc2.RemoveContent(this.Id);
            } catch {}
            if (!this.IsTemporary)
                ops.Add(this.GetDelOps());
            // include self in count
            ObjectRegistry.RemoveObject(this);
            // toDelete includes self plus any recursively deleted via Move failure path already added to ops
            // count is 1 + number of recursively deleted via ops? But ops already includes their del ops; count should be 1 plus those counts? Simplify: count = 1 + (ops.Count) // but ops.Count includes one per deleted object
            // For non-recursive test, we just return 1
            return (1, ops);
        }
    }
}
