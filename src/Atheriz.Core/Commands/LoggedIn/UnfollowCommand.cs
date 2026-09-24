
namespace Atheriz.Core.Commands.LoggedIn;

public sealed class UnfollowCommand : Command
{
    public override string Key => "unfollow";
    public override string Desc => "Stop following whoever you are following.";
    public override string Category => "General";
    public override bool UseParser => false;
    public override void Run(IMessageTarget caller, object? args)
    {
        if (!CommandHelpers.RequirePuppet(caller, out var go)) return;
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
