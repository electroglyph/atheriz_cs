namespace Atheriz.Core.Commands.LoggedIn;

public abstract class DoorDirectionCommand : LoggedInCommand
{
    protected abstract string VerbNoun { get; }
    protected abstract void Act(Door door, GameObject go);
    public override string ExtraDesc => "Also accepts n,s,e,w,u,d as arguments.";
    protected override bool AllowMissingArgs => true;

    protected sealed override void SetupParser(GameArgumentParser p)
    {
        p.AddArgument("-n", "--north").Help("North").Action(GameArgumentParser.ArgAction.StoreTrue);
        p.AddArgument("-s", "--south").Help("South").Action(GameArgumentParser.ArgAction.StoreTrue);
        p.AddArgument("-e", "--east").Help("East").Action(GameArgumentParser.ArgAction.StoreTrue);
        p.AddArgument("-w", "--west").Help("West").Action(GameArgumentParser.ArgAction.StoreTrue);
        p.AddArgument("-u", "--up").Help("Up").Action(GameArgumentParser.ArgAction.StoreTrue);
        p.AddArgument("-d", "--down").Help("Down").Action(GameArgumentParser.ArgAction.StoreTrue);
        p.AddArgument(ParsedArgKeys.Args).Help("Other args").Nargs("*");
    }

    protected sealed override void RunPuppet(GameObject go, GameArgumentParser.ParsedArgs pa, CancellationToken ct)
    {
        // single location resolve (open.py reads caller.location once).
        var loc = go.ResolveLocationObject() as Node;
        if (loc is null)
        {
            CommandHelpers.MsgInvalidLocation(go);
            return;
        }
        var lower = pa.GetList(ParsedArgKeys.Args).Select(a => a.ToLowerInvariant()).ToList();
        bool n = pa.GetBool(ParsedArgKeys.North) || lower.Contains("n") || lower.Contains("north");
        bool s = pa.GetBool(ParsedArgKeys.South) || lower.Contains("s") || lower.Contains("south");
        bool e = pa.GetBool(ParsedArgKeys.East) || lower.Contains("e") || lower.Contains("east");
        bool w = pa.GetBool(ParsedArgKeys.West) || lower.Contains("w") || lower.Contains("west");
        bool u = pa.GetBool(ParsedArgKeys.Up) || lower.Contains("u") || lower.Contains("up");
        bool d = pa.GetBool(ParsedArgKeys.Down) || lower.Contains("d") || lower.Contains("down");
        if (!(n || s || e || w || u || d))
        {
            string cap = char.ToUpperInvariant(VerbNoun[0]) + VerbNoun.Substring(1);
            go.Msg($"{cap} what?");
            go.Msg(PrintHelp());
            return;
        }
        var nh = NodeHandler.GetCurrent();
        if (nh is null)
        {
            // Open shows message, others silently return — we preserve Open behavior for all to avoid silent failure in tests
            go.Msg("No door handler.");
            return;
        }
        Door? GetDoor(string[] names)
        {
            var doors = nh.GetDoors(loc.Coord);
            if (doors is null) return null;
            foreach (var nn in names) if (doors.TryGetValue(nn, out var door)) return door;
            return null;
        }
        // Row order is the established act order (n, s, e, w, u, d). The up/down
        // rows carry their own wording ("no door up", no "to the") — keep the
        // per-row message, never a shared template.
        var dirs = new (string longName, string shortName, bool active, string missing)[]
        {
            ("north", "n", n, "There is no door to the north."),
            ("south", "s", s, "There is no door to the south."),
            ("east", "e", e, "There is no door to the east."),
            ("west", "w", w, "There is no door to the west."),
            ("up", "u", u, "There is no door up."),
            ("down", "d", d, "There is no door down."),
        };
        foreach (var (longName, shortName, active, missing) in dirs)
        {
            if (!active) continue;
            var door = GetDoor([longName, shortName]);
            if (door is not null) Act(door, go);
            else go.Msg(missing);
        }
    }
}

public sealed class CloseCommand : DoorDirectionCommand
{
    public override string Key => "close";
    public override string Desc => "Close doors.";
    public override string Category => "General";
    protected override string VerbNoun => "close";
    protected override void Act(Door d, GameObject go) => d.TryClose(go);
}

public sealed class FollowCommand : LoggedInCommand
{
    public override string Key => "follow";
    public override string Desc => "Follow another character or creature.";
    public override string Category => "General";
    protected override bool AllowMissingArgs => true;
    protected override void SetupParser(GameArgumentParser p) { p.AddArgument(ParsedArgKeys.Target, nargs: "?", help: "Character or creature to follow."); }
    // Global lock order for the follow pair (the MoveTo CompareLockOrder
    // sequence for non-nodes, identity-hash tiebreak for same-id
    // collisions): both directions take First then Second, so opposite
    // simultaneous follows can never hold-and-wait in opposite order.
    internal static (GameObject First, GameObject Second) FollowLockOrder(GameObject go, GameObject target)
    {
        int order = go.Id.CompareTo(target.Id);
        if (order > 0 || (order == 0 && System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(go) > System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(target)))
            return (target, go);
        return (go, target);
    }
    protected override void RunPuppet(GameObject go, GameArgumentParser.ParsedArgs pa, CancellationToken ct)
    {
        var targetName = pa.GetString(ParsedArgKeys.Target);
        if (string.IsNullOrEmpty(targetName)) { go.Msg("Follow who?"); return; }
        var matches = CommandHelpers.SearchWithFallback(go, targetName!);
        if (matches.Count == 0) { CommandHelpers.MsgCouldNotFind(go, targetName); return; }
        if (matches.Count > 1) { CommandHelpers.MsgMultipleMatchesFound(go, targetName); return; }
        var target = matches[0];
        if (target == go) { go.Msg("You can't follow yourself!"); return; }
        if (!target.IsPc && !target.IsNpc) { go.Msg("You can't follow that!"); return; }
        if (target.NoFollow && !go.IsBuilder) { go.Msg($"{target.Name} will not lead you."); return; }
        if (go.Following == target.Id) { go.Msg($"You are already following {target.Name}!"); return; }
        // View gate after the specific diagnostics (self/type/nofollow/
        // already-following keep their messages): a deleted or unviewable
        // target is not followable, reported as not-found.
        if (target.IsDeleted || !target.Access(go, "view")) { CommandHelpers.MsgCouldNotFind(go, targetName); return; }
        // Lock order registry -> object: create + register the script BEFORE
        // taking the target lock (AddObject takes the registry AllLock, which
        // must never nest under an object lock). Re-checked under the lock;
        // a lost race leaves an orphan that is removed after unlock.
        Atheriz.Core.Objects.FollowScript? fresh = null;
        if (!target.GetScriptsByType("FollowScript").Any())
        {
            fresh = new Atheriz.Core.Objects.FollowScript();
            fresh.Name = $"FollowScript_for_{go.Id}";
            fresh.IsModified = true;
            Atheriz.Core.Globals.ObjectRegistry.AddObject(fresh);
        }
        Atheriz.Core.Objects.FollowScript? orphan = null;
        // Break the previous follow BEFORE taking the target lock: every other path
        // that clears Following removes the follower id from the old leader
        // (ClearFollowing, Delete teardown, unfollow). Overwriting the pointer
        // without cleanup strands a stale id on the old leader.
        // Runs outside all object locks (registry -> object order).
        var prevId = go.Following;
        if (prevId is not null && prevId != target.Id)
        {
            var prev = Atheriz.Core.Globals.ObjectRegistry.GetSingle(prevId.Value);
            if (prev is not null)
            {
                try { prev.RemoveFollower(go.Id); } catch (Exception logEx) { Atheriz.Core.AtherizLogger.LogDebug("Suppressed FollowCommand old-leader cleanup: " + logEx.Message, "FollowCommand"); }
            }
        }
        // Ordered pair acquire by Id (FollowLockOrder above): holding target
        // while the Following setter takes go's lock inverts the global
        // order, so opposite simultaneous follows deadlock (ABBA). Both
        // locks are taken here in First/Second sequence; the body uses raw
        // no-lock helpers, never the locking setters.
        var (first, second) = FollowLockOrder(go, target);
        first.SyncRoot.EnterWriteLock();
        if (!ReferenceEquals(first, second)) second.SyncRoot.EnterWriteLock();
        try
        {
            target.AddFollowerRawNoLock(go.Id);
            if (fresh is not null)
            {
                if (!target.GetScriptsByType("FollowScript").Any()) target.AddScript(fresh);
                else orphan = fresh;
            }
            go.SetFollowingRawNoLock(target.Id);
        }
        finally
        {
            if (!ReferenceEquals(first, second))
            {
                try { second.SyncRoot.ExitWriteLock(); } catch (Exception logEx) { Atheriz.Core.AtherizLogger.LogDebug("Suppressed FollowCommand unlock: " + logEx.Message, "FollowCommand"); }
            }
            try { first.SyncRoot.ExitWriteLock(); } catch (Exception logEx) { Atheriz.Core.AtherizLogger.LogDebug("Suppressed FollowCommand unlock: " + logEx.Message, "FollowCommand"); }
        }
        if (orphan is not null) Atheriz.Core.Globals.ObjectRegistry.RemoveObject(orphan);
        var loc2 = go.ResolveLocationObject();
        if (loc2 is Node node && target.Access(go, "view")) node.MsgContents($"$You(caller) $conj(start) following $you(target).", exclude: null, fromObj: go, mapping: new Dictionary<string, object?> { ["caller"] = go, ["target"] = target });
    }
}

public sealed class LockCommand : DoorDirectionCommand
{
    public override string Key => "lock";
    public override string Desc => "Lock doors.";
    public override string Category => "General";
    protected override string VerbNoun => "lock";
    protected override void Act(Door d, GameObject go) => d.TryLock(go);
}

public sealed class NofollowCommand : LoggedInCommand
{
    public override string Key => "nofollow";
    public override string Desc => "Disallow others from following you.";
    public override bool UseParser => false;
    public override string ExtraDesc => "Nofollow is a toggle. Use it to allow or disallow others from following you. Anybody who is following you will immediately stop following you when you use this command.";
    public override string Category => "General";
    protected override void RunPuppetRaw(GameObject go, string raw, CancellationToken ct)
    {
        go.NoFollow = !go.NoFollow;
        if (go.NoFollow)
        {
            go.Msg("You will no longer allow others to follow you.");
            var followers = go.FollowersSnapshot;
            HashSet<int> keep = [];
            foreach (var id in followers)
            {
                var follower = ObjectRegistry.GetSingle(id);
                if (follower is not null && follower.IsBuilder) { keep.Add(id); continue; }
                if (follower is not null)
                {
                    follower.Following = null;
                    if (go.Access(follower, "view")) follower.Msg($"{go.GetDisplayName(follower)} is no longer leading you.");
                    if (follower.Access(go, "view")) go.Msg($"You are no longer leading {follower.GetDisplayName(go)}.");
                }
            }
            go.ClearFollowersExcept(keep);
            FollowHelper.RemoveScriptsIfDrained(go);
        }
        else go.Msg("You will now allow others to follow you.");
    }
}

public sealed class OpenCommand : DoorDirectionCommand
{
    public override string Key => "open";
    public override string Desc => "Open doors.";
    public override string Category => "General";
    protected override string VerbNoun => "open";
    protected override void Act(Door d, GameObject go) => d.TryOpen(go);
}

public sealed class UnfollowCommand : LoggedInCommand
{
    public override string Key => "unfollow";
    public override string Desc => "Stop following whoever you are following.";
    public override string Category => "General";
    public override bool UseParser => false;
    protected override void RunPuppetRaw(GameObject go, string raw, CancellationToken ct)
    {
        if (go.Following is null) { go.Msg("You aren't following anyone."); return; }
        var leader = ObjectRegistry.GetSingle(go.Following.Value);
        if (leader is not null)
        {
            leader.RemoveFollower(go.Id);
            FollowHelper.NotifyUnfollowedLeader(leader, go);
            // clean up the drained FollowScript like nofollow does.
            // Python leaves this to the leader's next move (follow.py:170-192
            // has no script cleanup); without a move the script lingers, so
            // remove it eagerly once the follower set drains.
            FollowHelper.RemoveScriptsIfDrained(leader);
        }
        go.Following = null;
        go.Msg("You stop following.");
    }
}

public sealed class UnlockCommand : DoorDirectionCommand
{
    public override string Key => "unlock";
    public override string Desc => "Unlock doors.";
    public override string Category => "General";
    protected override string VerbNoun => "unlock";
    protected override void Act(Door d, GameObject go) => d.TryUnlock(go);
}

/// NodeLinks metadata.</summary>
/// <remarks>
/// Key "exit" is the verbatim Python exit.py:15 class key, but live room-exit instances are always
/// re-keyed per direction (Node.AddExits calls SetKey(link name)), and this type is never added to
/// the global registry — so it can never collide with QuitCommand's "exit" alias at CmdSet.Add time.
/// Dispatch order (InternalCmdSet before global registry) resolves any residual overlap. Do not rename.
/// </remarks>
public sealed class LoggedInExitCommand : Command
{
    public override string Key => "exit";
    public override string Desc => "bleh";
    public override bool Hide => true;
    public override bool UseParser => false;
    public int CallerId { get; set; } = -1;
    public Coord? Location { get; set; }
    public Coord? Destination { get; set; }
    public string ExitName { get; set; } = "";
    // Faithful aliases for Python attributes: self.name / self.key
    public string Name { get => ExitName; set => ExitName = value; }
    public string DoorKey { get => ExitName; set => ExitName = value; }
    public override void Run(CommandContext ctx)
    {
        if (ctx.Caller is GameObject go)
        {
            // Python's run just calls do_move which fetches via caller_id; if CallerId not set, set it from caller
            if (CallerId == -1) CallerId = go.Id;
            if (Location is null)
            {
                var loc = go.ResolveLocationObject() as Node;
                if (loc is not null) Location = loc.Coord;
            }
            DoMove();
        }
        else
        {
            DoMove();
        }
    }

// verbatim faithful
    public void DoMove()
    {
        var nh = NodeHandler.GetCurrent();
        if (nh is null) return;
        GameObject? c = ObjectRegistry.GetSingle(CallerId);
        if (c is null)
        {
            AtherizLogger.LogError($"Exit command with invalid caller. id = {CallerId}, destination = {Destination}, location = {Location}, name = {ExitName}");
            return;
        }
        if (Location is null || Destination is null)
        {
            AtherizLogger.LogError($"invalid Exit command. id = {CallerId}, destination = {Destination}, location = {Location}, name = {ExitName}");
            return;
        }
        var dest = nh.GetNode(Destination.Value);
        if (dest is null)
        {
            AtherizLogger.LogError($"Error getting destination node for: {Destination}");
            return;
        }
        var doors = nh.GetDoors(Location.Value);
        if (doors is not null)
        {
            string lookup = ExitName;
            if (doors.TryGetValue(lookup, out var door) && door is not null)
            {
                if (door.Closed && door.TryOpen(c))
                {
                    ClearFollowing(c);
                    // Re-validate the open verdict: a close/remove/
                    // relock landing between TryOpen and MoveTo must not be
                    // walked through.
                    if (door.Closed) { RestoreClosedDoor(door, c); return; }
                    bool moved = false;
                    try { moved = c.MoveTo(dest, null, false, true, lookup); }
                    catch
                    {
                        RestoreClosedDoor(door, c);
                        throw;
                    }
                    if (moved)
                    {
                        door.TryClose(c);
                    }
                    else
                    {
                        RestoreClosedDoor(door, c);
                    }
                    return;
                }
                else if (!door.Closed)
                {
                    ClearFollowing(c);
                    // Same re-validation as the TryOpen path.
                    if (door.Closed) return;
                    c.MoveTo(dest, null, false, true, lookup);
                    return;
                }
                else
                {
                    // broadcast the reason to the room when there is one).
                    return;
                }
            }
        }
        ClearFollowing(c);
        c.MoveTo(dest, null, false, true, ExitName);
    }

    // Legacy overload: delegates to the single DoMove core (no test callers
    // remain; kept for source compat). CallerId routing makes it identical
    // for registered callers; unregistered callers hit the invalid-caller log.
    public void DoMove(GameObject go)
    {
        CallerId = go.Id;
        DoMove();
    }

    // Best-effort re-close after a refused or failed move through an opened
    // door: TryClose first, forced flag + map step when it refuses or throws.
    internal static void RestoreClosedDoor(Door door, GameObject c)
    {
        bool closedOk = false;
        try { closedOk = door.TryClose(c); } catch { closedOk = false; }
        if (!closedOk)
        {
            try { door.Lock.EnterWriteLock(); try { if (!door.Closed) door.Closed = true; } finally { door.Lock.ExitWriteLock(); } } catch (Exception) { }
            try { door.MapClose(); } catch (Exception) { }
        }
    }

    // Shared with Objects.ExitCommand (exit objects move through the same
    // exit.py:95-103.
    internal static void ClearFollowing(GameObject c)
    {
        if (c.Following is null) return;
        var leader = ObjectRegistry.Get(c.Following.Value).FirstOrDefault();
        if (leader is not null)
        {
            try
            {
                leader.SyncRoot.EnterWriteLock();
                try
                {
                    // need to remove c.id if present (typed raw helper: lock already held)
                    leader.RemoveFollowerRawNoLock(c.Id);
                    // mark modified via IsModified true
                    leader.IsModified = true;
                }
                finally { leader.SyncRoot.ExitWriteLock(); }
            }
            catch (Exception) { }
// the leader's notice is gated on the
            // follower's view of the leader, and vice versa.
            try { FollowHelper.NotifyUnfollowedLeader(leader, c); } catch (Exception) { }
            try { if (leader.Access(c, "view")) c.Msg($"You are no longer following {leader.GetDisplayName(c)}."); } catch (Exception) { }
        }
        c.Following = null;
    }
}
