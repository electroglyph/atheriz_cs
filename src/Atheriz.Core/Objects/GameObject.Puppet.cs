using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Persistence.Dto;

namespace Atheriz.Core.Objects;

// Partial for puppet handling — Port of atheriz/commands/loggedin/puppet.py:110,138-142
// Wontfix document: snapshot only is_pc/privilege_level, quell/can_hear/is_mapable not part of snapshot by design.
public partial class GameObject
{
    // Port of target._puppet_restore: transient dict with is_pc/privilege_level only
    // Wontfix: puppet.py:110 restore_snapshot = {"is_pc": target.is_pc, "privilege_level": target.privilege_level}
    // quelled/can_hear/is_mapable are not part of the snapshot by design — documented here per AGENTS.md.
    // Transient puppet-restore dict keys (in-memory only, never persisted).
    // Consts make typos compile-time errors; values stay byte-identical.
    internal const string PuppetRestoreIsPcKey = "is_pc";
    internal const string PuppetRestorePrivilegeKey = "privilege_level";

    // Shared suppressed-log wrapper for the hook/state fan-out below: every site
    // catches exactly Exception, logs only logEx.Message under its own context
    // prefix, and swallows. Takes no locks — call sites keep today's boundaries
    // (game hooks run outside session.Lock), so this is leaf replacement only.
    private static void Suppress(string context, Action action)
    {
        try { action(); }
        catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed GameObject." + context + ": " + logEx.Message, "GameObject"); }
    }

    // Snapshot + null-guard + send core for the three AtPostPuppet session
    // commands below (logged_in, player_commands, map_enable). The Session read
    // and the send stay lock-free, as today. Wire strings are byte-identical;
    // the (command, args, kwargs) triple preserves each site's payload — the
    // bare "logged_in" call used the params overload, which forwards as
    // (empty-list, null), and both transports normalize null/empty payloads
    // identically, so routing it through the 3-arg overload is wire-identical.
    private void SendSessionCommand(string command, List<object?>? args = null, Dictionary<string, object?>? kwargs = null)
    {
        var sess = Session;
        var conn = sess?.Connection;
        if (conn is not null)
            conn.SendCommand(command, args, kwargs);
    }

    // Single-observation read of the mutable global map flag. Both AtPostPuppet
    // gates keep their own call (two observations — the global can flip
    // mid-login), each falling back to false exactly as today. Read helper
    // only: never hoist the two calls into one.
    private static bool TryGetGlobalMapEnabled(out bool enabled)
    {
        try { enabled = AtherizSettings.Global.MapEnabled; return true; }
        catch { enabled = false; return false; }
    }

    internal Dictionary<string, object>? GetPuppetRestore()
        => Read(() => _puppetRestore is not null ? new Dictionary<string, object>(_puppetRestore) : null);

    internal void SetPuppetRestore(Dictionary<string, object> restore)
        => Write(() => _puppetRestore = new Dictionary<string, object>(restore));

    internal void ClearPuppetRestore()
        => Write(() => _puppetRestore = null);

    internal void RestorePuppetSnapshot(Dictionary<string, object> restore)
    {
        _lock.EnterWriteLock();
        try
        {
            if (restore.TryGetValue(PuppetRestoreIsPcKey, out var v) && v is bool b) _flags.IsPc = b;
            // Single lookup: boxed Privilege never matches `is int` and vice
            // versa, so ordered arms decide exactly what the double lookup did.
            if (restore.TryGetValue(PuppetRestorePrivilegeKey, out var p))
            {
                switch (p)
                {
                    case Privilege priv:
                        _privilege = priv;
                        break;
                    case int i:
                        _privilege = (Privilege)i;
                        break;
                }
            }
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
        if (session is null || npc is null) return false;
        // Port of puppet.py:84-110 checks (`target is caller` plus same-id
        // reload instances, which share identity through the registry).
        if (npc == this) return false;
        if (npc.IsAccount || npc.IsChannel || npc.IsNode) return false; // Port of _puppetable
        if (!npc.Access(this, "puppet")) return false; // Port of puppet.py:94
        // Fast-path peek (unlocked, advisory only): skip work when the target
        // is obviously unavailable. Authoritative checks run inside the
        // single critical section below, with the caller's session intact.
        if ((npc.Session is not null && npc.Session != session) || npc.IsDeleted) return false;
        lock (session.Lock)
        {
            npc.SyncRoot.EnterReadLock();
            Dictionary<string, object> snapshot;
            Privilege callerPriv;
            try
            {
                if (npc.Session is not null && npc.Session != session) return false; // already puppeted
                if (npc.IsDeleted) return false;
                if (!npc.Access(this, "puppet")) return false;
                snapshot = new Dictionary<string, object> // Port of puppet.py:110
                {
                    [PuppetRestoreIsPcKey] = npc.IsPc,
                    [PuppetRestorePrivilegeKey] = npc.PrivilegeLevel
                };
                callerPriv = this.PrivilegeLevel;
            }
            finally { npc.SyncRoot.ExitReadLock(); }
            // Port of puppet.py:112 caller.at_disconnect() — state part runs
            // here (session intact for the checks above); the game hook fires
            // after release below. Hooks must never run under session.Lock.
            IsConnected = false;
            Session = null;
            npc.SyncRoot.EnterWriteLock();
            try
            {
                if (npc.Session is not null && npc.Session != session) { ReattachCaller(session); return false; }
                if (npc.IsDeleted) { ReattachCaller(session); return false; }
                session.PushPuppetEntry(this, npc);
                npc.SetPuppetRestore(snapshot); // Port of puppet.py:138 target._puppet_restore = restore_snapshot
                npc.IsPc = true; // Port of puppet.py:139
                npc.PrivilegeLevel = callerPriv; // Port of puppet.py:140
                session.Puppet = npc;
                npc.Session = session; // Port of puppet.py:142
            }
            finally { npc.SyncRoot.ExitWriteLock(); }
        }
        // Port of puppet.py:112 hook half (state already settled above).
        Suppress("Puppet", () => this.AtDisconnect());
        Suppress("Puppet", () => npc.AtPuppet(this));
        Suppress("AtPostPuppet", () => npc.AtPostPuppet());
        return true;
    }

    // Port of puppet.py failure path: a failed puppet leaves the caller
    // attached (caller.session = session, is_connected True). Runs inside
    // the single session.Lock hold; property setters take the object lock
    // internally (session -> object order).
    private void ReattachCaller(Session session)
    {
        Suppress("ReattachCaller", () =>
        {
            Session = session;
            IsConnected = true;
            session.Puppet = this;
        });
    }

    /// <summary>
    /// Port of unpuppet — mirrors <c>atheriz/commands/loggedin/puppet.py:164-192</c>.
    /// </summary>
    public bool Unpuppet(Session session)
    {
        if (session is null) return false;
        GameObject prev;
        GameObject target;
        Dictionary<string, object>? restore;
        // Single critical section : pop + restore-apply + rewire are
        // atomic — a concurrent Puppet/Unpuppet/AtDisconnect in the old gap
        // can no longer clobber the puppet pointer with a stale write. Only
        // state runs under the lock; game hooks fire after release.
        lock (session.Lock)
        {
            if (!session.TryPopPuppetEntry(out var prevEntry, out target)) return false;
            prev = prevEntry!;
            // Read the restore here; applied after AtUnpuppet below so game
            // hooks observe the pre-restore target like puppet.py:164-192.
            restore = target.GetPuppetRestore();
            target.Session = null;
            session.Puppet = prev;
            prev.Session = session;
        }
        Suppress("Unpuppet", () => target.AtUnpuppet(prev));
        // Ownership re-check: a concurrent Puppet during AtUnpuppet owns the target
        // now. A stolen target skips BOTH the stale
        // restore and AtDisconnect — tearing down another session's live puppet is
        // exactly the clobber this guards against. Hooks already observed the
        // pre-restore target above.
        bool stolen = true;
        lock (session.Lock)
        {
            try { stolen = target.Session is not null; }
            catch { stolen = true; }
        }
        if (restore is not null && !stolen)
        {
            // GetPuppetRestore returns a copy, so compare by content. Apply only if
            // the installed snapshot still matches the one read above.
            bool apply = false;
            lock (session.Lock)
            {
                try
                {
                    var current = target.GetPuppetRestore();
                    apply = current is not null
                        && current.TryGetValue(PuppetRestoreIsPcKey, out var cv) && restore.TryGetValue(PuppetRestoreIsPcKey, out var rv) && Equals(cv, rv)
                        && current.TryGetValue(PuppetRestorePrivilegeKey, out var cp) && restore.TryGetValue(PuppetRestorePrivilegeKey, out var rp) && Convert.ToInt32(cp) == Convert.ToInt32(rp);
                }
                catch { apply = false; }
            }
            if (apply)
            {
                target.RestorePuppetSnapshot(restore);
                target.ClearPuppetRestore();
            }
            else Suppress("Unpuppet", () => AtherizLogger.LogWarning($"GameObject.Unpuppet skipped stale restore for #{target.Id} (snapshot changed during AtUnpuppet).", "GameObject"));
        }
        else if (stolen)
        {
            Suppress("Unpuppet", () => AtherizLogger.LogWarning($"GameObject.Unpuppet target #{target.Id} re-puppeted during AtUnpuppet; skipping restore and disconnect.", "GameObject"));
        }
        if (!stolen)
        {
            Suppress("Unpuppet", () => target.AtDisconnect());
        }
        Suppress("Unpuppet", () => prev.AtPostPuppet());
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
        Suppress("AtPostPuppet", () => SendSessionCommand("logged_in"));
        // Port of base_obj.py:1457-1460 with self.lock: for c in self.channels: if channel := get(c): channel[0].add_listener(self)
        Suppress("AtPostPuppet", () =>
        {
            List<int> channelsCopy = ChannelsSnapshot;
            foreach (var c in channelsCopy)
            {
                Suppress("AtPostPuppet", () =>
                {
                    var ch = ObjectRegistry.GetSingle(c);
                    if (ch is not null)
                    {
                        if (ch is Channel channelObj)
                            channelObj.AddListener(this);
                        // non-Channel IsChannel objects are ignored
                        // (no dynamic dispatch, no throw).
                    }
                });
            }
        });
        // Port of base_obj.py:1461-1462 if channel := get_server_channel(): channel.msg(f"{wrap_xterm256(self.name, fg=15, bold=True)} (#{self.id}) has logged in.")
        Suppress("AtPostPuppet", () =>
        {
            var serverChannel = GlobalServices.GetServerChannel();
            if (serverChannel is not null)
            {
                var wrapped = GameUtils.WrapXterm256(Name ?? "", fg: 15, bold: true);
                serverChannel.Msg($"{wrapped} (#{Id}) has logged in.");
            }
        });
        // Port of base_obj.py:1463-1470 cs = get_loggedin_cmdset(); commands = [cmd.key for cmd in cs.get_all() if cmd.access(self) and not cmd.hide]; try: SOCIALS_DICT
        List<string> commands = new();
        Suppress("AtPostPuppet", () =>
        {
            var cs = GlobalServices.GetLoggedInCmdSet();
            foreach (var cmd in cs.GetAll())
            {
                Suppress("AtPostPuppet", () =>
                {
                    if (!cmd.Hide && cmd.Access(this))
                        commands.Add(cmd.Key);
                });
            }
        });
        Suppress("AtPostPuppet", () =>
        {
            foreach (var key in SocialsCommand.SocialsDict.Keys)
                commands.Add(key);
        });
        // Port of base_obj.py:1471 self.msg(player_commands=commands)
        // Port of self.msg(player_commands=commands) -> connection.send_command("player_commands", commands)
        Suppress("AtPostPuppet", () => SendSessionCommand("player_commands", new List<object?> { commands }, null));
        // Port of base_obj.py:1472 self.msg(f"You become {wrap_xterm256(self.name, fg=15, bold=True)}.")
        Suppress("AtPostPuppet", () =>
        {
            var wrapped = GameUtils.WrapXterm256(Name ?? "", fg: 15, bold: true);
            Msg($"You become {wrapped}.");
        });
        // Port of base_obj.py:1473-1485 if self.location: map handling + move_to + map_enable + render
        // Outermost guard is the same suppressed-log shape as the inner sites
        // (no locks held anywhere in AtPostPuppet), so it rides the helper too.
        Suppress("AtPostPuppet", () =>
        {
            LocationRef locRef = Location;
            bool hasLocation = locRef is not null && !(locRef is LocationRef.NullLocation);
            if (hasLocation)
            {
                // Port of base_obj.py:1474-1478 if settings.MAP_ENABLED: mh.add_listener(self); if self.is_mapable: mh.add_mapable(self)
                bool mapEnabledSettings = false;
                Suppress("AtPostPuppet", () => { TryGetGlobalMapEnabled(out mapEnabledSettings); });
                if (mapEnabledSettings)
                {
                    Suppress("AtPostPuppet", () =>
                    {
                        var mh = GlobalServices.GetMapHandler();
                        Suppress("AtPostPuppet", () => mh.AddListener(this));
                        bool isMapable = false;
                        Suppress("AtPostPuppet", () => { isMapable = IsMapable; });
                        if (isMapable)
                        {
                            Suppress("AtPostPuppet", () => mh.AddMapable(this));
                        }
                    });
                }
                // Port of base_obj.py:1479 self.move_to(self.location, announce=False)
                // (return value ignored upstream too; map_enable below is
                // unconditional). MoveTo runs force:true so hooks fire
                // exactly once (cf. FollowScript stack).
                Suppress("AtPostPuppet", () =>
                {
                    var destObj = ResolveLocationObject();
                    object? destArg = null;
                    if (destObj is not null)
                        destArg = destObj;
                    else if (locRef is LocationRef.CoordLocation cl)
                        destArg = cl.Coord;
                    // No ObjectLocation arm: both remaining cases assign locRef
                    // itself (plain `is` tests, no guards or side effects), so
                    // the extra test was dead. The NullLocation re-guard below
                    // still filters null destinations.
                    else
                        destArg = locRef;
                    if (destArg is not null && !(destArg is LocationRef.NullLocation))
                    {
                        // announce=False to avoid "walks in" spam — test_puppet_announce expects no walk broadcast
                        MoveTo(destArg, force: true, announce: false);
                    }
                });
                // Port of base_obj.py:1480-1485 if settings.MAP_ENABLED and self.map_enabled: self.msg(map_enable=""); mh = get_map_handler(); mi = mh.get_mapinfo(...); if mi: mi.render(True)
                bool mapEnabled2 = false;
                bool selfMapEnabled = false;
                TryGetGlobalMapEnabled(out mapEnabled2);
                try { selfMapEnabled = MapEnabled; } catch { try { selfMapEnabled = IsMapable; } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed GameObject.AtPostPuppet: " + logEx.Message, "GameObject"); } }
                // Port of base_obj.py:1480-1485 (unconditional once flags hold).
                if (mapEnabled2 && selfMapEnabled)
                {
                    // Port of self.msg(map_enable="") -> connection.send_command("map_enable","")
                    Suppress("AtPostPuppet", () => SendSessionCommand("map_enable", new List<object?> { "" }, null));
                    Suppress("AtPostPuppet", () =>
                    {
                        Coord? coord = null;
                        // Re-resolve after move — location may have been re-wired to same Node
                        // (second observation; hoisting to the pre-move read above would
                        // go stale across MoveTo, so both reads stay).
                        locRef = Location;
                        if (locRef is LocationRef.CoordLocation cl2)
                            coord = cl2.Coord;
                        else
                        {
                            var locObj2 = ResolveLocationObject();
                            if (locObj2 is Node n)
                                coord = n.Coord;
                            else if (locObj2 is not null)
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
                            if (mi is not null)
                                mi.Render(true);
                        }
                    });
                }
            }
        });
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

    public virtual bool AtDelete(GameObject? caller) // Port of base_obj.py:467 at_delete
    {
        return Hookable("at_delete", () => Access(caller, "delete"), caller);
    }

    public virtual void AtTick() // Port of base_obj.py:676 at_tick
    {
        Hookable("at_tick", () => 0);
    }

    // Server-event virtuals (replaces TryInvokeVirtual reflection): game-defined
    // subclasses override these with the exact (object? sender) signature to
    // receive at_server_start/stop/reload. Base no-ops equal the old
    // skip-when-declared-on-GameObject rule.
    public virtual void AtServerStart(object? sender) { }
    public virtual void AtServerStop(object? sender) { }
    public virtual void AtServerReload(object? sender) { }

    public virtual void AtSolarEvent(string message) // Port of time.py solar
    {
        Hookable("at_solar_event", () => { try { Msg(message); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed GameObject.AtSolarEvent: " + logEx.Message, "GameObject"); } return 0; }, message);
    }
    public virtual void AtLunarEvent(string message) // Port of time.py lunar
    {
        Hookable("at_lunar_event", () => { try { Msg(message); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed GameObject.AtLunarEvent: " + logEx.Message, "GameObject"); } return 0; }, message);
    }
    public virtual void AtAlarm(Globals.GameTime.GameTimeInfo time, Dictionary<string, System.Text.Json.JsonElement>? data) // Port of time.py alarm
    {
        Hookable("at_alarm", () => 0, time, data);
    }

    public virtual void AtInit() // Port of base_obj.py:669 at_init
    {
        Hookable("at_init", () => 0);
    }

}
