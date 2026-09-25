
namespace Atheriz.Core.Objects;

/// <summary>
/// </summary>
public sealed class FollowScript : Script
{
    // per-move pairing, not a single shared slot. Concurrent moves of
    // the same leader each push their pre-move location; each post-move pops
    // its own partner. Python keeps a single _old_loc (follow.py) — the
    // stack is a deliberate hardening; no Python test pins the slot.
    // Pending entries carry their destination, so a veto/cancel pops
    // only its own move's entry instead of stealing a concurrent move's.
    private readonly Lock _movesLock = new();
    private readonly List<(int? DestId, GameObject? OldLoc)> _pendingMoves = new();

    public FollowScript()
    {
        IsTemporary = true;
    }

    public GameObject? OldLoc
    {
        get { lock (_movesLock) return _pendingMoves.Count == 0 ? null : _pendingMoves[^1].OldLoc; }
    }

    internal void CancelPush(int? destId)
    {
        lock (_movesLock)
        {
            for (int i = _pendingMoves.Count - 1; i >= 0; i--)
            {
                if (_pendingMoves[i].DestId == destId)
                {
                    _pendingMoves.RemoveAt(i);
                    return;
                }
            }
        }
    }

    internal static void CancelPendingPush(GameObject owner, GameObject? dest)
    {
        try
        {
            foreach (var s in owner.GetScriptsByType("FollowScript"))
            {
                try { if (s is FollowScript fs) fs.CancelPush(dest?.Id); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed FollowScript.CancelPendingPush: " + logEx.Message, "FollowScript"); }
            }
        }
        catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed FollowScript.CancelPendingPush: " + logEx.Message, "FollowScript"); }
    }

    private bool TryTakePush(int? destId, out GameObject? oldLoc)
    {
        lock (_movesLock)
        {
            for (int i = _pendingMoves.Count - 1; i >= 0; i--)
            {
                if (_pendingMoves[i].DestId == destId)
                {
                    oldLoc = _pendingMoves[i].OldLoc;
                    _pendingMoves.RemoveAt(i);
                    return true;
                }
            }
        }
        oldLoc = null;
        return false;
    }

    [Before]
    public void at_pre_move(GameObject? destination, string? toExit = null)
    {
        GameObject? loc;
        try { loc = Child?.ResolveLocationObject(); }
        catch { loc = null; }
        lock (_movesLock) _pendingMoves.Add((destination?.Id, loc));
    }

    [After]
    public void at_post_move(GameObject? destination, string? toExit = null)
    {
        // Take the partner for THIS move first, on every post-move — early
        // returns below used to skip the pop, leaking one entry per failed
        // move and shifting the pairing of all later moves. A move with no
        // entry (vetoed before its push) takes nothing instead of stealing
        // a concurrent move's partner.
        TryTakePush(destination?.Id, out var oldLoc);
        if (destination is null) return;
        var child = Child;
        if (child is null) { Delete(); return; }
        if (child.FollowersSnapshot.Count == 0) { Delete(); return; }
        if (oldLoc is null) return;
        // FollowersSnapshot is already a fresh snapshot collection, so iterate
        // it directly instead of copying it again with ToList().
        foreach (var id in child.FollowersSnapshot)
        {
            // Single lookup with no list alloc: a missing id is a gone
            // follower, skipped exactly like the old empty-list branch.
            var follower = ObjectRegistry.GetSingle(id);
            if (follower is null) continue;
            if (follower.ResolveLocationObject() != oldLoc) continue;
            bool success = follower.MoveTo(destination, toExit: toExit);
            if (!success)
            {
                follower.Msg($"You can't follow {child.Name} there!");
            }
        }
    }

    // Convenience overload (not an override — the base Delete takes
    // (caller, recursive) and returns ops). Direct teardown for a drained
    // script: unregisters and unwires hooks without the veto round-trip.
    public bool Delete()
    {
        IsDeleted = true;
        ObjectRegistry.RemoveObject(this);
        var child = Child;
        if (child is not null)
        {
            RemoveHooks(child);
        }
        return true;
    }
}
