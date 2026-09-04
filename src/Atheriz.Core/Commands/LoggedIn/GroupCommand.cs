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
        if (caller is not GameObject go) { caller.Msg("You can't do that."); return; }
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
            var channel = chObjs[0] as Channel ?? (Channel)chObjs[0];
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
            // check leader
            var createdByField = channel.GetType().GetField("_createdBy", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            int createdBy = channel.Id; // fallback
            // try property CreatedBy via reflection
            var prop = channel.GetType().GetProperty("CreatedBy");
            if (prop != null) createdBy = (int)(prop.GetValue(channel) ?? createdBy);
            // alternative: check via Tag? For simplicity allow if caller is in group and we treat first as leader
            // Instead use channel's stored CreatedBy via dynamic
            try { createdBy = (int)((dynamic)channel).CreatedBy; } catch { }
            if (createdBy != go.Id) { go.Msg("You are not the leader of this group."); return; }
            var targetName = list[1];
            var matches = ContentUtils.Search(go, targetName, id => ObjectRegistry.Get(id).FirstOrDefault(), true, go);
            GameObject? locForKick = null;
            if (matches.Count == 0) { var locTmp2 = go.ResolveLocationObject() as GameObject; if (locTmp2 != null && locTmp2.Access(go, "view")) locForKick = locTmp2; }
            else locForKick = go.ResolveLocationObject() as GameObject;
            if (matches.Count == 0 && locForKick != null)
                matches = locForKick is Node n ? n.Search(targetName, true, go) : ContentUtils.Search(locForKick, targetName, id => ObjectRegistry.Get(id).FirstOrDefault(), true, go);
            if (matches.Count == 1)
            {
                try {
                    var goAllK = ContentUtils.Search(go, "all " + targetName, id => ObjectRegistry.Get(id).FirstOrDefault(), true, go);
                    if (goAllK.Count > 1) matches = goAllK;
                    else if (locForKick != null)
                    {
                        var locAllK = locForKick is Node nAllK ? nAllK.Search("all " + targetName, true, go) : ContentUtils.Search(locForKick, "all " + targetName, id => ObjectRegistry.Get(id).FirstOrDefault(), true, go);
                        if (locAllK.Count > 1) matches = locAllK;
                    }
                } catch {}
            }
            if (matches.Count == 0) { go.Msg($"Could not find '{targetName}'."); return; }
            if (matches.Count > 1) { go.Msg($"Multiple matches found for '{targetName}'."); return; }
            var tgt = matches[0];
            if (tgt == go) { go.Msg("You can't kick yourself!"); return; }
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
            bool wasLeader = false;
            try { wasLeader = (int)((dynamic)channel).CreatedBy == go.Id; } catch { }
            channel.Msg($"{go.GetDisplayName(null)} left the group.");
            channel.RemoveListener(go);
            ClearGroupChannel(go);
            // if was leader and remaining, pick new leader
            if (wasLeader && channel.Listeners.Count > 0)
            {
                var newLeader = channel.Listeners.First();
                try { ((dynamic)channel).CreatedBy = newLeader; } catch { }
            }
            if (channel.Listeners.Count == 0)
            {
                try { channel.Delete(); } catch { try { channel.IsDeleted = true; ObjectRegistry.RemoveObject(channel); } catch { } }
            }
            return;
        }
        if (sub == "add")
        {
            if (list.Count < 2) { go.Msg("Usage: group add <name>"); return; }
            var targetName = list[1];
            var matches = ContentUtils.Search(go, targetName, id => ObjectRegistry.Get(id).FirstOrDefault(), true, go);
            GameObject? locForAdd = null;
            if (matches.Count == 0) { var locTmp = go.ResolveLocationObject() as GameObject; if (locTmp != null && locTmp.Access(go, "view")) locForAdd = locTmp; }
            else locForAdd = go.ResolveLocationObject() as GameObject;
            if (matches.Count == 0 && locForAdd != null)
                matches = locForAdd is Node n2 ? n2.Search(targetName, true, go) : ContentUtils.Search(locForAdd, targetName, id => ObjectRegistry.Get(id).FirstOrDefault(), true, go);
            // Detect hidden multiples when Search returns first match only (port of Python mocked multiple handling) — check exhaustive "all" query
            if (matches.Count == 1)
            {
                List<GameObject> all = new();
                try {
                    // Try go container first
                    var goAll = ContentUtils.Search(go, "all " + targetName, id => ObjectRegistry.Get(id).FirstOrDefault(), true, go);
                    if (goAll.Count > 1) all = goAll;
                    else if (locForAdd != null)
                    {
                        var locAll = locForAdd is Node nAll ? nAll.Search("all " + targetName, true, go) : ContentUtils.Search(locForAdd, "all " + targetName, id => ObjectRegistry.Get(id).FirstOrDefault(), true, go);
                        if (locAll.Count > 1) all = locAll;
                    }
                } catch {}
                if (all.Count > 1) matches = all;
            }
            if (matches.Count == 0) { go.Msg($"Could not find '{targetName}'."); return; }
            if (matches.Count > 1) { go.Msg($"Multiple matches found for '{targetName}'."); return; }
            var tgt = matches[0];
            if (tgt == go) { go.Msg("You can't add yourself!"); return; }
            if (!go.FollowersSnapshot.Contains(tgt.Id)) { go.Msg($"{tgt.GetDisplayName(go)} is not following you."); return; }
            var gc = GetGroupChannelId(go);
            Channel? channel = null;
            if (gc == null)
            {
                try
                {
                    channel = Channel.Create($"{go.Name}'s group", go);
                }
                catch (InvalidOperationException)
                {
                    // ValueError retry 5 times with random suffix
                    channel = null;
                    for (int r = 0; r < 5; r++)
                    {
                        try
                        {
                            channel = Channel.Create($"{go.Name}'s group {Random.Shared.Next(0, 100)}", go);
                            break;
                        }
                        catch (InvalidOperationException) { continue; }
                        catch (ArgumentException) { continue; }
                    }
                    if (channel == null)
                    {
                        go.Msg("Could not create a group channel; try again.");
                        return;
                    }
                }
                catch (ArgumentException)
                {
                    channel = null;
                    for (int r = 0; r < 5; r++)
                    {
                        try
                        {
                            channel = Channel.Create($"{go.Name}'s group {Random.Shared.Next(0, 100)}", go);
                            break;
                        }
                        catch { continue; }
                    }
                    if (channel == null)
                    {
                        go.Msg("Could not create a group channel; try again.");
                        return;
                    }
                }
                // leaked handling: if caller already has group_channel after creation (race)
                var afterGc = GetGroupChannelId(go);
                if (afterGc != null)
                {
                    var leaked = channel;
                    var existing = ObjectRegistry.Get(afterGc.Value);
                    if (existing.Count > 0)
                    {
                        channel = existing[0] as Channel ?? leaked;
                        try { leaked.Delete(); } catch { }
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
                try { if ((int)((dynamic)channel).CreatedBy != go.Id) { go.Msg("You are not the leader of this group."); return; } } catch { }
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
    private static int? GetGroupChannelId(GameObject go)
    {
        try { return (int?)((dynamic)go).GroupChannel; } catch { }
        // fallback via tag or field
        var f = go.GetType().GetField("_groupChannel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (f != null) return f.GetValue(go) as int?;
        // try property GroupChannel via extra dict
        var prop = go.GetType().GetProperty("GroupChannel");
        if (prop != null) return prop.GetValue(go) as int?;
        return null;
    }
    private static void SetGroupChannel(GameObject go, int id)
    {
        try { ((dynamic)go).GroupChannel = id; return; } catch { }
        var f = go.GetType().GetField("_groupChannel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (f != null) f.SetValue(go, id);
    }
    private static void ClearGroupChannel(GameObject go)
    {
        try { ((dynamic)go).GroupChannel = null; return; } catch { }
        var f = go.GetType().GetField("_groupChannel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (f != null) f.SetValue(go, null);
    }
}

// extension helpers for Channel/GameObject group handling
internal static class GroupExtensions
{
    public static void RemoveGroupChannel(this GameObject go)
    {
        try { ((dynamic)go).GroupChannel = null; } catch { }
    }
    public static void AddChannel(this GameObject go, int chId)
    {
        // GameObject channels list
        go.SyncRoot.EnterWriteLock();
        try
        {
            var f = go.GetType().GetField("_channels", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (f?.GetValue(go) is List<int> lst && !lst.Contains(chId)) { lst.Add(chId); go.IsModified = true; }
        }
        finally { go.SyncRoot.ExitWriteLock(); }
    }
    public static void RemoveChannel(this GameObject go, int chId)
    {
        go.SyncRoot.EnterWriteLock();
        try
        {
            var f = go.GetType().GetField("_channels", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (f?.GetValue(go) is List<int> lst) { lst.Remove(chId); go.IsModified = true; }
        }
        finally { go.SyncRoot.ExitWriteLock(); }
    }
}
