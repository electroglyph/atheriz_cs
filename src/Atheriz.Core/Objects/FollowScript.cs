using Atheriz.Core.Globals;

namespace Atheriz.Core.Objects;

/// <summary>
/// Port of atheriz/commands/loggedin/follow.py:FollowScript (before/after move hooks).
/// </summary>
public sealed class FollowScript : Script
{
    // per-move pairing, not a single shared slot. Concurrent moves of
    // the same leader each push their pre-move location; each post-move pops
    // its own partner. Python keeps a single _old_loc (follow.py) — the
    // stack is a deliberate hardening; no Python test pins the slot.
    private readonly System.Collections.Concurrent.ConcurrentStack<GameObject?> _oldLocStack = new();

    public FollowScript()
    {
        IsTemporary = true;
    }

    public GameObject? OldLoc => _oldLocStack.TryPeek(out var v) ? v : null;

    [Before]
    public void at_pre_move(GameObject? destination, string? toExit = null)
    {
        GameObject? loc;
        try { loc = Child?.ResolveLocationObject(); }
        catch { loc = null; }
        _oldLocStack.Push(loc);
    }

    [After]
    public void at_post_move(GameObject? destination, string? toExit = null)
    {
        // Pop the partner for THIS move first, on every post-move — early returns
        // below used to skip the pop, leaking one entry per failed move and
        // shifting the pairing of all later moves.
        if (!_oldLocStack.TryPop(out var oldLoc)) oldLoc = null;
        if (destination == null) return;
        var child = Child;
        if (child == null) { Delete(); return; }
        if (child.FollowersSnapshot.Count == 0) { Delete(); return; }
        if (oldLoc == null) return;
        List<int> followers;
        // Snapshot followers under lock via the typed snapshot (no reflection).
        followers = child.FollowersSnapshot.ToList();
        foreach (var id in followers)
        {
            var followerList = ObjectRegistry.Get(id);
            if (followerList.Count == 0) continue;
            var follower = followerList[0];
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
        if (child != null)
        {
            RemoveHooks(child);
        }
        return true;
    }
}
