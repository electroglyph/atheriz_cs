// Port of atheriz/commands/loggedin/group.py:222
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Commands.LoggedIn;

public sealed class GroupCommand : Command
{
    public override string Key => "group";
    public override string Desc => "Add a follower to your group.";
    public override string Category => "Communication";
    public override string ExtraDesc => "Use 'group add <name>' to add a follower to your group, 'group <message>' to talk to your group, 'group kick <name>' to remove a follower from your group, 'group leave' to leave your current group, or 'group list' to see your current group.";
    protected override void SetupParser(GameArgumentParser p) { p.AddArgument("args", nargs: "REMAINDER", help: "Subcommand (add, kick, leave, list) or a message to group."); }
    public override void Run(IMessageTarget caller, object? args)
    {
        if (!CommandHelpers.RequirePuppet(caller, out var go)) return;
        var pa = args as GameArgumentParser.ParsedArgs;
        var list = pa?.GetList("args") ?? [];
        if (list.Count == 0) { go.Msg(PrintHelp()); return; }
        string sub = list[0].ToLowerInvariant();
        if (sub == "list")
        {
            var gc = GetGroupChannelId(go);
            if (gc == null) { go.Msg("You are not in a group."); return; }
            var chObjs = ObjectRegistry.Get(gc.Value);
            if (chObjs.Count == 0) { go.Msg("Error: Group channel not found."); return; }
            var channel = chObjs[0] as Channel;
            if (channel == null) { go.Msg("Error: Group channel not found."); return; }
            var names = channel.Listeners.Select(id => ObjectRegistry.Get(id).FirstOrDefault()?.GetDisplayName(go) ?? id.ToString()).ToList();
            go.Msg($"Group members: {string.Join(", ", names)}");
            return;
        }
        if (sub == "kick")
        {
            if (list.Count < 2) { go.Msg("Usage: group kick <name>"); return; }
            var gc = GetGroupChannelId(go);
            if (gc == null) { go.Msg("You are not in a group."); return; }
            var channel = ObjectRegistry.Get(gc.Value).FirstOrDefault() as Channel;
            if (channel == null) { go.Msg("Error: Group channel not found."); return; }
            // check leader (typed: Channel.CreatedBy, F001)
            int createdBy = channel.CreatedBy;
            if (createdBy != go.Id) { go.Msg("You are not the leader of this group."); return; }
            var targetName = list[1];
            var tgt = ResolveMember(go, targetName, "You can't kick yourself!");
            if (tgt == null) return;
            channel.Msg($"{go.GetDisplayName(null)} kicked {tgt.GetDisplayName(null)} from the group.");
            channel.RemoveListener(tgt);
            try { tgt.RemoveGroupChannel(); } catch { ClearGroupChannel(tgt); }
            return;
        }
        if (sub == "leave")
        {
            var gc = GetGroupChannelId(go);
            if (gc == null) { go.Msg("You are not in a group."); return; }
            var channel = ObjectRegistry.Get(gc.Value).FirstOrDefault() as Channel;
            if (channel == null) { ClearGroupChannel(go); go.Msg("Error: Group channel not found."); return; }
            bool wasLeader = channel.CreatedBy == go.Id;
            channel.Msg($"{go.GetDisplayName(null)} left the group.");
            channel.RemoveListener(go);
            ClearGroupChannel(go);
            // if was leader and remaining, pick new leader
            if (wasLeader && channel.Listeners.Count > 0)
            {
                var newLeader = channel.Listeners.First();
                channel.CreatedBy = newLeader;
            }
            if (channel.Listeners.Count == 0)
            {
                try { channel.Delete(); } catch { try { channel.IsDeleted = true; ObjectRegistry.RemoveObject(channel); } catch (Exception) { } }
            }
            return;
        }
        if (sub == "add")
        {
            if (list.Count < 2) { go.Msg("Usage: group add <name>"); return; }
            var targetName = list[1];
            var tgt = ResolveMember(go, targetName, "You can't add yourself!");
            if (tgt == null) return;
            if (!go.FollowersSnapshot.Contains(tgt.Id)) { go.Msg($"{tgt.GetDisplayName(go)} is not following you."); return; }
            var gc = GetGroupChannelId(go);
            Channel? channel = null;
            if (gc == null)
            {
                channel = CreateGroupChannel(go);
                if (channel == null) return;
                // leaked handling: if caller already has group_channel after creation (race)
                var afterGc = GetGroupChannelId(go);
                if (afterGc != null)
                {
                    var leaked = channel;
                    var existing = ObjectRegistry.Get(afterGc.Value);
                    if (existing.Count > 0)
                    {
                        channel = existing[0] as Channel ?? leaked;
                        try { leaked.Delete(); } catch (Exception) { }
                    }
                    else
                    {
                        channel.AddListener(go);
                        SetGroupChannel(go, channel.Id);
                    }
                }
                else
                {
                    channel.AddListener(go);
                    SetGroupChannel(go, channel.Id);
                }
            }
            else
            {
                channel = ObjectRegistry.Get(gc.Value).FirstOrDefault() as Channel;
                if (channel == null) { go.Msg("Error: Group channel not found."); return; }
                if (channel.CreatedBy != go.Id) { go.Msg("You are not the leader of this group."); return; }
            }
            channel.AddListener(tgt);
            channel.Msg($"{go.GetDisplayName(null)} added {tgt.GetDisplayName(null)} to the group.");
            SetGroupChannel(tgt, channel.Id);
            return;
        }
        // message to group
        string message = string.Join(" ", list);
        var gc2 = GetGroupChannelId(go);
        if (gc2 == null) { go.Msg("You are not in a group."); return; }
        var ch2 = ObjectRegistry.Get(gc2.Value).FirstOrDefault() as Channel;
        if (ch2 == null) { go.Msg("Error: Group channel not found."); return; }
        ch2.Msg(message, go);
    }
    // Shared member resolution for add/kick: inventory, then viewable location,
    // then hidden-multiple detection. Msgs and returns null on any failure.
    private static GameObject? ResolveMember(GameObject go, string targetName, string selfMsg)
    {
        var matches = ContentUtils.Search(go, targetName, id => ObjectRegistry.Get(id).FirstOrDefault(), true, go);
        GameObject? loc = null;
        if (matches.Count == 0) { var locTmp = go.ResolveLocationObject() as GameObject; if (locTmp != null && locTmp.Access(go, "view")) loc = locTmp; }
        else loc = go.ResolveLocationObject() as GameObject;
        if (matches.Count == 0 && loc != null)
            matches = loc is Node n ? n.Search(targetName, true, go) : ContentUtils.Search(loc, targetName, id => ObjectRegistry.Get(id).FirstOrDefault(), true, go);
        // Plain search returns the first match only, so a second object sharing
        // the resolved member's name would stay hidden. Probe both pools for a
        // same-named sibling and report multiples instead of guessing wrong.
        if (matches.Count == 1)
        {
            var seen = matches[0];
            bool multiple = false;
            foreach (var pool in new GameObject?[] { go, loc })
            {
                if (pool == null || multiple) break;
                foreach (var o in ContentUtils.GatherContents(pool, id => ObjectRegistry.Get(id).FirstOrDefault(), looker: go))
                {
                    if (o != seen && string.Equals(o.Name, seen.Name, StringComparison.OrdinalIgnoreCase)) { multiple = true; break; }
                }
            }
            if (multiple) { CommandHelpers.MsgMultipleMatchesFound(go, targetName); return null; }
        }
        if (matches.Count == 0) { go.Msg($"Could not find '{targetName}'."); return null; }
        if (matches.Count > 1) { CommandHelpers.MsgMultipleMatchesFound(go, targetName); return null; }
        var tgt = matches[0];
        if (tgt == go) { go.Msg(selfMsg); return null; }
        return tgt;
    }

    // Channel.Create throws on name collision (Python group.py:163 retries with
    // randint(0, 99)). Leader names are unique so the base name effectively
    // never collides; the fallback uses a deterministic leader-Id suffix
    // instead of randomness — distinct leaders can never collide with it.
    private static Channel? CreateGroupChannel(GameObject go)
    {
        try { return Channel.Create($"{go.Name}'s group", go); }
        catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException)
        {
            try { return Channel.Create($"{go.Name}'s group {go.Id}", go); }
            catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed GroupCommand.CreateGroupChannel: " + logEx.Message, "GroupCommand"); }
            go.Msg("Could not create a group channel; try again.");
            return null;
        }
    }

    private static int? GetGroupChannelId(GameObject go) => go.GroupChannel;
    private static void SetGroupChannel(GameObject go, int id) { go.GroupChannel = id; }
    private static void ClearGroupChannel(GameObject go) { go.GroupChannel = null; }
}

// extension helpers for Channel/GameObject group handling
internal static class GroupExtensions
{
    public static void RemoveGroupChannel(this GameObject go)
    {
        go.GroupChannel = null;
    }
}
