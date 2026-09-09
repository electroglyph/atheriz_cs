// Port of atheriz/commands/loggedin/follow.py:192

namespace Atheriz.Core.Commands.LoggedIn;

public sealed class FollowCommand : Command
{
    public override string Key => "follow";
    public override string Desc => "Follow another character or creature.";
    public override string Category => "General";
    protected override void SetupParser(GameArgumentParser p) { p.AddArgument("target", nargs: "?", help: "Character or creature to follow."); }
    public override void Run(IMessageTarget caller, object? args)
    {
        if (!CommandHelpers.RequirePuppet(caller, out var go)) return;
        var pa = args as GameArgumentParser.ParsedArgs;
        var targetName = pa?.GetString("target");
        if (string.IsNullOrEmpty(targetName)) { go.Msg("Follow who?"); return; }
        var matches = CommandHelpers.SearchWithFallback(go, targetName!);
        if (matches.Count == 0) { go.Msg($"Could not find '{targetName}'."); return; }
        if (matches.Count > 1) { CommandHelpers.MsgMultipleMatchesFound(go, targetName); return; }
        var target = matches[0];
        if (target == go) { go.Msg("You can't follow yourself!"); return; }
        if (!target.IsPc && !target.IsNpc) { go.Msg("You can't follow that!"); return; }
        if (target.NoFollow && !go.IsBuilder) { go.Msg($"{target.Name} will not lead you."); return; }
        if (go.Following == target.Id) { go.Msg($"You are already following {target.Name}!"); return; }
        // Lock order registry -> object: create + register the script BEFORE
        // taking the target lock (AddObject takes the registry AllLock, which
        // must never nest under an object lock). Re-checked under the lock;
        // a lost race leaves an orphan that is removed after unlock.
        Atheriz.Core.Objects.FollowScript? fresh = null;
        if (!target.GetScriptsByType("FollowScript").Any())
        {
            fresh = new Atheriz.Core.Objects.FollowScript();
            fresh.Id = Atheriz.Core.Globals.IdGenerator.GetUniqueId();
            fresh.Name = $"FollowScript_for_{go.Id}";
            fresh.IsModified = true;
            Atheriz.Core.Globals.ObjectRegistry.AddObject(fresh);
        }
        Atheriz.Core.Objects.FollowScript? orphan = null;
        // Break the previous follow BEFORE taking the target lock: every other path
        // that clears Following removes the follower id from the old leader
        // (ClearFollowing, Delete teardown, unfollow). Overwriting the pointer
        // without cleanup strands a stale id on the old leader (owner decision
        // 2026-09-08). Runs outside all object locks (registry -> object order).
        var prevId = go.Following;
        if (prevId is not null && prevId != target.Id)
        {
            var prev = Atheriz.Core.Globals.ObjectRegistry.Get(prevId.Value).FirstOrDefault();
            if (prev is not null)
            {
                try { prev.RemoveFollower(go.Id); } catch (Exception logEx) { Atheriz.Core.AtherizLogger.LogDebug("Suppressed FollowCommand old-leader cleanup: " + logEx.Message, "FollowCommand"); }
            }
        }
        target.SyncRoot.EnterWriteLock();
        try
        {
            target.AddFollowerRawNoLock(go.Id);
            if (fresh is not null)
            {
                if (!target.GetScriptsByType("FollowScript").Any()) target.AddScript(fresh);
                else orphan = fresh;
            }
            go.Following = target.Id;
        }
        finally { target.SyncRoot.ExitWriteLock(); }
        if (orphan is not null) Atheriz.Core.Globals.ObjectRegistry.RemoveObject(orphan);
        var loc2 = go.ResolveLocationObject();
        if (loc2 is Node node && target.Access(go, "view")) node.MsgContents($"$You(caller) $conj(start) following $you(target).", exclude: null, fromObj: go, mapping: new Dictionary<string, object?> { ["caller"] = go, ["target"] = target });
    }
}

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
        var leader = ObjectRegistry.Get(go.Following.Value).FirstOrDefault();
        if (leader is not null)
        {
            leader.RemoveFollower(go.Id);
            if (go.Access(leader, "view")) leader.Msg($"{go.GetDisplayName(leader)} is no longer following you.");
            // clean up the drained FollowScript like nofollow does.
            // Python leaves this to the leader's next move (follow.py:170-192
            // has no script cleanup); without a move the script lingers, so
            // remove it eagerly once the follower set drains.
            if (leader.FollowersSnapshot.Count == 0)
            {
                foreach (var script in leader.GetScriptsByType("FollowScript").ToList())
                {
                    try { script.IsDeleted = true; ObjectRegistry.RemoveObject(script); leader.RemoveScript(script); } catch (Exception) { }
                }
            }
        }
        go.Following = null;
        go.Msg("You stop following.");
    }
}

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
            var followers = go.FollowersSnapshot.ToList();
            HashSet<int> keep = [];
            foreach (var id in followers)
            {
                var follower = ObjectRegistry.Get(id).FirstOrDefault();
                if (follower is not null && follower.IsBuilder) { keep.Add(id); continue; }
                if (follower is not null)
                {
                    follower.Following = null;
                    if (go.Access(follower, "view")) follower.Msg($"{go.GetDisplayName(follower)} is no longer leading you.");
                    if (follower.Access(go, "view")) go.Msg($"You are no longer leading {follower.GetDisplayName(go)}.");
                }
            }
            go.ClearFollowersExcept(keep);
            if (go.FollowersSnapshot.Count == 0)
            {
                foreach (var script in go.GetScriptsByType("FollowScript").ToList())
                {
                    try { script.IsDeleted = true; ObjectRegistry.RemoveObject(script); go.RemoveScript(script); } catch (Exception) { }
                }
            }
        }
        else go.Msg("You will now allow others to follow you.");
    }
}
