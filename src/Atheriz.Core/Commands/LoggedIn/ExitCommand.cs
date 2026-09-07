// Port of atheriz/commands/loggedin/exit.py:104
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Commands.LoggedIn;

/// <summary>Port of atheriz/commands/loggedin/exit.py:ExitCommand (hidden) — NodeLinks metadata.</summary>
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
    public override void Run(IMessageTarget caller, object? args)
    {
        if (caller is GameObject go)
        {
            // Python's run just calls do_move which fetches via caller_id; if CallerId not set, set it from caller
            if (CallerId == -1) CallerId = go.Id;
            if (Location == null)
            {
                var loc = go.ResolveLocationObject() as Node;
                if (loc != null) Location = loc.Coord;
            }
            DoMove();
        }
        else
        {
            DoMove();
        }
    }

    // Port of exit.py:31 do_move — verbatim faithful
    public void DoMove()
    {
        var nh = NodeHandler.GetCurrent();
        if (nh == null) return;
        var lst = ObjectRegistry.Get(CallerId);
        GameObject? c = null;
        if (lst.Count > 0) c = lst[0];
        if (c == null)
        {
            try { Console.Error.WriteLine($"Exit command with invalid caller. id = {CallerId}, destination = {Destination}, location = {Location}, name = {ExitName}"); } catch (Exception) { }
            return;
        }
        if (Location == null || Destination == null)
        {
            try { Console.Error.WriteLine($"invalid Exit command. id = {CallerId}, destination = {Destination}, location = {Location}, name = {ExitName}"); } catch (Exception) { }
            return;
        }
        var dest = nh.GetNode(Destination.Value);
        if (dest == null)
        {
            try { Console.Error.WriteLine($"Error getting destination node for: {Destination}"); } catch (Exception) { }
            return;
        }
        var doors = nh.GetDoors(Location.Value);
        if (doors != null)
        {
            string lookup = ExitName;
            if (doors.TryGetValue(lookup, out var door) && door != null)
            {
                if (door.Closed && door.TryOpen(c))
                {
                    ClearFollowing(c);
                    bool moved = false;
                    try { moved = c.MoveTo(dest, null, false, true, lookup); }
                    catch
                    {
                        bool closedOk = false;
                        try { closedOk = door.TryClose(c); } catch { closedOk = false; }
                        if (!closedOk)
                        {
                            try { door.Lock.EnterWriteLock(); try { if (!door.Closed) door.Closed = true; } finally { door.Lock.ExitWriteLock(); } } catch (Exception) { }
                            try { door.MapClose(); } catch (Exception) { }
                        }
                        throw;
                    }
                    if (moved)
                    {
                        door.TryClose(c);
                    }
                    else
                    {
                        bool closedOk = false;
                        try { closedOk = door.TryClose(c); } catch { closedOk = false; }
                        if (!closedOk)
                        {
                            try { door.Lock.EnterWriteLock(); try { if (!door.Closed) door.Closed = true; } finally { door.Lock.ExitWriteLock(); } } catch (Exception) { }
                            try { door.MapClose(); } catch (Exception) { }
                        }
                    }
                    return;
                }
                else if (!door.Closed)
                {
                    ClearFollowing(c);
                    c.MoveTo(dest, null, false, true, lookup);
                    return;
                }
                else
                {
                    // Door stayed closed and TryOpen already broadcast the
                    // reason via loc MsgContents — except when the caller has
                    // no location to broadcast to (loc null), where TryOpen's
                    // loc?.MsgContents reaches nobody. Message directly so the
                    // move never fails silently (Python is silent here too).
                    if (c.ResolveLocationObject() == null)
                        c.Msg("You can't go that way.");
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

    // Shared with Objects.ExitCommand (exit objects move through the same
    // follow-breaking rule): internal so both exit paths use one port of
    // exit.py:95-103.
    internal static void ClearFollowing(GameObject c)
    {
        if (c.Following == null) return;
        var leader = ObjectRegistry.Get(c.Following.Value).FirstOrDefault();
        if (leader != null)
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
            // Port of exit.py:100-103 — the leader's notice is gated on the
            // follower's view of the leader, and vice versa.
            try { if (c.Access(leader, "view")) leader.Msg($"{c.GetDisplayName(leader)} is no longer following you."); } catch (Exception) { }
            try { if (leader.Access(c, "view")) c.Msg($"You are no longer following {leader.GetDisplayName(c)}."); } catch (Exception) { }
        }
        c.Following = null;
    }
}
