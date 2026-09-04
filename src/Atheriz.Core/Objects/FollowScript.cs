using Atheriz.Core.Globals;

namespace Atheriz.Core.Objects;

/// <summary>
/// Port of atheriz/commands/loggedin/follow.py:FollowScript (before/after move hooks).
/// </summary>
public sealed class FollowScript : Script
{
    private GameObject? _oldLoc;

    public FollowScript()
    {
        IsTemporary = true;
    }

    public GameObject? OldLoc => _oldLoc;

    [Before]
    public void at_pre_move(GameObject? destination, string? toExit = null)
    {
        try { _oldLoc = Child?.ResolveLocationObject(); }
        catch { _oldLoc = null; }
    }

    [After]
    public void at_post_move(GameObject? destination, string? toExit = null)
    {
        if (destination == null) return;
        var child = Child;
        if (child == null) { Delete(); return; }
        if (child.FollowersSnapshot.Count == 0) { Delete(); return; }
        var oldLoc = _oldLoc;
        try { _oldLoc = null; } catch {}
        if (oldLoc == null) return;
        List<int> followers;
        // snapshot followers under lock
        var f = typeof(GameObject).GetField("_followers", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (f != null)
        {
            var set = f.GetValue(child) as HashSet<int>;
            followers = set != null ? new List<int>(set) : child.FollowersSnapshot.ToList();
        }
        else followers = child.FollowersSnapshot.ToList();
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

    public bool Delete()
    {
        IsDeleted = true;
        ObjectRegistry.RemoveObject(this);
        var child = Child;
        if (child != null)
        {
            RemoveHooks(child);
            try
            {
                var f = typeof(GameObject).GetField("_followers", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                // do not clear followers here, just remove script
            } catch {}
        }
        return true;
    }
}
