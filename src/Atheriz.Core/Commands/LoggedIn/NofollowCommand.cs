
namespace Atheriz.Core.Commands.LoggedIn;

public sealed class NofollowCommand : Command
{
    public override string Key => "nofollow";
    public override string Desc => "Disallow others from following you.";
    public override bool UseParser => false;
    public override string ExtraDesc => "Nofollow is a toggle. Use it to allow or disallow others from following you. Anybody who is following you will immediately stop following you when you use this command.";
    public override string Category => "General";
    public override void Run(IMessageTarget caller, object? args)
    {
        if (!CommandHelpers.RequirePuppet(caller, out var go)) return;
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
