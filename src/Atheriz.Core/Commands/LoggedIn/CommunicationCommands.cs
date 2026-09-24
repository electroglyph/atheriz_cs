using System.Diagnostics.CodeAnalysis;

namespace Atheriz.Core.Commands.LoggedIn;

/// lazy _channel_cache wontfix.</summary>
public sealed class ChannelCommand : LoggedInCommand
{
    public override string Key => "channel";
    public override string Desc => "Use and subscribe to channels.";
    public override string Category => "Communication";
    // wontfix: lazy cache only, cleared on is_deleted/name mismatch or via filter_by scan. No eager invalidation on delete/rename.
    private static readonly Dictionary<string, Channel> ChannelCache = new(StringComparer.OrdinalIgnoreCase);
    // Reverse index for the alias-eviction below: the full-cache ToList scan
    // removed every non-matching key holding the same channel id, but the
    // cache holds at most one key per id (every insert evicts the previous
    // one), so evicting the indexed key removes exactly the same entry.
    private static readonly Dictionary<int, string> ChannelIdToKey = [];
    private static readonly Lock CacheLock = new();
    public static void ClearCache() { lock (CacheLock) { ChannelCache.Clear(); ChannelIdToKey.Clear(); } }
    public static IReadOnlyDictionary<string, Channel> GetCacheSnapshot() { lock (CacheLock) return new Dictionary<string, Channel>(ChannelCache, StringComparer.OrdinalIgnoreCase); }
    public static bool TryGetCached(string name, out Channel? ch) { lock (CacheLock) return ChannelCache.TryGetValue(name, out ch); }
    protected override void SetupParser(GameArgumentParser p)
    {
        ChannelActionParser.AddCommonArgs(p);
        p.AddArgument("-l", "--list").Help("List all channels").Action(GameArgumentParser.ArgAction.StoreTrue);
        p.AddArgument("-c", "--channel").Help("Channel to target");
        p.AddArgument("-s", "--subscribe").Help("Subscribe to channel").Action(GameArgumentParser.ArgAction.StoreTrue);
    }
    protected override void RunPuppet(GameObject go, GameArgumentParser.ParsedArgs pa, CancellationToken ct)
    {
        var action = ChannelActionParser.Parse(pa);
        if (action is ChannelAction.List)
        {
            var channels = ObjectRegistry.FilterBy(x => x.IsChannel);
            var visible = channels.Where(c => c.Access(go, "view")).ToList();
            if (visible.Count > 0)
            {
                var msg = string.Join("\n", visible.Select(ch => $"{GameUtils.WrapXterm256(ch.Name, fg:15, bold:true)}: {ch.Desc}"));
                go.Msg($"{visible.Count} available channels:\n{msg}");
            }
            else go.Msg("No channels found.");
            return;
        }
        var chName = pa.GetString(ParsedArgKeys.Channel);
        if (string.IsNullOrEmpty(chName)) { go.Msg(PrintHelp()); return; }
        var nameLower = chName!.ToLowerInvariant();
        Channel? channel = null;
        lock (CacheLock)
        {
            if (ChannelCache.TryGetValue(nameLower, out var cached) && (cached.IsDeleted || !cached.Name.Equals(chName, StringComparison.OrdinalIgnoreCase)))
            {
                ChannelCache.Remove(nameLower);
                if (ChannelIdToKey.TryGetValue(cached.Id, out var staleKey) && staleKey == nameLower) ChannelIdToKey.Remove(cached.Id);
                cached = null;
            }
            channel = ChannelCache.TryGetValue(nameLower, out var c) ? c : null;
        }
        if (channel is null)
        {
            var result = ObjectRegistry.FilterBy(x => x.IsChannel && x.Name.Equals(chName, StringComparison.OrdinalIgnoreCase));
            if (result.Count == 0) { go.Msg($"Channel {chName} not found."); return; }
            if (result[0] is not Channel ch2) { go.Msg($"Channel {chName} not found."); return; }
            channel = ch2;
            if (channel.IsDeleted) { go.Msg($"Channel {chName} not found."); return; }
            lock (CacheLock)
            {
                // Re-check under the insert lock: a delete landing between the
                // scan above and this insert must not cache a dead channel (R5).
                // CacheLock -> channel _histLock matches the order at the
                // lookup above, so no new lock-order edge is introduced.
                if (channel.IsDeleted) { go.Msg($"Channel {chName} not found."); return; }
                if (ChannelCache.TryGetValue(nameLower, out var existing) && !existing.IsDeleted && existing.Name.Equals(chName, StringComparison.OrdinalIgnoreCase))
                    channel = existing;
                else
                {
                    // Direct stale-key removal via the reverse index instead of
                    // the old full-cache ToList scan: same surviving entry.
                    if (ChannelIdToKey.TryGetValue(channel.Id, out var oldKey) && oldKey != nameLower)
                    {
                        ChannelCache.Remove(oldKey);
                        ChannelIdToKey.Remove(channel.Id);
                    }
                    ChannelCache[nameLower] = channel;
                    ChannelIdToKey[channel.Id] = nameLower;
                }
            }
        }
        // One triage shared with BaseChannelCommand (same flag precedence);
        // the arms stay local — the view-as-not-found spoofing here must not
        // cross over to BaseChannelCommand's denied messages.
        switch (action)
        {
            case ChannelAction.Unsubscribe:
                // silent, no confirmation message.
                // View-gated like every other branch, and denied reads as
                // not-found (same message as an unknown name) so probing names
                // via -u cannot distinguish "view-locked" from "nonexistent".
                if (!channel.Access(go, "view")) { go.Msg($"Channel {chName} not found."); return; }
                go.Unsubscribe(channel);
                break;
            case ChannelAction.Subscribe:
                // View-denied reads as not-found (same message as an unknown
                // name) so probing names via -s cannot distinguish "view-locked"
                // from "nonexistent" — same spoofing as the -u branch above.
                if (!channel.Access(go, "view")) { go.Msg($"Channel {chName} not found."); return; }
                go.Subscribe(channel);
                break;
            case ChannelAction.Replay:
                // Same not-found spoofing as -u/-s: a view-denied history probe
                // must not answer differently from an unknown channel name.
                if (!channel.Access(go, "view")) { go.Msg($"Channel {chName} not found."); return; }
                var h = channel.GetHistory();
                if (!string.IsNullOrEmpty(h)) go.Msg(h);
                else CommandHelpers.MsgNoChannelHistory(go);
                break;
            default:
                var msgs = pa.GetList(ParsedArgKeys.Message);
                var message = string.Join(" ", msgs);
                // an empty message
                // list is falsy and falls through silently (no help text).
                if (string.IsNullOrWhiteSpace(message)) return;
                // Sending requires view AND send: a view-denied channel reads as
                // not-found (no send-denied oracle), while a viewable channel
                // without send keeps the send-denied message.
                if (!channel.Access(go, "view")) { go.Msg($"Channel {chName} not found."); return; }
                if (!channel.Access(go, "send")) { CommandHelpers.MsgChannelSendDenied(go); return; }
                channel.Send(message, go);
                break;
        }
    }
}

public sealed class EmoteCommand : LoggedInCommand
{
    public override string Key => "emote";
    public override IReadOnlyList<string> Aliases => [":"];
    public override string Category => "Communication";
    public override string Desc => "Emote something.";
    protected override void SetupParser(GameArgumentParser p)
    {
        p.AddArgument(ParsedArgKeys.Text).Nargs("REMAINDER").Help("Text to emote.");
    }
    protected override void RunPuppet(GameObject p, GameArgumentParser.ParsedArgs pa, CancellationToken ct)
    {
        var lst = pa.GetList(ParsedArgKeys.Text);
        if (lst.Count > 0 && p.ResolveLocationObject() is not null)
        {
            string text = $"{p.Name} {string.Join(" ", lst)}";
            // Same AtSay entry as say so game-code hooks observe emotes; the
            // literal text keeps the established wording for actor and room.
            p.AtSayFull(text, msgSelf: text, msgLocation: text, msgType: "emote",
                mapping: new Dictionary<string, object?> { ["you"] = p });
        }
        else p.Msg(PrintHelp());
    }
}

public sealed class GroupCommand : LoggedInCommand
{
    public override string Key => "group";
    public override string Desc => "Add a follower to your group.";
    public override string Category => "Communication";
    public override string ExtraDesc => "Use 'group add <name>' to add a follower to your group, 'group <message>' to talk to your group, 'group kick <name>' to remove a follower from your group, 'group leave' to leave your current group, or 'group list' to see your current group.";
    protected override bool AllowMissingArgs => true;
    protected override void SetupParser(GameArgumentParser p) { p.AddArgument(ParsedArgKeys.Args, nargs: "REMAINDER", help: "Subcommand (add, kick, leave, list) or a message to group."); }
    protected override void RunPuppet(GameObject go, GameArgumentParser.ParsedArgs pa, CancellationToken ct)
    {
        var list = pa.GetList(ParsedArgKeys.Args);
        if (list.Count == 0) { go.Msg(PrintHelp()); return; }
        // One subcommand word, one op object: adding a subcommand is a new
        // class, not a longer if-chain. An unrecognized word (including a
        // typo) is a message to the group, exactly as the old if-chain's
        // fallthrough did — MessageOp owns that default.
        IGroupOp op = list[0].ToLowerInvariant() switch
        {
            "list" => new ListOp(),
            "kick" => new KickOp(),
            "leave" => new LeaveOp(),
            "add" => new AddOp(),
            _ => new MessageOp(),
        };
        op.Run(new GroupContext(go, list));
    }

    private sealed record GroupContext(GameObject Go, List<string> Tokens);

    private interface IGroupOp
    {
        void Run(GroupContext ctx);
    }

    private sealed class ListOp : IGroupOp
    {
        public void Run(GroupContext ctx)
        {
            var go = ctx.Go;
            if (!TryGetGroupChannel(go, out var channel)) return;
            var names = channel.Listeners.Select(id => ObjectRegistry.GetSingle(id)?.GetDisplayName(go) ?? id.ToString()).ToList();
            go.Msg($"Group members: {string.Join(", ", names)}");
            return;
        }
    }

    private sealed class KickOp : IGroupOp
    {
        public void Run(GroupContext ctx)
        {
            var go = ctx.Go;
            var list = ctx.Tokens;
            if (list.Count < 2) { go.Msg("Usage: group kick <name>"); return; }
            if (!TryGetGroupChannel(go, out var channel)) return;
            // check leader (typed: Channel.CreatedBy, F001)
            int createdBy = channel.CreatedBy;
            if (createdBy != go.Id) { go.Msg("You are not the leader of this group."); return; }
            var targetName = string.Join(" ", list.Skip(1));
            var tgt = ResolveMember(go, targetName, "You can't kick yourself!");
            if (tgt is null) return;
            channel.Msg($"{go.GetDisplayName(null)} kicked {tgt.GetDisplayName(null)} from the group.");
            channel.RemoveListener(tgt);
            try { tgt.RemoveGroupChannel(); } catch { ClearGroupChannel(tgt); }
            return;
        }
    }

    private sealed class LeaveOp : IGroupOp
    {
        public void Run(GroupContext ctx)
        {
            var go = ctx.Go;
            if (!TryGetGroupChannel(go, out var channel, clearOnMissing: true)) return;
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
    }

    private sealed class AddOp : IGroupOp
    {
        public void Run(GroupContext ctx)
        {
            var go = ctx.Go;
            var list = ctx.Tokens;
            if (list.Count < 2) { go.Msg("Usage: group add <name>"); return; }
            var targetName = string.Join(" ", list.Skip(1));
            var tgt = ResolveMember(go, targetName, "You can't add yourself!");
            if (tgt is null) return;
            if (!go.FollowersSnapshot.Contains(tgt.Id)) { go.Msg($"{tgt.GetDisplayName(go)} is not following you."); return; }
            // Single-membership model (one GroupChannel slot): overwriting
            // silently leaves the target listening to the old group's
            // traffic with no way back (leave/list only see the new group).
            // Refuse instead; the old leader kicks first.
            if (GetGroupChannelId(tgt) is not null) { go.Msg($"{tgt.GetDisplayName(go)} is already in a group."); return; }
            var gc = GetGroupChannelId(go);
            Channel? channel = null;
            if (gc is null)
            {
                channel = CreateGroupChannel(go);
                if (channel is null) return;
                // leaked handling: if caller already has group_channel after creation (race)
                var afterGc = GetGroupChannelId(go);
                if (afterGc is not null)
                {
                    var leaked = channel;
                    var existing = ObjectRegistry.GetSingle(afterGc.Value);
                    if (existing is not null)
                    {
                        channel = existing as Channel ?? leaked;
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
                if (!TryGetGroupChannel(go, out channel)) return;
                if (channel.CreatedBy != go.Id) { go.Msg("You are not the leader of this group."); return; }
            }
            channel.AddListener(tgt);
            channel.Msg($"{go.GetDisplayName(null)} added {tgt.GetDisplayName(null)} to the group.");
            SetGroupChannel(tgt, channel.Id);
            return;
        }
    }

    private sealed class MessageOp : IGroupOp
    {
        public void Run(GroupContext ctx)
        {
            var go = ctx.Go;
            var list = ctx.Tokens;
            // list/kick/leave/add handled above; anything else (including a
            // typo) reads as a message to the group.
            string message = string.Join(" ", list);
            if (!TryGetGroupChannel(go, out var ch2)) return;
            ch2.Msg(message, go);
        }
    }
    // Shared member resolution for add/kick: inventory, then viewable location,
    // then hidden-multiple detection. Msgs and returns null on any failure.
    private static GameObject? ResolveMember(GameObject go, string targetName, string selfMsg)
    {
        var matches = ContentUtils.Search(go, targetName, ObjectRegistry.GetSingle, true, go);
        GameObject? loc = null;
        if (matches.Count == 0) { var locTmp = go.ResolveLocationObject() as GameObject; if (locTmp is not null && locTmp.Access(go, "view")) loc = locTmp; }
        else loc = go.ResolveLocationObject() as GameObject;
        if (matches.Count == 0 && loc is not null)
            matches = loc is Node n ? n.Search(targetName, true, go) : ContentUtils.Search(loc, targetName, ObjectRegistry.GetSingle, true, go);
        // Plain search returns the first match only, so a second object sharing
        // the resolved member's name would stay hidden. Probe both pools for a
        // same-named sibling and report multiples instead of guessing wrong.
        if (matches.Count == 1)
        {
            var seen = matches[0];
            bool multiple = false;
            foreach (var pool in new GameObject?[] { go, loc })
            {
                if (pool is null || multiple) break;
                foreach (var o in ContentUtils.GatherContents(pool, ObjectRegistry.GetSingle, looker: go))
                {
                    if (o != seen && string.Equals(o.Name, seen.Name, StringComparison.OrdinalIgnoreCase)) { multiple = true; break; }
                }
            }
            if (multiple) { CommandHelpers.MsgMultipleMatchesFound(go, targetName); return null; }
        }
        if (matches.Count == 0) { CommandHelpers.MsgCouldNotFind(go, targetName); return null; }
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

    private static bool TryGetGroupChannel(GameObject go, [NotNullWhen(true)] out Channel? ch, bool clearOnMissing = false)
    {
        ch = null;
        var gc = GetGroupChannelId(go);
        if (gc is null) { go.Msg("You are not in a group."); return false; }
        ch = ObjectRegistry.GetSingle(gc.Value) as Channel;
        if (ch is null)
        {
            if (clearOnMissing) ClearGroupChannel(go);
            go.Msg("Error: Group channel not found.");
            return false;
        }
        return true;
    }
}

/// <summary>
/// Mirrors <c>atheriz/commands/loggedin/say.py</c> (27 LOC).
/// </summary>
public sealed class SayCommand : LoggedInCommand
{
    public override string Key => "say";
    public override IReadOnlyList<string> Aliases => ["'"];
    public override string Desc => "Say something.";
    public override string Category => "Communication";
    protected override void SetupParser(GameArgumentParser p)
    {
        p.AddArgument(ParsedArgKeys.Text).Nargs("REMAINDER").Help("Text to say.");
    }
    protected override void RunPuppet(GameObject puppet, GameArgumentParser.ParsedArgs pa, CancellationToken ct)
    {
        var lst = pa.GetList(ParsedArgKeys.Text);
        if (lst.Count > 0)
        {
            puppet.AtSay(string.Join(" ", lst), msgSelf: true);
        }
        else puppet.Msg(PrintHelp());
    }
}
