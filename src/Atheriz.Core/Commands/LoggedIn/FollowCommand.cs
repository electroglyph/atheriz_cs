
namespace Atheriz.Core.Commands.LoggedIn;

public sealed class FollowCommand : Command
{
    public override string Key => "follow";
    public override string Desc => "Follow another character or creature.";
    public override string Category => "General";
    protected override void SetupParser(GameArgumentParser p) { p.AddArgument(ParsedArgKeys.Target, nargs: "?", help: "Character or creature to follow."); }
    public override void Run(IMessageTarget caller, object? args)
    {
        if (!CommandHelpers.RequirePuppet(caller, out var go)) return;
        var pa = args as GameArgumentParser.ParsedArgs;
        var targetName = pa?.GetString(ParsedArgKeys.Target);
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
