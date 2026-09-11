// Shared follower-script drain core behind Unfollow/Nofollow (follow.py:170-192
// leaves cleanup to the leader's next move; the commands remove the drained
// script eagerly so it never lingers without a move).
//
// Takes NO locks itself: callers run it after their own follower mutation,
// so whatever order they already hold (leader lock, follower lock, none)
// is preserved. Reads FollowersSnapshot once for the drain gate.
namespace Atheriz.Core.Commands.LoggedIn;

internal static class FollowHelper
{
    internal static void NotifyUnfollowedLeader(GameObject leader, GameObject follower)
    {
        ArgumentNullException.ThrowIfNull(leader);
        ArgumentNullException.ThrowIfNull(follower);
        if (follower.Access(leader, "view")) leader.Msg($"{follower.GetDisplayName(leader)} is no longer following you.");
    }
    internal static void RemoveScriptsIfDrained(GameObject leader)
    {
        ArgumentNullException.ThrowIfNull(leader);
        if (leader.FollowersSnapshot.Count != 0) return;
        foreach (var script in leader.GetScriptsByType("FollowScript").ToList())
        {
            try { script.IsDeleted = true; ObjectRegistry.RemoveObject(script); leader.RemoveScript(script); } catch (Exception) { }
        }
    }
}
