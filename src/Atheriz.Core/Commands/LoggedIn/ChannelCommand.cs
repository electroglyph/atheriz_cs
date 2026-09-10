// Port of atheriz/commands/loggedin/channel.py:131

namespace Atheriz.Core.Commands.LoggedIn;

/// <summary>Port of atheriz/commands/loggedin/channel.py:ChannelCommand — lazy _channel_cache wontfix.</summary>
public sealed class ChannelCommand : Command
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
        p.AddArgument("message").Help("Message to send").Nargs("*");
        p.AddArgument("-l", "--list").Help("List all channels").Action(GameArgumentParser.ArgAction.StoreTrue);
        p.AddArgument("-c", "--channel").Help("Channel to target");
        p.AddArgument("-u", "--unsubscribe").Help("Unsubscribe from channel").Action(GameArgumentParser.ArgAction.StoreTrue);
        p.AddArgument("-s", "--subscribe").Help("Subscribe to channel").Action(GameArgumentParser.ArgAction.StoreTrue);
        p.AddArgument("-r", "--replay").Help("View channel history").Action(GameArgumentParser.ArgAction.StoreTrue);
    }
    public override void Run(IMessageTarget caller, object? args)
    {
        if (!CommandHelpers.RequirePuppet(caller, out var go)) return;
        var pa = args as GameArgumentParser.ParsedArgs;
        if (pa is null) { caller.Msg(PrintHelp()); return; }
        if (pa.GetBool("list"))
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
        var chName = pa.GetString("channel");
        if (string.IsNullOrEmpty(chName)) { caller.Msg(PrintHelp()); return; }
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
        if (pa.GetBool("unsubscribe"))
        {
            // Port of channel.py:110-111 — silent, no confirmation message.
            go.Unsubscribe(channel);
            return;
        }
        else if (pa.GetBool("subscribe"))
        {
            if (!channel.Access(go, "view")) { go.Msg("You do not have permission to view this channel."); return; }
            go.Subscribe(channel);
        }
        else if (pa.GetBool("replay"))
        {
            if (!channel.Access(go, "view")) { go.Msg("You do not have permission to view this channel."); return; }
            var h = channel.GetHistory();
            if (!string.IsNullOrEmpty(h)) go.Msg(h);
            else go.Msg("No history available.");
        }
        else
        {
            var msgs = pa.GetList("message");
            var message = string.Join(" ", msgs);
            // Port of channel.py:122 elif args.message — an empty message
            // list is falsy and falls through silently (no help text).
            if (string.IsNullOrWhiteSpace(message)) return;
            if (!channel.Access(go, "send")) { go.Msg("You do not have permission to send to this channel."); return; }
            channel.Send(message, go);
        }
    }
}
